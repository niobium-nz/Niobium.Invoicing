using System.Globalization;
using System.Text.Json;
using Niobium.Invoicing.Testing;

namespace Niobium.Invoicing.Deployment.Tests;

public sealed partial class DeployedInvoiceTests
{
    private static DateTimeOffset diagnosticsStarted;

    // This runs only in the test-only deployed harness. It neither changes the test
    // verdict nor substitutes telemetry absence for application/hosting health.
    private static async Task CaptureHostDiagnostics()
    {
        DateTimeOffset ended = DateTimeOffset.UtcNow;
        string stage = "test-configuration";
        object report;
        int? observedMemoryMB = null;
        try
        {
            string app = await VerifiedTestFunctionApp();
            string version = Required("buildVersion");
            string query = DeployedHostDiagnostics.Query(diagnosticsStarted, ended, version);
            string commit = Required("TEST_EXPECTED_COMMIT");
            Assert.IsTrue(commit.Length == 40 && commit.All(Char.IsAsciiHexDigit), "An exact commit is required.");
            stage = "test-host-memory";
            string memory = await ReadAzureValue("resource", "show", "--resource-group", Required("AZURE_RESOURCE_GROUP"),
                "--name", app, "--resource-type", "Microsoft.Web/sites", "--api-version", "2024-11-01",
                "--query", "properties.functionAppConfig.scaleAndConcurrency.instanceMemoryMB");
            bool parsedMemory = int.TryParse(memory, NumberStyles.None, CultureInfo.InvariantCulture, out int memoryMB);
            Assert.IsTrue(parsedMemory && memoryMB > 0, "The configured test instance memory is required.");
            observedMemoryMB = memoryMB;
            stage = "test-telemetry-destination";
            Guid insights = await ReadTestTelemetryApp(app);
            stage = "test-telemetry-query";
            string url = $"https://api.applicationinsights.io/v1/apps/{insights:D}/query?query={Uri.EscapeDataString(query)}";
            string aggregates = await ReadAzureValue("rest", "--method", "get", "--resource", "https://api.applicationinsights.io",
                "--url", url, "--query", "tables[0].rows");
            stage = "aggregate-validation";
            IReadOnlyList<DeployedHostDiagnostics.Counts> counts = DeployedHostDiagnostics.Parse(aggregates);
            report = new
            {
                success = true, commit, version, started = diagnosticsStarted, ended, instanceMemoryMB = memoryMB, counts,
                limitation = "Sampled telemetry may be delayed or absent. Other-version/host events and the preceding window do not prove a cause in this deployment."
            };
        }
        catch (Exception)
        {
            // CLI responses, exception messages and telemetry can contain credentials
            // or customer data. Record a fixed stage only, never the raw failure.
            report = new { success = false, started = diagnosticsStarted, ended, instanceMemoryMB = observedMemoryMB, failedStage = stage };
        }
        try
        {
            string directory = Path.Combine(Required("GITHUB_WORKSPACE"), "artifacts", "deployed");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "test-host-diagnostics.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            Console.WriteLine("Test host diagnostic report could not be written; no telemetry evidence is available.");
        }
    }
}
