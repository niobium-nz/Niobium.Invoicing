using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;

namespace Niobium.Invoicing.Testing;

// Reviewed raw representation checks precede the application rollout. Do not
// import the candidate payment models, their codecs, or their key factories.
internal static class InvoicePaymentFixture
{
    internal const string PaymentsTable = "InvoicePayment";
    internal const string PostingsTable = "InvoicePaymentPosting";
    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdef");
    private static readonly string[] ReceiptFields = ["PartitionKey", "RowKey", "Version", "Tenant", "InvoiceCreated", "Operation",
        "KindCode", "AmountCents", "Currency", "Reverses", "OccurredAt", "Actor", "Reason", "Evidence", "RecordedAt", "ContentDigest"];
    private static readonly string[] PostingFields = ["PartitionKey", "RowKey", "Version", "Tenant", "InvoiceCreated", "Operation",
        "Payment", "PaymentDigest", "Previous", "Sequence", "Index", "Currency", "OpeningSettledCents", "SettledCents"];
    private sealed record Entry(Guid Operation, int Kind, long AmountCents, string Currency, Guid? Reverses,
        DateTimeOffset OccurredAt, Guid Actor, string Reason, string Evidence, DateTimeOffset RecordedAt);
    private sealed record ReceiptContents(int Version, Guid Biller, Guid Tenant, DateTimeOffset InvoiceCreated, Entry Entry);
    private sealed record PostingContents(int Version, Guid Biller, Guid Tenant, DateTimeOffset InvoiceCreated,
        Guid Operation, string Payment, string PaymentDigest, string? Previous, long Sequence,
        string Index, string Currency, long OpeningSettledCents, long SettledCents);

    internal static async Task RemoveAsync(TableClient payments, TableClient postings, TableClient indexes, TableEntity owned,
        Guid biller, long invoice, Guid customer, Func<Task> deleteOwnedInvoice, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deleteOwnedInvoice);
        var rows = await ReadOwnedAsync(payments, postings, indexes, owned, biller, invoice, customer, cancellationToken: cancellationToken);

