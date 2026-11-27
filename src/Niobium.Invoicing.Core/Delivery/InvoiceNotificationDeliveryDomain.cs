using Niobium.Notification;

namespace Niobium.Invoicing.Delivery;

/// <summary>Correlates delivery facts and projects progress without writing the invoice.</summary>
public sealed class InvoiceNotificationDeliveryDomain(
    Lazy<IRepository<InvoiceNotificationDelivery>> repository,
    IEnumerable<IDomainEventHandler<IDomain<InvoiceNotificationDelivery>>> handlers)
    : GenericDomain<InvoiceNotificationDelivery>(repository, handlers)
{
    internal static (Guid Biller, long InvoiceID) VerifyReference(NotificationDeliveryEvent receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (!Invoice.TryParseFullID(receipt.NotificationID, out Guid biller, out long invoice)
            || biller != receipt.Tenant || receipt.Channel != Constants.InvoiceNotificationChannel
            || !Enum.IsDefined(receipt.Kind) || receipt.OccurredAt == default
            || !ValidValue(receipt.ID, 128) || !ValidValue(receipt.SubmissionID, 128)
            || !ValidValue(receipt.Provider, 50) || !ValidValue(receipt.ProviderEventID, 128)
            || !ValidValue(receipt.ProviderMessageID, 128))
            throw new ApplicationException(InternalError.BadRequest, "Invalid invoice notification receipt.");
        return (biller, invoice);
    }

    internal async Task RecordAsync(NotificationDeliveryEvent receipt, Guid biller, long invoice,
        CancellationToken cancellationToken)
    {
        StorageKey key = InvoiceNotificationDelivery.BuildKey(biller, invoice);
        if (key.PartitionKey != PartitionKey || key.RowKey != RowKey)
            throw Conflict("The receipt belongs to another invoice.");
        InvoiceNotificationDelivery? existing = await TryGetEntityAsync(cancellationToken);
        if (existing != null)
        {
            VerifyStoredEvidence(existing);
            if (existing.SubmissionID != receipt.SubmissionID || existing.Provider != receipt.Provider
                || existing.ProviderMessageID != receipt.ProviderMessageID)
                throw Conflict("The receipt belongs to another notification submission.");
        }

        InvoiceNotificationDelivery current = existing ?? InvoiceNotificationDelivery.Create(key,
            receipt.SubmissionID, receipt.Provider, receipt.ProviderMessageID);
        InvoiceNotificationDelivery next = Observe(current, receipt);
        if (next != current)
            await SaveAsync([next], cancellationToken: cancellationToken);
    }

    internal async Task<InvoiceLifecycleStatus> ProjectAsync(InvoiceLifecycleStatus status, CancellationToken cancellationToken)
    {
        InvoiceNotificationDelivery? evidence = await TryGetEntityAsync(cancellationToken);
        if (evidence == null)
            return status;

        VerifyStoredEvidence(evidence);
        DateTimeOffset? delivered = status.ConfirmedDeliveredAt;
        if (evidence.DeliveredAt is DateTimeOffset observed && (delivered == null || observed < delivered))
            delivered = observed;

        InvoiceDeliveryState delivery = delivered.HasValue ? InvoiceDeliveryState.Delivered
            : evidence.SubmittedAt.HasValue ? InvoiceDeliveryState.Delivering : status.Delivery;
        return status with
        {
            Delivery = delivery,
            ConfirmedDeliveredAt = delivered,
            Notification = new(evidence.SubmittedAt, evidence.LastDelayedAt, evidence.LastFailureAt,
                (InvoiceNotificationFailure?)evidence.LastFailureCode),
        };
    }

    private static InvoiceNotificationDelivery Observe(InvoiceNotificationDelivery current, NotificationDeliveryEvent receipt)
    {
        DateTimeOffset at = receipt.OccurredAt.ToUniversalTime();
        string id = receipt.ID;
        switch (receipt.Kind)
        {
            case NotificationDeliveryKind.Submitted:
                if (!Earlier(at, current.SubmittedAt, id, current.SubmittedEventID))
                    return current;
                return current with
                {
                    SubmittedEventID = id,
                    SubmittedAt = at,
                };
            case NotificationDeliveryKind.Delivered:
                if (!Earlier(at, current.DeliveredAt, id, current.DeliveredEventID))
                    return current;
                return current with
                {
                    DeliveredEventID = id,
                    DeliveredAt = at,
                };
            case NotificationDeliveryKind.Delayed:
                if (!Later(at, current.LastDelayedAt, id, current.DelayEventID))
                    return current;
                return current with
                {
                    DelayEventID = id,
                    LastDelayedAt = at,
                };
            default:
                InvoiceNotificationFailure failure = Failure(receipt.Kind);
                if (!Later(at, current.LastFailureAt, id, current.FailureEventID))
                    return current;
                return current with
                {
                    FailureEventID = id,
                    LastFailureAt = at,
                    LastFailureCode = (int)failure,
                };
        }
    }

    private static InvoiceNotificationFailure Failure(NotificationDeliveryKind kind) => kind switch
    {
        NotificationDeliveryKind.Bounced => InvoiceNotificationFailure.Bounced,
        NotificationDeliveryKind.Failed => InvoiceNotificationFailure.Failed,
        NotificationDeliveryKind.Suppressed => InvoiceNotificationFailure.Suppressed,
        NotificationDeliveryKind.Complained => InvoiceNotificationFailure.Complained,
        _ => throw Conflict("Unsupported notification delivery event."),
    };

    private void VerifyStoredEvidence(InvoiceNotificationDelivery evidence)
    {
        if (evidence.Version != InvoiceNotificationDelivery.CurrentVersion
            || evidence.Biller != PartitionKey || evidence.InvoiceID != RowKey
            || String.IsNullOrWhiteSpace(evidence.ETag) || evidence.ETag == "*"
            || !ValidValue(evidence.SubmissionID, 128) || !ValidValue(evidence.Provider, 50)
            || !ValidValue(evidence.ProviderMessageID, 128)
            || !ValidObservation(evidence.SubmittedAt, evidence.SubmittedEventID)
            || !ValidObservation(evidence.DeliveredAt, evidence.DeliveredEventID)
            || !ValidObservation(evidence.LastDelayedAt, evidence.DelayEventID)
            || !ValidObservation(evidence.LastFailureAt, evidence.FailureEventID)
            || (evidence.LastFailureAt == null) != (evidence.LastFailureCode == null)
            || (evidence.LastFailureCode is int code && !Enum.IsDefined((InvoiceNotificationFailure)code)))
            throw Conflict("Invoice notification evidence is not supported.");
    }

    private static bool ValidObservation(DateTimeOffset? at, string? id)
        => at == null ? id == null : at != default(DateTimeOffset) && ValidValue(id, 128);

    private static bool ValidValue(string? value, int max)
        => value is { Length: > 0 } && value.Length <= max && value == value.Trim() && !value.Any(Char.IsControl);

    private static bool Earlier(DateTimeOffset at, DateTimeOffset? storedAt, string id, string? storedID)
        => storedAt == null || at < storedAt
            || (at == storedAt && StringComparer.Ordinal.Compare(id, storedID) < 0);

    private static bool Later(DateTimeOffset at, DateTimeOffset? storedAt, string id, string? storedID)
        => storedAt == null || at > storedAt
            || (at == storedAt && StringComparer.Ordinal.Compare(id, storedID) > 0);

    private static ApplicationException Conflict(string message) => new(InternalError.Conflict, message);
}
