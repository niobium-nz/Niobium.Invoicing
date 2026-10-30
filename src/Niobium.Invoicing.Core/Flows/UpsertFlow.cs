using Niobium.Invoicing.Domains;
using Niobium.Profile;

namespace Niobium.Invoicing.Flows
{
    /// <summary>
    /// Rebuilds an invoice snapshot and replaces its line set using separate repository operations.
    /// </summary>
    /// <param name="invoiceRepo">Repository for invoice domains.</param>
    /// <param name="itemRepo">Repository for replacing persisted invoice lines.</param>
    /// <param name="billeeRepo">Repository for biller-scoped customer records.</param>
    /// <param name="profileService">External biller profile service.</param>
    public class UpsertFlow(
        IDomainRepository<InvoiceDomain, Invoice> invoiceRepo,
        IRepository<InvoiceItem> itemRepo,
        IRepository<Billee> billeeRepo,
        IProfileService<Biller> profileService) : IFlow
    {
        /// <summary>
        /// Builds and saves a fresh invoice snapshot, then deletes and recreates its supplied line set.
        /// </summary>
        /// <param name="request">Invoice request to apply.</param>
        /// <param name="billee">Optional embedded customer snapshot; retrieved from storage when null.</param>
        /// <param name="cancellationToken">Token forwarded to supported asynchronous operations.</param>
        /// <returns>A task completing after the separate invoice and item operations.</returns>
        /// <remarks>
        /// Retrieves the biller profile and retrieves the customer when no snapshot is supplied. Does not load
        /// existing invoice state, rewrite item parent keys or provide an atomic transaction/replay guard.
        /// Failures can leave partial updates.
        /// </remarks>
        /// <exception cref="Niobium.ApplicationException">The biller or customer cannot be found.</exception>
        public virtual async Task RunAsync(IssueInvoiceRequest request, Billee? billee, CancellationToken cancellationToken)
        {
            InvoiceValidation.ValidateRequest(request);
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

            InvoiceValidation.ValidateParties(request, biller, billee);
            Invoice newInvoice = Invoice.BuildNew(request.InvoiceID, biller, billee);
            InvoiceDomain domain = await invoiceRepo.BuildAsync(newInvoice, cancellationToken);
            await domain.UpdateAsync(request, request.InvoiceItems, cancellationToken);

            InvoiceItem[] existingInvoiceItems = await itemRepo.GetAsync(
                InvoiceItem.BuildPartitionKey(request.InvoiceID),
                cancellationToken: cancellationToken)
                .ToArrayAsync(cancellationToken: cancellationToken);
            if (existingInvoiceItems.Length > 0)
            {
                await itemRepo.DeleteAsync(existingInvoiceItems, successIfNotExist: true, cancellationToken: cancellationToken);
            }
            await itemRepo.CreateAsync(request.InvoiceItems, replaceIfExist: true, cancellationToken: cancellationToken);
        }
    }
}
