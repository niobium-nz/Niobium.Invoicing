namespace Niobium.Invoicing.Flows
{
    /// <summary>
    /// Coordinates invoice persistence followed by optional notification enqueueing.
    /// </summary>
    /// <param name="upsert">Flow that persists invoice and items.</param>
    /// <param name="email">Flow that queues notification after persistence.</param>
    public class IssueFlow(UpsertFlow upsert, EmailFlow email) : IFlow
    {
        /// <summary>
        /// Persists the invoice first, then queues a notification when requested.
        /// </summary>
        /// <param name="request">Invoice request to apply.</param>
        /// <param name="billee">Optional embedded customer snapshot; retrieved from storage when null.</param>
        /// <param name="cancellationToken">Token forwarded to supported asynchronous operations.</param>
        /// <returns>A task completing after the selected child flows.</returns>
        /// <remarks>
        /// An upsert failure prevents the notification call; a later failure does not roll back earlier
        /// persistence.
        /// </remarks>
        public async Task RunAsync(IssueInvoiceRequest request, Billee? billee, CancellationToken cancellationToken)
        {
            await upsert.RunAsync(request, billee, cancellationToken);
            if (request.NotifyBillee)
            { 
                await email.RunAsync(request.BillerID, request.InvoiceID, cancellationToken);
            }
        }
    }
}
