using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using FromBodyAttribute = Microsoft.Azure.Functions.Worker.Http.FromBodyAttribute;

namespace Niobium.Invoicing.Server;

public sealed partial class InvoiceLifecycleFunctions
{
    [Function(nameof(GetInvoicePaymentBalance))]
    public async Task<IActionResult> GetInvoicePaymentBalance(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "{issuer}/invoices/{invoice}/payments/balance")] HttpRequest request,
        Guid issuer, long invoice, CancellationToken cancellationToken)
    {
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        IActionResult? rejection = Authorize(request, issuer, invoice, out Guid tenant);
        if (rejection != null)
            return rejection;
        return new OkObjectResult(await flow.GetPaymentBalanceAsync(issuer, invoice, tenant, cancellationToken));
    }

    [Function(nameof(GetInvoicePayment))]
    public async Task<IActionResult> GetInvoicePayment(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "{issuer}/invoices/{invoice}/payments/{operation:guid}")] HttpRequest request,
        Guid issuer, long invoice, Guid operation, CancellationToken cancellationToken)
    {
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        IActionResult? rejection = Authorize(request, issuer, invoice, out Guid tenant);
        if (rejection != null)
            return rejection;
        return new OkObjectResult(await flow.GetPaymentAsync(issuer, invoice, tenant, operation, cancellationToken));
    }

    [Function(nameof(RecordInvoicePayment))]
    public async Task<IActionResult> RecordInvoicePayment(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "{issuer}/invoices/{invoice}/payments")] HttpRequest request,
        Guid issuer, long invoice, [FromBody] RecordInvoicePaymentRequest? body, CancellationToken cancellationToken)
    {
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        IActionResult? rejection = Authorize(request, issuer, invoice, out Guid tenant);
        if (rejection != null)
            return rejection;
        if (body == null)
            return new BadRequestResult();
        return new OkObjectResult(await flow.RecordPaymentAsync(issuer, invoice, tenant, body, cancellationToken));
    }

    [Function(nameof(ReverseInvoicePayment))]
    public async Task<IActionResult> ReverseInvoicePayment(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "{issuer}/invoices/{invoice}/payments/reversals")] HttpRequest request,
        Guid issuer, long invoice, [FromBody] ReverseInvoicePaymentRequest? body, CancellationToken cancellationToken)
    {
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        IActionResult? rejection = Authorize(request, issuer, invoice, out Guid tenant);
        if (rejection != null)
            return rejection;
        if (body == null)
            return new BadRequestResult();
        return new OkObjectResult(await flow.ReversePaymentAsync(issuer, invoice, tenant, body, cancellationToken));
    }
}
