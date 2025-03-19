using Cod;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Niobium.Billing
{
    public class GetHTMLInvoice(IDomainRepository<InvoiceDomain, Invoice> repo)
    {
        [Function(nameof(GetHTMLInvoice))]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "{issuer}/invoices/{invoice}")] HttpRequest req,
            Guid issuer,
            long invoice,
            [FromQuery(Name = "token")] string token)
        {
            if (string.IsNullOrWhiteSpace(token)
                || invoice <= 0
                || issuer == Guid.Empty)
            {
                return new NotFoundResult();
            }

            var domain = await repo.GetAsync(Invoice.BuildPartitionKey(issuer), Invoice.BuildRowKey(invoice));
            var html = await domain.GetHTMLOutputAsync(token);

            return new ContentResult
            {
                Content = html,
                ContentType = "text/html",
                StatusCode = StatusCodes.Status200OK
            };
        }

    }
}
