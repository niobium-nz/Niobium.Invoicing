using Azure;
using Azure.Data.Tables;

namespace Niobium.Invoicing.Testing;

// Independent raw fixture cleanup can be installed before the application schema.
// Invoke only after the owning synthetic invoice's conditional delete succeeds.
internal static class InvoiceHistoryFixture
{
    internal const string RevisionsTable = "InvoiceRevision";
    internal const string PartsTable = "InvoiceRevisionPart";
    private static readonly string[] IdentityFields = ["PartitionKey", "RowKey", "Version", "InvoiceCreated", "Revision"];

    internal static async Task RemoveAsync(TableClient revisions, TableClient parts, TableEntity owned,
        Guid biller, long invoice, Guid customer, CancellationToken cancellationToken = default)
    {
        if (biller == Guid.Empty || customer == Guid.Empty || owned.PartitionKey != Invoice.BuildPartitionKey(biller)
            || owned.RowKey != Invoice.BuildRowKey(invoice) || owned.GetGuid(nameof(Invoice.Billee)) != customer
            || !Conditional(owned))
            throw Invalid();
        bool expected = owned.GetString(nameof(Invoice.IssuanceState)) == "lifecycle-v1"
            && InvoiceCheckpoint.ReadEvidence(owned).Version == 2;
        DateTimeOffset created = Invoice.ParseID(invoice);
        List<TableEntity> revisionRows = await ReadAsync(revisions, owned.PartitionKey, created, false, expected, cancellationToken);
        List<TableEntity> partRows = await ReadAsync(parts, owned.PartitionKey, created, true, expected || revisionRows.Count > 0, cancellationToken);

        // Validate the complete selected set before removing any child. No key
        // reconstruction, broad partition deletion, wildcard ETag or body logging.
        foreach (TableEntity part in partRows)
            await parts.DeleteEntityAsync(part.PartitionKey, part.RowKey, part.ETag, cancellationToken);
        foreach (TableEntity revision in revisionRows)
            await revisions.DeleteEntityAsync(revision.PartitionKey, revision.RowKey, revision.ETag, cancellationToken);
    }

    private static async Task<List<TableEntity>> ReadAsync(TableClient table, string biller, DateTimeOffset invoice,
        bool part, bool required, CancellationToken cancellationToken)
    {
        List<TableEntity> result = [];
        try
        {
            string filter = TableClient.CreateQueryFilter($"PartitionKey eq {biller} and InvoiceCreated eq {invoice}");
            await foreach (TableEntity row in table.QueryAsync<TableEntity>(filter, maxPerPage: 100, select: IdentityFields,
                cancellationToken: cancellationToken))
            {
                if (result.Count >= 10000 || row.PartitionKey != biller || row.GetDateTimeOffset("InvoiceCreated") != invoice
                    || row.GetInt32("Version") != 1 || !Conditional(row) || String.IsNullOrWhiteSpace(row.RowKey))
                    throw Invalid();
                if (part)
                {
                    if (!row.TryGetValue("Revision", out object? value) || value is not Guid revision || revision == Guid.Empty)
                        throw Invalid();
                }
                else if (!Guid.TryParseExact(row.RowKey, "D", out Guid revision) || revision == Guid.Empty)
                    throw Invalid();
                result.Add(row);
            }
        }
        catch (RequestFailedException error) when (!required && error.Status == 404 && error.ErrorCode == "TableNotFound")
        {
            return [];
        }
        return result;
    }

    private static bool Conditional(TableEntity row) => !String.IsNullOrWhiteSpace(row.ETag.ToString()) && row.ETag != ETag.All;
    private static InvalidOperationException Invalid() => new("Invoice history fixture ownership or representation could not be verified.");
}
