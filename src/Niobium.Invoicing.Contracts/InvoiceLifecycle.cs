using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Niobium.Invoicing;

public enum InvoicePreparationStage
{
    Draft = 0, Preparing = 1, Retryable = 2, Ready = 3, Frozen = 4
}
public enum InvoicePaymentHistory
{
    Unknown = 0, NoPayments = 1, PaymentOccurred = 2
}
public enum InvoiceDeliveryState
{
    Unknown = 0, NotRequested = 1, Delivering = 2, Delivered = 3, DeliveryProblem = 4
}
public enum InvoiceCancellationBlock
{
    None = 0, AlreadyCancelled = 1, PaymentOccurred = 2, PaymentHistoryUnknown = 3
}

public sealed record InvoiceCancellation(Guid Operation, Guid Actor, string Reason, DateTimeOffset At);
public sealed record InvoicePaymentHistoryDecision(Guid Operation, Guid Actor, InvoicePaymentHistory Finding,
    string Reason, string Evidence, DateTimeOffset At);

// A stored data contract. Rich invoice domains choose transitions; the invoice
// model alone encodes this snapshot into its mapped scalar column.
public sealed record InvoiceLifecycleSnapshot
{
    public const int CurrentVersion = 3;
    private const int HistoryVersion = 2;
    private const int UnanchoredVersion = 1;
    [JsonInclude]
    public int Version
    {
        get; private init;
    }
    [JsonInclude] public string WriteID { get; private init; } = String.Empty;
    [JsonInclude]
    public Guid? HistoryRevision
    {
        get; private init;
    }
    [JsonInclude]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InvoicePaymentPosition? Payments
    {
        get; private init;
    }
    public InvoicePreparationStage Preparation
    {
        get; init;
    }
    public InvoicePaymentHistory PaymentHistory
    {
        get; init;
    }
    public DateTimeOffset? PaymentObservedAt
    {
        get; init;
    }
    public IReadOnlyList<InvoicePaymentHistoryDecision> Reconciliations { get; init; } = [];
    public InvoiceCancellation? Cancellation
    {
        get; init;
    }
    public InvoiceDeliveryState Delivery
    {
        get; init;
    }
    public DateTimeOffset? NotificationRequestedAt
    {
        get; init;
    }
    public DateTimeOffset? QueuedAt
    {
        get; init;
    }
    public DateTimeOffset? ConfirmedDeliveredAt
    {
        get; init;
    }

    public static InvoiceLifecycleSnapshot Create(InvoicePreparationStage preparation)
        => new()
        {
            // A standalone lifecycle snapshot has no committed history reference.
            // The domain advances it only after recording immutable history.
            Version = UnanchoredVersion,
            Preparation = preparation
        };

    internal InvoiceLifecycleSnapshot CaptureWrite() => this with { WriteID = Guid.NewGuid().ToString("N") };
    internal InvoiceLifecycleSnapshot CaptureHistory(Guid revision)
        => revision == Guid.Empty ? throw new InvalidOperationException("A history revision is required.")
            : this with
            {
                Version = Payments == null ? HistoryVersion : CurrentVersion,
                HistoryRevision = revision
            };

    internal InvoiceLifecycleSnapshot CapturePayments(InvoicePaymentPosition position)
        => HistoryRevision.HasValue && HistoryRevision.Value != Guid.Empty && ValidPaymentPosition(position)
            ? this with
            {
                Version = CurrentVersion,
                Payments = position
            }
            : throw new InvalidOperationException("Unsupported invoice payment position.");

    internal bool HasSupportedRepresentation()
        => Version switch
        {
            UnanchoredVersion => HistoryRevision == null && Payments == null,
            HistoryVersion => HistoryRevision.HasValue && HistoryRevision.Value != Guid.Empty && Payments == null,
            CurrentVersion => HistoryRevision.HasValue && HistoryRevision.Value != Guid.Empty && ValidPaymentPosition(Payments),
            _ => false
        };

    private static bool ValidPaymentPosition(InvoicePaymentPosition? position)
        => position != null && !String.IsNullOrWhiteSpace(position.Head) && position.Head.Length <= 160
            && position.Head == position.Head.Trim() && position.Sequence > 0 && position.Operation != Guid.Empty
            && !String.IsNullOrWhiteSpace(position.Index) && position.Index.Length <= 160 && position.Index == position.Index.Trim()
            && position.Currency is { Length: 3 } && position.Currency.All(c => c is >= 'A' and <= 'Z');
}

public partial class Invoice
{
    private const string LifecycleCheckpoint = "lifecycle-v1";
    private const int MaximumLifecycleBytes = 32 * 1024;
    private static readonly JsonSerializerOptions LifecycleJson = new() { MaxDepth = 16 };

    // JsonIgnore preserves historical invoice JSON and v1 content fingerprints.
    // The Table mapper persists public scalar properties, including this private setter.
    [JsonIgnore]
    public string? LifecycleData
    {
        get; private set;
    }

