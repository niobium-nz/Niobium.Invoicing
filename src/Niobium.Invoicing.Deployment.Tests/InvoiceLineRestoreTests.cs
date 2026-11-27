using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;

namespace Niobium.Invoicing.Deployment.Tests;

public sealed partial class DeployedInvoiceTests
{
    [TestMethod]
    public async Task Deployed_owned_invoice_line_is_restored_from_a_captured_copy()
    {
        await VerifiedTestFunctionApp();
        string commit = verifiedTestCommit ?? throw new InvalidOperationException("A verified test deployment is required.");
        long id = await SeedInvoice(recipient: String.Empty);
        IssueInvoiceRequest request = Request(id);
        request.ChangeReason = "Synthetic line restore drill";
        request.InvoiceItems[0].Description = "Synthetic line retained in the restore copy";
        using (HttpResponseMessage response = await Post(request, Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        TableEntity owner = await Read(id);
        Assert.IsTrue(owner.GetGuid(nameof(Invoice.Billee)) == customer,
            "The restore fixture must belong to this test.");
        Guid[] revisions = (await ReadHistory(id)).Select(entry => entry.Revision.ID).ToArray();
        Assert.IsTrue(revisions.Length > 0, "The test invoice must have committed history.");

        string partition = InvoiceItem.BuildPartitionKey(id);
        string row = InvoiceItem.BuildRowKey(1);
        TableClient table = tables.GetTableClient(nameof(InvoiceItem));
        TableEntity observed = (await table.GetEntityAsync<TableEntity>(partition, row)).Value;
        Assert.IsTrue(observed.GetString(nameof(InvoiceItem.Subject)) == itemSubject,
            "The line must belong to this test.");
        Assert.IsTrue(observed.ETag != ETag.All && !String.IsNullOrWhiteSpace(observed.ETag.ToString()),
            "The drill requires an observed conditional version.");

        IRepository<InvoiceItem> lines = services.GetRequiredService<IRepository<InvoiceItem>>();
        InvoiceItem captured = await lines.RetrieveAsync(partition, row)
            ?? throw new InvalidOperationException("The owned line could not be captured.");
        Assert.IsTrue(observed.ETag.ToString() == captured.ETag, "The copy must match the observed version.");
        Assert.IsTrue(captured.GetInvoiceID() == id && captured.ID == 1,
            "The captured line must have this test's invoice and line identity.");
        captured.UpdatedAt = null;
        captured.ETag = null;
        byte[] backup = JsonSerializer.SerializeToUtf8Bytes(captured);
        InvoiceItem restore = JsonSerializer.Deserialize<InvoiceItem>(backup)
            ?? throw new InvalidOperationException("The owned line copy could not be read.");

        bool removed = false;
        Stopwatch restoreTime = new();
        try
        {
            await table.DeleteEntityAsync(observed.PartitionKey, observed.RowKey, observed.ETag);
            removed = true;
            NullableResponse<TableEntity> missing = await table.GetEntityIfExistsAsync<TableEntity>(partition, row);
            Assert.IsFalse(missing.HasValue, "The drill must observe the missing line before restoration.");
        }
        finally
        {
            if (removed)
            {
                restoreTime.Start();
                await lines.CreateAsync([restore], replaceIfExist: false);
            }
        }

        InvoiceItem recovered = await lines.RetrieveAsync(partition, row)
            ?? throw new InvalidOperationException("The restored line could not be read.");
        restoreTime.Stop();
        Assert.IsFalse(String.IsNullOrWhiteSpace(recovered.ETag), "Recovered data must have a storage version.");
        recovered.UpdatedAt = null;
        recovered.ETag = null;
        Assert.IsTrue(backup.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(recovered)),
            "The stored line must match the captured copy.");
        Assert.IsTrue(owner.ETag == (await Read(id)).ETag, "The owning invoice must retain its version.");
        Assert.IsTrue(revisions.SequenceEqual((await ReadHistory(id)).Select(entry => entry.Revision.ID)),
            "The normal history reader must resolve the original committed chain.");

        string directory = Path.Combine(Required("GITHUB_WORKSPACE"), "artifacts", "deployed");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "test-invoice-line-restore.json"),
            JsonSerializer.Serialize(new
            {
                schema = 1,
                environment = "test",
                commit,
                completed = true,
                restoredLineCount = 1,
                insertAndReadBackMilliseconds = restoreTime.Elapsed.TotalMilliseconds,
            }));
    }
}
