using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Niobium.Invoicing.Flows;

namespace Niobium.Invoicing.Server;

/// <summary>Read-only invoice HTML for operators authorized with a Functions key.</summary>
/// <param name="flow">Shared HTML renderer with an explicit debug entry point.</param>
public class DebugInvoice(HTMLFlow flow)
{
    /// <summary>Views a saved invoice without requiring or generating an invoice-link token.</summary>
    /// <param name="req">Request authorized by the Functions host.</param>
    /// <param name="issuer">Issuing biller identifier.</param>
    /// <param name="invoice">Invoice identifier in reverse Unix milliseconds.</param>
    /// <param name="cancellationToken">Token forwarded to reads and rendering.</param>
    /// <returns>200 HTML, or 404 for invalid identifiers; domain failures propagate.</returns>
    /// <remarks>
    /// The function key grants operational access across billers. Public token validation is separate.
    /// This endpoint never saves, freezes, generates a token, or sends a notification.
    /// </remarks>
    [Function(nameof(DebugInvoice))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "debug/{issuer}/invoices/{invoice}")] HttpRequest req,
        Guid issuer,
        long invoice,
        CancellationToken cancellationToken)
    {
        req.HttpContext.Response.Headers.CacheControl = "no-store";
        if (issuer == Guid.Empty || invoice <= 0)
        {
            return new NotFoundResult();
        }

        string html = await flow.RunForDebugAsync(issuer, invoice, cancellationToken);
        return new ContentResult
        {
            Content = html,
            ContentType = "text/html",
            StatusCode = StatusCodes.Status200OK
        };
    }
}
