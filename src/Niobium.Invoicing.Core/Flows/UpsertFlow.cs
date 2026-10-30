using Niobium.Invoicing.Domains;
using Niobium.Profile;

namespace Niobium.Invoicing.Flows
{
    /// <summary>
    /// Preserves invoice snapshots and coordinates conditional invoice/item preparation.
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
        /// Loads the current invoice, recognizes replays, and prepares changed content before replacing items.
        /// </summary>
        /// <param name="request">Invoice request to apply.</param>
        /// <param name="billee">Optional embedded customer snapshot; retrieved from storage when null.</param>
        /// <param name="cancellationToken">Token forwarded to supported asynchronous operations.</param>
        /// <returns>A task completing after the separate invoice and item operations.</returns>
        /// <remarks>
        /// Profiles are fetched for a new invoice; existing snapshots are preserved. Conditional checkpoints
        /// prevent overlapping writers. Item keys remain unchanged. Definite storage failures allow exact
        /// retries; interrupted or uncertain writes remain blocked for recovery. This is not a transaction.
        /// </remarks>
        /// <exception cref="Niobium.ApplicationException">The biller or customer cannot be found.</exception>
        public virtual async Task RunAsync(IssueInvoiceRequest request, Billee? billee, CancellationToken cancellationToken)
        {
            InvoiceValidation.ValidateRequest(request);
            InvoiceDomain domain = await invoiceRepo.GetAsync(
                Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID),
                forceLoad: true, cancellationToken: cancellationToken);
            Invoice? stored = await domain.TryGetEntityAsync(cancellationToken);
            if (stored == null)
            {
                Biller biller = await profileService.RetrieveAsync(request.Tenant, request.BillerID, cancellationToken: cancellationToken)
                    ?? throw new ApplicationException(InternalError.NotFound, "Biller does not exist.");
                billee ??= await billeeRepo.RetrieveAsync(Billee.BuildPartitionKey(request.BillerID),
                    Billee.BuildRowKey(request.BilleeID), cancellationToken: cancellationToken)
                    ?? throw new ApplicationException(InternalError.NotFound, "Billee does not exist.");
                InvoiceValidation.ValidateParties(request, biller, billee);
                domain = await invoiceRepo.BuildAsync(Invoice.BuildNew(request.InvoiceID, biller, billee), cancellationToken);
            }
            else
            {
                if (stored.Tenant != request.Tenant)
                {
                    throw new ApplicationException(InternalError.BadRequest, "Invoice tenant must match the request.");
                }
                if (InvoiceContent.IsFrozen(stored) && stored.Billee != request.BilleeID)
                {
                    throw new ApplicationException(InternalError.Conflict, "Notification content is frozen; the customer cannot change.");
                }
                if (billee == null && stored.Billee != request.BilleeID)
                {
                    billee = await billeeRepo.RetrieveAsync(Billee.BuildPartitionKey(request.BillerID),
                        Billee.BuildRowKey(request.BilleeID), cancellationToken: cancellationToken)
                        ?? throw new ApplicationException(InternalError.NotFound, "Billee does not exist.");
                }
                if (billee != null)
                {
                    InvoiceValidation.ValidateCustomer(request, billee, stored.GrandTotalCurrency);
                }
            }

            InvoiceItem[] existing = await itemRepo.GetAsync(InvoiceItem.BuildPartitionKey(request.InvoiceID),
                cancellationToken: cancellationToken).ToArrayAsync(cancellationToken);
            if (!await domain.UpdateAsync(request, existing, cancellationToken, billee))
            {
                return;
            }

            try
            {
                if (existing.Length > 0)
                {
                    await itemRepo.DeleteAsync(existing, preconditionCheck: true, successIfNotExist: true, cancellationToken: cancellationToken);
                }
                await itemRepo.CreateAsync(request.InvoiceItems, replaceIfExist: false, cancellationToken: cancellationToken);
                InvoiceItem[] persisted = await itemRepo.GetAsync(InvoiceItem.BuildPartitionKey(request.InvoiceID),
                    cancellationToken: cancellationToken).ToArrayAsync(cancellationToken);
                await domain.CompletePreparationAsync(persisted, cancellationToken);
            }
            catch (Exception failure) when (IsDefinitiveStorageFailure(failure))
            {
                // A timeout/cancellation can leave a writer running remotely. Only an explicit storage
                // rejection releases the claim; unknown outcomes require operator recovery.
                try
                {
                    await domain.FailPreparationAsync(cancellationToken);
                }
                catch (Exception checkpointFailure)
                {
                    throw new AggregateException("Item preparation failed and its recovery checkpoint could not be saved.", failure, checkpointFailure);
                }
                throw;
            }
        }

        private static bool IsDefinitiveStorageFailure(Exception failure) => failure switch
        {
            Azure.RequestFailedException error => error.Status is 400 or 404 or 409 or 412 or 413 or 422,
            HttpRequestException error => (int?)error.StatusCode is 400 or 404 or 409 or 412 or 413 or 422,
            _ => false
        };
    }
}
