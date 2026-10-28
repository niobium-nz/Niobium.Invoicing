using Microsoft.Extensions.Options;
using Niobium.Invoicing.Options;
using System.Globalization;

namespace Niobium.Invoicing.Domains
{
    /// <summary>
    /// Updates and renders an invoice through the Niobium domain/repository abstraction.
    /// </summary>
    /// <param name="config">Bound billing configuration for invoice links.</param>
    /// <param name="repo">Lazy storage repository for loading and saving invoice entities.</param>
    /// <param name="eventHandlers">Domain event handlers passed to the base Niobium domain.</param>
    public partial class InvoiceDomain(
        IOptions<BillingOptions> config,
        Lazy<IRepository<Invoice>> repo,
        IEnumerable<IDomainEventHandler<IDomain<Invoice>>> eventHandlers)
          : GenericDomain<Invoice>(repo, eventHandlers)
    {
        /// <summary>
        /// Process-wide cached invoice template text.
        /// </summary>
        private static string? htmlTemplate;

        /// <summary>
        /// Applies mutable request fields, recalculates totals and force-saves the loaded entity.
        /// </summary>
        /// <param name="update">Requested mutable fields and reported settlement.</param>
        /// <param name="invoiceItems">Supplied lines whose totals are recalculated in memory.</param>
        /// <param name="cancellationToken">Token forwarded to supported asynchronous operations.</param>
        /// <returns>A task completing after the invoice save.</returns>
        /// <remarks>
        /// The Delivered guard applies to the loaded entity; UpsertFlow initializes that entity from a fresh
        /// snapshot. Supplied line totals are mutated in memory; line persistence is the caller's
        /// responsibility. Force-save uses repository create/upsert semantics, without an ETag update
        /// precondition. Particulars and Reference are not applied; DueBy and PaymentInstructions may be
        /// overwritten with null.
        /// </remarks>
        /// <exception cref="Niobium.ApplicationException">The loaded entity has a Delivered timestamp.</exception>
        public async Task UpdateAsync(IssueInvoiceRequest update, IEnumerable<InvoiceItem> invoiceItems, CancellationToken cancellationToken)
        {
            Invoice entity = await GetEntityAsync(cancellationToken);
            if (entity.Delivered.HasValue)
            {
                throw new ApplicationException(InternalError.Conflict, "Invoice has been delivered.") { Reference = entity.GetFullID() };
            }

            foreach (InvoiceItem item in invoiceItems)
            {
                item.LineTotalCents = item.FigureLineTotalCents();
            }

            entity.Terms = update.Terms?.Trim();
            entity.PaymentInstructions = update.PaymentInstructions?.Trim();
            entity.InvoiceCycle = update.InvoiceCycle;
            entity.BillingPeriodStartDay = update.BillingPeriodStartDay;
            entity.BillingPeriodEndDay = update.BillingPeriodEndDay;
            entity.DueBy = update.DueBy;
            entity.SubtotalCents = invoiceItems.FigureSubTotalCents();
            entity.TaxCents = entity.FigureTaxTotalCents(invoiceItems);
            entity.GrandTotalCents = entity.FigureGrandTotalCents(invoiceItems);
            entity.SettledCents = update.Settled.Cents;

            await SaveAsync(force: true, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Renders the invoice-level HTML using its stored link-token policy.
        /// </summary>
        /// <param name="token">Supplied invoice-link token.</param>
        /// <param name="cancellationToken">Token forwarded to supported asynchronous operations.</param>
        /// <returns>HTML containing the invoice-item template block for later replacement.</returns>
        /// <remarks>
        /// Loads and caches the embedded template; the stored Template selector is not used. This method does
        /// not establish a recipient principal or an expiry policy. Formatting failures propagate.
        /// </remarks>
        /// <exception cref="Niobium.ApplicationException">The stored-token comparison rejects the supplied token, or the embedded template is missing.</exception>
        public async Task<string> BuildHTMLAsync(string token, CancellationToken cancellationToken)
        {
            Invoice entity = await GetEntityAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(entity.Token) && !entity.Token.Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                throw new ApplicationException(InternalError.Forbidden, "Invalid token.") { Reference = entity.GetFullID() };
            }

            TimeZoneInfo timezone = TimeZoneInfoHelper.ParseTimeZoneFromIANA(entity.TimeZone);
            CultureInfo culture = CultureInfo.GetCultureInfo(entity.Culture, true);
            htmlTemplate ??= await R.GetEmbededResourceAsStringAsync(Constants.InvoiceTemplateResourceName, cancellationToken)
                ?? throw new ApplicationException(InternalError.InternalServerError, "Missing invoice template.");
            var parameters = entity.BuildTemplateParameters(timezone, culture);
            var html = htmlTemplate;
            foreach (var parameter in parameters)
            {
                html = html.Replace($"{{{{{parameter.Key}}}}}", parameter.Value);
            }

            return html;
        }

        /// <summary>
        /// Builds formatted invoice values and a public invoice URL for notification templates.
        /// </summary>
        /// <param name="token">Link token inserted into the notification URL.</param>
        /// <param name="cancellationToken">Token forwarded to supported asynchronous operations.</param>
        /// <returns>Invoice display values including INVOICE_URL.</returns>
        /// <remarks>
        /// Formatting and configuration failures propagate; this method does not persist or send.
        /// </remarks>
        public async Task<IReadOnlyDictionary<string, string>> BuildNotificationParametersAsync(string token, CancellationToken cancellationToken)
        {
            Invoice entity = await GetEntityAsync(cancellationToken);
            TimeZoneInfo timezone = TimeZoneInfoHelper.ParseTimeZoneFromIANA(entity.TimeZone);
            CultureInfo culture = CultureInfo.GetCultureInfo(entity.Culture, true);
            var parameters = entity.BuildTemplateParameters(timezone, culture).ToDictionary();
            parameters.Add("INVOICE_URL", $"{config.Value.GetInvoiceEndpoint}/{entity.Biller}/invoices/{entity.GetID()}?token={token}");
            return parameters;
        }

        /// <summary>
        /// Stores the link token and current UTC enqueue-related timestamp, then saves the invoice.
        /// </summary>
        /// <param name="token">Link token to persist on the invoice.</param>
        /// <param name="cancellationToken">Token forwarded to supported asynchronous operations.</param>
        /// <returns>A task completing after persistence.</returns>
        /// <remarks>
        /// Called after enqueueing; this is not recipient-delivery confirmation. Persistence failure can occur
        /// after the notification has already been accepted by the broker.
        /// </remarks>
        public async Task OnDeliveredAsync(string token, CancellationToken cancellationToken)
        {
            Invoice entity = await GetEntityAsync(cancellationToken);
            entity.Token = token;
            entity.Delivered = DateTimeOffset.UtcNow;
            await SaveAsync(cancellationToken: cancellationToken);
        }
    }
}
