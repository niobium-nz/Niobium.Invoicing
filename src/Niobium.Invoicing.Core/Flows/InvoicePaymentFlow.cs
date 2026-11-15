using Niobium.Invoicing.Domains;

namespace Niobium.Invoicing.Flows;

public sealed partial class InvoiceLifecycleFlow
{
    public async Task<InvoicePaymentBalance> GetPaymentBalanceAsync(Guid biller, long invoice, Guid tenant,
        CancellationToken cancellationToken = default)
    {
        InvoiceDomain domain = await LoadAsync(biller, invoice, cancellationToken);
        return await domain.GetPaymentBalanceAsync(tenant, biller, cancellationToken);
    }

    public async Task<InvoicePaymentEntry> GetPaymentAsync(Guid biller, long invoice, Guid tenant, Guid operation,
        CancellationToken cancellationToken = default)
    {
        InvoiceDomain domain = await LoadAsync(biller, invoice, cancellationToken);
        return await domain.GetPaymentAsync(tenant, biller, operation, cancellationToken);
    }

    public async Task<InvoicePaymentBalance> RecordPaymentAsync(Guid biller, long invoice, Guid tenant,
        RecordInvoicePaymentRequest request, CancellationToken cancellationToken = default)
    {
        InvoiceDomain domain = await LoadAsync(biller, invoice, cancellationToken);
        return await domain.RecordPaymentAsync(tenant, biller, request, cancellationToken);
    }

    public async Task<InvoicePaymentBalance> ReversePaymentAsync(Guid biller, long invoice, Guid tenant,
        ReverseInvoicePaymentRequest request, CancellationToken cancellationToken = default)
    {
        InvoiceDomain domain = await LoadAsync(biller, invoice, cancellationToken);
        return await domain.ReversePaymentAsync(tenant, biller, request, cancellationToken);
    }
}
