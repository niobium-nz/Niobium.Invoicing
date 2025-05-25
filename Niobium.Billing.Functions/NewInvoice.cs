using Cod;
using Cod.Platform.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using System.Security.Claims;
using System.Text.Json;
using ApplicationException = Cod.ApplicationException;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Niobium.Billing.Functions
{
    public class NewInvoice(IDomainRepository<InvoiceDomain, Invoice> repo, PrincipalParser principalParser)
    {
        private static readonly JsonSerializerOptions serializationOptions = new(JsonSerializerDefaults.Web);

        [Function(nameof(NewInvoice))]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "invoices")] HttpRequest req,
            CancellationToken cancellationToken)
        {
            var biller = await principalParser.GetClaimAsync<Guid>(req, ClaimTypes.NameIdentifier);
            var request = await JsonSerializer.DeserializeAsync<NewInvoiceRequest>(req.Body, options: serializationOptions, cancellationToken: cancellationToken);
            ArgumentNullException.ThrowIfNull(request);

            var domain = await repo.GetAsync(Invoice.BuildPartitionKey(biller), Invoice.BuildRowKey(request.Invoice.GetID()), cancellationToken: cancellationToken) ?? throw new ApplicationException(InternalError.NotFound);
            await domain.UpdateAsync(request.Invoice, request.InvoiceItems, cancellationToken);

            return new OkResult();
        }

    }

    public class NewInvoiceRequest
    {
        public required Invoice Invoice { get; set; }

        public required InvoiceItem[] InvoiceItems { get; set; }
    }
}
