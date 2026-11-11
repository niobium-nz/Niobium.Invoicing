using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Azure.Messaging.ServiceBus;

namespace Niobium.Invoicing.Deployment.Tests;

public sealed partial class DeployedInvoiceTests
{
    [TestMethod]
    public async Task Deployed_queue_measurements_reach_the_current_test_Application_Insights()
    {
        string app = await VerifiedTestFunctionApp();
        Guid insights = await ReadTestTelemetryApp(app);
        string build = Required("buildVersion");
        bool validVersion = Version.TryParse(build, out _);
        Assert.IsTrue(validVersion && build.Split('.').Length == 3, "The workflow build version is required.");
        string version = build + "-prerelease";
        string configuredVersion = await ReadAzureValue("functionapp", "config", "appsettings", "list",
            "--resource-group", Required("AZURE_RESOURCE_GROUP"), "--name", app,
            "--query", "[?name=='SERVICE_VERSION'].value | [0]");
        Assert.IsTrue(configuredVersion == version, "Telemetry must identify the current workflow deployment.");

        long id = await SeedInvoice();
        IssueInvoiceRequest request = Request(id);
        IssueInvoiceCommand command = new()
        {
            Tenant = tenant,
            BillerID = biller,
            BilleeID = customer,
            InvoiceID = id,
            Billee = Customer(),
            InvoiceItems = request.InvoiceItems,
            Terms = request.Terms,
            NotifyBillee = false
        };
        DateTimeOffset started = DateTimeOffset.UtcNow;
        await using ServiceBusSender sender = bus.CreateSender("issueinvoicecommand");
        await sender.SendMessageAsync(new ServiceBusMessage(JsonMarshaller.Marshall(command))
        {
            MessageId = Guid.NewGuid().ToString(),
            ContentType = "application/json"
        });
        await Eventually(async () => (await Read(id)).GetInt64(nameof(Invoice.GrandTotalCents)) == 11500L,
            "The telemetry fixture was not processed by the deployed queue consumer.");

        // Azure Monitor exports aggregated metrics at the end of each collection interval.
        // Match the run-specific service version and this test window, never an older deployment.
        // Return only a count of verified instruments, not resource/customer/telemetry dimensions.
        string query = $$"""
            customMetrics
            | where timestamp >= datetime({{started:O}})
            | where application_Version == {{JsonSerializer.Serialize(version)}}
                and cloud_RoleName == "Niobium.Invoicing.Server.Test"
            | where name in ("niobium.invoicing.queue.age", "niobium.invoicing.queue.processing.duration", "niobium.invoicing.queue.processed")
            | extend outcome = tostring(customDimensions.outcome), total = coalesce(valueSum, value), samples = coalesce(valueCount, 1)
            | where samples > 0 and total >= 0
            | where (name == "niobium.invoicing.queue.age" and isempty(outcome))
                or (name == "niobium.invoicing.queue.processing.duration" and outcome == "succeeded")
                or (name == "niobium.invoicing.queue.processed" and outcome == "succeeded" and total >= 1)
            | summarize by name
            | count
            """;
        string url = $"https://api.applicationinsights.io/v1/apps/{insights:D}/query?query={Uri.EscapeDataString(query)}";
        Stopwatch ingestion = Stopwatch.StartNew();
        while (ingestion.Elapsed < TimeSpan.FromMinutes(6))
        {
            string observed = await ReadAzureValue("rest", "--method", "get", "--resource", "https://api.applicationinsights.io",
                "--url", url, "--query", "tables[0].rows[0][0]");
            bool parsed = int.TryParse(observed, out int count);
            Assert.IsTrue(parsed && count is >= 0 and <= 3, "The test metrics query must return an instrument count.");
            if (count == 3)
                return;
            await Task.Delay(TimeSpan.FromSeconds(10));
        }
        Assert.Fail("The current test deployment did not export all three queue instruments within six minutes.");
    }

    private static async Task<Guid> ReadTestTelemetryApp(string app)
    {
        string group = Required("AZURE_RESOURCE_GROUP");
        string resources = await ReadAzureValue("resource", "list", "--resource-group", group,
            "--resource-type", "Microsoft.Insights/components", "--query", "[].id");
        string[] ids = resources.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.IsTrue(ids.Length == 1 && ids[0].EndsWith("test", StringComparison.OrdinalIgnoreCase),
            "Exactly one existing test Application Insights component is required.");
        string id = ids[0];
        string appId = await ReadAzureValue("resource", "show", "--ids", id, "--api-version", "2020-02-02", "--query", "properties.AppId");
        bool validApp = Guid.TryParse(appId, out Guid insights);
        Assert.IsTrue(validApp && insights != Guid.Empty, "The existing test telemetry application ID is required.");

        // Match the configured exporter destination to the selected component. Capture settings
        // privately through the existing workflow identity; never print configuration or CLI output.
        string connection = await ReadAzureValue("functionapp", "config", "appsettings", "list", "--resource-group", group,
            "--name", app, "--query", "[?name=='APPLICATIONINSIGHTS_CONNECTION_STRING'].value | [0]");
        DbConnectionStringBuilder settings = new() { ConnectionString = connection };
        bool hasKey = settings.TryGetValue("InstrumentationKey", out object? configuredKey);
        Assert.IsTrue(hasKey, "The test Functions exporter must have an Application Insights destination.");
        string componentKey = await ReadAzureValue("resource", "show", "--ids", id, "--api-version", "2020-02-02",
            "--query", "properties.InstrumentationKey");
        bool validKey = Guid.TryParse(componentKey, out Guid expectedKey);
        bool configured = Guid.TryParse(configuredKey?.ToString(), out Guid actualKey);
        Assert.IsTrue(validKey && configured && expectedKey != Guid.Empty && expectedKey == actualKey,
            "The selected test telemetry component must match the verified Functions app's exporter.");
        return insights;
    }
}
