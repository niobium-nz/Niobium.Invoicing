using Azure;
using Niobium.Invoicing.Payments;

namespace Niobium.Invoicing.Domains;

public partial class InvoiceDomain
{
    private async Task<List<InvoicePaymentIndex>> PaymentPathAsync(Invoice invoice, string? root, long entries, Guid operation,
        Dictionary<string, InvoicePaymentIndex> cache, CancellationToken cancellationToken)
    {
        if (root == null)
            return [];
        List<InvoicePaymentIndex> path = [];
        InvoicePaymentIndex.Reference? expected = new(root, entries);
        try
        {
            while (expected != null)
            {
                // The model bounds node depth and forbids children at its leaf
                // limit; matching each step below also rejects cycles.
                if (!cache.TryGetValue(expected.ID, out InvoicePaymentIndex? node))
                {
                    StorageKey key = InvoicePaymentIndex.Key(invoice.Biller, expected.ID);
                    node = await paymentIndexRepository.RetrieveAsync(key.PartitionKey, key.RowKey, cancellationToken: cancellationToken)
                        ?? throw Conflict("A committed payment index node is missing.");
                    cache.Add(expected.ID, node);
                }
                if (!node.Describes(invoice) || node.ReadReference() != expected || node.Depth != path.Count)
                    throw Conflict("The committed payment index does not match the invoice.");
                path.Add(node);
                expected = node.Select(operation);
            }
            Guid? found = path[^1].ReadOperation();
            if (found.HasValue)
                for (int i = 0; i + 1 < path.Count; i++)
                    if (path[i].Select(found.Value) != path[i + 1].ReadReference())
                        throw Conflict("The committed payment index has an invalid operation route.");
        }
        catch (InvalidOperationException) { throw Conflict("The committed payment index representation is not supported."); }
        return path;
    }

    private async Task<InvoicePayment?> IndexedPaymentAsync(Invoice invoice, List<InvoicePaymentIndex> path,
        Guid operation, CancellationToken cancellationToken)
    {
        if (path.Count == 0 || path[^1].ReadOperation() != operation)
            return null;
        InvoicePaymentIndex leaf = path[^1];
        StorageKey key = leaf.GetPaymentKey();
        InvoicePayment receipt = await paymentRepository.RetrieveAsync(key.PartitionKey, key.RowKey, cancellationToken: cancellationToken)
            ?? throw Conflict("A committed payment receipt is missing.");
        try
        {
            InvoicePaymentEntry entry = receipt.Read();
            long reversed = leaf.ReadReversedCents();
            if (!leaf.References(receipt) || !receipt.Describes(invoice) || entry.AmountCents <= 0
                || entry.Currency != invoice.GrandTotalCurrency || entry.Actor != invoice.Biller || entry.OccurredAt > entry.RecordedAt
                || reversed > (entry.Kind == InvoicePaymentKind.Received ? entry.AmountCents : 0))
                throw Conflict("The committed payment evidence is inconsistent.");
        }
        catch (InvalidOperationException) { throw Conflict("The committed payment representation is not supported."); }
        return receipt;
    }

    private async Task VerifyPaymentHeadAsync(Invoice invoice, Dictionary<string, InvoicePaymentIndex> cache,
        CancellationToken cancellationToken)
    {
        InvoicePaymentPosition? position = Lifecycle(invoice).Payments;
        _ = PaymentBalance(invoice);
        if (position == null)
            return;
        try
        {
            InvoicePaymentPosting posting = await LoadPaymentPostingAsync(invoice, position.Head, cancellationToken);
            if (posting.ReadPosition() != position)
                throw Conflict("The committed payment posting does not match the invoice.");
            List<InvoicePaymentIndex> path = await PaymentPathAsync(invoice, position.Index, position.Sequence,
                position.Operation, cache, cancellationToken);
            InvoicePayment receipt = await IndexedPaymentAsync(invoice, path, position.Operation, cancellationToken)
                ?? throw Conflict("The latest payment is absent from the committed operation index.");
            if (!posting.References(receipt))
                throw Conflict("The committed posting and indexed receipt differ.");
            long before = position.OpeningSettledCents;
            if (posting.Previous != null)
            {
                InvoicePaymentPosting previous = await LoadPaymentPostingAsync(invoice, posting.Previous, cancellationToken);
                if (previous.Sequence != position.Sequence - 1 || previous.Currency != position.Currency
                    || previous.OpeningSettledCents != position.OpeningSettledCents || previous.SettledCents < before)
                    throw Conflict("The payment predecessor does not match its committed successor.");
                before = previous.SettledCents;
            }
            InvoicePaymentEntry entry = receipt.Read();
            long expected = checked(before + (entry.Kind == InvoicePaymentKind.Received ? entry.AmountCents : -entry.AmountCents));
            if (expected != position.SettledCents)
                throw Conflict("The committed payment does not explain the current balance.");
        }
        catch (InvalidOperationException) { throw Conflict("The committed payment representation is not supported."); }
        catch (OverflowException) { throw Conflict("The committed payment amounts exceed the supported range."); }
    }

    private async Task<InvoicePaymentPosting> LoadPaymentPostingAsync(Invoice invoice, string id, CancellationToken cancellationToken)
    {
        StorageKey key = InvoicePaymentPosting.Key(invoice.Biller, id);
        InvoicePaymentPosting posting = await paymentPostingRepository.RetrieveAsync(key.PartitionKey, key.RowKey, cancellationToken: cancellationToken)
            ?? throw Conflict("A committed payment posting is missing.");
        if (!posting.Describes(invoice) || posting.ID != id)
            throw Conflict("The committed payment posting belongs to another invoice.");
        return posting;
    }

    private async Task RecordPaymentIndexAsync(Invoice invoice, IEnumerable<InvoicePaymentIndex> proposed, CancellationToken cancellationToken)
    {
        foreach (InvoicePaymentIndex node in proposed.DistinctBy(node => node.ID))
        {
            try
            {
                await paymentIndexRepository.CreateAsync([node], replaceIfExist: false, cancellationToken: cancellationToken);
            }
            catch (RequestFailedException failure) when (failure.Status == 409) { }
            StorageKey key = node.GetKey();
            InvoicePaymentIndex stored = await paymentIndexRepository.RetrieveAsync(key.PartitionKey, key.RowKey, cancellationToken: cancellationToken)
                ?? throw Conflict("Payment index read-back is missing.");
            try
            {
                if (!stored.Describes(invoice) || stored.ReadReference() != node.ReadReference())
                    throw Conflict("Payment index read-back differs from the proposed change.");
            }
            catch (InvalidOperationException) { throw Conflict("The stored payment index is not supported."); }
        }
    }
}
