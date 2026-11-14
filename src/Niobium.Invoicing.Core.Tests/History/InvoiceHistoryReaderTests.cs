using Niobium.Invoicing.Core.Tests.Support;
using Niobium.Invoicing.History;

namespace Niobium.Invoicing.Core.Tests.History;

[TestClass]
public sealed class InvoiceHistoryReaderTests
{
    [TestMethod]
    public async Task Paging_follows_only_committed_history_and_retains_its_position_after_another_edit()
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        IReadOnlyList<InvoiceRevision> original = scenario.History.Committed(scenario.Stored!);
        scenario.FailInvoiceWrite = true;
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => scenario.Domain().CancelAsync(scenario.Request.Tenant,
            scenario.Request.BillerID, new()
            {
                Operation = Guid.NewGuid(),
                Reason = "Synthetic rejected cancellation"
            }, DateTimeOffset.UtcNow));
        scenario.FailInvoiceWrite = false;
        Assert.AreEqual(3, scenario.History.Revisions.Count, "The failed commit leaves an unreferenced candidate.");
        int writes = scenario.InvoiceWrites, itemWrites = scenario.ItemWrites;
        InvoiceHistoryPage first = await scenario.Domain().ReadHistoryAsync(scenario.Request.Tenant, scenario.Request.BillerID);
        Assert.AreEqual(original[0].ID, first.Entry!.ID);
        Assert.AreEqual(InvoiceChangeKind.PreparationCompleted, first.Entry.Change.Kind);
        Assert.IsNotNull(first.Continuation);
        Assert.IsNull(first.Entry.Snapshot.Invoice.Token);
        Assert.AreEqual(writes, scenario.InvoiceWrites);
        Assert.AreEqual(itemWrites, scenario.ItemWrites);
        scenario.Request.InvoiceItems[0].Quantity = 2;
        await scenario.RunAsync();
        writes = scenario.InvoiceWrites;
        InvoiceHistoryPage second = await scenario.Domain().ReadHistoryAsync(scenario.Request.Tenant, scenario.Request.BillerID, first.Continuation);
        Assert.AreEqual(original[1].ID, second.Entry!.ID);
        Assert.AreEqual(1, second.Entry.Snapshot.Lines.Single().Quantity);
        Assert.IsNull(second.Continuation);
        Assert.AreEqual(writes, scenario.InvoiceWrites);
        Assert.AreEqual(0, scenario.Messages.Count);
    }

    [TestMethod]
    public async Task A_legacy_invoice_has_no_invented_history_and_reading_does_not_create_it()
    {
        IssuanceScenario scenario = new();
        scenario.Stored = Invoice.BuildNew(scenario.Request.InvoiceID, scenario.Biller, scenario.Billee);
        scenario.Stored.ETag = "synthetic-legacy";
        InvoiceHistoryPage page = await scenario.Domain().ReadHistoryAsync(scenario.Request.Tenant, scenario.Request.BillerID);
        Assert.IsNull(page.Entry);
        Assert.IsNull(page.Continuation);
        Assert.AreEqual(0, scenario.InvoiceWrites);
        Assert.AreEqual(0, scenario.History.Revisions.Count);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task History_requires_the_current_biller_and_management_group(bool wrongBiller)
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        int writes = scenario.InvoiceWrites;
        ApplicationException denied = await Assert.ThrowsExactlyAsync<ApplicationException>(() => scenario.Domain().ReadHistoryAsync(
            wrongBiller ? scenario.Request.Tenant : Guid.NewGuid(), wrongBiller ? Guid.NewGuid() : scenario.Request.BillerID));
        Assert.AreEqual(403, denied.ErrorCode);
        Assert.AreEqual(writes, scenario.InvoiceWrites);
    }

    [TestMethod]
    [DataRow("modified")]
    [DataRow("wrong-invoice")]
    [DataRow("wrong-biller")]
    [DataRow("wrong-group")]
    [DataRow("wrong-key")]
    [DataRow("expired")]
    [DataRow("empty")]
    [DataRow("oversized")]
    public void Continuations_reject_tampering_cross_identity_reuse_expiry_and_invalid_shapes(string kind)
    {
        IssuanceScenario scenario = new();
        Invoice invoice = Invoice.BuildNew(scenario.Request.InvoiceID, scenario.Biller, scenario.Billee);
        DateTimeOffset now = new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);
        Guid revision = Guid.NewGuid();
        string secret = scenario.Options.InvoiceTokenSecretSalt;
        string cursor = InvoiceHistoryCursor.Create(invoice, revision, now, secret);
        Assert.IsTrue(InvoiceHistoryCursor.TryResolve(cursor, invoice, now.AddMinutes(29), secret, out Guid valid));
        Assert.AreEqual(revision, valid);
        switch (kind)
        {
            case "modified":
                cursor = (cursor[0] == 'A' ? "B" : "A") + cursor[1..];
                break;
            case "wrong-invoice":
                invoice.Created = invoice.Created.AddMilliseconds(1);
                break;
            case "wrong-biller":
                invoice.Biller = Guid.NewGuid();
                break;
            case "wrong-group":
                invoice.Tenant = Guid.NewGuid();
                break;
            case "wrong-key":
                secret = "different-synthetic-key";
                break;
            case "expired":
                now = now.AddMinutes(30);
                break;
            case "empty":
                cursor = String.Empty;
                break;
            case "oversized":
                cursor = new String('A', 10000);
                break;
        }
        Assert.IsFalse(InvoiceHistoryCursor.TryResolve(cursor, invoice, now, secret, out Guid rejected));
        Assert.AreEqual(Guid.Empty, rejected);
    }

    [TestMethod]
    public async Task Arbitrary_revision_ids_are_not_accepted_as_continuations()
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        ApplicationException invalid = await Assert.ThrowsExactlyAsync<ApplicationException>(() => scenario.Domain().ReadHistoryAsync(
            scenario.Request.Tenant, scenario.Request.BillerID, Guid.NewGuid().ToString()));
        Assert.AreEqual(400, invalid.ErrorCode);
    }

    [TestMethod]
    public async Task A_missing_committed_part_fails_instead_of_returning_incomplete_evidence()
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        InvoiceRevision revision = scenario.History.Committed(scenario.Stored!)[0];
        scenario.History.Parts.Remove(revision.GetPartKey(0));
        ApplicationException failure = await Assert.ThrowsExactlyAsync<ApplicationException>(() => scenario.Domain().ReadHistoryAsync(
            scenario.Request.Tenant, scenario.Request.BillerID));
        Assert.AreEqual(409, failure.ErrorCode);
    }
}
