using Niobium;
using Niobium.Platform;
using Niobium.Profile;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using System.Security.Claims;
using FromBodyAttribute = Microsoft.Azure.Functions.Worker.Http.FromBodyAttribute;

namespace Niobium.Invoicing.Functions;

public class NewInvoice(
    IDomainRepository<InvoiceDomain, Invoice> repo,
    IProfileService<Biller> profileService,
    IRepository<Billee> billeeRepo)
{
    private static readonly TimeSpan InvoiceCreateTimeMaxOffset = TimeSpan.FromMinutes(30);

    [Function(nameof(NewInvoice))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "invoices")] HttpRequest req,
        [FromBody] IssueInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        if (!req.HttpContext.User.TryGetClaim<Guid>(ClaimTypes.NameIdentifier, out var user))
        {
            return new UnauthorizedResult();
        }

        if (request.BillerID != user)
        {
            return new ForbidResult("Biller does not match the authenticated user.");
        }

        request.TryValidate(out var validationState);
        if (!validationState.IsValid)
        {
            return validationState.MakeResponse();
        }

        var biller = await profileService.RetrieveAsync(cancellationToken: cancellationToken);
        if (biller == null)
        {
            return new NotFoundObjectResult("Biller does not exist.");
        }

        var billee = await billeeRepo.RetrieveAsync(
            Billee.BuildPartitionKey(request.BillerID),
            Billee.BuildRowKey(request.BilleeID),
            cancellationToken: cancellationToken);
        if (billee == null)
        {
            return new NotFoundObjectResult("Billee does not exist.");
        }

        var invoice = Invoice.BuildNew(request.ID, biller, billee);
        if (DateTimeOffset.UtcNow - invoice.Created > InvoiceCreateTimeMaxOffset)
        {
            return new ForbidResult("Invalid issue invoice request.");
        }

        var domain = await repo.BuildAsync(invoice, cancellationToken);
        await domain.UpdateAsync(request, request.InvoiceItems, cancellationToken);

        return new OkResult();
    }
}
