using Niobium.Invoicing.Delivery;
using Niobium.Invoicing.Domains;
using Niobium.Invoicing.History;

namespace Niobium.Invoicing.Flows;

public sealed partial class InvoiceLifecycleFlow(IDomainRepository<InvoiceDomain, Invoice> invoices, TimeProvider clock,
    IDomainRepository<InvoiceNotificationDeliveryDomain, InvoiceNotificationDelivery> deliveries) : IFlow
{
    public async Task<InvoiceHistoryPage> GetHistoryAsync(Guid biller, long invoice, Guid tenant, string? continuation = null,
        CancellationToken cancellationToken = default)
    {
        InvoiceDomain domain = await LoadAsync(biller, invoice, cancellationToken);
        return await domain.ReadHistoryAsync(tenant, biller, continuation, cancellationToken);
    }

    public async Task<InvoiceLifecycleStatus> GetAsync(Guid biller, long invoice, Guid tenant, CancellationToken cancellationToken = default)
    {
        InvoiceDomain domain = await LoadAsync(biller, invoice, cancellationToken);
        InvoiceLifecycleStatus status = await domain.GetLifecycleStatusAsync(tenant, biller, cancellationToken);
        return await ReadDeliveryAsync(biller, invoice, status, cancellationToken);
    }

    public async Task<InvoiceLifecycleStatus> CancelAsync(Guid biller, long invoice, Guid tenant,
        CancelInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        InvoiceDomain domain = await LoadAsync(biller, invoice, cancellationToken);
        InvoiceLifecycleStatus status = await domain.CancelAsync(tenant, biller, request, clock.GetUtcNow(), cancellationToken);
        return await ReadDeliveryAsync(biller, invoice, status, cancellationToken);
    }

    public async Task<InvoiceLifecycleStatus> ReconcileAsync(Guid biller, long invoice, Guid tenant,
        ReconcileInvoicePaymentHistoryRequest request, CancellationToken cancellationToken = default)
    {
        InvoiceDomain domain = await LoadAsync(biller, invoice, cancellationToken);
        InvoiceLifecycleStatus status = await domain.ReconcilePaymentHistoryAsync(tenant, biller, request, clock.GetUtcNow(), cancellationToken);
        return await ReadDeliveryAsync(biller, invoice, status, cancellationToken);
    }

    private async Task<InvoiceLifecycleStatus> ReadDeliveryAsync(Guid biller, long invoice, InvoiceLifecycleStatus status,
        CancellationToken cancellationToken)
    {
        if (status.Preparation != InvoicePreparationStage.Frozen)
            return status;
        StorageKey key = InvoiceNotificationDelivery.BuildKey(biller, invoice);
        InvoiceNotificationDeliveryDomain delivery = await deliveries.GetAsync(key.PartitionKey, key.RowKey,
            forceLoad: true, cancellationToken: cancellationToken);
        return await delivery.ProjectAsync(status, cancellationToken);
    }

    private Task<InvoiceDomain> LoadAsync(Guid biller, long invoice, CancellationToken cancellationToken)
    {
        if (biller == Guid.Empty || invoice <= 0)
            throw new ApplicationException(InternalError.BadRequest, "A valid invoice identity is required.");
        return invoices.GetAsync(Invoice.BuildPartitionKey(biller), Invoice.BuildRowKey(invoice), forceLoad: true, cancellationToken: cancellationToken);
    }
}
