using Niobium.Invoicing.Domains;
using System.Text;

namespace Niobium.Invoicing.Flows;

/// <summary>
/// Assembles invoice HTML and all persisted line fragments without writing storage.
/// </summary>
/// <param name="invoiceRepo">Repository for invoice domains.</param>
/// <param name="itemRepo">Repository for enumerating line domains during rendering.</param>
public class HTMLFlow(
    IDomainRepository<InvoiceDomain, Invoice> invoiceRepo,
    IDomainRepository<InvoiceItemDomain, InvoiceItem> itemRepo) : IFlow
{
    /// <summary>
    /// Renders an invoice and replaces the marked template block with all persisted line fragments.
    /// </summary>
    /// <param name="issuer">Issuing biller identifier.</param>
    /// <param name="invoice">Invoice identifier in reverse Unix milliseconds.</param>
    /// <param name="token">Supplied invoice-link token.</param>
    /// <param name="cancellationToken">Token forwarded to supported asynchronous operations.</param>
    /// <returns>The assembled HTML document.</returns>
    /// <remarks>
    /// Domain token, resource and formatting failures propagate. Enumeration/rendering performs no storage
    /// writes.
    /// </remarks>
    public async Task<string> RunAsync(Guid issuer, long invoice, string token, CancellationToken cancellationToken)
    {
        InvoiceDomain domain = await invoiceRepo.GetAsync(
            Invoice.BuildPartitionKey(issuer),
            Invoice.BuildRowKey(invoice), cancellationToken: cancellationToken);
        string invoiceHTML = await domain.BuildHTMLAsync(token, cancellationToken);
        return await RenderItemsAsync(invoice, invoiceHTML, cancellationToken);
    }

    /// <summary>Renders persisted invoice data for a caller already authorized with a Functions key.</summary>
    /// <param name="issuer">Issuing biller identifier.</param>
    /// <param name="invoice">Invoice identifier in reverse Unix milliseconds.</param>
    /// <param name="cancellationToken">Token forwarded to storage and rendering.</param>
    /// <returns>The same encoded HTML used by public viewing, without invoice-link authorization.</returns>
    /// <remarks>
    /// Only the key-protected debug trigger may expose this flow over HTTP. It does not save, create a
    /// token, freeze, or notify. Separate invoice/item reads are a diagnostic view, not an atomic snapshot.
    /// </remarks>
    public virtual async Task<string> RunForDebugAsync(Guid issuer, long invoice, CancellationToken cancellationToken)
    {
        InvoiceDomain domain = await invoiceRepo.GetAsync(
            Invoice.BuildPartitionKey(issuer),
            Invoice.BuildRowKey(invoice), cancellationToken: cancellationToken);
        string invoiceHTML = await domain.BuildDebugHTMLAsync(cancellationToken);
        return await RenderItemsAsync(invoice, invoiceHTML, cancellationToken);
    }

    private async Task<string> RenderItemsAsync(long invoice, string invoiceHTML, CancellationToken cancellationToken)
    {
        IAsyncEnumerable<InvoiceItemDomain> items = itemRepo.GetAsync(InvoiceItem.BuildPartitionKey(invoice), cancellationToken: cancellationToken);
        StringBuilder itemsHtml = new();
        await foreach (InvoiceItemDomain item in items.WithCancellation(cancellationToken))
        {
            string itemHTML = await item.BuildHTMLAsync(cancellationToken);
            itemsHtml.Append(itemHTML);
        }

        string invoiceItemsPlaceholder = await InvoiceItemDomain.GetInvoiceLineTemplateAsync(cancellationToken);
        string result = invoiceHTML.Replace(invoiceItemsPlaceholder, itemsHtml.ToString());
        return result;
    }
}
