using Niobium.Invoicing.Domains;
using System.Text;

namespace Niobium.Invoicing.Flows
{
    public class HTMLFlow(
        IDomainRepository<InvoiceDomain, Invoice> invoiceRepo,
        IDomainRepository<InvoiceItemDomain, InvoiceItem> itemRepo) : IFlow
    {
        public async Task<string> RunAsync(Guid issuer, long invoice, string token, CancellationToken cancellationToken)
        {
            InvoiceDomain domain = await invoiceRepo.GetAsync(
                Invoice.BuildPartitionKey(issuer),
                Invoice.BuildRowKey(invoice), cancellationToken: cancellationToken);
            string invoiceHTML = await domain.BuildHTMLAsync(token, cancellationToken);

            IAsyncEnumerable<InvoiceItemDomain> items = itemRepo.GetAsync(InvoiceItem.BuildPartitionKey(invoice), cancellationToken: cancellationToken);
            StringBuilder itemsHtml = new();
            await foreach (InvoiceItemDomain item in items)
            {
                string itemHTML = await item.BuildHTMLAsync(cancellationToken);
                itemsHtml.Append(itemHTML);
            }

            string invoiceItemsPlaceholder = await InvoiceItemDomain.GetInvoiceLineTemplateAsync(cancellationToken);
            string result = invoiceHTML.Replace(invoiceItemsPlaceholder, itemsHtml.ToString());
            return result;
        }
    }
}
