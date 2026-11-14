using Niobium.Invoicing.History;

namespace Niobium.Invoicing.Domains;

public partial class InvoiceDomain
{
    public async Task<InvoiceLifecycleStatus> GetLifecycleStatusAsync(Guid tenant, Guid actor, CancellationToken cancellationToken = default)
    {
        Invoice current = await GetEntityAsync(cancellationToken);
        VerifyOwner(current, tenant, actor);
        return Status(current);
    }

    public async Task<InvoiceLifecycleStatus> CancelAsync(Guid tenant, Guid actor, CancelInvoiceRequest request,
        DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string reason = RequiredEvidence(request.Reason, 500);
        if (request.Operation == Guid.Empty || now == default)
            throw BadRequest("A stable cancellation operation is required.");
        Invoice current = await GetEntityAsync(cancellationToken);
        VerifyOwner(current, tenant, actor);
        InvoiceLifecycleSnapshot lifecycle = Lifecycle(current);
        if (lifecycle.Cancellation != null)
        {
            InvoiceCancellation existing = lifecycle.Cancellation;
            if (existing.Operation != request.Operation || existing.Actor != actor || existing.Reason != reason)
                throw Conflict("The invoice is already cancelled by another operation.");
            return Status(current);
        }
        InvoiceCancellationBlock block = CancellationBlock(current, lifecycle);
        if (block == InvoiceCancellationBlock.PaymentOccurred)
            throw Conflict("An invoice with any payment history cannot be cancelled.");
        if (block == InvoiceCancellationBlock.PaymentHistoryUnknown)
            throw Conflict("Reconcile unknown payment history before cancellation.");
        RequireConditionalVersion(current);
        Invoice next = InvoiceContent.Copy(current);
        next.StoreLifecycle(lifecycle with
        {
            Cancellation = new(request.Operation, actor, reason, now.ToUniversalTime())
        });
        // State and actor/reason/operation evidence are one conditional invoice
        // write. A failure cannot commit cancellation without its audit record.
        await PersistWithHistoryAsync(next, InvoiceChangeKind.Cancelled, reason,
            authority: InvoiceChangeAuthority.AuthenticatedBiller, actor: actor.ToString(), cancellationToken: cancellationToken);
        return Status(await GetEntityAsync(cancellationToken));
    }

    public async Task<InvoiceLifecycleStatus> ReconcilePaymentHistoryAsync(Guid tenant, Guid actor,
        ReconcileInvoicePaymentHistoryRequest request, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string reason = RequiredEvidence(request.Reason, 500);
        string evidence = RequiredEvidence(request.Evidence, 1000);
        if (request.Operation == Guid.Empty || now == default
            || request.Finding is not (InvoicePaymentHistory.NoPayments or InvoicePaymentHistory.PaymentOccurred))
            throw BadRequest("A stable payment-history reconciliation and finding are required.");
        Invoice current = await GetEntityAsync(cancellationToken);
        VerifyOwner(current, tenant, actor);
        InvoiceLifecycleSnapshot lifecycle = Lifecycle(current);
        InvoicePaymentHistoryDecision? prior = lifecycle.Reconciliations.FirstOrDefault(x => x.Operation == request.Operation);
        if (prior != null)
        {
            if (prior.Actor != actor || prior.Finding != request.Finding || prior.Reason != reason || prior.Evidence != evidence)
                throw Conflict("Reconciliation operation identity conflicts with its recorded evidence.");
            return Status(current);
        }
        if (lifecycle.Cancellation != null && request.Finding != InvoicePaymentHistory.PaymentOccurred)
            throw Conflict("A cancelled invoice can only record newly discovered payment evidence.");
        if (lifecycle.Cancellation == null && lifecycle.Preparation == InvoicePreparationStage.Preparing)
            throw Conflict("Complete or recover active invoice preparation before reconciling payment history.");
        InvoicePaymentHistory history = EffectiveHistory(current, lifecycle);
        if (history == InvoicePaymentHistory.PaymentOccurred)
            throw Conflict("Recorded payment history cannot be erased or reconciled away.");
        if (history == InvoicePaymentHistory.NoPayments && request.Finding == InvoicePaymentHistory.NoPayments)
            throw Conflict("Payment history is already known; no reconciliation transition is required.");
        RequireConditionalVersion(current);
        Invoice next = InvoiceContent.Copy(current);
        next.StoreLifecycle(lifecycle with
        {
            PaymentHistory = request.Finding,
            Reconciliations = [.. lifecycle.Reconciliations, new(request.Operation, actor, request.Finding, reason, evidence, now.ToUniversalTime())],
        });
        await PersistWithHistoryAsync(next, InvoiceChangeKind.PaymentHistoryReconciled, reason,
            authority: InvoiceChangeAuthority.AuthenticatedBiller, actor: actor.ToString(), cancellationToken: cancellationToken);
        return Status(await GetEntityAsync(cancellationToken));
    }

    public async Task<bool> CanQueueNotificationAsync(string token, CancellationToken cancellationToken = default)
    {
        // Re-read after preparing the message so an already committed cancellation
        // suppresses this enqueue. There is no atomic transaction with Service Bus
        // and no recall promise if cancellation races a send already in progress.
        await ReloadAsync(cancellationToken);
        Invoice current = await GetEntityAsync(cancellationToken);
        InvoiceLifecycleSnapshot lifecycle = Lifecycle(current);
        if (current.GetPreparationStage() != InvoicePreparationStage.Frozen || current.Token != token)
            throw Conflict("Notification does not match the frozen invoice.");
        return lifecycle.Cancellation == null;
    }

