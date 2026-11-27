using System.Text.Json;
using Azure;
using Azure.Data.Tables;

namespace Niobium.Invoicing.Deployment.Tests;

public sealed partial class DeployedInvoiceTests
{
    private static async Task<string> DescribeReceiptBoundaryAsync(Guid sender, long id)
    {
        // All probes use the existing test workflow identity and exact fixture keys.
        // Reports contain only HTTP status codes, counts and fixed diagnostic values.
        Assert.AreEqual("test", Required("TEST_ENVIRONMENT"));
        Assert.AreEqual("test", Required("AZURE_ENV_NAME"));
        string app = http.BaseAddress!.Host.Split('.')[0];
        string owner = Invoice.BuildPartitionKey(sender);
        string invoice = Invoice.BuildRowKey(id);
        string operation = Invoice.BuildFullID(sender, id);

        Task<string> binding = DescribeBindingAsync(app);
        Task<string> projection = DescribeProjectionAsync(owner, invoice);
        Task<string> template = DescribeNotificationRowsAsync("Template",
            TableClient.CreateQueryFilter($"PartitionKey eq {owner} and RowKey eq {"Invoice"}"), "RowKey");
        Task<string> submission = DescribeSubmissionAsync(owner, operation);
        await Task.WhenAll(binding, projection, template, submission);
        return $"Binding {binding.Result}; projection {projection.Result}; Notification template {template.Result}; submission {submission.Result}.";
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

    private static async Task<string> DescribeSubmissionAsync(string owner, string operation)
    {
        try
        {
            string filter = TableClient.CreateQueryFilter($"PartitionKey eq {owner} and Operation eq {operation}");
            (JsonElement rows, string reason) = await ReadNotificationRowsAsync("NotificationSubmission", filter,
                "RowKey,StateCode,OutcomeCode,ProviderID");
            if (rows.ValueKind != JsonValueKind.Array)
                return reason;
            if (rows.GetArrayLength() != 1)
                return $"matches-{rows.GetArrayLength()}";
            JsonElement row = rows[0];
            int state = row.GetProperty("StateCode").GetInt32();
            int outcome = row.GetProperty("OutcomeCode").GetInt32();
            bool accepted = row.TryGetProperty("ProviderID", out JsonElement provider)
                && provider.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(provider.GetString());
            string description = $"state-{state}-outcome-{outcome}-provider-evidence-{accepted}";
            if (!accepted)
                return description;

            // Use the stored opaque reference and the same operation's tenant.
            // Preserve only bounded numeric evidence for comparison with Notification's model.
            string submission = row.GetProperty("RowKey").GetString()!;
            string receiptFilter = TableClient.CreateQueryFilter($"PartitionKey eq {"resend"} and Tenant eq {owner} and SubmissionID eq {submission}");
            (JsonElement receipts, string receiptReason) = await ReadNotificationRowsAsync("NotificationReceipt", receiptFilter,
                "KindCode,PublicationCode", 8);
            if (receipts.ValueKind != JsonValueKind.Array)
                return description + " receipt-" + receiptReason;
            string codes = string.Join(",", receipts.EnumerateArray().Select(receipt =>
                $"{receipt.GetProperty("KindCode").GetInt32()}/{receipt.GetProperty("PublicationCode").GetInt32()}"));
            return $"{description} receipt-kind/publication-codes [{codes}]";
        }
        catch (Exception)
        {
            return "probe-unavailable";
        }
    }

    private static async Task<string> DescribeNotificationRowsAsync(string table, string filter, string columns)
    {
        try
        {
            (JsonElement rows, string reason) = await ReadNotificationRowsAsync(table, filter, columns);
            return rows.ValueKind == JsonValueKind.Array ? $"matches-{rows.GetArrayLength()}" : reason;
        }
        catch (Exception)
        {
            return "probe-unavailable";
        }
    }

    private static async Task<(JsonElement Rows, string Reason)> ReadNotificationRowsAsync(string table, string filter,
        string columns, int limit = 2)
    {
        string url = $"https://niobiumnotifystdtest.table.core.windows.net/{table}()?$filter={Uri.EscapeDataString(filter)}&$select={columns}&$top={limit}";
        (int code, string value, string reason) = await QueryAzureAsync("json", "rest", "--method", "get",
            "--resource", "https://storage.azure.com/", "--url", url,
            "--headers", "Accept=application/json;odata=nometadata", "x-ms-version=2019-02-02");
        if (code != 0)
            return (default, reason);
        using JsonDocument response = JsonDocument.Parse(value);
        return (response.RootElement.GetProperty("value").Clone(), String.Empty);
    }
}
