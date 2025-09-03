using Microsoft.Extensions.Options;
using Niobium.Invoicing.Options;
using System.Globalization;

namespace Niobium.Invoicing.Domains
{
    public partial class InvoiceDomain(
        IOptions<BillingOptions> config,
        Lazy<IRepository<Invoice>> repo,
        IEnumerable<IDomainEventHandler<IDomain<Invoice>>> eventHandlers)
          : GenericDomain<Invoice>(repo, eventHandlers)
    {
        private static string? invoiceTemplate;
        private static string? emailTemplate;

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

            await SaveAsync(cancellationToken: cancellationToken);
        }

        public async Task<string> BuildHTMLAsync(string token, CancellationToken cancellationToken)
        {
            Invoice entity = await GetEntityAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(entity.Token) && !entity.Token.Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                throw new ApplicationException(InternalError.Forbidden, "Invalid token.") { Reference = entity.GetFullID() };
            }

            TimeZoneInfo timezone = TimeZoneInfoHelper.ParseTimeZoneFromIANA(entity.TimeZone);
            CultureInfo culture = CultureInfo.GetCultureInfo(entity.Culture, true);
            invoiceTemplate ??= await R.GetEmbededResourceAsStringAsync(Constants.InvoiceTemplateResourceName, cancellationToken)
                ?? throw new ApplicationException(InternalError.InternalServerError, "Missing invoice template.");

            return entity.BuildHTML(invoiceTemplate, timezone, culture);
        }

        public async Task<string> BuildEmailAsync(string token, CancellationToken cancellationToken)
        {
            Invoice entity = await GetEntityAsync(cancellationToken);
            emailTemplate ??= await R.GetEmbededResourceAsStringAsync(Constants.EmailTemplateResourceName, cancellationToken)
                ?? throw new ApplicationException(InternalError.InternalServerError, "Missing email template.") { Reference = entity.GetFullID() };

            TimeZoneInfo timezone = TimeZoneInfoHelper.ParseTimeZoneFromIANA(entity.TimeZone);
            CultureInfo culture = CultureInfo.GetCultureInfo(entity.Culture, true);

            string result = entity.BuildHTML(emailTemplate, timezone, culture);
            string invoiceURL = $"{config.Value.GetInvoiceEndpoint}/{entity.Biller}/invoices/{entity.GetID()}?token={token}";
            result = result.Replace("{{InvoiceURL}}", invoiceURL);
            return result;
        }

        public async Task OnDeliveredAsync(string token, CancellationToken cancellationToken)
        {
            Invoice entity = await GetEntityAsync(cancellationToken);
            entity.Token = token;
            entity.Delivered = DateTimeOffset.UtcNow;
            await SaveAsync(cancellationToken: cancellationToken);
        }
    }
}