    private static InvoiceLifecycleSnapshot Lifecycle(Invoice invoice)
    {
        InvoiceLifecycleSnapshot lifecycle;
        try
        {
            lifecycle = invoice.ReadLifecycle() ?? InvoiceLifecycleSnapshot.Create(invoice.GetPreparationStage());
        }
        catch (InvalidOperationException) { throw Conflict("Invoice lifecycle version is not supported."); }
        if (!Enum.IsDefined(lifecycle.PaymentHistory) || !Enum.IsDefined(lifecycle.Delivery)
            || lifecycle.Reconciliations == null || lifecycle.Reconciliations.Count > 2
            || lifecycle.Reconciliations.Any(x => !ValidReconciliation(x))
            || lifecycle.Reconciliations.Select(x => x.Operation).Distinct().Count() != lifecycle.Reconciliations.Count
            || (lifecycle.Reconciliations.Count == 2
                && (lifecycle.Reconciliations[0].Finding != InvoicePaymentHistory.NoPayments
                    || lifecycle.Reconciliations[1].Finding != InvoicePaymentHistory.PaymentOccurred))
            || (lifecycle.PaymentHistory == InvoicePaymentHistory.Unknown && lifecycle.Reconciliations.Count != 0)
            || (lifecycle.Delivery == InvoiceDeliveryState.Delivered && lifecycle.ConfirmedDeliveredAt == null)
            || (lifecycle.Cancellation != null && !ValidCancellation(lifecycle.Cancellation)))
            throw Conflict("Invoice lifecycle state is not supported.");
        return lifecycle;
    }

    private static bool ValidReconciliation(InvoicePaymentHistoryDecision? decision)
        => decision != null && decision.Operation != Guid.Empty && decision.Actor != Guid.Empty
            && decision.At != default
            && decision.Finding is InvoicePaymentHistory.NoPayments or InvoicePaymentHistory.PaymentOccurred
            && ValidEvidence(decision.Reason, 500) && ValidEvidence(decision.Evidence, 1000);

    private static bool ValidCancellation(InvoiceCancellation cancellation)
        => cancellation.Operation != Guid.Empty && cancellation.Actor != Guid.Empty
            && cancellation.At != default && ValidEvidence(cancellation.Reason, 500);

    private static bool ValidEvidence(string? value, int maximum)
        => !String.IsNullOrWhiteSpace(value) && value.Length <= maximum && value == value.Trim();

    private static InvoiceLifecycleStatus Status(Invoice invoice)
    {
        InvoiceLifecycleSnapshot lifecycle = Lifecycle(invoice);
        return new(invoice.GetPreparationStage(), lifecycle.Delivery, EffectiveHistory(invoice, lifecycle),
            lifecycle.Cancellation != null, lifecycle.Cancellation?.At, lifecycle.QueuedAt ?? invoice.Delivered,
            lifecycle.ConfirmedDeliveredAt, CancellationBlock(invoice, lifecycle));
    }

    private static InvoicePaymentHistory EffectiveHistory(Invoice invoice, InvoiceLifecycleSnapshot lifecycle)
        => invoice.SettledCents != 0 || lifecycle.PaymentObservedAt != null
            || lifecycle.Reconciliations.Any(x => x.Finding == InvoicePaymentHistory.PaymentOccurred)
            ? InvoicePaymentHistory.PaymentOccurred : lifecycle.PaymentHistory;

    private static InvoiceCancellationBlock CancellationBlock(Invoice invoice, InvoiceLifecycleSnapshot lifecycle)
        => lifecycle.Cancellation != null ? InvoiceCancellationBlock.AlreadyCancelled
            : EffectiveHistory(invoice, lifecycle) switch
            {
                InvoicePaymentHistory.PaymentOccurred => InvoiceCancellationBlock.PaymentOccurred,
                InvoicePaymentHistory.Unknown => InvoiceCancellationBlock.PaymentHistoryUnknown,
                _ => InvoiceCancellationBlock.None,
            };

    private static void CheckActive(Invoice invoice)
    {
        if (Lifecycle(invoice).Cancellation != null)
            throw Conflict("A cancelled invoice cannot be changed or sent.");
    }

    private static void VerifyOwner(Invoice invoice, Guid tenant, Guid actor)
    {
        if (tenant == Guid.Empty || actor == Guid.Empty || invoice.Tenant != tenant || invoice.Biller != actor)
            throw new ApplicationException(InternalError.Forbidden, "Invoice ownership does not match the authenticated principal.");
    }

    private static void RequireConditionalVersion(Invoice invoice)
    {
        if (String.IsNullOrWhiteSpace(invoice.ETag) || invoice.ETag == "*")
            throw Conflict("A conditional invoice version is required.");
    }

    private static string RequiredEvidence(string? value, int maximum)
    {
        string result = value?.Trim() ?? String.Empty;
        if (result.Length == 0 || result.Length > maximum)
            throw BadRequest("Bounded nonempty reason and evidence are required.");
        return result;
    }

    private static ApplicationException BadRequest(string message) => new(InternalError.BadRequest, message);
}
