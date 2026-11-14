using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Niobium.Invoicing.Payments;

/// <summary>A candidate application of one immutable receipt to a particular predecessor.</summary>
public sealed class InvoicePaymentPosting
{
    private const int CurrentVersion = 1;
    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdef");

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
    public string Payment { get; private init; } = String.Empty;
    public string PaymentDigest { get; private init; } = String.Empty;
    public string? Previous
    {
        get; private init;
    }
    public long Sequence
    {
        get; private init;
    }
    public string Index { get; private init; } = String.Empty;
    public string Currency { get; private init; } = String.Empty;
    public long OpeningSettledCents
    {
        get; private init;
    }
    public long SettledCents
    {
        get; private init;
    }

    private sealed record Contents(int Version, Guid Biller, Guid Tenant, DateTimeOffset InvoiceCreated,
        Guid Operation, string Payment, string PaymentDigest, string? Previous, long Sequence,
        string Index, string Currency, long OpeningSettledCents, long SettledCents);

    public static InvoicePaymentPosting Capture(InvoicePayment payment, InvoicePaymentPosition? previous,
        long openingSettledCents, long settledCents, string index)
    {
        ArgumentNullException.ThrowIfNull(payment);
        InvoicePaymentEntry entry = payment.Read();
        long sequence = checked((previous?.Sequence ?? 0) + 1);
        if (previous != null && (previous.Currency != entry.Currency || previous.OpeningSettledCents != openingSettledCents))
            throw Invalid();
        Contents contents = new(CurrentVersion, payment.Biller, payment.Tenant, payment.InvoiceCreated,
            entry.Operation, payment.ID, payment.ContentDigest, previous?.Head, sequence, index, entry.Currency, openingSettledCents, settledCents);
        Validate(contents);
        return new()
        {
            Version = CurrentVersion,
            Biller = contents.Biller,
            Tenant = contents.Tenant,
            InvoiceCreated = contents.InvoiceCreated,
            ID = Identity(contents),
            Operation = contents.Operation,
            Payment = contents.Payment,
            PaymentDigest = contents.PaymentDigest,
            Previous = contents.Previous,
            Sequence = contents.Sequence,
            Index = contents.Index,
            Currency = contents.Currency,
            OpeningSettledCents = contents.OpeningSettledCents,
            SettledCents = contents.SettledCents
        };
    }

    public InvoicePaymentPosition ReadPosition()
    {
        Contents contents = new(Version, Biller, Tenant, InvoiceCreated, Operation, Payment, PaymentDigest,
            Previous, Sequence, Index, Currency, OpeningSettledCents, SettledCents);
        Validate(contents);
        if (ID != Identity(contents))
            throw Invalid();
        return new(ID, Sequence, Operation, Currency, OpeningSettledCents, SettledCents, Index);
    }

    public bool Describes(Invoice invoice)
    {
        _ = ReadPosition();
        return Biller == invoice.Biller && Tenant == invoice.Tenant && InvoiceCreated == invoice.Created;
    }

    public bool References(InvoicePayment payment)
    {
        _ = ReadPosition();
        _ = payment.Read();
        return payment.Biller == Biller && payment.Tenant == Tenant && payment.InvoiceCreated == InvoiceCreated
            && payment.Operation == Operation && payment.ID == Payment && payment.ContentDigest == PaymentDigest && payment.Currency == Currency;
    }

    public StorageKey GetKey() => Key(Biller, ID);
    public StorageKey GetPaymentKey() => new(Biller.ToString(), Payment);
    public StorageKey? GetPreviousKey() => Previous == null ? null : Key(Biller, Previous);

    public static StorageKey Key(Guid biller, string id)
        => biller != Guid.Empty && ValidIdentity(id) ? new(biller.ToString(), id) : throw Invalid();

    private static string Identity(Contents contents)
        => "v1-" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(contents)));

    private static bool ValidIdentity(string? value)
        => value != null && value.Length == 67 && value.StartsWith("v1-", StringComparison.Ordinal)
            && value.AsSpan(3).IndexOfAnyExcept(HexDigits) < 0;

    private static void Validate(Contents contents)
    {
        if (contents.Version != CurrentVersion || contents.Biller == Guid.Empty || contents.Tenant == Guid.Empty || contents.Operation == Guid.Empty
            || contents.InvoiceCreated == default || contents.InvoiceCreated.Offset != TimeSpan.Zero
            || !ValidIdentity(contents.Payment) || !ValidIdentity(contents.Index) || contents.PaymentDigest == null || contents.PaymentDigest.Length != 64
            || contents.PaymentDigest.AsSpan().IndexOfAnyExcept(HexDigits) >= 0
            || contents.Sequence < 1 || (contents.Sequence == 1 ? contents.Previous != null
                : contents.Previous == null || !ValidIdentity(contents.Previous))
            || contents.Currency == null || contents.Currency.Length != 3 || contents.Currency.Any(c => c is < 'A' or > 'Z'))
            throw Invalid();
    }

    private static InvalidOperationException Invalid() => new("Unsupported invoice payment posting representation.");
}
