using System.Buffers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Niobium.Invoicing.History;

/// <summary>An immutable historical revision. Only a reference committed on its invoice makes it history.</summary>
public sealed class InvoiceRevision
{
    private const int CurrentVersion = 1;
    private const int PartBytes = 18 * 1024;
    private const int MaximumSnapshotBytes = 16 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 32 };
    // Every current line field is a string, integer or date. Empty strings,
    // zero integers and a whole-second date give their shortest JSON values;
    // cleared storage metadata remains null in every historical copy.
    private static readonly int MinimumLineBytes = JsonSerializer.SerializeToUtf8Bytes(new InvoiceItem
    {
        Invoice = default,
        ID = 0,
        Subject = String.Empty,
        Description = String.Empty,
        UnitPriceCurrency = String.Empty,
        LineTotalCurrency = String.Empty
    }, Json).Length;
    private static readonly PropertyInfo[] InvoiceText = TextProperties(typeof(Invoice), nameof(Invoice.Token), nameof(Invoice.ETag));
    private static readonly PropertyInfo[] LineText = TextProperties(typeof(InvoiceItem), nameof(InvoiceItem.ETag));

    [EntityKey(EntityKeyKind.PartitionKey)]
    public Guid Biller
    {
        get; private init;
    }
    [EntityKey(EntityKeyKind.RowKey)]
    public Guid ID
    {
        get; private init;
    }
    [EntityKey(EntityKeyKind.ETag)]
    public string? ETag
    {
        get; private init;
    }
    [EntityKey(EntityKeyKind.Timestamp)]
    public DateTimeOffset? Timestamp
    {
        get; private init;
    }

    public int Version
    {
        get; private init;
    }
    public DateTimeOffset InvoiceCreated
    {
        get; private init;
    }
    public Guid? Previous
    {
        get; private init;
    }
    public DateTimeOffset RecordedAt
    {
        get; private init;
    }
    public int ChangeCode
    {
        get; private init;
    }
    public int AuthorityCode
    {
        get; private init;
    }
    public string Actor { get; private init; } = String.Empty;
    public string Reason { get; private init; } = String.Empty;
    public int PartCount
    {
        get; private init;
    }
    public string ContentDigest { get; private init; } = String.Empty;

    private sealed record Envelope(int Version, Guid Biller, Guid ID, DateTimeOffset InvoiceCreated,
        Guid? Previous, InvoiceChange Change, InvoiceRevisionSnapshot Snapshot);

    public sealed record Prepared(InvoiceRevision Revision, IReadOnlyList<InvoiceRevisionPart> Parts);

    public static Prepared Capture(Invoice invoice, IReadOnlyList<InvoiceItem> lines,
        InvoiceLineEvidence lineEvidence, InvoiceChange change, Guid? previous = null)
        => CaptureWithinCapacity(invoice, lines, lineEvidence, change, previous) ?? throw InvalidRepresentation();

    // The model owns the serialized size. The domain chooses the application
    // response for an oversized request or an existing row that cannot be captured.
    internal static Prepared? CaptureWithinCapacity(Invoice invoice, IReadOnlyList<InvoiceItem> lines,
        InvoiceLineEvidence lineEvidence, InvoiceChange change, Guid? previous = null)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(change);
        if (invoice.Biller == Guid.Empty || invoice.Created == default || previous == Guid.Empty
            || !Enum.IsDefined(lineEvidence) || !ValidChange(change)
            || lines.Any(line => line == null || line.Invoice != invoice.Created))
            throw InvalidRepresentation();

        if (ExceedsCapacity(invoice, lines))
            return null;
        if (lines.Select(line => line.ID).Distinct().Count() != lines.Count)
            throw InvalidRepresentation();

        // Copy through the public business JSON contract and explicitly remove
        // credentials/transport metadata. The original aggregate is never mutated.
        Invoice copy = CopyBusiness(invoice);
        InvoiceItem[] itemCopies = CopyLines(lines);
        InvoiceRevisionLifecycle? historyLifecycle = CopyLifecycle(invoice);
        InvoiceRevisionSnapshot snapshot = new(copy, invoice.GetPreparationStage(), historyLifecycle, lineEvidence, itemCopies);
        Guid id = Guid.NewGuid();
        InvoiceChange normalized = change with
        {
            At = change.At.ToUniversalTime()
        };
        Envelope envelope = new(CurrentVersion, invoice.Biller, id, invoice.Created.ToUniversalTime(), previous, normalized, snapshot);
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(envelope, Json);
        if (content.Length > MaximumSnapshotBytes)
            return null;
        int count = (content.Length + PartBytes - 1) / PartBytes;
        InvoiceRevision revision = new()
        {
            Version = CurrentVersion,
            Biller = invoice.Biller,
            ID = id,
            InvoiceCreated = invoice.Created.ToUniversalTime(),
            Previous = previous,
            RecordedAt = normalized.At,
            ChangeCode = (int)change.Kind,
            AuthorityCode = (int)change.Authority,
            Actor = change.Actor,
            Reason = change.Reason,
            PartCount = count,
            ContentDigest = Digest(content)
        };
        InvoiceRevisionPart[] parts = Enumerable.Range(0, count).Select(index => InvoiceRevisionPart.Capture(
            revision, index, content.AsSpan(index * PartBytes, Math.Min(PartBytes, content.Length - index * PartBytes)))).ToArray();
        return new(revision, Array.AsReadOnly(parts));
    }

    // A lower bound on the exact envelope size, counted with the same JSON
    // encoder in bounded scratch space. False still requires the final byte check;
    // true proves that copying/fingerprinting cannot produce a valid snapshot.
    internal static bool ExceedsCapacity(Invoice invoice, IReadOnlyList<InvoiceItem> lines)
    {
        long remaining = MaximumSnapshotBytes - (long)MinimumLineBytes * lines.Count
            - Math.Max(0, lines.Count - 1); // Commas between line objects.
        if (remaining < 0)
            return true;
        bool invoiceExceeds = TextExceedsCapacity(invoice, InvoiceText, ref remaining);
        if (invoiceExceeds)
            return true;
        foreach (InvoiceItem line in lines)
        {
            bool lineExceeds = TextExceedsCapacity(line, LineText, ref remaining);
            if (lineExceeds)
                return true;
        }
        return false;
    }

    private static PropertyInfo[] TextProperties(Type type, params string[] excluded)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.PropertyType == typeof(string)
                && property.GetMethod?.IsPublic == true && property.GetIndexParameters().Length == 0
                && !excluded.Contains(property.Name)
                && property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition != JsonIgnoreCondition.Always)
            .ToArray();

    private static bool TextExceedsCapacity(object value, PropertyInfo[] properties, ref long remaining)
    {
        Span<char> buffer = stackalloc char[512];
        JavaScriptEncoder encoder = Json.Encoder ?? JavaScriptEncoder.Default;
        foreach (PropertyInfo property in properties)
        {
            ReadOnlySpan<char> input = ((string?)property.GetValue(value)).AsSpan();
            // Each UTF-16 code unit requires at least one encoded JSON byte.
            if (input.Length > remaining)
                return true;
            while (!input.IsEmpty)
            {
                OperationStatus status = encoder.Encode(input, buffer, out int consumed, out int written);
                if (consumed == 0 || status is not (OperationStatus.Done or OperationStatus.DestinationTooSmall))
                    throw InvalidRepresentation();
                remaining -= Encoding.UTF8.GetByteCount(buffer[..written]);
                if (remaining < 0)
                    return true;
                input = input[consumed..];
            }
        }
        return false;
    }

    public StorageKey GetKey() => Key(Biller, ID);

    public static StorageKey Key(Guid biller, Guid revision)
    {
        if (biller == Guid.Empty || revision == Guid.Empty)
            throw InvalidRepresentation();
        return new(biller.ToString(), revision.ToString());
    }

    public StorageKey GetPartKey(int index)
    {
        ValidateHeader();
        if (index < 0 || index >= PartCount)
            throw InvalidRepresentation();
        return InvoiceRevisionPart.Key(Biller, ID, index);
    }

    public InvoiceChange ReadChange()
    {
        ValidateHeader();
        return new((InvoiceChangeKind)ChangeCode, (InvoiceChangeAuthority)AuthorityCode, Actor, Reason, RecordedAt);
    }

    public InvoiceRevisionSnapshot ReadSnapshot(IReadOnlyList<InvoiceRevisionPart> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ValidateHeader();
        if (parts.Count != PartCount)
            throw InvalidRepresentation();
        using MemoryStream stream = new();
        for (int index = 0; index < parts.Count; index++)
        {
            InvoiceRevisionPart part = parts[index] ?? throw InvalidRepresentation();
            byte[] bytes = part.ReadContent(this, index, PartBytes);
            if (index + 1 < parts.Count && bytes.Length != PartBytes)
                throw InvalidRepresentation();
            stream.Write(bytes);
            if (stream.Length > MaximumSnapshotBytes)
                throw InvalidRepresentation();
        }
        byte[] content = stream.ToArray();
        if (Digest(content) != ContentDigest)
            throw InvalidRepresentation();
        try
        {
            Envelope? envelope = JsonSerializer.Deserialize<Envelope>(content, Json);
            if (envelope == null || envelope.Version != Version || envelope.Biller != Biller || envelope.ID != ID
                || envelope.InvoiceCreated != InvoiceCreated || envelope.Previous != Previous || envelope.Change != ReadChange()
                || envelope.Snapshot == null || envelope.Snapshot.Invoice == null || envelope.Snapshot.Invoice.Biller != Biller
                || envelope.Snapshot.Invoice.Created != InvoiceCreated || envelope.Snapshot.Invoice.Token != null
                || envelope.Snapshot.Invoice.ETag != null || envelope.Snapshot.Invoice.Timestamp != null
                || !Enum.IsDefined(envelope.Snapshot.Preparation) || !Enum.IsDefined(envelope.Snapshot.LineEvidence)
                || envelope.Snapshot.Lines == null
                || envelope.Snapshot.Lines.Any(line => line == null || line.Invoice != InvoiceCreated || line.ETag != null || line.UpdatedAt != null)
                || envelope.Snapshot.Lines.Select(line => line.ID).Distinct().Count() != envelope.Snapshot.Lines.Count)
                throw InvalidRepresentation();
            return envelope.Snapshot;
        }
        catch (JsonException) { throw InvalidRepresentation(); }
    }

    public bool Describes(Guid biller, DateTimeOffset invoice)
    {
        ValidateHeader();
        return Biller == biller && InvoiceCreated == invoice;
    }

    public static bool MatchesInvoice(InvoiceRevisionSnapshot snapshot, Invoice invoice)
        => snapshot.Preparation == invoice.GetPreparationStage()
            && JsonSerializer.Serialize(snapshot.Invoice, Json) == JsonSerializer.Serialize(CopyBusiness(invoice), Json)
            && JsonSerializer.Serialize(snapshot.Lifecycle, Json) == JsonSerializer.Serialize(CopyLifecycle(invoice), Json);

    public static bool MatchesLines(IReadOnlyList<InvoiceItem> expected, IReadOnlyList<InvoiceItem> observed)
        => JsonSerializer.Serialize(CopyLines(expected), Json) == JsonSerializer.Serialize(CopyLines(observed), Json);

    private static InvoiceItem[] CopyLines(IReadOnlyList<InvoiceItem> lines)
        => lines.OrderBy(x => x.ID).Select(line => new InvoiceItem
        {
            Invoice = line.Invoice,
            ID = line.ID,
            Subject = line.Subject,
            Description = line.Description,
            UnitPriceCurrency = line.UnitPriceCurrency,
            UnitPriceCents = line.UnitPriceCents,
            Quantity = line.Quantity,
            LineTotalCurrency = line.LineTotalCurrency,
            LineTotalCents = line.LineTotalCents
        }).ToArray();

    private static Invoice CopyBusiness(Invoice invoice)
    {
        Invoice copy = JsonSerializer.Deserialize<Invoice>(JsonSerializer.Serialize(invoice, Json), Json)!;
        copy.Token = null;
        copy.ETag = null;
        copy.Timestamp = null;
        copy.Created = copy.Created.ToUniversalTime();
        copy.DueBy = copy.DueBy?.ToUniversalTime();
        copy.BillingPeriodStartDay = copy.BillingPeriodStartDay?.ToUniversalTime();
        copy.BillingPeriodEndDay = copy.BillingPeriodEndDay?.ToUniversalTime();
        copy.Delivered = copy.Delivered?.ToUniversalTime();
        return copy;
    }

    private static InvoiceRevisionLifecycle? CopyLifecycle(Invoice invoice)
    {
        InvoiceLifecycleSnapshot? lifecycle = invoice.ReadLifecycle();
        return lifecycle == null ? null : new(lifecycle.PaymentHistory, lifecycle.PaymentObservedAt,
            lifecycle.Reconciliations, lifecycle.Cancellation, lifecycle.Delivery, lifecycle.NotificationRequestedAt,
            lifecycle.QueuedAt, lifecycle.ConfirmedDeliveredAt);
    }

    private void ValidateHeader()
    {
        if (Version != CurrentVersion || Biller == Guid.Empty || ID == Guid.Empty || InvoiceCreated == default
            || Previous == Guid.Empty || Previous == ID || PartCount < 1
            || PartCount > (MaximumSnapshotBytes + PartBytes - 1) / PartBytes
            || String.IsNullOrEmpty(ContentDigest) || ContentDigest.Length != 64 || !ContentDigest.All(Uri.IsHexDigit)
            || !ValidChange(new((InvoiceChangeKind)ChangeCode, (InvoiceChangeAuthority)AuthorityCode, Actor, Reason, RecordedAt)))
            throw InvalidRepresentation();
    }

    private static bool ValidChange(InvoiceChange change)
        => Enum.IsDefined(change.Kind) && Enum.IsDefined(change.Authority) && change.At != default
            && !String.IsNullOrWhiteSpace(change.Actor) && change.Actor.Length <= 256 && change.Actor == change.Actor.Trim()
            && !String.IsNullOrWhiteSpace(change.Reason) && change.Reason.Length <= 500 && change.Reason == change.Reason.Trim();

    private static string Digest(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    internal static InvalidOperationException InvalidRepresentation() => new("Invalid invoice history representation.");
}