    public InvoiceLifecycleSnapshot? ReadLifecycle()
    {
        if (IssuanceState != LifecycleCheckpoint)
        {
            if (LifecycleData != null)
                throw InvalidLifecycle();
            return null;
        }
        if (String.IsNullOrWhiteSpace(LifecycleData) || Encoding.UTF8.GetByteCount(LifecycleData) > MaximumLifecycleBytes)
            throw InvalidLifecycle();
        try
        {
            InvoiceLifecycleSnapshot? snapshot = JsonSerializer.Deserialize<InvoiceLifecycleSnapshot>(LifecycleData, LifecycleJson);
            if (snapshot == null || !snapshot.HasSupportedRepresentation()
                || !Enum.IsDefined(snapshot.Preparation)
                || !Guid.TryParseExact(snapshot.WriteID, "N", out Guid write) || write == Guid.Empty)
                throw InvalidLifecycle();
            return snapshot;
        }
        catch (JsonException) { throw InvalidLifecycle(); }
    }

    public InvoicePreparationStage GetPreparationStage()
    {
        InvoiceLifecycleSnapshot? snapshot = ReadLifecycle();
        if (snapshot != null)
            return snapshot.Preparation;
        // Existing checkpoints remain readable. No old key, token or content is rewritten on read.
        return IssuanceState switch
        {
            null => InvoicePreparationStage.Draft,
            "preparing-v1" => InvoicePreparationStage.Preparing,
            "retryable-v1" => InvoicePreparationStage.Retryable,
            "ready-v1" => InvoicePreparationStage.Ready,
            "frozen-v1" => InvoicePreparationStage.Frozen,
            _ => throw InvalidLifecycle(),
        };
    }

    public void StoreLifecycle(InvoiceLifecycleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.HasSupportedRepresentation() || !Enum.IsDefined(snapshot.Preparation))
            throw InvalidLifecycle();
        InvoiceLifecycleSnapshot? current = ReadLifecycle();
        if ((current?.HistoryRevision != null && snapshot.HistoryRevision == null)
            || (current?.Payments != null && snapshot.Payments == null))
            throw InvalidLifecycle();
        string serialized = JsonSerializer.Serialize(snapshot.CaptureWrite(), LifecycleJson);
        if (Encoding.UTF8.GetByteCount(serialized) > MaximumLifecycleBytes)
            throw InvalidLifecycle();
        LifecycleData = serialized;
        // Prior workers reject this checkpoint before their replace-mode write can
        // erase the new lifecycle column. Never downgrade this row for rollback.
        IssuanceState = LifecycleCheckpoint;
    }

    public void SetPreparationStage(InvoicePreparationStage stage)
    {
        InvoiceLifecycleSnapshot snapshot = ReadLifecycle() ?? InvoiceLifecycleSnapshot.Create(GetPreparationStage());
        StoreLifecycle(snapshot with
        {
            Preparation = stage
        });
    }

    public void ReferenceHistory(Guid revision)
    {
        InvoiceLifecycleSnapshot snapshot = ReadLifecycle() ?? InvoiceLifecycleSnapshot.Create(GetPreparationStage());
        StoreLifecycle(snapshot.CaptureHistory(revision));
    }

    public void ReferencePayments(InvoicePaymentPosition position)
    {
        InvoiceLifecycleSnapshot snapshot = ReadLifecycle() ?? InvoiceLifecycleSnapshot.Create(GetPreparationStage());
        StoreLifecycle(snapshot.CapturePayments(position));
    }

    private static InvalidOperationException InvalidLifecycle() => new("Unsupported invoice lifecycle representation.");
}

public sealed class CancelInvoiceRequest
{
    public Guid Operation
    {
        get; set;
    }
    public string Reason { get; set; } = String.Empty;
}

public sealed class ReconcileInvoicePaymentHistoryRequest
{
    public Guid Operation
    {
        get; set;
    }
    public InvoicePaymentHistory Finding
    {
        get; set;
    }
    public string Reason { get; set; } = String.Empty;
    public string Evidence { get; set; } = String.Empty;
}

public enum InvoiceNotificationFailure
{
    Bounced = 1, Failed = 2, Suppressed = 3, Complained = 4
}

public sealed record InvoiceNotificationProgress(DateTimeOffset? SubmittedAt, DateTimeOffset? LastDelayedAt,
    DateTimeOffset? LastFailureAt, InvoiceNotificationFailure? LastFailure);

public sealed record InvoiceLifecycleStatus(InvoicePreparationStage Preparation, InvoiceDeliveryState Delivery,
    InvoicePaymentHistory PaymentHistory, bool Cancelled, DateTimeOffset? CancelledAt,
    DateTimeOffset? QueuedAt, DateTimeOffset? ConfirmedDeliveredAt, InvoiceCancellationBlock CancellationBlock)
{
    public bool CanCancel => CancellationBlock == InvoiceCancellationBlock.None;

    /// <summary>Additional provider progress when an authenticated receipt was recorded.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public InvoiceNotificationProgress? Notification { get; init; }
}
