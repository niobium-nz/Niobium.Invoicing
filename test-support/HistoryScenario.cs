using Azure;
using Moq;
using Niobium.Invoicing.History;

namespace Niobium.Invoicing.Testing;

internal sealed class HistoryScenario
{
    internal Dictionary<StorageKey, InvoiceRevision> Revisions { get; } = [];
    internal Dictionary<StorageKey, InvoiceRevisionPart> Parts { get; } = [];
    internal bool FailParts
    {
        get; set;
    }
    internal bool FailRevision
    {
        get; set;
    }
    internal Action? BeforePartWrite
    {
        get; set;
    }
    internal Action? BeforeRevisionWrite
    {
        get; set;
    }
    private readonly Mock<IRepository<InvoiceRevision>> revisions = new(MockBehavior.Strict);
    private readonly Mock<IRepository<InvoiceRevisionPart>> parts = new(MockBehavior.Strict);

    internal HistoryScenario()
    {
        revisions.Setup(r => r.CreateAsync(It.IsAny<IEnumerable<InvoiceRevision>>(), false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<InvoiceRevision> values, bool _, DateTimeOffset? __, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                BeforeRevisionWrite?.Invoke();
                if (FailRevision)
                    throw new TimeoutException("Synthetic unknown history revision write");
                return values.Select(value => Create(Revisions, value)).ToArray();
            });
        parts.Setup(r => r.CreateAsync(It.IsAny<IEnumerable<InvoiceRevisionPart>>(), false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<InvoiceRevisionPart> values, bool _, DateTimeOffset? __, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                BeforePartWrite?.Invoke();
                if (FailParts)
                    throw new TimeoutException("Synthetic unknown history part write");
                return values.Select(value => Create(Parts, value)).ToArray();
            });
        revisions.Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string partition, string id, IList<string>? _, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                return Revisions.TryGetValue(new(partition, id), out InvoiceRevision? value) ? Copy(value) : null;
            });
        parts.Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string partition, string id, IList<string>? _, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                return Parts.TryGetValue(new(partition, id), out InvoiceRevisionPart? value) ? Copy(value) : null;
            });
    }

    internal IDomainRepository<InvoiceHistoryDomain, InvoiceRevision> Domains()
        => new GenericDomainRepository<InvoiceHistoryDomain, InvoiceRevision>(
            () => new(new(() => revisions.Object), new(() => parts.Object), []), new(() => revisions.Object));

    internal IReadOnlyList<InvoiceRevision> Committed(Invoice invoice)
    {
        List<InvoiceRevision> result = [];
        Guid? id = invoice.ReadLifecycle()?.HistoryRevision;
        while (id.HasValue)
        {
            InvoiceRevision revision = Revisions[InvoiceRevision.Key(invoice.Biller, id.Value)];
            if (result.Any(item => item.ID == revision.ID))
                throw new InvalidOperationException("Synthetic history cycle");
            result.Add(revision);
            id = revision.Previous;
        }
        return result;
    }

    internal InvoiceRevisionSnapshot Snapshot(InvoiceRevision revision)
        => revision.ReadSnapshot(Enumerable.Range(0, revision.PartCount).Select(index => Parts[revision.GetPartKey(index)]).ToArray());

    private static T Create<T>(Dictionary<StorageKey, T> store, T value) where T : class, new()
    {
        T saved = Copy(value);
        StorageKey key = new(EntityMappingHelper.GetField<string>(saved, EntityKeyKind.PartitionKey),
            EntityMappingHelper.GetField<string>(saved, EntityKeyKind.RowKey));
        typeof(T).GetProperty("ETag")!.SetValue(saved, Guid.NewGuid().ToString("N"));
        typeof(T).GetProperty("Timestamp")!.SetValue(saved, DateTimeOffset.UtcNow);
        if (!store.TryAdd(key, saved))
            throw new RequestFailedException(409, "Synthetic immutable history already exists");
        return Copy(saved);
    }

    private static T Copy<T>(T value) where T : class, new()
    {
        T result = new();
        foreach (var field in EntityMappingHelper.GetMapping(typeof(T)).Values)
            field.SetValue(result, field.GetValue(value));
        return result;
    }
}
