using System.Net;
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
