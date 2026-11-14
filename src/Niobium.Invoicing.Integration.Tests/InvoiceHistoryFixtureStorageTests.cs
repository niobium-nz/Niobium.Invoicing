using Azure;
using Azure.Data.Tables;
using Niobium.Invoicing.Testing;

namespace Niobium.Invoicing.Integration.Tests;

public sealed partial class StorageFlowTests
{
    [TestMethod]
    [DataRow("valid")]
    [DataRow("missing-revision")]
    [DataRow("stale-parent")]
    public async Task History_fixture_cleanup_preserves_ownership_and_rejects_missing_evidence_or_stale_parent(string mode)
    {
        Guid biller = Guid.NewGuid(), customer = Guid.NewGuid();
        long invoice = Invoice.ParseID(DateTimeOffset.UtcNow);
        TableClient invoices = tableService!.GetTableClient(prefix + nameof(Invoice));
        TableClient revisions = tableService.GetTableClient(prefix + InvoiceHistoryFixture.RevisionsTable);
        TableClient parts = tableService.GetTableClient(prefix + InvoiceHistoryFixture.PartsTable);
        Guid first = Guid.NewGuid(), second = Guid.NewGuid();
        TableEntity owned = new(Invoice.BuildPartitionKey(biller), Invoice.BuildRowKey(invoice))
        {
            [nameof(Invoice.Billee)] = customer,
            [nameof(Invoice.IssuanceState)] = "lifecycle-v1",
            ["LifecycleData"] = "{\"Version\":2,\"Preparation\":3,\"WriteID\":\"" + Guid.NewGuid().ToString("N")
                + "\",\"HistoryRevision\":\"" + first.ToString() + "\"}"
        };
        await invoices.AddEntityAsync(owned);
        owned = (await invoices.GetEntityAsync<TableEntity>(owned.PartitionKey, owned.RowKey)).Value;
        foreach ((Guid revision, long id) in new[] { (first, invoice), (second, invoice - 1000) })
        {
            if (mode != "missing-revision" || revision != first)
                await revisions.AddEntityAsync(new TableEntity(biller.ToString(), revision.ToString())
                {
                    ["Version"] = 1,
                    ["InvoiceCreated"] = Invoice.ParseID(id)
                });
            await parts.AddEntityAsync(new TableEntity(biller.ToString(), revision.ToString("N") + ".opaque")
            {
                ["Version"] = 1,
                ["InvoiceCreated"] = Invoice.ParseID(id),
                ["Revision"] = revision,
                ["Content"] = "synthetic-body-not-needed-for-cleanup"
            });
        }
        if (mode == "stale-parent")
            await invoices.UpdateEntityAsync(new TableEntity(owned.PartitionKey, owned.RowKey) { ["SyntheticConcurrentEdit"] = true },
                owned.ETag, TableUpdateMode.Merge);
        Func<Task> cleanup = () => InvoiceHistoryFixture.RemoveAsync(revisions, parts, owned, biller, invoice, customer,
            () => invoices.DeleteEntityAsync(owned.PartitionKey, owned.RowKey, owned.ETag));
        if (mode != "valid")
        {
            if (mode == "missing-revision")
                await Assert.ThrowsExactlyAsync<InvalidOperationException>(cleanup);
            else
            {
                RequestFailedException conflict = await Assert.ThrowsExactlyAsync<RequestFailedException>(cleanup);
                Assert.AreEqual(412, conflict.Status);
            }
            TableEntity retained = (await invoices.GetEntityAsync<TableEntity>(owned.PartitionKey, owned.RowKey)).Value;
            Assert.AreEqual(customer, retained.GetGuid(nameof(Invoice.Billee)));
            Assert.AreEqual(first, (await parts.GetEntityAsync<TableEntity>(biller.ToString(), first.ToString("N") + ".opaque")).Value.GetGuid("Revision"));
            Assert.AreEqual(second, (await parts.GetEntityAsync<TableEntity>(biller.ToString(), second.ToString("N") + ".opaque")).Value.GetGuid("Revision"));
            return;
        }
        await cleanup();
        foreach (TableClient table in new[] { revisions, parts })
        {
            List<TableEntity> remaining = [];
            await foreach (TableEntity row in table.QueryAsync<TableEntity>(
                TableClient.CreateQueryFilter($"PartitionKey eq {biller.ToString()}")))
                remaining.Add(row);
            Assert.HasCount(1, remaining);
            Assert.AreEqual(Invoice.ParseID(invoice - 1000), remaining[0].GetDateTimeOffset("InvoiceCreated"));
            Assert.AreNotEqual(ETag.All, remaining[0].ETag);
        }
    }
}
