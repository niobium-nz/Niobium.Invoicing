using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Niobium.Invoicing.Flows;

namespace Niobium.Invoicing.Functions
{
    public class GetHTMLInvoice(HTMLFlow flow)
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

            string html = await flow.RunAsync(issuer, invoice, token, cancellationToken);
            return new ContentResult
            {
                Content = html,
                ContentType = "text/html",
                StatusCode = StatusCodes.Status200OK
            };
        }

    }
}
