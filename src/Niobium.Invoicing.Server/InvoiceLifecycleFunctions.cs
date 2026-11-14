using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Niobium.Invoicing.Flows;
using Niobium.Platform;
using FromBodyAttribute = Microsoft.Azure.Functions.Worker.Http.FromBodyAttribute;

namespace Niobium.Invoicing.Server;

public sealed class InvoiceLifecycleFunctions(InvoiceLifecycleFlow flow)
{
    [Function(nameof(GetInvoiceStatus))]
    public async Task<IActionResult> GetInvoiceStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "{issuer}/invoices/{invoice}/status")] HttpRequest request,
        Guid issuer, long invoice, CancellationToken cancellationToken)
    {
        IActionResult? rejection = Authorize(request, issuer, invoice, out Guid tenant);
        if (rejection != null)
            return rejection;
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        return new OkObjectResult(await flow.GetAsync(issuer, invoice, tenant, cancellationToken));
    }

    [Function(nameof(CancelInvoice))]
    public async Task<IActionResult> CancelInvoice(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "{issuer}/invoices/{invoice}/cancel")] HttpRequest request,
        Guid issuer, long invoice, [FromBody] CancelInvoiceRequest? body, CancellationToken cancellationToken)
    {
        IActionResult? rejection = Authorize(request, issuer, invoice, out Guid tenant);
        if (rejection != null)
            return rejection;
        if (body == null)
            return new BadRequestResult();
        return new OkObjectResult(await flow.CancelAsync(issuer, invoice, tenant, body, cancellationToken));
    }

    [Function(nameof(ReconcileInvoicePaymentHistory))]
    public async Task<IActionResult> ReconcileInvoicePaymentHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "{issuer}/invoices/{invoice}/payment-history/reconcile")] HttpRequest request,
        Guid issuer, long invoice, [FromBody] ReconcileInvoicePaymentHistoryRequest? body, CancellationToken cancellationToken)
    {
        IActionResult? rejection = Authorize(request, issuer, invoice, out Guid tenant);
        if (rejection != null)
            return rejection;
        if (body == null)
            return new BadRequestResult();
        return new OkObjectResult(await flow.ReconcileAsync(issuer, invoice, tenant, body, cancellationToken));
    }

    private static IActionResult? Authorize(HttpRequest request, Guid biller, long invoice, out Guid tenant)
    {
        tenant = Guid.Empty;
        if (request.HttpContext.User.Identity?.IsAuthenticated != true
            || !request.HttpContext.User.TryGetClaim<Guid>(ClaimTypes.Sid, out Guid actor)
            || !request.HttpContext.User.TryGetClaim<Guid>(ClaimTypes.GroupSid, out tenant)
            || actor == Guid.Empty || tenant == Guid.Empty)
            return new UnauthorizedResult();
        if (actor != biller)
            return new StatusCodeResult(StatusCodes.Status403Forbidden);
        if (invoice <= 0)
            return new BadRequestResult();
        return null;
    }
}
