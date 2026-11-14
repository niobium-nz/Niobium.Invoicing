using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using Niobium.Invoicing.Flows;
using Niobium.Invoicing.History;
using Niobium.Invoicing.Testing;

namespace Niobium.Invoicing.Integration.Tests;

public sealed partial class StorageFlowTests
{
    [TestMethod]
    public async Task Large_history_snapshots_round_trip_across_the_frameworks_Table_batch_boundary()
    {
        IssueInvoiceRequest request = await NewRequest();
        request.InvoiceItems = Enumerable.Range(1, 110).Select(index => new InvoiceItem
        {
            Invoice = Invoice.ParseID(request.InvoiceID),
            ID = index,
            Subject = "Synthetic large history line",
            Description = new String('x', 20000),
            UnitPriceCurrency = "NZD",
            LineTotalCurrency = "NZD",
            UnitPriceCents = 100,
            Quantity = 1
        }).ToList();
        await host!.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, default);
        IRepository<Invoice> invoices = host.Services.GetRequiredService<IRepository<Invoice>>();
        Invoice invoice = (await invoices.RetrieveAsync(Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID)))!;
        var entries = await History(invoice);
        Assert.AreEqual(2, entries.Count);
        Assert.IsTrue(entries.All(entry => entry.Revision.PartCount > 100),
            "Each snapshot must cross the pinned adapter's 100-operation transaction boundary.");
        Assert.AreEqual(InvoiceLineEvidence.Verified, entries[0].Snapshot.LineEvidence);
        Assert.AreEqual(110, entries[0].Snapshot.Lines.Count);
        Assert.AreEqual(12650, entries[0].Snapshot.Invoice.GrandTotalCents);
        Assert.IsTrue(entries[0].Snapshot.Lines.All(line => line.Description == new String('x', 20000)));
        await host.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, default);
        Invoice replay = (await invoices.RetrieveAsync(Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID)))!;
        Assert.AreEqual(invoice.ETag, replay.ETag);
        Assert.AreEqual(entries[0].Revision.ID, replay.ReadLifecycle()!.HistoryRevision);
    }

    [TestMethod]
    public async Task Real_history_preserves_earlier_lines_and_private_model_keys_across_edits_and_restarts()
    {
        IssueInvoiceRequest request = await NewRequest();
        request.InvoiceItems[0].Description = new String('x', 20000);
        InvoiceChangeOrigin origin = new(InvoiceChangeAuthority.AuthenticatedBiller, request.BillerID.ToString());
        await host!.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, origin, default);
        IRepository<Invoice> invoices = host.Services.GetRequiredService<IRepository<Invoice>>();
        Invoice first = (await invoices.RetrieveAsync(Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID)))!;
        Guid prior = first.ReadLifecycle()!.HistoryRevision!.Value;
        request.InvoiceItems[0].Quantity = 2;
        await host.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, origin, default);
        Invoice current = (await invoices.RetrieveAsync(Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID)))!;
        var entries = await History(current);
        Assert.AreEqual(4, entries.Count);
        var earlier = entries.Single(entry => entry.Revision.ID == prior);
        Assert.AreEqual(1, earlier.Snapshot.Lines[0].Quantity);
        Assert.AreEqual(11500, earlier.Snapshot.Invoice.GrandTotalCents);
        Assert.AreEqual(2, entries[0].Snapshot.Lines[0].Quantity);
        Assert.AreEqual(23000, entries[0].Snapshot.Invoice.GrandTotalCents);
        Assert.IsTrue(entries.All(entry => entry.Revision.ReadChange().Authority == origin.Authority));
        InvoiceRevision head = entries[0].Revision;
        Assert.IsGreaterThan(1, head.PartCount);
        TableEntity raw = (await tableService!.GetTableClient(prefix + nameof(InvoiceRevision)).GetEntityAsync<TableEntity>(head.GetKey().PartitionKey, head.GetKey().RowKey)).Value;
        Assert.IsFalse(raw.ContainsKey(nameof(InvoiceRevision.Biller)));
        Assert.IsFalse(raw.ContainsKey(nameof(InvoiceRevision.ID)));
        Assert.AreEqual(request.BillerID.ToString(), raw.PartitionKey);
        Assert.AreEqual(head.ID.ToString(), raw.RowKey);
        Assert.AreEqual(head.InvoiceCreated, raw.GetDateTimeOffset(nameof(InvoiceRevision.InvoiceCreated)));
        string before = current.ETag!;
        await host.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, origin, default);
        Invoice replay = (await invoices.RetrieveAsync(Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID)))!;
        Assert.AreEqual(before, replay.ETag);
        Assert.AreEqual(head.ID, replay.ReadLifecycle()!.HistoryRevision);
    }

    [TestMethod]
    public async Task An_immutable_history_part_conflict_or_corruption_never_silently_rewrites_evidence()
    {
        IssueInvoiceRequest request = await NewRequest();
        await host!.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, default);
        Invoice invoice = (await host.Services.GetRequiredService<IRepository<Invoice>>().RetrieveAsync(Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID)))!;
        var entries = await History(invoice);
        InvoiceRevision revision = entries[0].Revision;
        IRepository<InvoiceRevisionPart> repository = host.Services.GetRequiredService<IRepository<InvoiceRevisionPart>>();
        StorageKey key = revision.GetPartKey(0);
        InvoiceRevisionPart part = (await repository.RetrieveAsync(key.PartitionKey, key.RowKey))!;
        RequestFailedException conflict = await Assert.ThrowsExactlyAsync<RequestFailedException>(() => repository.CreateAsync([part]));
        Assert.AreEqual(409, conflict.Status);
        TableClient table = tableService!.GetTableClient(prefix + nameof(InvoiceRevisionPart));
        TableEntity raw = (await table.GetEntityAsync<TableEntity>(key.PartitionKey, key.RowKey)).Value;
        Assert.AreEqual(invoice.Created, raw.GetDateTimeOffset(nameof(InvoiceRevisionPart.InvoiceCreated)));
        raw[nameof(InvoiceRevisionPart.Content)] = "eA==";
        await table.UpdateEntityAsync(raw, raw.ETag, TableUpdateMode.Replace);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => History(invoice));
        Invoice after = (await host.Services.GetRequiredService<IRepository<Invoice>>().RetrieveAsync(Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID)))!;
        Assert.AreEqual(invoice.ETag, after.ETag);
    }

    [TestMethod]
    public async Task Real_fixture_cleanup_removes_only_the_deleted_invoice_history_and_keeps_another_invoice_under_the_same_biller()
    {
        IssueInvoiceRequest request = await NewRequest();
        await host!.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, default);
        IRepository<Invoice> invoices = host.Services.GetRequiredService<IRepository<Invoice>>();
        Invoice first = (await invoices.RetrieveAsync(Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID)))!;
        TableClient invoiceTable = tableService!.GetTableClient(prefix + nameof(Invoice));
        TableEntity owned = (await invoiceTable.GetEntityAsync<TableEntity>(Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID))).Value;
        long otherID = request.InvoiceID - 1000;
        request.InvoiceID = otherID;
        request.InvoiceItems[0].Invoice = Invoice.ParseID(otherID);
        await host.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, default);
        Invoice other = (await invoices.RetrieveAsync(Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(otherID)))!;
        await InvoiceHistoryFixture.RemoveAsync(tableService.GetTableClient(prefix + nameof(InvoiceRevision)),
            tableService.GetTableClient(prefix + nameof(InvoiceRevisionPart)), owned, first.Biller, first.GetID(), request.BilleeID,
            () => invoiceTable.DeleteEntityAsync(owned.PartitionKey, owned.RowKey, owned.ETag));
        Assert.AreEqual(2, (await History(other)).Count);
        foreach (string name in new[] { nameof(InvoiceRevision), nameof(InvoiceRevisionPart) })
        {
            await foreach (TableEntity row in tableService.GetTableClient(prefix + name).QueryAsync<TableEntity>(
                TableClient.CreateQueryFilter($"PartitionKey eq {first.Biller.ToString()}")))
                Assert.AreEqual(other.Created, row.GetDateTimeOffset("InvoiceCreated"));
        }
    }

    private static async Task<List<(InvoiceRevision Revision, InvoiceRevisionSnapshot Snapshot)>> History(Invoice invoice)
    {
        var domains = host!.Services.GetRequiredService<IDomainRepository<InvoiceHistoryDomain, InvoiceRevision>>();
        List<(InvoiceRevision, InvoiceRevisionSnapshot)> result = [];
        Guid? id = invoice.ReadLifecycle()?.HistoryRevision;
        while (id.HasValue)
        {
            Assert.IsLessThan(30, result.Count, "Owned synthetic history must remain bounded.");
            StorageKey key = InvoiceRevision.Key(invoice.Biller, id.Value);
            InvoiceHistoryDomain domain = await domains.GetAsync(key.PartitionKey, key.RowKey, forceLoad: true);
            InvoiceRevision revision = await domain.GetEntityAsync();
            Assert.IsTrue(revision.Describes(invoice.Biller, invoice.Created));
            result.Add((revision, await domain.ReadSnapshotAsync()));
            id = revision.Previous;
        }
        return result;
    }
}
