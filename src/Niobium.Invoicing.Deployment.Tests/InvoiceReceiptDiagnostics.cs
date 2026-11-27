using Azure;
using Azure.Data.Tables;

namespace Niobium.Invoicing.Deployment.Tests;

public sealed partial class DeployedInvoiceTests
{
    private static async Task<string> DescribeReceiptBoundaryAsync(Guid sender, long id)
    {
        // Use the indexed test app and the exact fixture's own invoice table.
        // Reports contain only HTTP status codes, counts and fixed diagnostic values.
        Assert.AreEqual("test", Required("TEST_ENVIRONMENT"));
        Assert.AreEqual("test", Required("AZURE_ENV_NAME"));
        string app = http.BaseAddress!.Host.Split('.')[0];
        string owner = Invoice.BuildPartitionKey(sender);
        string invoice = Invoice.BuildRowKey(id);
        Task<string> binding = DescribeBindingAsync(app);
        Task<string> projection = DescribeProjectionAsync(owner, invoice);
        await Task.WhenAll(binding, projection);
        return $"Binding {binding.Result}; projection {projection.Result}.";
    }

    private static async Task<string> DescribeBindingAsync(string app)
    {
        try
        {
            (int code, string value, string reason) = await QueryAzureAsync("tsv", "functionapp", "function", "show",
                "--resource-group", Required("AZURE_RESOURCE_GROUP"), "--name", app,
                "--function-name", "InvoiceNotificationDeliveryConsumer",
                "--query", "length(config.bindings[?type=='serviceBusTrigger' && queueName=='notificationdeliveryevent-invoicing' && connection=='NotificationQueueOptions'])");
            return code == 0 ? value == "1" ? "present" : "missing" : reason;
        }
        catch (Exception)
        {
            return "probe-unavailable";
        }
    }

    private static async Task<string> DescribeProjectionAsync(string owner, string invoice)
    {
        try
        {
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(20));
            NullableResponse<TableEntity> row = await tables.GetTableClient(NotificationDeliveryTable)
                .GetEntityIfExistsAsync<TableEntity>(owner, invoice, ["DeliveredAt"], deadline.Token);
            return !row.HasValue ? "missing" : row.Value.GetDateTimeOffset("DeliveredAt").HasValue ? "confirmed" : "unconfirmed";
        }
        catch (RequestFailedException e)
        {
            return $"read-http-{e.Status}";
        }
        catch (OperationCanceledException)
        {
            return "probe-timeout";
        }
    }
}
