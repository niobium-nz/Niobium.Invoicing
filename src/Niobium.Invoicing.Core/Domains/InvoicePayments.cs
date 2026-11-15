using Azure;
using Niobium.Invoicing.History;
using Niobium.Invoicing.Payments;

namespace Niobium.Invoicing.Domains;

public partial class InvoiceDomain
{
    public Task<InvoicePaymentBalance> RecordPaymentAsync(Guid tenant, Guid actor, RecordInvoicePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ApplyPaymentAsync(tenant, actor, request.Operation, InvoicePaymentKind.Received, request.AmountCents,
            request.Currency, null, request.OccurredAt, request.Reason, request.Evidence, cancellationToken);
    }

    public Task<InvoicePaymentBalance> ReversePaymentAsync(Guid tenant, Guid actor, ReverseInvoicePaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ApplyPaymentAsync(tenant, actor, request.Operation, InvoicePaymentKind.Reversed, request.AmountCents,
            request.Currency, request.Reverses, request.OccurredAt, request.Reason, request.Evidence, cancellationToken);
    }

    public async Task<InvoicePaymentBalance> GetPaymentBalanceAsync(Guid tenant, Guid actor,
        CancellationToken cancellationToken = default)
    {
        Invoice current = await GetEntityAsync(cancellationToken);
        VerifyOwner(current, tenant, actor);
        await VerifyPaymentHeadAsync(current, [], cancellationToken);
        return PaymentBalance(current);
    }

    public async Task<InvoicePaymentEntry> GetPaymentAsync(Guid tenant, Guid actor, Guid operation,
        CancellationToken cancellationToken = default)
    {
        Invoice current = await GetEntityAsync(cancellationToken);
        VerifyOwner(current, tenant, actor);
        if (operation == Guid.Empty)
            throw BadRequest("A payment operation is required.");
        Dictionary<string, InvoicePaymentIndex> cache = [];
        await VerifyPaymentHeadAsync(current, cache, cancellationToken);
        InvoicePaymentPosition? position = Lifecycle(current).Payments;
        List<InvoicePaymentIndex> path = await PaymentPathAsync(current, position?.Index, position?.Sequence ?? 0, operation, cache, cancellationToken);
        InvoicePayment? receipt = await IndexedPaymentAsync(current, path, operation, cancellationToken);
        return receipt?.Read() ?? throw new ApplicationException(InternalError.NotFound, "The payment operation is not committed to this invoice.");
    }

    private async Task<InvoicePaymentBalance> ApplyPaymentAsync(Guid tenant, Guid actor, Guid operation,
        InvoicePaymentKind kind, long amountCents, string? currency, Guid? reverses, DateTimeOffset occurredAt,
        string? reason, string? evidence, CancellationToken cancellationToken)
    {
        Invoice current = await GetEntityAsync(cancellationToken);
        VerifyOwner(current, tenant, actor);
        RequireConditionalVersion(current);
        InvoiceLifecycleSnapshot lifecycle = Lifecycle(current);
        DateTimeOffset now = clock.GetUtcNow();
        string normalizedCurrency = currency?.Trim().ToUpperInvariant() ?? String.Empty;
        if (operation == Guid.Empty || amountCents <= 0 || normalizedCurrency is not ("NZD" or "AUD")
            || normalizedCurrency != current.GrandTotalCurrency
            || occurredAt < new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero) || occurredAt > now
            || (kind == InvoicePaymentKind.Reversed && (reverses is null || reverses == Guid.Empty || reverses == operation)))
            throw BadRequest("A positive payment amount, matching currency, past occurrence and valid operation are required.");
        InvoicePaymentEntry requested = new(operation, kind, amountCents, normalizedCurrency, reverses,
            occurredAt.ToUniversalTime(), actor, RequiredEvidence(reason, 500), RequiredEvidence(evidence, 1000), now);
        InvoicePayment? receipt = await FindPaymentReceiptAsync(current, operation, cancellationToken);
        if (receipt != null && !receipt.Matches(requested))
            throw Conflict("The payment operation already has different evidence.");

