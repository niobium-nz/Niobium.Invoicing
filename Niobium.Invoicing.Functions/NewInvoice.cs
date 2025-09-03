using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Niobium.Invoicing.Flows;
using Niobium.Platform;
using System.Security.Claims;
using FromBodyAttribute = Microsoft.Azure.Functions.Worker.Http.FromBodyAttribute;

namespace Niobium.Invoicing.Functions;

public class NewInvoice(UpsertFlow flow)
{
    private static readonly TimeSpan InvoiceCreateTimeMaxOffset = TimeSpan.FromMinutes(30);

    [Function(nameof(NewInvoice))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "invoices")] HttpRequest req,
        [FromBody] IssueInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        if (!req.HttpContext.User.TryGetClaim<Guid>(ClaimTypes.Sid, out Guid user))
        {
            return new UnauthorizedResult();
        }

        if (!req.HttpContext.User.TryGetClaim<Guid>(ClaimTypes.GroupSid, out Guid tenant))
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

        await flow.RunAsync(request, null, cancellationToken);
        return new OkResult();
    }
}
