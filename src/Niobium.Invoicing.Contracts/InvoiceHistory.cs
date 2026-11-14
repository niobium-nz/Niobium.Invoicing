namespace Niobium.Invoicing.History;

public enum InvoiceChangeKind
{
    ObservedBaseline = 0, PreparationStarted = 1, PreparationCompleted = 2,
    PreparationFailed = 3, Frozen = 4, Cancelled = 5,
    PaymentHistoryReconciled = 6, NotificationQueued = 7, AccessTokenSaved = 8
}

public enum InvoiceChangeAuthority
{
    UnattributedCoreCaller = 0, AuthenticatedBiller = 1, TrustedQueue = 2, FunctionKey = 3
}

public enum InvoiceLineEvidence
{
    Observed = 0, Proposed = 1, Verified = 2
}

public sealed record InvoiceChange(InvoiceChangeKind Kind, InvoiceChangeAuthority Authority,
    string Actor, string Reason, DateTimeOffset At);

public sealed record InvoiceChangeOrigin(InvoiceChangeAuthority Authority, string Actor, string? Reason = null);

// These are historical business snapshots, not a backup format for restoring
// service checkpoints. Link credentials and storage versions are not retained.
public sealed record InvoiceRevisionLifecycle(InvoicePaymentHistory PaymentHistory, DateTimeOffset? PaymentObservedAt,
    IReadOnlyList<InvoicePaymentHistoryDecision> Reconciliations, InvoiceCancellation? Cancellation,
    InvoiceDeliveryState Delivery, DateTimeOffset? NotificationRequestedAt, DateTimeOffset? QueuedAt,
    DateTimeOffset? ConfirmedDeliveredAt);

public sealed record InvoiceRevisionSnapshot(Invoice Invoice, InvoicePreparationStage Preparation,
    InvoiceRevisionLifecycle? Lifecycle, InvoiceLineEvidence LineEvidence, IReadOnlyList<InvoiceItem> Lines);

public sealed record InvoiceHistoryEntry(Guid ID, InvoiceChange Change, InvoiceRevisionSnapshot Snapshot);

/// <summary>One bounded snapshot and an optional opaque continuation to an older committed change.</summary>
public sealed record InvoiceHistoryPage(InvoiceHistoryEntry? Entry, string? Continuation);
