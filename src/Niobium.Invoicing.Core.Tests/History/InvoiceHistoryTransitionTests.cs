using Azure;
using Niobium.Invoicing.Core.Tests.Support;
using Niobium.Invoicing.History;

namespace Niobium.Invoicing.Core.Tests.History;

[TestClass]
public sealed class InvoiceHistoryTransitionTests
{
    [TestMethod]
    public async Task First_legacy_token_creation_captures_the_committed_lifecycle_without_inventing_an_external_change()
    {
        IssuanceScenario scenario = new();
        scenario.Stored = Invoice.BuildNew(scenario.Request.InvoiceID, scenario.Biller, scenario.Billee);
        scenario.Stored.ETag = "synthetic-legacy";
        Assert.IsNull(scenario.Stored.ReadLifecycle());
        await scenario.Domain().EnsureAccessTokenAsync(scenario.Options.InvoiceTokenSecretSalt, default);
        IReadOnlyList<InvoiceRevision> tokenHistory = scenario.History.Committed(scenario.Stored);
        Assert.AreEqual(2, tokenHistory.Count);
        Assert.AreEqual(InvoiceChangeKind.AccessTokenSaved, tokenHistory[0].ReadChange().Kind);
        InvoiceRevisionSnapshot tokenSnapshot = scenario.History.Snapshot(tokenHistory[0]);
        Assert.IsNotNull(tokenSnapshot.Lifecycle);
        Assert.AreEqual(InvoicePaymentHistory.Unknown, tokenSnapshot.Lifecycle.PaymentHistory);
        Assert.IsTrue(InvoiceRevision.MatchesInvoice(tokenSnapshot, scenario.Stored));
        Assert.IsNull(tokenSnapshot.Invoice.Token);
        Assert.IsNull(scenario.History.Snapshot(tokenHistory[1]).Lifecycle,
            "The observed legacy baseline must still describe its original representation.");
        await scenario.Domain().ReconcilePaymentHistoryAsync(scenario.Request.Tenant, scenario.Request.BillerID,
            new()
            {
                Operation = Guid.NewGuid(),
                Finding = InvoicePaymentHistory.NoPayments,
                Reason = "Synthetic review",
                Evidence = "Synthetic legacy evidence"
            },
            DateTimeOffset.UtcNow);
        IReadOnlyList<InvoiceRevision> reconciled = scenario.History.Committed(scenario.Stored);
        CollectionAssert.AreEqual(new[] { InvoiceChangeKind.PaymentHistoryReconciled, InvoiceChangeKind.AccessTokenSaved, InvoiceChangeKind.ObservedBaseline },
            reconciled.Select(entry => entry.ReadChange().Kind).ToArray());
        Assert.IsTrue(InvoiceRevision.MatchesInvoice(scenario.History.Snapshot(reconciled[0]), scenario.Stored));
    }

