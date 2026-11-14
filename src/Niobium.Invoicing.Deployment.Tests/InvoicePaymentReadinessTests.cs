using Azure.Data.Tables;
using Niobium.Invoicing.Testing;

namespace Niobium.Invoicing.Deployment.Tests;

public sealed partial class DeployedInvoiceTests
{
    [TestMethod]
    public async Task Payment_storage_tables_are_available_before_application_activation()
    {
        foreach (string name in new[] { InvoicePaymentFixture.PaymentsTable, InvoicePaymentFixture.PostingsTable, InvoicePaymentIndexFixture.Table })
        {
            // Probe only this run's synthetic biller partition. No receipt bodies,
            // production fixtures or new application routes are involved.
            string filter = TableClient.CreateQueryFilter($"PartitionKey eq {biller.ToString()}");
            await foreach (TableEntity _ in tables.GetTableClient(name).QueryAsync<TableEntity>(filter,
                maxPerPage: 1, select: ["PartitionKey", "RowKey"]))
                break;
        }
    }
}
