using System.Net;
using Microsoft.AspNetCore.Mvc;
using Niobium.Invoicing.Flows;

namespace Niobium.Invoicing.Web.Controllers
{
    [ApiController]
    [Route("[controller]/{issuer}/invoices/{invoice}")]
    public class EmailController(EmailFlow flow) : ControllerBase
    {
        [HttpGet]
        public async Task<IActionResult> Action(
            Guid issuer,
            long invoice,
            CancellationToken cancellationToken)
        {
            await flow.RunAsync(issuer, invoice, cancellationToken);
            return new StatusCodeResult((int)HttpStatusCode.Created);
        }
    }
}
