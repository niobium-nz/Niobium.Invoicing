using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Niobium.Invoicing.Payments;

/// <summary>An immutable operation receipt. Only a committed posting applies it to an invoice.</summary>
public sealed class InvoicePayment
{
    private const int CurrentVersion = 1;

    [EntityKey(EntityKeyKind.PartitionKey)]
    public Guid Biller
    {
        get; private init;
    }
    [EntityKey(EntityKeyKind.RowKey)] public string ID { get; private init; } = String.Empty;
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
    public Guid Tenant
    {
        get; private init;
    }
    public DateTimeOffset InvoiceCreated
    {
        get; private init;
    }
    public Guid Operation
    {
        get; private init;
    }
    public int KindCode
    {
        get; private init;
    }
    public long AmountCents
    {
        get; private init;
    }
    public string Currency { get; private init; } = String.Empty;
    public Guid? Reverses
    {
        get; private init;
    }
    public DateTimeOffset OccurredAt
    {
        get; private init;
    }
    public Guid Actor
    {
        get; private init;
    }
    public string Reason { get; private init; } = String.Empty;
    public string Evidence { get; private init; } = String.Empty;
    public DateTimeOffset RecordedAt
    {
        get; private init;
    }
    public string ContentDigest { get; private init; } = String.Empty;

    private sealed record Contents(int Version, Guid Biller, Guid Tenant, DateTimeOffset InvoiceCreated,
        InvoicePaymentEntry Entry);

    public static InvoicePayment Capture(Invoice invoice, InvoicePaymentEntry entry)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(entry);
        InvoicePaymentEntry normalized = entry with
        {
            OccurredAt = entry.OccurredAt.ToUniversalTime(),
            RecordedAt = entry.RecordedAt.ToUniversalTime()
        };
        ValidateEntry(normalized);
        StorageKey key = Key(invoice.Biller, invoice.GetID(), entry.Operation);
        if (invoice.Tenant == Guid.Empty)
            throw Invalid();
        return new()
        {
            Version = CurrentVersion,
            Biller = invoice.Biller,
            Tenant = invoice.Tenant,
            ID = key.RowKey,
            InvoiceCreated = invoice.Created.ToUniversalTime(),
            Operation = normalized.Operation,
            KindCode = (int)normalized.Kind,
            AmountCents = normalized.AmountCents,
            Currency = normalized.Currency,
            Reverses = normalized.Reverses,
            OccurredAt = normalized.OccurredAt,
            Actor = normalized.Actor,
            Reason = normalized.Reason,
            Evidence = normalized.Evidence,
            RecordedAt = normalized.RecordedAt,
            ContentDigest = Digest(new(CurrentVersion, invoice.Biller, invoice.Tenant, invoice.Created.ToUniversalTime(), normalized))
        };
    }

    public InvoicePaymentEntry Read()
    {
        InvoicePaymentEntry entry = new(Operation, (InvoicePaymentKind)KindCode, AmountCents, Currency,
            Reverses, OccurredAt, Actor, Reason, Evidence, RecordedAt);
        ValidateEntry(entry);
        StorageKey expected = Key(Biller, Invoice.ParseID(InvoiceCreated), Operation);
        if (Version != CurrentVersion || Tenant == Guid.Empty || ID != expected.RowKey
            || InvoiceCreated.Offset != TimeSpan.Zero || OccurredAt.Offset != TimeSpan.Zero || RecordedAt.Offset != TimeSpan.Zero
            || ContentDigest != Digest(new(Version, Biller, Tenant, InvoiceCreated, entry)))
            throw Invalid();
        return entry;
    }

    public bool Describes(Invoice invoice)
    {
        _ = Read();
        return Biller == invoice.Biller && Tenant == invoice.Tenant && InvoiceCreated == invoice.Created;
    }

    // Transport retries have a new observation time; the first immutable receipt
    // retains its RecordedAt. Every caller-supplied business value must still match.
    public bool Matches(InvoicePaymentEntry entry)
    {
        InvoicePaymentEntry stored = Read();
        return stored with
        {
            RecordedAt = entry.RecordedAt.ToUniversalTime()
        }
            == entry with
            {
                OccurredAt = entry.OccurredAt.ToUniversalTime(),
                RecordedAt = entry.RecordedAt.ToUniversalTime()
            };
    }

    public StorageKey GetKey() => Key(Biller, Invoice.ParseID(InvoiceCreated), Operation);

    public static StorageKey Key(Guid biller, long invoice, Guid operation)
    {
        if (biller == Guid.Empty || invoice <= 0 || operation == Guid.Empty)
            throw Invalid();
        string identity = FormattableString.Invariant($"Niobium.Invoicing.InvoicePayment/v1|{biller:D}|{invoice}|{operation:D}");
        return new(biller.ToString(), "v1-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity))));
    }

    private static string Digest(Contents contents)
        => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(contents)));

    private static void ValidateEntry(InvoicePaymentEntry entry)
    {
        if (entry.Operation == Guid.Empty || !Enum.IsDefined(entry.Kind) || entry.Actor == Guid.Empty
            || entry.Currency == null || entry.Currency.Length != 3 || entry.Currency.Any(c => c is < 'A' or > 'Z')
            || entry.OccurredAt == default || entry.RecordedAt == default
            || (entry.Kind == InvoicePaymentKind.Received ? entry.Reverses != null
                : entry.Reverses is null || entry.Reverses == Guid.Empty || entry.Reverses == entry.Operation)
            || !Bounded(entry.Reason, 500) || !Bounded(entry.Evidence, 1000))
            throw Invalid();
    }

    private static bool Bounded(string value, int maximum)
        => !String.IsNullOrWhiteSpace(value) && value.Length <= maximum && value == value.Trim();

    private static InvalidOperationException Invalid() => new("Unsupported invoice payment representation.");
}
