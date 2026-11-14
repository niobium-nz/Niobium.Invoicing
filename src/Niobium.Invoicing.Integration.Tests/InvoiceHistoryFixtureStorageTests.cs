using Azure;
using Azure.Data.Tables;
using Niobium.Invoicing.Testing;

namespace Niobium.Invoicing.Integration.Tests;

public sealed partial class StorageFlowTests
{
    [TestMethod]
    public async Task History_fixture_cleanup_uses_real_table_identity_and_retains_another_invoice_under_the_same_biller()
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
        await invoices.DeleteEntityAsync(owned.PartitionKey, owned.RowKey, owned.ETag);
        await InvoiceHistoryFixture.RemoveAsync(revisions, parts, owned, biller, invoice, customer);
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