        Dictionary<string, InvoicePaymentIndex> indexCache = [];
        await VerifyPaymentHeadAsync(current, indexCache, cancellationToken);
        InvoicePaymentBalance before = PaymentBalance(current);
        List<InvoicePaymentIndex> path = await PaymentPathAsync(current, lifecycle.Payments?.Index, before.Entries,
            operation, indexCache, cancellationToken);
        InvoicePayment? committed = await IndexedPaymentAsync(current, path, operation, cancellationToken);
        InvoicePaymentEntry? prior = committed?.Read();
        if (prior != null)
        {
            if (prior with
            {
                RecordedAt = requested.RecordedAt
            } != requested)
                throw Conflict("The committed payment operation has different evidence.");
            return before;
        }
        if (lifecycle.Cancellation == null && current.GetPreparationStage() == InvoicePreparationStage.Preparing)
            throw Conflict("Invoice preparation must finish before recording a payment.");
        InvoicePaymentIndex.Prepared? reversalIndex = null;
        if (kind == InvoicePaymentKind.Reversed)
        {
            List<InvoicePaymentIndex> originalPath = await PaymentPathAsync(current, lifecycle.Payments?.Index, before.Entries,
                reverses!.Value, indexCache, cancellationToken);
            InvoicePayment? originalReceipt = await IndexedPaymentAsync(current, originalPath, reverses.Value, cancellationToken);
            InvoicePaymentEntry? original = originalReceipt?.Read();
            long alreadyReversed = originalReceipt == null ? 0 : originalPath[^1].ReadReversedCents();
            if (original == null || original.Kind != InvoicePaymentKind.Received || amountCents > original.AmountCents - alreadyReversed)
                throw Conflict("The reversal exceeds the remaining amount of the committed receipt.");
            if (occurredAt < original.OccurredAt)
                throw BadRequest("A reversal cannot precede its receipt.");
            reversalIndex = InvoicePaymentIndex.Capture(current, originalPath, originalReceipt!, alreadyReversed + amountCents);
            foreach (InvoicePaymentIndex node in reversalIndex.Nodes)
                indexCache[node.ID] = node;
            path = await PaymentPathAsync(current, reversalIndex.Root.ID, before.Entries, operation, indexCache, cancellationToken);
        }
        long settled;
        try
        {
            settled = checked(before.SettledCents + (kind == InvoicePaymentKind.Received ? amountCents : -amountCents));
            _ = MakePaymentBalance(current, before.OpeningSettledCents, settled, checked(before.Entries + 1));
        }
        catch (OverflowException) { throw BadRequest("The payment exceeds the supported monetary range."); }

