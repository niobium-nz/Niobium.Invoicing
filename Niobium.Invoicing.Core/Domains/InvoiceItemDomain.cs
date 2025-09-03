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
            return entity.BuildHTML(itemTemplate);
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

        [GeneratedRegex(@"<!-- Invoice Line Start -->[\s\S]*<!-- Invoice Line End -->", RegexOptions.Compiled)]
        private static partial Regex CreateInvoiceLineRegex();
    }
}
