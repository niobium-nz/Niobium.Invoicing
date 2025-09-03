using Niobium.Invoicing.Domains;
using Niobium.Profile;

namespace Niobium.Invoicing.Flows
{
    public class UpsertFlow(
        IDomainRepository<InvoiceDomain, Invoice> invoiceRepo,
        IRepository<InvoiceItem> itemRepo,
        IRepository<Billee> billeeRepo,
        IProfileService<Biller> profileService) : IFlow
    {
        public async Task RunAsync(IssueInvoiceRequest request, Billee? billee, CancellationToken cancellationToken)
        {
            Biller? biller = await profileService.RetrieveAsync(request.Tenant, request.BillerID, cancellationToken: cancellationToken)
                ?? throw new ApplicationException(InternalError.NotFound, "Biller does not exist.");

            billee ??= await billeeRepo.RetrieveAsync(
                Billee.BuildPartitionKey(request.BillerID),
                Billee.BuildRowKey(request.BilleeID),
                cancellationToken: cancellationToken);
            if (billee == null)
            {
                throw new ApplicationException(InternalError.NotFound, "Billee does not exist.");
            }

            Invoice newInvoice = Invoice.BuildNew(request.InvoiceID, biller, billee);
            InvoiceDomain domain = await invoiceRepo.BuildAsync(newInvoice, cancellationToken);
            await domain.UpdateAsync(request, request.InvoiceItems, cancellationToken);

            InvoiceItem[] existingInvoiceItems = await itemRepo.GetAsync(
                InvoiceItem.BuildPartitionKey(request.InvoiceID),
                cancellationToken: cancellationToken)
                .ToArrayAsync(cancellationToken: cancellationToken);
            if (existingInvoiceItems.Length > 0)
            {
                await itemRepo.DeleteAsync(existingInvoiceItems, cancellationToken: cancellationToken);
            }
            await itemRepo.CreateAsync(request.InvoiceItems, cancellationToken: cancellationToken);
        }
    }
}
