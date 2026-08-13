using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Niobium.Invoicing.Flows;
using Niobium.Platform;

namespace Niobium.Invoicing.Web.Controllers
{
    [ApiController]
    public class InvoiceController(HTMLFlow htmlFlow, IssueFlow issueFlow) : ControllerBase
    {
        private static readonly TimeSpan InvoiceCreateTimeMaxOffset = TimeSpan.FromMinutes(30);

        [HttpGet]
        [Route("{issuer}/invoices/{invoice}")]
        public async Task<IActionResult> Display(
            Guid issuer,
            long invoice,
            [FromQuery(Name = "token")] string token,
            CancellationToken cancellationToken)
        {
            if (String.IsNullOrWhiteSpace(token)
                || invoice <= 0
                || issuer == Guid.Empty)
            {
                return new NotFoundResult();
            }

            string html = await htmlFlow.RunAsync(issuer, invoice, token, cancellationToken);
            return new ContentResult
            {
                Content = html,
                ContentType = "text/html",
                StatusCode = StatusCodes.Status200OK
            };
        }

        [HttpPost]
        [Route("invoices")]
        public async Task<IActionResult> Create([FromBody] IssueInvoiceRequest request, CancellationToken cancellationToken)
        {
            if (!this.HttpContext.User.TryGetClaim(ClaimTypes.Sid, out Guid user))
            {
                return new UnauthorizedResult();
            }

            if (!this.HttpContext.User.TryGetClaim(ClaimTypes.GroupSid, out Guid tenant))
            {
                return new UnauthorizedResult();
            }

            if (request.BillerID != user)
            {
                return new ForbidResult("Biller does not match the authenticated user.");
            }

            if (request.Tenant != tenant)
            {
                return new ForbidResult("Tenant does not match the authenticated user.");
            }

            request.TryValidate(out ValidationState? validationState);
            if (!validationState.IsValid)
            {
                return validationState.MakeResponse();
            }

            DateTimeOffset invoiceCreateTime = DateTimeOffsetExtensions.FromReverseUnixTimeMilliseconds(request.InvoiceID);
            if (DateTimeOffset.UtcNow - invoiceCreateTime > InvoiceCreateTimeMaxOffset)
            {
                return new ForbidResult("Invalid issue invoice request.");
            }

            request.NotifyBillee = false;
            await issueFlow.RunAsync(request, null, cancellationToken);
            return new OkResult();
        }
    }
}
