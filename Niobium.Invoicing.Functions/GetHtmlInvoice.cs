using Cod;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Niobium.Invoicing.Functions
{
    public class GetHTMLInvoice(IDomainRepository<InvoiceDomain, Invoice> repo)
    {
        [Function(nameof(GetHTMLInvoice))]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "{issuer}/invoices/{invoice}")] HttpRequest req,
            Guid issuer,
            long invoice,
            [FromQuery(Name = "token")] string token,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(token)
                || invoice <= 0
                || issuer == Guid.Empty)
            {
                return new NotFoundResult();
            }

            var domain = await repo.GetAsync(Invoice.BuildPartitionKey(issuer), Invoice.BuildRowKey(invoice), cancellationToken: cancellationToken);
            var html = await domain.GetHTMLOutputAsync(token, cancellationToken);

            return new ContentResult
            {
                Content = html,
                ContentType = "text/html",
                StatusCode = StatusCodes.Status200OK
            };
        }

    }
}
