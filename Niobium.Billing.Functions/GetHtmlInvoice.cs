using Cod;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Niobium.Billing.Functions
{
    // 778585698149 -> d4433ce47bc0b9c6
    // 778586072671 -> 995c0a37c5da4a1e
    // 778586474963 -> b2a395c895a3d394
    // 778586722821 -> b7e16d0feacc0639
    // 778587221403 -> a0e72452bea8d1eb
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

            var domain = await repo.GetAsync(Invoice.BuildPartitionKey(issuer), Invoice.BuildRowKey(invoice), cancellationToken);
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
