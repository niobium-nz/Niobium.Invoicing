using Niobium.Invoicing.Domains;
using Niobium.Invoicing.History;

namespace Niobium.Invoicing.Flows;

public sealed class InvoiceLifecycleFlow(IDomainRepository<InvoiceDomain, Invoice> invoices, TimeProvider clock) : IFlow
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
        return await domain.GetLifecycleStatusAsync(tenant, biller, cancellationToken);
    }

    public async Task<InvoiceLifecycleStatus> CancelAsync(Guid biller, long invoice, Guid tenant,
        CancelInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        InvoiceDomain domain = await LoadAsync(biller, invoice, cancellationToken);
        return await domain.CancelAsync(tenant, biller, request, clock.GetUtcNow(), cancellationToken);
    }

    public async Task<InvoiceLifecycleStatus> ReconcileAsync(Guid biller, long invoice, Guid tenant,
        ReconcileInvoicePaymentHistoryRequest request, CancellationToken cancellationToken = default)
    {
        InvoiceDomain domain = await LoadAsync(biller, invoice, cancellationToken);
        return await domain.ReconcilePaymentHistoryAsync(tenant, biller, request, clock.GetUtcNow(), cancellationToken);
    }

    private Task<InvoiceDomain> LoadAsync(Guid biller, long invoice, CancellationToken cancellationToken)
    {
        if (biller == Guid.Empty || invoice <= 0)
            throw new ApplicationException(InternalError.BadRequest, "A valid invoice identity is required.");
        return invoices.GetAsync(Invoice.BuildPartitionKey(biller), Invoice.BuildRowKey(invoice), forceLoad: true, cancellationToken: cancellationToken);
    }
}
