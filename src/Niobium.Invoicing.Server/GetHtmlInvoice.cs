using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Niobium.Invoicing.Flows;

namespace Niobium.Invoicing.Server
{
    /// <summary>
    /// Public invoice HTML endpoint accepting an invoice-link token without a recipient account.
    /// </summary>
    /// <param name="flow">Application flow invoked by this trigger.</param>
    public class GetHTMLInvoice(HTMLFlow flow)
    {
        /// <summary>
        /// Checks basic link inputs and renders invoice HTML through the domain token policy.
        /// </summary>
        /// <param name="req">Incoming Functions HTTP request.</param>
        /// <param name="issuer">Issuing biller identifier.</param>
        /// <param name="invoice">Invoice identifier in reverse Unix milliseconds.</param>
        /// <param name="token">Supplied invoice-link token.</param>
        /// <param name="cancellationToken">Token forwarded to supported asynchronous operations.</param>
        /// <returns>404 for blank token, nonpositive ID or empty issuer; otherwise a 200 text/html result on success.</returns>
        /// <remarks>
        /// No recipient account is required. Domain/storage/formatting failures propagate to middleware or the
        /// host.
        /// </remarks>
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