        // The callback validates history before conditionally deleting the observed
        // parent. Failed or unknown parent writes must leave all payment evidence intact.
        await deleteOwnedInvoice();
        foreach (TableEntity row in rows.Postings)
            await postings.DeleteEntityAsync(row.PartitionKey, row.RowKey, row.ETag, cancellationToken);
        foreach (TableEntity row in rows.Indexes)
            await indexes.DeleteEntityAsync(row.PartitionKey, row.RowKey, row.ETag, cancellationToken);
        foreach (TableEntity row in rows.Receipts)
            await payments.DeleteEntityAsync(row.PartitionKey, row.RowKey, row.ETag, cancellationToken);
    }

    internal static async Task<(List<TableEntity> Receipts, List<TableEntity> Postings, List<TableEntity> Indexes)> ReadOwnedAsync(
        TableClient payments, TableClient postings, TableClient indexes, TableEntity owned, Guid biller, long invoice, Guid customer,
        bool includeAllProperties = false, CancellationToken cancellationToken = default)
    {
        if (biller == Guid.Empty || customer == Guid.Empty || owned.PartitionKey != Invoice.BuildPartitionKey(biller)
            || owned.RowKey != Invoice.BuildRowKey(invoice) || owned.GetGuid(nameof(Invoice.Billee)) != customer
            || owned.GetGuid(nameof(Invoice.Tenant)) is not Guid tenant || tenant == Guid.Empty || !Conditional(owned))
            throw Invalid();
        InvoiceCheckpoint.PaymentPosition? position = InvoiceCheckpoint.ReadPaymentPosition(owned);
        DateTimeOffset created = Invoice.ParseID(invoice);
        List<TableEntity> receipts = await ReadAsync(payments, owned.PartitionKey, created,
            includeAllProperties ? null : ReceiptFields, position != null, cancellationToken);
        List<TableEntity> links = await ReadAsync(postings, owned.PartitionKey, created,
            includeAllProperties ? null : PostingFields, position != null, cancellationToken);
        List<TableEntity> indexRows = await ReadAsync(indexes, owned.PartitionKey, created,
            includeAllProperties ? null : InvoicePaymentIndexFixture.Fields, position != null, cancellationToken);
        Dictionary<string, Entry> entries = [];
        Dictionary<string, TableEntity> receiptRows = [];
        foreach (TableEntity row in receipts)
        {
            Entry entry = ValidateReceipt(row, biller, tenant, invoice);
            bool uniqueEntry = entries.TryAdd(row.RowKey, entry);
            bool uniqueRow = receiptRows.TryAdd(row.RowKey, row);
            if (!uniqueEntry || !uniqueRow)
                throw Invalid();
        }
        InvoicePaymentIndexFixture index = new(indexRows, biller, tenant, created, receiptRows);
        Dictionary<string, PostingContents> positions = [];
        foreach (TableEntity row in links)
        {
            PostingContents posting = ValidatePosting(row, biller, tenant);
            index.RequireRoot(posting.Index, posting.Sequence);
            bool uniquePosting = positions.TryAdd(row.RowKey, posting);
            if (!entries.TryGetValue(posting.Payment, out Entry? entry) || entry.Operation != posting.Operation
                || entry.Currency != posting.Currency || receiptRows[posting.Payment].GetString("ContentDigest") != posting.PaymentDigest
                || !uniquePosting)
                throw Invalid();
        }
        // Every selected posting must refer only to this owned invoice's rows,
        // including abandoned candidates. Never follow a reference into another partition.
        foreach (PostingContents posting in positions.Values)
            if (posting.Previous != null && (!positions.TryGetValue(posting.Previous, out PostingContents? previous)
                || previous.Sequence != posting.Sequence - 1 || previous.Currency != posting.Currency
                || previous.OpeningSettledCents != posting.OpeningSettledCents))
                throw Invalid();
        if (position != null)
            VerifyCommitted(position, positions, entries, index);
        return (receipts, links, indexRows);
    }

    private static void VerifyCommitted(InvoiceCheckpoint.PaymentPosition position,
        Dictionary<string, PostingContents> postings, Dictionary<string, Entry> entries, InvoicePaymentIndexFixture index)
    {
        string? id = position.Head;
        long sequence = position.Sequence, settled = position.SettledCents;
        Dictionary<Guid, InvoicePaymentIndexFixture.Entry> indexed = index.ReadCommitted(position.Index, position.Sequence);
        HashSet<Guid> operations = [];
        Dictionary<Guid, long> allReversals = [];
        Dictionary<Guid, long> reversals = [];
        try
        {
            while (id != null)
            {
                if (!postings.TryGetValue(id, out PostingContents? posting) || posting.Sequence != sequence
                    || posting.Currency != position.Currency || posting.OpeningSettledCents != position.OpeningSettledCents
                    || posting.SettledCents != settled || (sequence == position.Sequence && (posting.Operation != position.Operation || posting.Index != position.Index)))
                    throw Invalid();
                Entry entry = entries[posting.Payment];
                bool unique = operations.Add(entry.Operation);
                if (!unique || !indexed.TryGetValue(entry.Operation, out InvoicePaymentIndexFixture.Entry? leaf)
                    || leaf.Payment != posting.Payment || leaf.PaymentDigest != posting.PaymentDigest)
                    throw Invalid();
                if (entry.Kind == 2)
                {
                    reversals[entry.Reverses!.Value] = checked(reversals.GetValueOrDefault(entry.Reverses.Value) + entry.AmountCents);
                    allReversals[entry.Reverses.Value] = reversals[entry.Reverses.Value];
                }
                else
                {
                    bool reversed = reversals.Remove(entry.Operation, out long amount);
                    if (reversed && amount > entry.AmountCents)
                        throw Invalid();
                }
                settled = checked(settled - (entry.Kind == 1 ? entry.AmountCents : -entry.AmountCents));
                if (settled < position.OpeningSettledCents)
                    throw Invalid();
                sequence--;
                id = posting.Previous;
            }
            if (sequence != 0 || settled != position.OpeningSettledCents || reversals.Count != 0)
                throw Invalid();
            foreach (InvoicePaymentIndexFixture.Entry entry in indexed.Values)
                if (entry.ReversedCents != allReversals.GetValueOrDefault(entry.Operation))
                    throw Invalid();
        }
        catch (OverflowException) { throw Invalid(); }
    }

    private static Entry ValidateReceipt(TableEntity row, Guid biller, Guid tenant, long invoice)
    {
        Guid operation = GuidField(row, "Operation");
        int kind = row.GetInt32("KindCode") ?? throw Invalid();
        long amount = row.GetInt64("AmountCents") ?? throw Invalid();
        string currency = row.GetString("Currency") ?? throw Invalid();
        Guid? reverses = row.GetGuid("Reverses");
        DateTimeOffset occurred = DateField(row, "OccurredAt"), recorded = DateField(row, "RecordedAt");
        Guid actor = GuidField(row, "Actor");
        string reason = row.GetString("Reason") ?? throw Invalid(), evidence = row.GetString("Evidence") ?? throw Invalid();
        if (row.GetGuid("Tenant") != tenant || amount <= 0 || currency is not ("NZD" or "AUD") || actor != biller
            || occurred > recorded || !Bounded(reason, 500) || !Bounded(evidence, 1000)
            || (kind == 1 ? reverses != null : kind != 2 || reverses is null || reverses == Guid.Empty || reverses == operation))
            throw Invalid();
        string identity = FormattableString.Invariant($"Niobium.Invoicing.InvoicePayment/v1|{biller:D}|{invoice}|{operation:D}");
        if (row.RowKey != "v1-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity))))
            throw Invalid();
        Entry entry = new(operation, kind, amount, currency, reverses, occurred, actor, reason, evidence, recorded);
        if (row.GetString("ContentDigest") != Digest(new ReceiptContents(1, biller, tenant, DateField(row, "InvoiceCreated"), entry)))
            throw Invalid();
        return entry;
    }

    private static PostingContents ValidatePosting(TableEntity row, Guid biller, Guid tenant)
    {
        PostingContents contents = new(1, biller, GuidField(row, "Tenant"), DateField(row, "InvoiceCreated"),
            GuidField(row, "Operation"), row.GetString("Payment") ?? throw Invalid(), row.GetString("PaymentDigest") ?? throw Invalid(),
            row.GetString("Previous"), row.GetInt64("Sequence") ?? throw Invalid(), row.GetString("Index") ?? throw Invalid(), row.GetString("Currency") ?? throw Invalid(),
            row.GetInt64("OpeningSettledCents") ?? throw Invalid(), row.GetInt64("SettledCents") ?? throw Invalid());
        if (contents.Tenant != tenant || !ValidID(contents.Index) || !ValidID(contents.Payment) || contents.PaymentDigest.Length != 64
            || contents.PaymentDigest.AsSpan().IndexOfAnyExcept(HexDigits) >= 0
            || contents.Sequence < 1 || (contents.Sequence == 1 ? contents.Previous != null : !ValidID(contents.Previous))
            || contents.Currency is not ("NZD" or "AUD") || contents.OpeningSettledCents < 0
            || contents.SettledCents < contents.OpeningSettledCents || row.RowKey != "v1-" + Digest(contents))
            throw Invalid();
        return contents;
    }

    private static async Task<List<TableEntity>> ReadAsync(TableClient table, string biller, DateTimeOffset invoice,
        string[]? fields, bool required, CancellationToken cancellationToken)
    {
        List<TableEntity> result = [];
        try
        {
            string filter = TableClient.CreateQueryFilter($"PartitionKey eq {biller} and InvoiceCreated eq {invoice}");
            await foreach (TableEntity row in table.QueryAsync<TableEntity>(filter, maxPerPage: 100, select: fields, cancellationToken: cancellationToken))
            {
                if (result.Count >= 10000 || row.PartitionKey != biller || row.GetDateTimeOffset("InvoiceCreated") != invoice
                    || row.GetInt32("Version") != 1 || !Conditional(row) || !ValidID(row.RowKey))
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

    internal static bool ValidID(string? value)
        => value != null && value.Length == 67 && value.StartsWith("v1-", StringComparison.Ordinal)
            && value.AsSpan(3).IndexOfAnyExcept(HexDigits) < 0;
    private static string Digest<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private static bool Conditional(TableEntity row) => !String.IsNullOrWhiteSpace(row.ETag.ToString()) && row.ETag != ETag.All;
    private static bool Bounded(string value, int maximum) => !String.IsNullOrWhiteSpace(value) && value.Length <= maximum && value == value.Trim();
    private static Guid GuidField(TableEntity row, string name) => row.GetGuid(name) is Guid value && value != Guid.Empty ? value : throw Invalid();
    private static DateTimeOffset DateField(TableEntity row, string name)
        => row.GetDateTimeOffset(name) is DateTimeOffset value && value >= new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero)
            && value.Offset == TimeSpan.Zero ? value : throw Invalid();
    private static InvalidOperationException Invalid() => new("Invoice payment fixture ownership or representation could not be verified.");
}
