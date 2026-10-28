using System.Text.RegularExpressions;

namespace Niobium.Invoicing.Domains
{
    /// <summary>
    /// Renders one invoice line from the marked block in the embedded template.
    /// </summary>
    /// <param name="repository">Lazy storage repository for invoice lines.</param>
    /// <param name="eventHandlers">Domain event handlers passed to the base Niobium domain.</param>
    public partial class InvoiceItemDomain(
        Lazy<IRepository<InvoiceItem>> repository,
        IEnumerable<IDomainEventHandler<IDomain<InvoiceItem>>> eventHandlers)
            : GenericDomain<InvoiceItem>(repository, eventHandlers)
    {
        /// <summary>
        /// Process-wide cached invoice template used to extract line fragments.
        /// </summary>
        private static string? invoiceTemplate;
        /// <summary>
        /// Cached generated expression selecting the marked invoice-item block.
        /// </summary>
        private static readonly Regex InvoiceLineRegex = CreateInvoiceLineRegex();

        /// <summary>
        /// Formats the loaded line values for notification or HTML substitution.
        /// </summary>
        /// <param name="cancellationToken">Token forwarded to supported asynchronous operations.</param>
        /// <returns>The line template-key dictionary.</returns>
        public async Task<IReadOnlyDictionary<string, string>> BuildNotificationParametersAsync(CancellationToken cancellationToken = default)
        {
            InvoiceItem entity = await GetEntityAsync(cancellationToken);
            return entity.BuildTemplateParameters();
        }

        /// <summary>
        /// Substitutes formatted line values into the embedded invoice-item block.
        /// </summary>
        /// <param name="cancellationToken">Token forwarded to supported asynchronous operations.</param>
        /// <returns>The rendered line fragment.</returns>
        /// <remarks>
        /// Formatting/resource failures propagate; no persistence or notification occurs.
        /// </remarks>
        public async Task<string> BuildHTMLAsync(CancellationToken cancellationToken = default)
        {
            string html = await GetInvoiceLineTemplateAsync(cancellationToken);
            IReadOnlyDictionary<string, string> parameters = await BuildNotificationParametersAsync(cancellationToken);
            foreach (KeyValuePair<string, string> parameter in parameters)
            {
                html = html.Replace($"{{{{{parameter.Key}}}}}", parameter.Value);
            }
            return html;
        }

        /// <summary>
        /// Loads the cached invoice template and extracts its marked item block.
        /// </summary>
        /// <param name="cancellationToken">Token forwarded to supported asynchronous operations.</param>
        /// <returns>The block including its start/end marker comments.</returns>
        /// <exception cref="Niobium.ApplicationException">The embedded invoice resource or marked item block is missing.</exception>
        public static async Task<string> GetInvoiceLineTemplateAsync(CancellationToken cancellationToken)
        {
            invoiceTemplate ??= await R.GetEmbededResourceAsStringAsync(Constants.InvoiceTemplateResourceName, cancellationToken)
                ?? throw new ApplicationException(InternalError.InternalServerError, "Missing invoice template.");

            Match itemTemplateMatch = InvoiceLineRegex.Match(invoiceTemplate);
            return !itemTemplateMatch.Success
                ? throw new ApplicationException(InternalError.InternalServerError, "Missing invoice line template.")
                : itemTemplateMatch.Value;
        }

        /// <summary>
        /// Creates the generated expression matching the marked invoice-item block.
        /// </summary>
        /// <returns>The generated regex instance.</returns>
        /// <remarks>
        /// The greedy match spans the first start marker through the last end marker, including both comments.
        /// </remarks>
        [GeneratedRegex(@"<!-- INVOICE_ITEM START -->[\s\S]*<!-- INVOICE_ITEM END -->", RegexOptions.Compiled)]
        private static partial Regex CreateInvoiceLineRegex();
    }
}
