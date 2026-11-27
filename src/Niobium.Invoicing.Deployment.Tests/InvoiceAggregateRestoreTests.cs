using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Niobium.Invoicing.History;

namespace Niobium.Invoicing.Deployment.Tests;

public sealed partial class DeployedInvoiceTests
{
    [TestMethod]
    public async Task Deployed_unpaid_invoice_and_committed_history_restore_from_a_typed_file()
    {
        await VerifiedTestFunctionApp();
        string commit = verifiedTestCommit ?? throw new InvalidOperationException("A verified test deployment is required.");
        long id = await SeedInvoice(recipient: String.Empty);
        IssueInvoiceRequest request = Request(id);
        request.InvoiceItems[0].Description = new String('x', 20000);
        request.ChangeReason = "Synthetic initial backup revision";
        using (HttpResponseMessage created = await Post(request, Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.OK, created.StatusCode);
        request.InvoiceItems[0].Quantity = 2;
        request.ChangeReason = "Synthetic backup quantity edit";
        using (HttpResponseMessage updated = await Post(request, Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.OK, updated.StatusCode);

        TableEntity owner = await Read(id);
        Assert.IsTrue(owner.GetGuid(nameof(Invoice.Billee)) == customer
            && !owner.ContainsKey(nameof(Invoice.Token)) && !owner.ContainsKey(nameof(Invoice.Delivered)),
            "The drill requires this test's own unsent invoice.");
        InvoiceRevision[] revisions = (await ReadHistory(id)).Select(entry => entry.Revision).ToArray();
        Assert.IsTrue(revisions.Length == 5 && revisions.Any(revision => revision.PartCount > 1),
            "The fixture must have its committed edits and multipart history.");

        List<CapturedRestoreRow> captured = [];
        Capture(tables.GetTableClient(nameof(Invoice)), owner);
        TableClient items = tables.GetTableClient(nameof(InvoiceItem));
        TableEntity line = (await items.GetEntityAsync<TableEntity>(InvoiceItem.BuildPartitionKey(id), InvoiceItem.BuildRowKey(1))).Value;
        Assert.IsTrue(line.GetString(nameof(InvoiceItem.Subject)) == itemSubject, "The line must belong to this test.");
        Capture(items, line);
        TableClient history = tables.GetTableClient(nameof(InvoiceRevision));
        TableClient parts = tables.GetTableClient(nameof(InvoiceRevisionPart));
        foreach (InvoiceRevision revision in revisions)
        {
            StorageKey header = revision.GetKey();
            Capture(history, (await history.GetEntityAsync<TableEntity>(header.PartitionKey, header.RowKey)).Value);
            for (int index = 0; index < revision.PartCount; index++)
            {
                StorageKey part = revision.GetPartKey(index);
                Capture(parts, (await parts.GetEntityAsync<TableEntity>(part.PartitionKey, part.RowKey)).Value);
            }
        }
        Assert.IsTrue(owner.ETag == (await Read(id)).ETag, "The captured invoice must retain its observed version.");

        byte[] snapshot = JsonSerializer.SerializeToUtf8Bytes(captured.Select(row => row.Backup).ToArray());
        byte[] digest = SHA256.HashData(snapshot);
        string temporaryPath = Path.Combine(Path.GetTempPath(), $"invoice-restore-{Guid.NewGuid():N}.json");
        await using FileStream file = new(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            bufferSize: 4096, options: FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        await file.WriteAsync(snapshot);
        await file.FlushAsync();

        int deletionAttempts = 0;
        Stopwatch restoreTime = new();
        try
        {
            // The conditional parent removal must succeed before removing any child.
            foreach (CapturedRestoreRow row in captured)
            {
                deletionAttempts++;
                await row.Table.DeleteEntityAsync(row.Backup.Partition, row.Backup.Row, row.ETag);
            }
            foreach (CapturedRestoreRow row in captured)
            {
                NullableResponse<TableEntity> remaining = await row.Table.GetEntityIfExistsAsync<TableEntity>(
                    row.Backup.Partition, row.Backup.Row);
                Assert.IsFalse(remaining.HasValue, "The captured rows must be absent before restoration.");
            }

            restoreTime.Start();
            file.Position = 0;
            byte[] retained = new byte[snapshot.Length];
            await file.ReadExactlyAsync(retained);
            Assert.IsTrue(SHA256.HashData(retained).SequenceEqual(digest), "The retained copy must match the captured digest.");
            RestoreRow[] restore = JsonSerializer.Deserialize<RestoreRow[]>(retained)
                ?? throw new InvalidOperationException("The retained copy could not be read.");
            // Restore the dependencies first so an invoice lookup sees the complete history.
            for (int index = restore.Length - 1; index >= 0; index--)
                await tables.GetTableClient(restore[index].TableName).AddEntityAsync(restore[index].BuildEntity());

            List<RestoreRow> restored = [];
            foreach (CapturedRestoreRow row in captured)
            {
                TableEntity value = (await row.Table.GetEntityAsync<TableEntity>(row.Backup.Partition, row.Backup.Row)).Value;
                restored.Add(Backup(row.Table, value));
            }
            Assert.IsTrue(snapshot.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(restored.ToArray())),
                "The restored keys, types and values must match the retained invoice copy.");
            var restoredHistory = await ReadHistory(id);
            Assert.IsTrue(revisions.Select(revision => revision.ID)
                .SequenceEqual(restoredHistory.Select(entry => entry.Revision.ID)),
                "The history reader must resolve and validate the restored chain.");
            Assert.IsTrue(restoredHistory[0].Snapshot.Invoice.GrandTotalCents == 23000L,
                "The latest restored snapshot must retain its committed total.");
            restoreTime.Stop();
        }
        catch (Exception failure)
        {
            // Repair only missing rows within the acknowledged fixture. Add preserves any
            // concurrent row, including one that arrived after the point read.
            List<Exception> failures = [failure];
            for (int index = deletionAttempts - 1; index >= 0; index--)
            {
                CapturedRestoreRow row = captured[index];
                try
                {
                    NullableResponse<TableEntity> existing = await row.Table.GetEntityIfExistsAsync<TableEntity>(
                        row.Backup.Partition, row.Backup.Row);
                    if (!existing.HasValue)
                        await row.Table.AddEntityAsync(row.Backup.BuildEntity());
                }
                catch (Exception repairFailure)
                {
                    failures.Add(repairFailure);
                }
            }
            if (failures.Count > 1)
                throw new AggregateException("The restore drill and owned-fixture repair failed.", failures);
            throw;
        }

        string directory = Path.Combine(Required("GITHUB_WORKSPACE"), "artifacts", "deployed");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "test-invoice-aggregate-restore.json"),
            JsonSerializer.Serialize(new
            {
                schema = 1,
                environment = "test",
                commit,
                completed = true,
                restoredRows = captured.Count,
                restoredRevisions = revisions.Length,
                restoreMilliseconds = restoreTime.Elapsed.TotalMilliseconds,
            }));

        void Capture(TableClient table, TableEntity entity)
        {
            Assert.IsTrue(entity.ETag != ETag.All && !String.IsNullOrWhiteSpace(entity.ETag.ToString()),
                "Each captured row requires an observed conditional version.");
            captured.Add(new(table, entity.ETag, Backup(table, entity)));
        }
    }