    [TestMethod]
    public async Task Creation_and_edit_retain_earlier_lines_amounts_and_actor_while_exact_replay_adds_nothing()
    {
        IssuanceScenario scenario = new();
        scenario.Request.ChangeReason = "  Customer corrected the quantity  ";
        InvoiceChangeOrigin origin = new(InvoiceChangeAuthority.AuthenticatedBiller, scenario.Request.BillerID.ToString());
        await scenario.CreateFlow().RunAsync(scenario.Request, null, origin, default);
        Guid first = scenario.Stored!.ReadLifecycle()!.HistoryRevision!.Value;
        Assert.AreEqual(2, scenario.History.Committed(scenario.Stored).Count);
        Assert.AreEqual(2, scenario.Stored.ReadLifecycle()!.Version);
        scenario.Request.InvoiceItems[0].Quantity = 3;
        await scenario.CreateFlow().RunAsync(scenario.Request, null, origin, default);
        IReadOnlyList<InvoiceRevision> entries = scenario.History.Committed(scenario.Stored!);
        Assert.AreEqual(4, entries.Count);
        Assert.IsTrue(entries.All(entry => entry.ReadChange().Authority == origin.Authority && entry.Actor == origin.Actor));
        Assert.IsTrue(entries.All(entry => entry.Reason == "Customer corrected the quantity"));
        InvoiceRevisionSnapshot earlier = scenario.History.Snapshot(entries.Single(entry => entry.ID == first));
        Assert.AreEqual(1, earlier.Lines.Single().Quantity);
        Assert.AreEqual(11500, earlier.Invoice.GrandTotalCents);
        InvoiceRevisionSnapshot latest = scenario.History.Snapshot(entries[0]);
        Assert.AreEqual(3, latest.Lines.Single().Quantity);
        Assert.AreEqual(34500, latest.Invoice.GrandTotalCents);
        Assert.AreEqual(InvoiceLineEvidence.Verified, latest.LineEvidence);
        Assert.AreEqual(InvoiceLineEvidence.Proposed, scenario.History.Snapshot(entries[1]).LineEvidence);
        int historyCount = scenario.History.Revisions.Count;
        await scenario.CreateFlow().RunAsync(scenario.Request, null, origin, default);
        Assert.AreEqual(historyCount, scenario.History.Revisions.Count);
        Assert.AreEqual(entries[0].ID, scenario.Stored!.ReadLifecycle()!.HistoryRevision);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task History_failure_prevents_the_corresponding_invoice_commit(bool parts)
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        string before = scenario.Stored!.LifecycleData!;
        int writes = scenario.InvoiceWrites;
        scenario.History.FailParts = parts;
        scenario.History.FailRevision = !parts;
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => scenario.Domain().CancelAsync(scenario.Request.Tenant,
            scenario.Request.BillerID, new() { Operation = Guid.NewGuid(), Reason = "Synthetic cancellation" }, DateTimeOffset.UtcNow));
        Assert.AreEqual(writes, scenario.InvoiceWrites);
        Assert.AreEqual(before, scenario.Stored.LifecycleData);
        Assert.IsNull(scenario.Stored.ReadLifecycle()!.Cancellation);
    }

    [TestMethod]
    public async Task A_rejected_invoice_update_leaves_an_unreferenced_history_candidate()
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        Guid before = scenario.Stored!.ReadLifecycle()!.HistoryRevision!.Value;
        int priorHistory = scenario.History.Revisions.Count;
        scenario.FailInvoiceWrite = true;
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => scenario.Domain().CancelAsync(scenario.Request.Tenant,
            scenario.Request.BillerID, new() { Operation = Guid.NewGuid(), Reason = "Synthetic cancellation" }, DateTimeOffset.UtcNow));
        Assert.IsGreaterThan(priorHistory, scenario.History.Revisions.Count);
        Assert.AreEqual(before, scenario.Stored.ReadLifecycle()!.HistoryRevision);
        Assert.IsTrue(scenario.History.Committed(scenario.Stored).All(entry => entry.ReadChange().Kind != InvoiceChangeKind.Cancelled));
        Assert.IsNull(scenario.Stored.ReadLifecycle()!.Cancellation);
    }

    [TestMethod]
    public async Task Failed_preparation_is_preserved_as_proposed_and_retry_retains_that_history()
    {
        IssuanceScenario scenario = new() { FailCreate = true };
        await Assert.ThrowsExactlyAsync<RequestFailedException>(() => scenario.RunAsync());
        InvoiceRevision failed = scenario.History.Committed(scenario.Stored!)[0];
        Assert.AreEqual(InvoiceChangeKind.PreparationFailed, failed.ReadChange().Kind);
        InvoiceRevisionSnapshot snapshot = scenario.History.Snapshot(failed);
        Assert.AreEqual(InvoicePreparationStage.Retryable, snapshot.Preparation);
        Assert.AreEqual(InvoiceLineEvidence.Proposed, snapshot.LineEvidence);
        scenario.FailCreate = false;
        await scenario.RunAsync();
        IReadOnlyList<InvoiceRevision> committed = scenario.History.Committed(scenario.Stored!);
        Assert.IsTrue(committed.Any(entry => entry.ID == failed.ID));
        Assert.AreEqual(InvoiceLineEvidence.Verified, scenario.History.Snapshot(committed[0]).LineEvidence);
    }

    [TestMethod]
    public async Task Observed_external_changes_are_explicitly_unattributed_and_do_not_erase_the_earlier_chain()
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        Guid previous = scenario.Stored!.ReadLifecycle()!.HistoryRevision!.Value;
        scenario.Stored.Terms = "Externally changed terms";
        await scenario.Domain().CancelAsync(scenario.Request.Tenant, scenario.Request.BillerID,
            new() { Operation = Guid.NewGuid(), Reason = "Synthetic cancellation" }, DateTimeOffset.UtcNow);
        IReadOnlyList<InvoiceRevision> committed = scenario.History.Committed(scenario.Stored);
        Assert.AreEqual(InvoiceChangeKind.Cancelled, committed[0].ReadChange().Kind);
        Assert.AreEqual(InvoiceChangeAuthority.AuthenticatedBiller, committed[0].ReadChange().Authority);
        Assert.AreEqual(InvoiceChangeKind.ObservedBaseline, committed[1].ReadChange().Kind);
        Assert.AreEqual(InvoiceChangeAuthority.UnattributedCoreCaller, committed[1].ReadChange().Authority);
        Assert.AreEqual(previous, committed[1].Previous);
        Assert.AreEqual("Externally changed terms", scenario.History.Snapshot(committed[1]).Invoice.Terms);
        Assert.AreNotEqual("Externally changed terms", scenario.History.Snapshot(committed[2]).Invoice.Terms);
    }

    [TestMethod]
    public async Task A_foreign_biller_cannot_be_recorded_as_the_authenticated_actor()
    {
        IssuanceScenario scenario = new();
        InvoiceChangeOrigin foreign = new(InvoiceChangeAuthority.AuthenticatedBiller, Guid.NewGuid().ToString());
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => scenario.CreateFlow().RunAsync(scenario.Request, null, foreign, default));
        Assert.AreEqual(0, scenario.History.Revisions.Count);
        Assert.AreEqual(0, scenario.InvoiceWrites);
        Assert.AreEqual(0, scenario.ItemWrites);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(501)]
    public async Task An_invalid_explicit_reason_is_rejected_before_any_persistence(int length)
    {
        IssuanceScenario scenario = new();
        scenario.Request.ChangeReason = new String('x', length);
        ApplicationException failure = await Assert.ThrowsExactlyAsync<ApplicationException>(() => scenario.RunAsync());
        Assert.AreEqual(400, failure.ErrorCode);
        Assert.AreEqual(0, scenario.InvoiceWrites);
        Assert.AreEqual(0, scenario.ItemWrites);
        Assert.AreEqual(0, scenario.History.Revisions.Count);
    }
}
