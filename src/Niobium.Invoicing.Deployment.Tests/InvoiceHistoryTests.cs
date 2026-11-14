using System.Net;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using Niobium.Invoicing.History;

namespace Niobium.Invoicing.Deployment.Tests;

public sealed partial class DeployedInvoiceTests
{
    [TestMethod]
    public async Task Deployed_material_edits_preserve_committed_snapshots_and_authenticated_actor_without_recording_replays()
    {
        long id = await SeedInvoice(recipient: String.Empty);
        IssueInvoiceRequest request = Request(id);
        request.ChangeReason = "Synthetic initial history revision";
        request.InvoiceItems[0].Description = new String('x', 20000);
        using (HttpResponseMessage response = await Post(request, Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var first = await ReadHistory(id);
        Assert.AreEqual(3, first.Count, "An owned legacy seed needs an observed baseline and two preparation records.");
        Assert.AreEqual(InvoiceChangeKind.ObservedBaseline, first[^1].Revision.ReadChange().Kind);
        Assert.AreEqual(InvoiceChangeAuthority.UnattributedCoreCaller, first[^1].Revision.ReadChange().Authority);
        Guid original = first[0].Revision.ID;
        request.InvoiceItems[0].Quantity = 2;
        request.ChangeReason = "Synthetic quantity correction";
        using (HttpResponseMessage response = await Post(request, Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadHistory(id);
        Assert.AreEqual(5, updated.Count);
        Assert.AreEqual(23000, updated[0].Snapshot.Invoice.GrandTotalCents);
        Assert.AreEqual(2, updated[0].Snapshot.Lines.Single().Quantity);
        Assert.AreEqual(InvoiceLineEvidence.Verified, updated[0].Snapshot.LineEvidence);
        Assert.AreEqual(InvoiceLineEvidence.Proposed, updated[1].Snapshot.LineEvidence);
        Assert.AreEqual("Synthetic quantity correction", updated[0].Revision.Reason);
        Assert.AreEqual(InvoiceChangeAuthority.AuthenticatedBiller, updated[0].Revision.ReadChange().Authority);
        Assert.AreEqual(biller.ToString(), updated[0].Revision.Actor);
        Assert.IsGreaterThan(1, updated[0].Revision.PartCount);
        var retained = updated.Single(entry => entry.Revision.ID == original);
        Assert.AreEqual(11500, retained.Snapshot.Invoice.GrandTotalCents);
        Assert.AreEqual(1, retained.Snapshot.Lines.Single().Quantity);
        Assert.IsNull(retained.Snapshot.Invoice.Token);
        string version = (await Read(id)).ETag.ToString();
        request.ChangeReason = "Exact replay is not another material change";
        using (HttpResponseMessage replay = await Post(request, Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        Assert.AreEqual(version, (await Read(id)).ETag.ToString());
        Assert.AreEqual(5, (await ReadHistory(id)).Count);

        // Mutate only this test's owned item, leaving the invoice version intact.
        IRepository<InvoiceItem> items = services.GetRequiredService<IRepository<InvoiceItem>>();
        InvoiceItem[] persisted = await items.GetAsync(InvoiceItem.BuildPartitionKey(id)).ToArrayAsync();
        persisted[0].Quantity = 9;
        await items.UpdateAsync(persisted);
        Assert.AreEqual(version, (await Read(id)).ETag.ToString());
        using (HttpResponseMessage reconciled = await LifecycleRequest(id, "payment-history/reconcile",
            PaymentReview(InvoicePaymentHistory.NoPayments), Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.OK, reconciled.StatusCode);
        var observed = await ReadHistory(id);
        Assert.AreEqual(7, observed.Count);
        Assert.AreEqual(InvoiceChangeKind.ObservedBaseline, observed[1].Revision.ReadChange().Kind);
        Assert.AreEqual(InvoiceChangeAuthority.UnattributedCoreCaller, observed[1].Revision.ReadChange().Authority);
        Assert.AreEqual(9, observed[0].Snapshot.Lines.Single().Quantity);
        Assert.AreEqual(9, observed[1].Snapshot.Lines.Single().Quantity);
        Assert.AreEqual(2, observed[2].Snapshot.Lines.Single().Quantity);
        Assert.AreEqual(InvoiceLineEvidence.Observed, observed[0].Snapshot.LineEvidence);
    }

    [TestMethod]
    [DataRow("plain")]
    [DataRow("escaped")]
    [DataRow("aggregate")]
    [DataRow("many")]
    public async Task Oversized_history_request_returns_http_bad_request_without_creating_an_observed_baseline(string shape)
    {
        long id = await SeedInvoice(recipient: String.Empty);
        TableEntity before = await Read(id);
        IssueInvoiceRequest request = Request(id);
        if (shape == "many")
        {
            string currency = request.InvoiceItems[0].UnitPriceCurrency;
            request.InvoiceItems = Enumerable.Range(0, 100_000).Select(index => new InvoiceItem
            {
                Invoice = Invoice.ParseID(id),
                ID = index,
                Subject = itemSubject,
                UnitPriceCurrency = currency,
                LineTotalCurrency = currency,
                Quantity = 1
            }).ToList();
        }
        else
        {
            request.InvoiceItems[0].Description = shape == "escaped"
                ? new String('\u0001', 3 * 1024 * 1024)
                : new String('x', (shape == "aggregate" ? 9 : 17) * 1024 * 1024);
            if (shape == "aggregate")
            {
                InvoiceItem second = Request(id).InvoiceItems[0];
                second.ID = request.InvoiceItems[0].ID + 1;
                second.Description = request.InvoiceItems[0].Description;
                request.InvoiceItems.Add(second);
            }
        }
        using HttpResponseMessage response = await Post(request, Token(biller, tenant));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        TableEntity after = await Read(id);
        Assert.AreEqual(before.ETag, after.ETag);
        Assert.IsFalse(after.ContainsKey(nameof(Invoice.LifecycleData)));
        foreach (string table in new[] { nameof(InvoiceRevision), nameof(InvoiceRevisionPart) })
            await foreach (TableEntity unused in tables.GetTableClient(table).QueryAsync<TableEntity>(
                TableClient.CreateQueryFilter($"PartitionKey eq {biller.ToString()} and InvoiceCreated eq {Invoice.ParseID(id)}")))
                Assert.Fail("Rejected oversized input must not leave any history revision or part.");
    }

    private static async Task AssertNotificationHistory(Guid owner, long id)
    {
        var entries = await ReadHistory(id, owner);
        Assert.AreEqual(InvoiceChangeKind.NotificationQueued, entries[0].Revision.ReadChange().Kind);
        var frozen = entries.Single(entry => entry.Revision.ReadChange().Kind == InvoiceChangeKind.Frozen);
        Assert.AreEqual(InvoicePreparationStage.Frozen, frozen.Snapshot.Preparation);
        Assert.AreEqual(InvoiceLineEvidence.Verified, frozen.Snapshot.LineEvidence);
        Assert.AreEqual(InvoiceChangeAuthority.FunctionKey, frozen.Revision.ReadChange().Authority);
        Assert.IsNull(frozen.Snapshot.Lifecycle!.QueuedAt);
        Assert.IsNotNull(entries[0].Snapshot.Lifecycle!.QueuedAt);
        Assert.AreEqual(InvoiceDeliveryState.Delivering, entries[0].Snapshot.Lifecycle!.Delivery);
        Assert.IsNull(entries[0].Snapshot.Lifecycle!.ConfirmedDeliveredAt);
        Assert.IsTrue(entries.All(entry => entry.Snapshot.Invoice.Token == null));
        Invoice current = (await services.GetRequiredService<IRepository<Invoice>>().RetrieveAsync(
            Invoice.BuildPartitionKey(owner), Invoice.BuildRowKey(id)))!;
        Assert.IsTrue(InvoiceRevision.MatchesInvoice(entries[0].Snapshot, current),
            "The actual freeze and acknowledgement must retain a history snapshot matching the live invoice.");
    }

    private static async Task<List<(InvoiceRevision Revision, InvoiceRevisionSnapshot Snapshot)>> ReadHistory(long id, Guid? issuer = null)
    {
        Guid owner = issuer ?? biller;
        Invoice invoice = (await services.GetRequiredService<IRepository<Invoice>>().RetrieveAsync(Invoice.BuildPartitionKey(owner), Invoice.BuildRowKey(id)))!;
        Assert.AreEqual(2, invoice.ReadLifecycle()!.Version);
        Guid? revisionID = invoice.ReadLifecycle()!.HistoryRevision;
        Assert.IsNotNull(revisionID);
        List<(InvoiceRevision, InvoiceRevisionSnapshot)> result = [];
        while (revisionID.HasValue)
        {
            Assert.IsLessThan(30, result.Count, "The owned history fixture must remain bounded.");
            StorageKey key = InvoiceRevision.Key(owner, revisionID.Value);
            InvoiceHistoryDomain domain = new(new(() => services.GetRequiredService<IRepository<InvoiceRevision>>()),
                new(() => services.GetRequiredService<IRepository<InvoiceRevisionPart>>()), []);
            domain.Initialize(key.PartitionKey, key.RowKey);
            InvoiceRevision revision = await domain.GetEntityAsync();
            Assert.IsTrue(revision.Describes(owner, invoice.Created));
            result.Add((revision, await domain.ReadSnapshotAsync()));
            revisionID = revision.Previous;
        }
        return result;
    }
}
