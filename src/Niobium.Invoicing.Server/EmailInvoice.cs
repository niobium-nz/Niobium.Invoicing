using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Niobium.Invoicing.Flows;
using System.Net;

namespace Niobium.Invoicing.Server
{
    /// <summary>
    /// Function-key-protected manual notification endpoint intended for debugging.
    /// </summary>
    /// <param name="flow">Application flow invoked by this trigger.</param>
    public class EmailInvoice(EmailFlow flow)
    {
        /// <summary>
        /// Invokes the notification flow behind Functions key authorization.
        /// </summary>
        /// <param name="req">Incoming Functions HTTP request.</param>
        /// <param name="issuer">Issuing biller identifier.</param>
        /// <param name="invoice">Invoice identifier in reverse Unix milliseconds.</param>
        /// <param name="cancellationToken">Token forwarded to supported asynchronous operations.</param>
        /// <returns>A status-only 201 result after flow completion, including a missing-recipient skip.</returns>
        /// <remarks>
        /// No application biller-principal check is made here. Intended for debugging; enqueueing is not
        /// confirmed email delivery.
        /// </remarks>
        [Function(nameof(EmailInvoice))]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "email/{issuer}/invoices/{invoice}")] HttpRequest req,
            Guid issuer,
            long invoice,
            CancellationToken cancellationToken)
        {
            await flow.RunAsync(issuer, invoice, cancellationToken);
            return new StatusCodeResult((int)HttpStatusCode.Created);
        }
    }
}
