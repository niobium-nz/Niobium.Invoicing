using Cod;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using System.Net;

namespace Niobium.Billing.Functions
{
    public class EmailInvoice(IDomainRepository<InvoiceDomain, Invoice> repo)
    {
        [Function(nameof(EmailInvoice))]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "email/{issuer}/invoices/{invoice}")] HttpRequest req,
            Guid issuer,
            long invoice,
            CancellationToken cancellationToken)
        {
            var domain = await repo.GetAsync(Invoice.BuildPartitionKey(issuer), Invoice.BuildRowKey(invoice), cancellationToken);
            var success = await domain.SendHTMLEmailAsync(cancellationToken);
            var statuscode = success ? HttpStatusCode.Created : HttpStatusCode.InternalServerError;
            return new StatusCodeResult((int)statuscode);
        }
    }
}
