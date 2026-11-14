using Azure;
using Niobium.Invoicing.Core.Tests.Support;
using Niobium.Invoicing.History;

namespace Niobium.Invoicing.Core.Tests.History;

[TestClass]
public sealed class InvoiceHistoryReviewTests
{
    [TestMethod]
    [DataRow("cancel")]
    [DataRow("reconcile")]
    [DataRow("token")]
    public async Task Item_only_external_changes_are_observed_before_a_later_invoice_mutation(string mutation)
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        Guid original = scenario.Stored!.ReadLifecycle()!.HistoryRevision!.Value;
        scenario.Items[0].Quantity = 9;
        if (mutation == "cancel")
            await scenario.Domain().CancelAsync(scenario.Request.Tenant, scenario.Request.BillerID,
                new() { Operation = Guid.NewGuid(), Reason = "Synthetic cancellation" }, DateTimeOffset.UtcNow);
        else if (mutation == "reconcile")
            await scenario.Domain().ReconcilePaymentHistoryAsync(scenario.Request.Tenant, scenario.Request.BillerID,
                new() { Operation = Guid.NewGuid(), Finding = InvoicePaymentHistory.PaymentOccurred,
                    Reason = "Synthetic reconciliation", Evidence = "Synthetic bank statement" }, DateTimeOffset.UtcNow);
        else
            await scenario.Domain().EnsureAccessTokenAsync(scenario.Options.InvoiceTokenSecretSalt, default);
        IReadOnlyList<InvoiceRevision> entries = scenario.History.Committed(scenario.Stored!);
        Assert.AreEqual(4, entries.Count);
        Assert.AreEqual(InvoiceChangeKind.ObservedBaseline, entries[1].ReadChange().Kind);
        Assert.AreEqual(InvoiceChangeAuthority.UnattributedCoreCaller, entries[1].ReadChange().Authority);
        Assert.AreEqual(original, entries[1].Previous);
        Assert.AreEqual(9, scenario.History.Snapshot(entries[0]).Lines[0].Quantity);
        Assert.AreEqual(9, scenario.History.Snapshot(entries[1]).Lines[0].Quantity);
        Assert.AreEqual(InvoiceLineEvidence.Observed, scenario.History.Snapshot(entries[0]).LineEvidence);
        Assert.AreEqual(1, scenario.History.Snapshot(entries[2]).Lines[0].Quantity);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Cancellation_records_observed_partial_lines_without_mislabeling_expected_preparation_as_external(bool retryable)
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        scenario.Request.InvoiceItems[0].Quantity = 2;
        InvoiceItem second = IssuanceScenario.Copy(scenario.Request.InvoiceItems[0]);
        second.ID = 2;
        scenario.Request.InvoiceItems.Add(second);
        if (retryable)
        {
            scenario.FailCreate = true;
            await Assert.ThrowsExactlyAsync<RequestFailedException>(() => scenario.RunAsync());
            InvoiceRevisionSnapshot failure = scenario.History.Snapshot(scenario.History.Committed(scenario.Stored!)[0]);
            Assert.AreEqual(InvoiceLineEvidence.Proposed, failure.LineEvidence);
            Assert.AreEqual(2, failure.Lines.Count);
        }
        else
            await scenario.Domain().UpdateAsync(scenario.Request, scenario.Items, default);
        Assert.AreEqual(1, scenario.Items.Count);
        await scenario.Domain().CancelAsync(scenario.Request.Tenant, scenario.Request.BillerID,
            new() { Operation = Guid.NewGuid(), Reason = "Synthetic cancellation" }, DateTimeOffset.UtcNow);
        IReadOnlyList<InvoiceRevision> entries = scenario.History.Committed(scenario.Stored!);
        Assert.IsTrue(entries.All(entry => entry.ReadChange().Kind != InvoiceChangeKind.ObservedBaseline));
        InvoiceRevisionSnapshot cancelled = scenario.History.Snapshot(entries[0]);
        Assert.AreEqual(1, cancelled.Lines.Count);
        Assert.AreEqual(scenario.Items[0].Quantity, cancelled.Lines[0].Quantity);
        Assert.AreEqual(InvoiceLineEvidence.Observed, cancelled.LineEvidence);
    }

    [TestMethod]
    public async Task Cancellation_during_preparation_observes_matching_lines_without_claiming_completed_preparation()
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        scenario.Request.InvoiceItems[0].Quantity = 2;
        await scenario.Domain().UpdateAsync(scenario.Request, scenario.Items, default);
        scenario.Items.Clear();
        scenario.Items.AddRange(scenario.Request.InvoiceItems.Select(IssuanceScenario.Copy));
        await scenario.Domain().CancelAsync(scenario.Request.Tenant, scenario.Request.BillerID,
            new() { Operation = Guid.NewGuid(), Reason = "Synthetic cancellation during preparation" }, DateTimeOffset.UtcNow);
        IReadOnlyList<InvoiceRevision> entries = scenario.History.Committed(scenario.Stored!);
        Assert.IsTrue(entries.All(entry => entry.ReadChange().Kind != InvoiceChangeKind.ObservedBaseline));
        InvoiceRevisionSnapshot cancelled = scenario.History.Snapshot(entries[0]);
        Assert.AreEqual(InvoicePreparationStage.Preparing, cancelled.Preparation);
        Assert.AreEqual(InvoiceLineEvidence.Observed, cancelled.LineEvidence);
        Assert.AreEqual(2, cancelled.Lines[0].Quantity);
    }

    [TestMethod]
    public async Task Item_storage_metadata_changes_do_not_invent_a_business_revision()
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        scenario.Items[0].ETag = "different-storage-version";
        scenario.Items[0].UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(1);
        await scenario.Domain().CancelAsync(scenario.Request.Tenant, scenario.Request.BillerID,
            new() { Operation = Guid.NewGuid(), Reason = "Synthetic cancellation" }, DateTimeOffset.UtcNow);
        IReadOnlyList<InvoiceRevision> entries = scenario.History.Committed(scenario.Stored!);
        Assert.AreEqual(3, entries.Count);
        Assert.IsTrue(entries.All(entry => entry.ReadChange().Kind != InvoiceChangeKind.ObservedBaseline));
        Assert.AreEqual(InvoiceLineEvidence.Verified, scenario.History.Snapshot(entries[0]).LineEvidence);
    }

    [TestMethod]
    [DataRow("new")]
    [DataRow("anchored")]
    [DataRow("legacy")]
    public async Task Oversized_requested_history_returns_a_client_error_without_any_persistence(string state)
    {
        IssuanceScenario scenario = new();
        if (state == "anchored")
            await scenario.RunAsync();
        else if (state == "legacy")
        {
            scenario.Stored = Invoice.BuildNew(scenario.Request.InvoiceID, scenario.Biller, scenario.Billee);
            scenario.Stored.ETag = "legacy";
        }
        int invoiceWrites = scenario.InvoiceWrites, itemWrites = scenario.ItemWrites;
        int historyCount = scenario.History.Revisions.Count;
        string? before = scenario.Stored?.LifecycleData;
        scenario.Request.InvoiceItems[0].Description = new String('x', 17 * 1024 * 1024);
        ApplicationException failure = await Assert.ThrowsExactlyAsync<ApplicationException>(() => scenario.RunAsync());
        Assert.AreEqual(400, failure.ErrorCode);
        Assert.AreEqual(invoiceWrites, scenario.InvoiceWrites);
        Assert.AreEqual(itemWrites, scenario.ItemWrites);
        Assert.AreEqual(historyCount, scenario.History.Revisions.Count);
        Assert.AreEqual(before, scenario.Stored?.LifecycleData);
    }

    [TestMethod]
    public async Task Oversized_legacy_history_returns_a_conflict_without_partial_history_or_invoice_writes()
    {
        IssuanceScenario scenario = new();
        scenario.Stored = Invoice.BuildNew(scenario.Request.InvoiceID, scenario.Biller, scenario.Billee);
        scenario.Stored.ETag = "legacy";
        InvoiceItem line = IssuanceScenario.Copy(scenario.Request.InvoiceItems[0]);
        line.Description = new String('x', 17 * 1024 * 1024);
        scenario.Items.Add(line);
        ApplicationException failure = await Assert.ThrowsExactlyAsync<ApplicationException>(() =>
            scenario.Domain().ReconcilePaymentHistoryAsync(scenario.Request.Tenant, scenario.Request.BillerID,
                new() { Operation = Guid.NewGuid(), Finding = InvoicePaymentHistory.NoPayments,
                    Reason = "Synthetic reconciliation", Evidence = "Synthetic legacy record" }, DateTimeOffset.UtcNow));
        Assert.AreEqual(409, failure.ErrorCode);
        Assert.AreEqual(0, scenario.History.Revisions.Count);
        Assert.AreEqual(0, scenario.InvoiceWrites);
        Assert.AreEqual(0, scenario.ItemWrites);
        Assert.IsNull(scenario.Stored.LifecycleData);
    }

    [TestMethod]
    public async Task Verified_completion_rejects_a_line_change_after_the_callers_read_before_writing_history()
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        scenario.Request.InvoiceItems[0].Quantity = 2;
        await scenario.Domain().UpdateAsync(scenario.Request, scenario.Items, default);
        scenario.Items.Clear();
        scenario.Items.AddRange(scenario.Request.InvoiceItems.Select(IssuanceScenario.Copy));
        InvoiceItem[] verified = scenario.Items.Select(IssuanceScenario.Copy).ToArray();
        scenario.Items[0].Quantity = 9;
        string before = scenario.Stored!.LifecycleData!;
        int historyCount = scenario.History.Revisions.Count;
        ApplicationException failure = await Assert.ThrowsExactlyAsync<ApplicationException>(() =>
            scenario.Domain().CompletePreparationAsync(verified, default));
        Assert.AreEqual(409, failure.ErrorCode);
        Assert.AreEqual(before, scenario.Stored.LifecycleData);
        Assert.AreEqual(historyCount, scenario.History.Revisions.Count);
    }

    [TestMethod]
    public void An_anchored_lifecycle_cannot_be_downgraded_by_the_current_model_api()
    {
        IssuanceScenario scenario = new();
        Invoice invoice = Invoice.BuildNew(scenario.Request.InvoiceID, scenario.Biller, scenario.Billee);
        invoice.StoreLifecycle(InvoiceLifecycleSnapshot.Create(InvoicePreparationStage.Ready));
        Assert.AreEqual(1, invoice.ReadLifecycle()!.Version);
        Guid revision = Guid.NewGuid();
        invoice.ReferenceHistory(revision);
        string before = invoice.LifecycleData!;
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            invoice.StoreLifecycle(InvoiceLifecycleSnapshot.Create(InvoicePreparationStage.Ready)));
        Assert.AreEqual(before, invoice.LifecycleData);
        invoice.StoreLifecycle(invoice.ReadLifecycle()! with { Delivery = InvoiceDeliveryState.Delivering });
        Assert.AreEqual(2, invoice.ReadLifecycle()!.Version);
        Assert.AreEqual(revision, invoice.ReadLifecycle()!.HistoryRevision);
    }
}
