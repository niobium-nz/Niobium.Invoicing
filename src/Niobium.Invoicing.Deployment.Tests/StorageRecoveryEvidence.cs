using System.Text.Json;

namespace Niobium.Invoicing.Deployment.Tests;

public sealed partial class DeployedInvoiceTests
{
    private const string AzureManagement = "https://management.azure.com";

    private static async Task CaptureStorageRecoveryEvidence()
    {
        if (http == null || Environment.GetEnvironmentVariable("AZURE_ENV_NAME") != "test"
            || Environment.GetEnvironmentVariable("TEST_ENVIRONMENT") != "test")
            return;

        string? commit = null;
        object? storage = null;
        JsonElement? blobProtection = null;
        object? backupVaults = null;
        using CancellationTokenSource budget = new(TimeSpan.FromSeconds(75));
        List<string> unavailable = [];
        string stage = "test-storage-account";
        try
        {
            string account = Required("TEST_STORAGE_ACCOUNT");
            string group = Required("AZURE_RESOURCE_GROUP");
            string expectedCommit = Required("TEST_EXPECTED_COMMIT");
            if (expectedCommit.Length != 40 || !expectedCommit.All(Char.IsAsciiHexDigit)
                || account.Length is < 3 or > 24 || !account.EndsWith("test", StringComparison.Ordinal)
                || account.Any(c => !Char.IsAsciiLetterLower(c) && !Char.IsAsciiDigit(c))
                || group.Any(c => !Char.IsAsciiLetterOrDigit(c) && !"_-.()".Contains(c)))
                throw new InvalidOperationException("A valid test deployment and storage target are required.");
            commit = expectedCommit;
            await VerifiedTestFunctionApp(budget.Token);
            using JsonDocument accountResponse = JsonDocument.Parse(await ReadAzureValueAs("json", budget.Token, "resource", "show",
                "--resource-group", group, "--name", account, "--resource-type", "Microsoft.Storage/storageAccounts",
                "--api-version", "2023-05-01", "--query", "{id:id, sku:sku.name, kind:kind}"));
            JsonElement properties = accountResponse.RootElement;
            string accountId = properties.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("The test storage resource ID is required.");
            string? subscription = accountId.Split('/').ElementAtOrDefault(2);
            if (!Guid.TryParseExact(subscription, "D", out Guid subscriptionId)
                || !String.Equals(accountId, $"/subscriptions/{subscriptionId:D}/resourceGroups/{group}/providers/Microsoft.Storage/storageAccounts/{account}",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The inventory target must belong to the test resource group.");
            storage = new
            {
                sku = properties.GetProperty("sku").GetString(),
                kind = properties.GetProperty("kind").GetString(),
            };

            stage = "blob-protection";
            try
            {
                using JsonDocument blob = JsonDocument.Parse(await ReadAzureValueAs("json", budget.Token, "rest", "--method", "get",
                    "--url", $"{AzureManagement}{accountId}/blobServices/default?api-version=2023-05-01",
                    "--query", "{versioning:properties.isVersioningEnabled, changeFeed:properties.changeFeed.enabled, "
                        + "blobSoftDelete:properties.deleteRetentionPolicy.enabled, blobSoftDeleteDays:properties.deleteRetentionPolicy.days, "
                        + "containerSoftDelete:properties.containerDeleteRetentionPolicy.enabled, containerSoftDeleteDays:properties.containerDeleteRetentionPolicy.days, "
                        + "pointInTimeRestore:properties.restorePolicy.enabled, pointInTimeRestoreDays:properties.restorePolicy.days}"));
                blobProtection = blob.RootElement.Clone();
            }
            catch (Exception)
            {
                unavailable.Add(stage);
            }

            stage = "data-protection-backup-vaults";
            backupVaults = await ReadStorageBackupCoverage(group, subscriptionId, accountId, budget.Token);
        }
        catch (Exception)
        {
            // Credentials and source records never enter the evidence.
            unavailable.Add(stage);
        }

        string json = JsonSerializer.Serialize(new
        {
            schema = 1,
            environment = "test",
            commit,
            capturedAt = DateTimeOffset.UtcNow,
            storage,
            blobProtection,
            backupVaults,
            unavailable
        });
        Console.WriteLine($"Storage recovery evidence {json}");
        try
        {
            string directory = Path.Combine(Required("GITHUB_WORKSPACE"), "artifacts", "deployed");
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "test-storage-recovery.json"), json);
        }
        catch (Exception)
        {
            Console.WriteLine("The test storage recovery evidence artifact could not be written.");
        }
    }

