using Cod;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Niobium.Billing
{
    public class GetHtmlInvoice(IDomainRepository<InvoiceDomain, Invoice> repo)
    {
        [Function(nameof(GetHtmlInvoice))]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "{issuer}/{invoice}/{token}")] HttpRequest req,
            Guid issuer,
            long invoice,
            string token)
        {
            var domain = await repo.GetAsync(Invoice.BuildPartitionKey(issuer), Invoice.BuildRowKey(invoice));
            var html = await domain.GetHtmlOutputAsync(token);

            return new ContentResult
            {
                Content = html,
                ContentType = "text/html",
                StatusCode = StatusCodes.Status200OK
            };
        }

    }
}
