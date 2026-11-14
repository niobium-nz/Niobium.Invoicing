using Niobium.Invoicing.History;

namespace Niobium.Invoicing.Domains;

public partial class InvoiceDomain
{
    private InvoiceChangeOrigin? historyOrigin;

    public void SetChangeOrigin(InvoiceChangeOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (!Enum.IsDefined(origin.Authority) || String.IsNullOrWhiteSpace(origin.Actor)
            || origin.Actor.Length > 256 || origin.Actor != origin.Actor.Trim()
            || (origin.Reason != null && (String.IsNullOrWhiteSpace(origin.Reason) || origin.Reason.Length > 500 || origin.Reason != origin.Reason.Trim())))
            throw BadRequest("A bounded invoice change origin is required.");
        if (origin.Authority == InvoiceChangeAuthority.AuthenticatedBiller
            && (!Guid.TryParse(origin.Actor, out Guid actor) || actor == Guid.Empty
                || !Guid.TryParse(PartitionKey, out Guid biller) || actor != biller))
            throw new ApplicationException(InternalError.Forbidden, "The change actor must match the invoice biller.");
        historyOrigin = origin;
    }

    private async Task PersistWithHistoryAsync(Invoice next, InvoiceChangeKind kind, string reason,
        IReadOnlyList<InvoiceItem>? lines = null, InvoiceLineEvidence lineEvidence = InvoiceLineEvidence.Observed,
        InvoiceChangeAuthority authority = InvoiceChangeAuthority.UnattributedCoreCaller, string actor = "InvoiceDomain",
        CancellationToken cancellationToken = default)
    {
        if (authority == InvoiceChangeAuthority.UnattributedCoreCaller && historyOrigin != null)
        {
            authority = historyOrigin.Authority;
            actor = historyOrigin.Actor;
        }
        if (kind is InvoiceChangeKind.PreparationStarted or InvoiceChangeKind.PreparationCompleted && historyOrigin?.Reason != null)
            reason = historyOrigin.Reason;
        // A legacy row first acquires lifecycle state with this commit. Capture
        // that target state before ReferenceHistory adds only the verified link.
        next.StoreLifecycle(Lifecycle(next));
        Invoice current = await GetEntityAsync(cancellationToken);
        Guid? previous = current.ReadLifecycle()?.HistoryRevision;
        InvoiceRevisionSnapshot? priorSnapshot = null;
        if (previous.HasValue)
        {
            InvoiceHistoryDomain prior = await LoadRevisionAsync(current, previous.Value, cancellationToken);
            priorSnapshot = await prior.ReadSnapshotAsync(cancellationToken);
        }

        // Observe the actual lines even when invoice fields have not changed.
        // ETags and timestamps do not constitute a business-content change.
        InvoiceItem[] observed = await historyItems.GetAsync(InvoiceItem.BuildPartitionKey(current.GetID()),
            cancellationToken: cancellationToken).ToArrayAsync(cancellationToken);
        bool sameLines = priorSnapshot != null && InvoiceRevision.MatchesLines(priorSnapshot.Lines, observed);
        bool pendingLines = priorSnapshot?.LineEvidence == InvoiceLineEvidence.Proposed
            && current.GetPreparationStage() is InvoicePreparationStage.Preparing or InvoicePreparationStage.Retryable;
        bool stored = !String.IsNullOrWhiteSpace(current.ETag) || current.Timestamp != null;
        bool unexplained = priorSnapshot != null && (!InvoiceRevision.MatchesInvoice(priorSnapshot, current)
            || (!sameLines && !pendingLines));
        InvoiceRevision.Prepared? baseline = null;
        if (stored && (priorSnapshot == null || unexplained))
        {
            baseline = InvoiceRevision.CaptureWithinCapacity(current, observed, InvoiceLineEvidence.Observed,
                new(InvoiceChangeKind.ObservedBaseline, InvoiceChangeAuthority.UnattributedCoreCaller,
                    "Observed storage state", unexplained ? "Observed change outside the invoice history writer" : "First observed pre-history invoice state",
                    clock.GetUtcNow()), previous)
                ?? throw Conflict("Existing invoice history exceeds the supported snapshot size.");
            previous = baseline.Revision.ID;
        }
        if (lines == null)
        {
            // A failed preparation retains its proposed contents, not a claim that
            // partial item writes completed. Cancellation records what was observed.
            bool retainProposal = kind == InvoiceChangeKind.PreparationFailed && pendingLines;
            lines = retainProposal ? priorSnapshot!.Lines : observed;
            lineEvidence = retainProposal ? InvoiceLineEvidence.Proposed
                : baseline == null && sameLines && !pendingLines ? priorSnapshot!.LineEvidence : InvoiceLineEvidence.Observed;
        }
        else if (lineEvidence == InvoiceLineEvidence.Verified && !InvoiceRevision.MatchesLines(lines, observed))
            throw Conflict("Invoice lines changed before their history could be verified.");
        InvoiceRevision.Prepared prepared = InvoiceRevision.CaptureWithinCapacity(next, lines, lineEvidence,
            new(kind, authority, actor, reason, clock.GetUtcNow()), previous)
            ?? throw (kind == InvoiceChangeKind.PreparationStarted
                ? BadRequest("Requested invoice history exceeds the supported snapshot size.")
                : Conflict("Existing invoice history exceeds the supported snapshot size."));
        // Validate both captures before writing any history, including an observed
        // baseline. An oversized request leaves invoice, items and history unchanged.
        if (baseline != null)
            await RecordRevisionAsync(baseline, cancellationToken);
        await RecordRevisionAsync(prepared, cancellationToken);
        next.ReferenceHistory(prepared.Revision.ID);
        // The only commit point: a conditional invoice update references already
        // verified immutable records. Failed competing writes leave unreferenced
        // candidates, which readers never discover by scanning the history table.
        await PersistInvoiceAsync(next, cancellationToken);
    }

    private async Task RecordRevisionAsync(InvoiceRevision.Prepared prepared, CancellationToken cancellationToken)
    {
        InvoiceHistoryDomain domain = await history.BuildAsync(prepared.Revision, cancellationToken);
        await domain.RecordAsync(prepared, cancellationToken);
    }

    private async Task<InvoiceHistoryDomain> LoadRevisionAsync(Invoice invoice, Guid revision, CancellationToken cancellationToken)
    {
        StorageKey key = InvoiceRevision.Key(invoice.Biller, revision);
        InvoiceHistoryDomain domain = await history.GetAsync(key.PartitionKey, key.RowKey, forceLoad: true, cancellationToken: cancellationToken);
        InvoiceRevision stored = await domain.GetEntityAsync(cancellationToken);
        if (!stored.Describes(invoice.Biller, invoice.Created))
            throw Conflict("Invoice history does not belong to this invoice.");
        return domain;
    }
}
