using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Niobium.Invoicing.Flows;
using System.Net;

namespace Niobium.Invoicing.Functions
{
    public class EmailInvoice(EmailFlow flow)
    {
        [Function(nameof(EmailInvoice))]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", Route = "email/{issuer}/invoices/{invoice}")] HttpRequest req,
            Guid issuer,
            long invoice,
            CancellationToken cancellationToken)
        {
            bool success = await flow.RunAsync(issuer, invoice, cancellationToken);
            HttpStatusCode statuscode = success ? HttpStatusCode.Created : HttpStatusCode.InternalServerError;
            return new StatusCodeResult((int)statuscode);
        }
    }
}
