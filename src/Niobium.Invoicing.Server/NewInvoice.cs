using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Niobium.Invoicing.Flows;
using Niobium.Platform;
using System.Security.Claims;
using FromBodyAttribute = Microsoft.Azure.Functions.Worker.Http.FromBodyAttribute;

namespace Niobium.Invoicing.Server;

/// <summary>
/// HTTP issuance endpoint that checks biller/group claims and disables notification for this path.
/// </summary>
/// <param name="flow">Application flow invoked by this trigger.</param>
public class NewInvoice(IssueFlow flow)
{
    /// <summary>
    /// Maximum accepted invoice age on the HTTP issuance path; future timestamps are not checked
    /// symmetrically.
    /// </summary>
    private static readonly TimeSpan InvoiceCreateTimeMaxOffset = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Checks biller/group claims and invoice age, then issues without notification.
    /// </summary>
    /// <param name="req">Incoming Functions HTTP request.</param>
    /// <param name="request">Invoice request to apply.</param>
    /// <param name="cancellationToken">Token forwarded to supported asynchronous operations.</param>
    /// <returns>An empty success result or an entry-check rejection result.</returns>
    /// <remarks>
    /// Checks only the upper age limit of 30 minutes. NotifyBillee is reset to false. ForbidResult string
    /// arguments are authentication schemes; final host rejection behavior is not integration-verified.
    /// </remarks>
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

        request.NotifyBillee = false;
        await flow.RunAsync(request, null, cancellationToken);
        return new OkResult();
    }
}
