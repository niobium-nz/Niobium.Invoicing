using System;
using System.Text.RegularExpressions;

namespace Niobium.Invoicing.Domains
{
    public partial class InvoiceItemDomain(
        Lazy<IRepository<InvoiceItem>> repository,
        IEnumerable<IDomainEventHandler<IDomain<InvoiceItem>>> eventHandlers)
            : GenericDomain<InvoiceItem>(repository, eventHandlers)
    {
        private static string? invoiceTemplate;
        private static readonly Regex InvoiceLineRegex = CreateInvoiceLineRegex();

        public async Task<string> BuildHTMLAsync(CancellationToken cancellationToken = default)
        {
            string itemTemplate = await GetInvoiceLineTemplateAsync(cancellationToken);
            InvoiceItem entity = await GetEntityAsync(cancellationToken);
            var parameters = entity.BuildTemplateParameters();
            var html = itemTemplate;
            foreach (var parameter in parameters)
            {
                html = html.Replace($"{{{{{parameter.Key}}}}}", parameter.Value);
            }
            return html;
        }

        public static async Task<string> GetInvoiceLineTemplateAsync(CancellationToken cancellationToken)
        {
            invoiceTemplate ??= await R.GetEmbededResourceAsStringAsync(Constants.InvoiceTemplateResourceName, cancellationToken)
                ?? throw new ApplicationException(InternalError.InternalServerError, "Missing invoice template.");

            Match itemTemplateMatch = InvoiceLineRegex.Match(invoiceTemplate);
            return !itemTemplateMatch.Success
                ? throw new ApplicationException(InternalError.InternalServerError, "Missing invoice line template.")
                : itemTemplateMatch.Value;
        }

        [GeneratedRegex(@"<!-- INVOICE_ITEM START -->[\s\S]*<!-- INVOICE_ITEM END -->", RegexOptions.Compiled)]
        private static partial Regex CreateInvoiceLineRegex();
    }
}