    private static async Task<object> ReadStorageBackupCoverage(string group, Guid subscriptionId, string accountId,
        CancellationToken cancellationToken)
    {
        using JsonDocument vaultResponse = JsonDocument.Parse(await ReadAzureValueAs("json", cancellationToken, "resource", "list",
            "--resource-group", group, "--resource-type", "Microsoft.DataProtection/backupVaults", "--query", "[].id"));
        string prefix = $"/subscriptions/{subscriptionId:D}/resourceGroups/{group}/providers/Microsoft.DataProtection/backupVaults/";
        int vaultsRead = 0;
        int matchingBlobInstances = 0;
        int matchingTableInstances = 0;
        int matchingOtherInstances = 0;
        int protectionConfigured = 0;

        foreach (JsonElement vault in vaultResponse.RootElement.EnumerateArray())
        {
            if (cancellationToken.IsCancellationRequested)
                break;
            string? vaultId = vault.GetString();
            if (vaultId == null || !vaultId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || vaultId.Length == prefix.Length || vaultId[prefix.Length..].Any(c => !Char.IsAsciiLetterOrDigit(c) && c != '-'))
                continue;

            string path = vaultId + "/backupInstances";
            string? next = $"{AzureManagement}{path}?api-version=2024-04-01";
            HashSet<string> seen = new(StringComparer.Ordinal);
            while (next != null && !cancellationToken.IsCancellationRequested
                && Uri.TryCreate(next, UriKind.Absolute, out Uri? url)
                && url.Scheme == Uri.UriSchemeHttps && url.Host == "management.azure.com" && url.IsDefaultPort
                && url.UserInfo.Length == 0 && url.Fragment.Length == 0
                && url.AbsolutePath.Equals(path, StringComparison.OrdinalIgnoreCase) && seen.Add(next))
            {
                try
                {
                    using JsonDocument instances = JsonDocument.Parse(await ReadAzureValueAs("json", cancellationToken, "rest", "--method",
                        "get", "--url", next, "--query", "{items:value[].properties.{source:dataSourceInfo,container:dataSourceSetInfo,state:currentProtectionState},next:nextLink}"));
                    foreach (JsonElement item in instances.RootElement.GetProperty("items").EnumerateArray())
                    {
                        JsonElement source = item.GetProperty("source");
                        if (!IsAccountSource(source, accountId) && !IsAccountSource(item.GetProperty("container"), accountId))
                            continue;
                        if (item.GetProperty("state").GetString() == "ProtectionConfigured")
                            protectionConfigured++;
                        string? type = source.ValueKind == JsonValueKind.Object
                            && source.TryGetProperty("datasourceType", out JsonElement dataType) ? dataType.GetString() : null;
                        if (type?.Contains("blob", StringComparison.OrdinalIgnoreCase) == true)
                            matchingBlobInstances++;
                        else if (type?.Contains("table", StringComparison.OrdinalIgnoreCase) == true)
                            matchingTableInstances++;
                        else
                            matchingOtherInstances++;
                    }
                    next = instances.RootElement.GetProperty("next").GetString();
                }
                catch (Exception)
                {
                    break;
                }
            }
            if (next == null)
                vaultsRead++;
        }

        int visibleVaults = vaultResponse.RootElement.GetArrayLength();
        return new
        {
            scope = "data-protection-vaults-in-test-resource-group",
            complete = visibleVaults == vaultsRead,
            visibleVaults,
            vaultsRead,
            matchingBlobInstances,
            matchingTableInstances,
            matchingOtherInstances,
            protectionConfigured
        };
    }

    private static bool IsAccountSource(JsonElement source, string accountId)
    {
        if (source.ValueKind != JsonValueKind.Object || !source.TryGetProperty("resourceID", out JsonElement resource))
            return false;
        string? id = resource.GetString();
        return String.Equals(id, accountId, StringComparison.OrdinalIgnoreCase)
            || id?.StartsWith(accountId + "/", StringComparison.OrdinalIgnoreCase) == true;
    }
}
