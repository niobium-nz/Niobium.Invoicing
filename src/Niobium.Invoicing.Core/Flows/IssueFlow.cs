namespace Niobium.Invoicing.Flows
{
    public class IssueFlow(UpsertFlow upsert, EmailFlow email) : IFlow
    {
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
