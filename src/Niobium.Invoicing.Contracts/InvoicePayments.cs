namespace Niobium.Invoicing;

public enum InvoicePaymentKind
{
    Received = 1, Reversed = 2
}

/// <summary>A manual receipt or reversal supported by an external evidence reference.</summary>
public sealed record InvoicePaymentEntry(Guid Operation, InvoicePaymentKind Kind, long AmountCents,
    string Currency, Guid? Reverses, DateTimeOffset OccurredAt, Guid Actor,
    string Reason, string Evidence, DateTimeOffset RecordedAt);

public sealed class RecordInvoicePaymentRequest
{
    public Guid Operation
    {
        get; set;
    }
    public long AmountCents
    {
        get; set;
    }
    public string Currency { get; set; } = String.Empty;
    public DateTimeOffset OccurredAt
    {
        get; set;
    }
    public string Reason { get; set; } = String.Empty;
    public string Evidence { get; set; } = String.Empty;
}

public sealed class ReverseInvoicePaymentRequest
{
    public Guid Operation
    {
        get; set;
    }
    public Guid Reverses
    {
        get; set;
    }
    public long AmountCents
    {
        get; set;
    }
    public string Currency { get; set; } = String.Empty;
    public DateTimeOffset OccurredAt
    {
        get; set;
    }
    public string Reason { get; set; } = String.Empty;
    public string Evidence { get; set; } = String.Empty;
}

/// <summary>The observed opening amount and committed manual payments, without claiming earlier transactions are known.</summary>
public sealed record InvoicePaymentBalance(string Currency, long OpeningSettledCents,
    long SettledCents, long OutstandingCents, long OverpaidCents, long Entries);

// A bounded stored position. Invoice owns its lifecycle serialization; domains
// choose transitions after verifying the immutable posting and receipt.
public sealed record InvoicePaymentPosition(string Head, long Sequence, Guid Operation, string Currency,
    long OpeningSettledCents, long SettledCents, string Index);
