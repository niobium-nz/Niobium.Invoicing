using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;

namespace Niobium.Invoicing.Deployment.Tests;

public sealed partial class DeployedInvoiceTests
{
    [TestMethod]
    [DataRow("missing", 401)]
    [DataRow("invalid", 401)]
    [DataRow("other-biller", 403)]
    [DataRow("no-partition", 403)]
    [DataRow("missing-invoice", 403)]
    public async Task Deployed_item_resource_token_rejects_unauthorized_or_unbounded_access(string mode, int status)
    {
        long id = await SeedInvoice();
        string? partition = mode == "no-partition" ? null : mode == "missing-invoice"
            ? "missing-" + Guid.NewGuid().ToString("N") : InvoiceItem.BuildPartitionKey(id);
        string? token = mode == "missing" ? null : mode == "invalid" ? "invalid-test-token"
            : Token(mode == "other-biller" ? Guid.NewGuid() : biller, tenant);
        using HttpResponseMessage response = await ItemResourceToken(partition, token);
        Assert.AreEqual(status, (int)response.StatusCode);
    }

    [TestMethod]
    public async Task Deployed_item_SAS_reads_only_the_owned_partition_and_rejects_writes()
    {
        long id = await SeedInvoice();
        long otherID = await SeedInvoice();
        foreach (long invoiceID in new[] { id, otherID })
        {
            using HttpResponseMessage issued = await Post(Request(invoiceID), Token(biller, tenant));
            Assert.AreEqual(HttpStatusCode.OK, issued.StatusCode);
        }
        string partition = InvoiceItem.BuildPartitionKey(id);
        string otherPartition = InvoiceItem.BuildPartitionKey(otherID);
        string row = InvoiceItem.BuildRowKey(1);
        TableClient privileged = tables.GetTableClient(nameof(InvoiceItem));
        TableEntity before = (await privileged.GetEntityAsync<TableEntity>(partition, row)).Value;
        _ = await privileged.GetEntityAsync<TableEntity>(otherPartition, row);

        using HttpResponseMessage response = await ItemResourceToken(partition, Token(biller, tenant));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        StorageSignature? signature;
        try
        {
            signature = JsonSerializer.Deserialize<StorageSignature>(await response.Content.ReadAsStringAsync(), JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            Assert.Fail("The resource-token response was not valid credential JSON; its content is intentionally omitted.");
            return;
        }
        Assert.IsNotNull(signature);
        Assert.IsFalse(string.IsNullOrWhiteSpace(signature.Signature), "A signed resource credential is required.");
        Assert.AreEqual(nameof(InvoiceItem), signature.Control.Resource);
        Assert.AreEqual((int)DatabasePermissions.Query, signature.Control.Permission);
        Assert.AreEqual(partition, signature.Control.StartPartitionKey);
        Assert.AreEqual(partition, signature.Control.EndPartitionKey);
        Assert.IsTrue(signature.Expiry > DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        TableClientOptions options = new();
        options.Retry.MaxRetries = 0;
        TableClient scoped = new(new Uri(tables.Uri, nameof(InvoiceItem)), new AzureSasCredential(signature.Signature), options);
        (int readStatus, TableEntity? item) = await SasRead(scoped, partition, row);
        Assert.AreEqual(200, readStatus, "The signed credential must read its owned item from real test storage.");
        Assert.IsNotNull(item);
        Assert.AreEqual(itemSubject, item.GetString(nameof(InvoiceItem.Subject)));
        Assert.AreEqual(403, (await SasRead(scoped, otherPartition, row)).Status,
            "The credential must not read another existing invoice partition.");

        // Any unexpected successful insert is still this run's marked data and is removed by existing cleanup.
        TableEntity attempted = new(partition, "readonly-probe-" + Guid.NewGuid().ToString("N"))
        {
            [nameof(InvoiceItem.Subject)] = itemSubject
        };
        Assert.AreEqual(403, await SasInsert(scoped, attempted), "A query-only item credential must reject writes.");
        Assert.AreEqual(before.ETag, (await privileged.GetEntityAsync<TableEntity>(partition, row)).Value.ETag);
    }

    private static async Task<HttpResponseMessage> ItemResourceToken(string? partition, string? token)
    {
        string path = $"/{Niobium.Identity.Constants.DefaultResourceTokenEndpoint}?type={(int)ResourceType.AzureStorageTable}&resource={nameof(InvoiceItem)}";
        if (partition != null)
            path += "&partition=" + Uri.EscapeDataString(partition);
        using HttpRequestMessage request = new(HttpMethod.Get, path);
        if (token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await http.SendAsync(request);
    }

    private static async Task<(int Status, TableEntity? Entity)> SasRead(TableClient client, string partition, string row)
    {
        try
        {
            Response<TableEntity> response = await client.GetEntityAsync<TableEntity>(partition, row);
            return (response.GetRawResponse().Status, response.Value);
        }
        catch (RequestFailedException error) { return (error.Status, null); }
        catch (Exception)
        {
            Assert.Fail("The SAS read failed before a storage status was available; credential details are omitted.");
            return (0, null);
        }
    }

    private static async Task<int> SasInsert(TableClient client, TableEntity entity)
    {
        try { return (await client.AddEntityAsync(entity)).Status; }
        catch (RequestFailedException error) { return error.Status; }
        catch (Exception)
        {
            Assert.Fail("The SAS write probe failed before a storage status was available; credential details are omitted.");
            return 0;
        }
    }
}