        // Receipts reserve operation identity; candidate postings bind that exact
        // evidence to a predecessor. Neither applies money until the invoice write.
        receipt ??= await RecordPaymentReceiptAsync(current, requested, cancellationToken);
        InvoicePaymentIndex.Prepared nextIndex = InvoicePaymentIndex.Capture(current, path, receipt, 0);
        if (nextIndex.Root.Entries != before.Entries + 1)
            throw Conflict("The candidate payment index has an unexpected operation count.");
        IEnumerable<InvoicePaymentIndex> indexNodes = (reversalIndex?.Nodes ?? []).Concat(nextIndex.Nodes);
        await RecordPaymentIndexAsync(current, indexNodes, cancellationToken);
        InvoicePaymentPosting posting = InvoicePaymentPosting.Capture(receipt, lifecycle.Payments,
            before.OpeningSettledCents, settled, nextIndex.Root.ID);
        await RecordPaymentPostingAsync(current, posting, receipt, cancellationToken);
        Invoice next = InvoiceContent.Copy(current);
        next.StoreLifecycle(lifecycle with
        {
            PaymentHistory = InvoicePaymentHistory.PaymentOccurred,
            PaymentObservedAt = lifecycle.PaymentObservedAt ?? now
        });
        await PersistWithHistoryAsync(next, kind == InvoicePaymentKind.Received
            ? InvoiceChangeKind.PaymentReceived : InvoiceChangeKind.PaymentReversed, requested.Reason,
            authority: InvoiceChangeAuthority.AuthenticatedBiller, actor: actor.ToString(),
            cancellationToken: cancellationToken, payments: posting.ReadPosition());
        return PaymentBalance(next);
    }

    private async Task<InvoicePayment?> FindPaymentReceiptAsync(Invoice invoice, Guid operation, CancellationToken cancellationToken)
    {
        StorageKey key = InvoicePayment.Key(invoice.Biller, invoice.GetID(), operation);
        InvoicePayment? receipt = await paymentRepository.RetrieveAsync(key.PartitionKey, key.RowKey, cancellationToken: cancellationToken);
        if (receipt == null)
            return null;
        try
        {
            if (!receipt.Describes(invoice) || receipt.Operation != operation || receipt.ID != key.RowKey)
                throw Conflict("Payment evidence does not belong to this invoice operation.");
            return receipt;
        }
        catch (InvalidOperationException) { throw Conflict("The stored payment representation is not supported."); }
    }

    private async Task<InvoicePayment> RecordPaymentReceiptAsync(Invoice invoice, InvoicePaymentEntry entry, CancellationToken cancellationToken)
    {
        InvoicePayment proposed = InvoicePayment.Capture(invoice, entry);
        try
        {
            await paymentRepository.CreateAsync([proposed], replaceIfExist: false, cancellationToken: cancellationToken);
        }
        catch (RequestFailedException failure) when (failure.Status == 409)
        {
            // A competing conditional create is resolved only by reading the
            // immutable operation. Unknown outcomes propagate for an exact retry.
        }
        InvoicePayment stored = await FindPaymentReceiptAsync(invoice, entry.Operation, cancellationToken)
            ?? throw Conflict("Payment receipt read-back is missing.");
        if (!stored.Matches(entry))
            throw Conflict("The payment operation already has different evidence.");
        return stored;
    }

    private async Task RecordPaymentPostingAsync(Invoice invoice, InvoicePaymentPosting proposed, InvoicePayment receipt,
        CancellationToken cancellationToken)
    {
        try
        {
            await paymentPostingRepository.CreateAsync([proposed], replaceIfExist: false, cancellationToken: cancellationToken);
        }
        catch (RequestFailedException failure) when (failure.Status == 409) { }
        StorageKey key = proposed.GetKey();
        InvoicePaymentPosting stored = await paymentPostingRepository.RetrieveAsync(key.PartitionKey, key.RowKey, cancellationToken: cancellationToken)
            ?? throw Conflict("Payment posting read-back is missing.");
        try
        {
            if (!stored.Describes(invoice) || stored.ReadPosition() != proposed.ReadPosition() || !stored.References(receipt))
                throw Conflict("Payment posting read-back differs from the proposed change.");
        }
        catch (InvalidOperationException) { throw Conflict("The stored payment posting is not supported."); }
    }

    private static InvoicePaymentBalance PaymentBalance(Invoice invoice)
    {
        InvoicePaymentPosition? position = Lifecycle(invoice).Payments;
        long opening = position?.OpeningSettledCents ?? invoice.SettledCents;
        long settled = position?.SettledCents ?? opening;
        if (opening < 0 || settled < opening || (position != null
            && (position.Currency != invoice.GrandTotalCurrency || opening != invoice.SettledCents)))
            throw Conflict("The invoice settlement no longer matches its payment ledger.");
        try
        {
            return MakePaymentBalance(invoice, opening, settled, position?.Sequence ?? 0);
        }
        catch (OverflowException) { throw Conflict("The invoice balance exceeds the supported monetary range."); }
    }

    private static InvoicePaymentBalance MakePaymentBalance(Invoice invoice, long opening, long settled, long entries)
    {
        long difference = checked(invoice.GrandTotalCents - settled);
        return new(invoice.GrandTotalCurrency, opening, settled, Math.Max(0, difference),
            difference < 0 ? checked(-difference) : 0, entries);
    }
}
