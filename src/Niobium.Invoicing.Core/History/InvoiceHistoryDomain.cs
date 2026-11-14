namespace Niobium.Invoicing.History;

/// <summary>Records immutable history candidates and verifies them before an invoice may reference one.</summary>
public sealed class InvoiceHistoryDomain(
    Lazy<IRepository<InvoiceRevision>> revisions,
    Lazy<IRepository<InvoiceRevisionPart>> parts,
    IEnumerable<IDomainEventHandler<IDomain<InvoiceRevision>>> eventHandlers)
    : GenericDomain<InvoiceRevision>(revisions, eventHandlers)
{
    public async Task RecordAsync(InvoiceRevision.Prepared prepared, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        InvoiceRevision current = await GetEntityAsync(cancellationToken);
        if (current.ID != prepared.Revision.ID || current.Biller != prepared.Revision.Biller
            || !String.IsNullOrWhiteSpace(current.ETag) || current.Timestamp != null)
            throw new ApplicationException(InternalError.Conflict, "A new immutable invoice revision is required.");
        _ = current.ReadSnapshot(prepared.Parts);

        // Every write is a conditional create. Parts are complete before the
        // manifest; both remain uncommitted until the invoice's own ETag update.
        await parts.Value.CreateAsync(prepared.Parts, replaceIfExist: false, cancellationToken: cancellationToken);
        await SaveAsync(cancellationToken: cancellationToken);
        await ReloadAsync(cancellationToken);
        InvoiceRevision persisted = await GetEntityAsync(cancellationToken);
        if (persisted.ContentDigest != current.ContentDigest || persisted.Previous != current.Previous
            || persisted.ReadChange() != current.ReadChange() || !persisted.Describes(current.Biller, current.InvoiceCreated))
            throw new ApplicationException(InternalError.Conflict, "Invoice history read-back does not match the intended revision.");
        _ = await ReadSnapshotAsync(cancellationToken);
    }

    public async Task<InvoiceRevisionSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken = default)
    {
        InvoiceRevision revision = await GetEntityAsync(cancellationToken);
        _ = revision.ReadChange(); // Validate bounded part count before allocating or reading.
        InvoiceRevisionPart[] stored = new InvoiceRevisionPart[revision.PartCount];
        for (int index = 0; index < stored.Length; index++)
        {
            StorageKey key = revision.GetPartKey(index);
            stored[index] = await parts.Value.RetrieveAsync(key.PartitionKey, key.RowKey, cancellationToken: cancellationToken)
                ?? throw new ApplicationException(InternalError.Conflict, "An invoice history part is missing.");
        }
        return revision.ReadSnapshot(stored);
    }
}