    private static RestoreRow Backup(TableClient table, TableEntity entity) => new(table.Name, entity.PartitionKey, entity.RowKey,
        entity.Where(property => property.Key is not ("PartitionKey" or "RowKey" or "Timestamp" or "ETag" or "odata.etag"))
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => RestoreProperty.Capture(property.Key, property.Value))
            .ToArray());

    private sealed record CapturedRestoreRow(TableClient Table, ETag ETag, RestoreRow Backup);

    private sealed record RestoreRow(string TableName, string Partition, string Row, RestoreProperty[] Properties)
    {
        public TableEntity BuildEntity()
        {
            TableEntity entity = new(Partition, Row);
            foreach (RestoreProperty property in Properties)
                entity.Add(property.Name, property.ReadValue());
            return entity;
        }
    }

    private sealed record RestoreProperty(string Name, string Kind, JsonElement Data)
    {
        public static RestoreProperty Capture(string name, object value) => value switch
        {
            string text => new(name, "string", JsonSerializer.SerializeToElement(text)),
            bool flag => new(name, "boolean", JsonSerializer.SerializeToElement(flag)),
            int integer => new(name, "int32", JsonSerializer.SerializeToElement(integer)),
            long integer => new(name, "int64", JsonSerializer.SerializeToElement(integer)),
            double number => new(name, "double", JsonSerializer.SerializeToElement(number)),
            DateTimeOffset timestamp => new(name, "dateTime", JsonSerializer.SerializeToElement(timestamp)),
            DateTime timestamp => new(name, "dateTime", JsonSerializer.SerializeToElement(new DateTimeOffset(timestamp))),
            Guid id => new(name, "guid", JsonSerializer.SerializeToElement(id)),
            byte[] bytes => new(name, "binary", JsonSerializer.SerializeToElement(bytes)),
            BinaryData bytes => new(name, "binary", JsonSerializer.SerializeToElement(bytes.ToArray())),
            _ => throw new InvalidOperationException("The test row contains an unsupported Table property type."),
        };

        public object ReadValue() => Kind switch
        {
            "string" => Data.GetString() ?? throw new InvalidOperationException("A retained string is required."),
            "boolean" => Data.GetBoolean(),
            "int32" => Data.GetInt32(),
            "int64" => Data.GetInt64(),
            "double" => Data.GetDouble(),
            "dateTime" => Data.GetDateTimeOffset(),
            "guid" => Data.GetGuid(),
            "binary" => Data.GetBytesFromBase64(),
            _ => throw new InvalidOperationException("The retained row contains an unsupported Table property type."),
        };
    }
}
