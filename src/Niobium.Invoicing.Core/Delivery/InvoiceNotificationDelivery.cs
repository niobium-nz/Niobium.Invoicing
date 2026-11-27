namespace Niobium.Invoicing.Delivery;

/// <summary>Persisted evidence for the original frozen invoice notification.</summary>
public sealed record InvoiceNotificationDelivery
{
    internal const int CurrentVersion = 1;

    public int Version
    {
        get;
        private init;
    }
    [EntityKey(EntityKeyKind.PartitionKey)]
    public string Biller
    {
        get;
        private init;
    } = String.Empty;
    [EntityKey(EntityKeyKind.RowKey)]
    public string InvoiceID
    {
        get;
        private init;
    } = String.Empty;
    [EntityKey(EntityKeyKind.ETag)]
    public string? ETag
    {
        get;
        init;
    }
    [EntityKey(EntityKeyKind.Timestamp)]
    public DateTimeOffset? Timestamp
    {
        get;
        init;
    }
    public string SubmissionID
    {
        get;
        private init;
    } = String.Empty;
    public string Provider
    {
        get;
        private init;
    } = String.Empty;
    public string ProviderMessageID
    {
        get;
        private init;
    } = String.Empty;
    public string? SubmittedEventID
    {
        get;
        init;
    }
    public DateTimeOffset? SubmittedAt
    {
        get;
        init;
    }
    public string? DeliveredEventID
    {
        get;
        init;
    }
    public DateTimeOffset? DeliveredAt
    {
        get;
        init;
    }
    public string? DelayEventID
    {
        get;
        init;
    }
    public DateTimeOffset? LastDelayedAt
    {
        get;
        init;
    }
    public string? FailureEventID
    {
        get;
        init;
    }
    public DateTimeOffset? LastFailureAt
    {
        get;
        init;
    }
    public int? LastFailureCode
    {
        get;
        init;
    }

    internal static StorageKey BuildKey(Guid biller, long invoice)
        => new(Invoice.BuildPartitionKey(biller), Invoice.BuildRowKey(invoice));

    internal static InvoiceNotificationDelivery Create(StorageKey key, string submissionID, string provider, string providerMessageID)
        => new()
        {
            Version = CurrentVersion,
            Biller = key.PartitionKey,
            InvoiceID = key.RowKey,
            SubmissionID = submissionID,
            Provider = provider,
            ProviderMessageID = providerMessageID,
        };
}
