using Azure;
using Azure.Data.Tables;
using Niobium.Invoicing.Testing;

namespace Niobium.Invoicing.Integration.Tests;

public sealed partial class StorageFlowTests
{
    [TestMethod]
    public async Task Raw_payment_fixture_cleanup_removes_only_the_owned_invoice_and_observed_children()
    {
        PaymentFixtureRows owned = new(), foreign = new();
        TableClient invoices = tableService!.GetTableClient(prefix + nameof(Invoice));
        TableClient receipts = tableService.GetTableClient(prefix + InvoicePaymentFixture.PaymentsTable);
        TableClient postings = tableService.GetTableClient(prefix + InvoicePaymentFixture.PostingsTable);
        TableClient indexes = tableService.GetTableClient(prefix + InvoicePaymentIndexFixture.Table);
        foreach (PaymentFixtureRows rows in new[] { owned, foreign })
        {
            await invoices.AddEntityAsync(rows.Owned);
            await receipts.AddEntityAsync(rows.Receipt);
            await indexes.AddEntityAsync(rows.Index);
            await postings.AddEntityAsync(rows.Posting);
        }
        TableEntity parent = (await invoices.GetEntityAsync<TableEntity>(owned.Owned.PartitionKey, owned.Owned.RowKey)).Value;
        await InvoicePaymentFixture.RemoveAsync(receipts, postings, indexes, parent, owned.Biller, owned.Invoice, owned.Customer,
            async () => await invoices.DeleteEntityAsync(parent.PartitionKey, parent.RowKey, parent.ETag));
        foreach ((TableClient table, TableEntity removed, TableEntity retained) in new[]
        {
            (invoices, owned.Owned, foreign.Owned), (receipts, owned.Receipt, foreign.Receipt),
            (postings, owned.Posting, foreign.Posting), (indexes, owned.Index, foreign.Index)
        })
        {
            NullableResponse<TableEntity> gone = await table.GetEntityIfExistsAsync<TableEntity>(removed.PartitionKey, removed.RowKey);
            Assert.IsFalse(gone.HasValue);
            NullableResponse<TableEntity> present = await table.GetEntityIfExistsAsync<TableEntity>(retained.PartitionKey, retained.RowKey);
            Assert.IsTrue(present.HasValue, "Cleanup cannot delete another invoice's evidence.");
        }
    }
}
