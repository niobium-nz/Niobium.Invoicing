using Cod;
using Cod.Platform.Notification.Email;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Niobium.Billing.Functions
{
    public partial class InvoiceDomain(
        IOptions<BillingOptions> config,
        Lazy<IRepository<Invoice>> repo,
        Lazy<IRepository<InvoiceItem>> itemRepo,
        IEmailNotificationClient sender,
        IEnumerable<IDomainEventHandler<IDomain<Invoice>>> eventHandlers)
          : GenericDomain<Invoice>(repo, eventHandlers)
    {
        private static readonly Regex InvoiceLineRegex = CreateInvoiceLineRegex();
        private static string? invoiceTemplate;
        private static string? emailTemplate;
        private const string InvoiceTemplateResourceName = "Niobium.Billing.Functions.InvoiceTemplate.html";
        private const string EmailTemplateResourceName = "Niobium.Billing.Functions.EmailTemplate.html";

        public async Task<string> GetHTMLOutputAsync(string token, CancellationToken cancellationToken)
        {
            var invoice = await GetEntityAsync() ?? throw new Cod.ApplicationException(InternalError.NotFound, "Invoice not found.") { Reference = Invoice.BuildFullID(PartitionKey, RowKey) };
            if (!string.IsNullOrWhiteSpace(invoice.Token) && !invoice.Token.Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                throw new Cod.ApplicationException(InternalError.Forbidden, "Invalid token.") { Reference = invoice.GetFullID() };
            }

            var items = await itemRepo.Value.GetAsync(InvoiceItem.BuildPartitionKey(invoice.GetID()), cancellationToken: cancellationToken)
                .ToArrayAsync(cancellationToken: cancellationToken);

            TimeZoneInfo timezone = TimeZoneInfo.FindSystemTimeZoneById(invoice.TimeZone);
            CultureInfo culture = CultureInfo.GetCultureInfo(invoice.Culture, true);

            invoiceTemplate ??= await GetEmbededResourceAsStringAsync(InvoiceTemplateResourceName) ?? throw new Cod.ApplicationException(InternalError.InternalServerError, "Missing invoice template.");

            var itemTemplateMatch = InvoiceLineRegex.Match(invoiceTemplate);
            if (!itemTemplateMatch.Success)
            {
                throw new Cod.ApplicationException(InternalError.InternalServerError, "Missing invoice line template.") { Reference = invoice.GetFullID() };
            }

            var itemTemplate = itemTemplateMatch.Value;
            string result = BuildInvoiceHtml(invoiceTemplate, invoice, timezone, culture);

            var itemsHtml = new StringBuilder();
            foreach (var item in items)
            {
                string itemHtml = BuildInvoiceLineHtml(itemTemplate, item);
                itemsHtml.Append(itemHtml);
            }
            result = result.Replace(itemTemplate, itemsHtml.ToString());

            return result;
        }

        public async Task<bool> SendHTMLEmailAsync(CancellationToken cancellationToken)
        {
            var invoice = await GetEntityAsync() ?? throw new Cod.ApplicationException(InternalError.NotFound, "Invoice not found.") { Reference = Invoice.BuildFullID(PartitionKey, RowKey) };
            var email = await GetHTMLEmailAsync(cancellationToken);
            return await sender.SendAsync(
                new EmailAddress { DisplayName = invoice.BillerName ?? invoice.ContactName, Address = config.Value.InvoiceEmailSenderAddress },
                [invoice.RecipientEmail],
                $"Invoice {invoice.GetID()} from {invoice.BillerName ?? invoice.ContactName} for {invoice.BilleeName}",
                email,
                cancellationToken);
        }

        private async Task<string> GetHTMLEmailAsync(CancellationToken cancellationToken)
        {
            var invoice = await GetEntityAsync() ?? throw new Cod.ApplicationException(InternalError.NotFound, "Invoice not found.") { Reference = Invoice.BuildFullID(PartitionKey, RowKey) };
            emailTemplate ??= await GetEmbededResourceAsStringAsync(EmailTemplateResourceName) ?? throw new Cod.ApplicationException(InternalError.InternalServerError, "Missing email template.") { Reference = invoice.GetFullID() };
            TimeZoneInfo timezone = TimeZoneInfo.FindSystemTimeZoneById(invoice.TimeZone);
            CultureInfo culture = CultureInfo.GetCultureInfo(invoice.Culture, true);
            var result = BuildInvoiceHtml(emailTemplate, invoice, timezone, culture);
            var items = await itemRepo.Value.GetAsync(InvoiceItem.BuildPartitionKey(invoice.GetID()), cancellationToken: cancellationToken)
                .ToArrayAsync(cancellationToken: cancellationToken);
            var invoiceURL = $"{config.Value.GetInvoiceEndpoint}/{invoice.Issuer}/invoices/{invoice.GetID()}";
            if (!string.IsNullOrWhiteSpace(invoice.Token))
            {
                invoiceURL += $"?token={invoice.Token}";
            }

            result = result.Replace("{{InvoiceURL}}", invoiceURL);
            return result;
        }

        private static string BuildAccessToken(Invoice invoice, IEnumerable<InvoiceItem> items, string salt)
        {
            var data = new StringBuilder();
            data.Append(invoice.GrandTotalCents);
            data.Append(invoice.GrandTotalCurrency);
            foreach (var item in items)
            {
                data.Append(item.LineTotalCents);
                data.Append(item.LineTotalCurrency);
            }

            var issuer = invoice.Issuer.ToString("N");
            var invoiceID = invoice.GetID().ToString().PadLeft(12, '0');
            var secret = $"{salt[..4]}{issuer.Substring(8, 16)}{invoiceID[..12]}";

            return SHA.SHA256Hash(data.ToString(), secret, 16);
        }

        private static string BuildInvoiceLineHtml(string itemTemplate, InvoiceItem item)
        {
            return itemTemplate.Replace("{{Subject}}", item.Subject)
                                .Replace("{{Description}}", item.Description ?? string.Empty)
                                .Replace("{{UnitPrice}}", Currency.Parse(item.UnitPriceCurrency).ToDisplayLocal(item.UnitPriceCents / 100d))
                                .Replace("{{Quantity}}", item.Quantity.ToString())
                                .Replace("{{LineTotal}}", Currency.Parse(item.LineTotalCurrency).ToDisplayLocal(item.LineTotalCents / 100d));
        }

        private static string BuildInvoiceHtml(string template, Invoice invoice, TimeZoneInfo timeZone, CultureInfo culture)
        {
            var result = template.Replace("{{BillDate}}", invoice.GetBillDate(timeZone).ToYearMonthDayInNames(culture))
                            .Replace("{{BillerName}}", invoice.BillerName)
                            .Replace("{{BillerAddressLine1}}", invoice.BillerAddressLine1)
                            .Replace("{{BillerAddressCity}}", invoice.BillerAddressCity)
                            .Replace("{{BillerAddressZipcode}}", invoice.BillerAddressZipcode)
                            .Replace("{{BilleeName}}", invoice.BilleeName)
                            .Replace("{{BilleeAddressLine1}}", invoice.BilleeAddressLine1)
                            .Replace("{{BilleeAddressCity}}", invoice.BilleeAddressCity)
                            .Replace("{{BilleeAddressZipcode}}", invoice.BilleeAddressZipcode)
                            .Replace("{{ContactName}}", invoice.ContactName)
                            .Replace("{{PaymentInstructions}}", invoice.PaymentInstructions)
                            .Replace("{{Subtotal}}", Currency.Parse(invoice.SubtotalCurrency).ToDisplayLocal(invoice.SubtotalCents / 100d))
                            .Replace("{{TaxAmount}}", Currency.Parse(invoice.TaxCurrency).ToDisplayLocal(invoice.TaxCents / 100d))
                            .Replace("{{TaxRate}}", invoice.TaxRatePercentile == invoice.TaxRatePercentile / 100 * 100 ? $"{invoice.TaxRatePercentile / 100}%" : string.Format("{0:N2}%", invoice.TaxRatePercentile / 100d))
                            .Replace("{{GrandTotal}}", Currency.Parse(invoice.GrandTotalCurrency).ToDisplayLocal(invoice.GrandTotalCents / 100d));

            result = invoice.BillerLogo != null
                ? result.Replace("{{BillerLogo}}", $"<img src=\"{invoice.BillerLogo}\" class=\"biller-logo\" />")
                : result.Replace("{{BillerLogo}}", string.Empty);

            result = invoice.BillerAddressLine2 != null
                ? result.Replace("{{BillerAddressLine2}}", $"{invoice.BillerAddressLine2}<br>")
                : result.Replace("{{BillerAddressLine2}}", string.Empty);

            result = invoice.BillerBusinessID != null
                ? result.Replace("{{BillerBusinessID}}", invoice.BillerBusinessID)
                : result.Replace("{{BillerBusinessID}}", string.Empty);

            result = invoice.BillerTaxID != null
                ? result.Replace("{{BillerTaxID}}", invoice.BillerTaxID)
                : result.Replace("{{BillerTaxID}}", string.Empty);

            result = invoice.BilleeAddressLine2 != null
                ? result.Replace("{{BilleeAddressLine2}}", invoice.BilleeAddressLine2)
                : result.Replace("{{BilleeAddressLine2}}", string.Empty);

            result = invoice.BilleeBusinessID != null
                ? result.Replace("{{BilleeBusinessID}}", invoice.BilleeBusinessID)
                : result.Replace("{{BilleeBusinessID}}", string.Empty);

            result = invoice.Particulars != null
                ? result.Replace("{{Particulars}}", invoice.Particulars)
                : result.Replace("{{Particulars}}", string.Empty);

            result = invoice.Reference != null
                ? result.Replace("{{Reference}}", invoice.Reference)
                : result.Replace("{{Reference}}", string.Empty);

            result = invoice.ContactPhoneNumber != null
                ? result.Replace("{{ContactPhoneNumber}}", invoice.ContactPhoneNumber)
                : result.Replace("{{ContactPhoneNumber}}", string.Empty);

            result = invoice.ContactEmailAddress != null
                ? result.Replace("{{ContactEmailAddress}}", invoice.ContactEmailAddress)
                : result.Replace("{{ContactEmailAddress}}", string.Empty);

            result = invoice.Terms != null
                ? result.Replace("{{Terms}}", $"{invoice.Terms}<br>")
                : result.Replace("{{Terms}}", string.Empty);

            result = invoice.BillerLogo != null
                ? result.Replace("{{BillerLogo}}", invoice.BillerLogo)
                : result.Replace("{{BillerLogo}}", string.Empty);

            string billingPeriod = string.Empty;
            if (invoice.BillingPeriodStartDay != null && invoice.BillingPeriodEndDay == null)
            {
                billingPeriod = invoice.BillingPeriodStartDay.Value.ToLocal(timeZone).ToYearMonth(culture);
            }
            else if (invoice.BillingPeriodStartDay != null && invoice.BillingPeriodEndDay != null)
            {
                var start = invoice.BillingPeriodStartDay.Value.ToLocal(timeZone).ToYearMonthDayInNames(culture);
                var end = invoice.BillingPeriodEndDay.Value.ToLocal(timeZone).ToYearMonthDayInNames(culture);
                billingPeriod = $"{start} - {end}";
            }
            if (billingPeriod != string.Empty)
            {
                billingPeriod = $"Billing Period: {billingPeriod}";
            }
            result = result.Replace("{{BillingPeriod}}", billingPeriod);

            string due = string.Empty;
            if (invoice.DueBy != null)
            {
                due = $"Payment is due by: {invoice.DueBy.Value.ToLocal(timeZone).ToYearMonthDayInNames(culture)}";
            }
            result = result.Replace("{{Due}}", due);
            return result;
        }

        private static async Task<string?> GetEmbededResourceAsStringAsync(string resourceName)
        {
            var assembly = typeof(GetHTMLInvoice).Assembly;
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    return null;
                }

                using (var reader = new StreamReader(stream))
                {
                    return await reader.ReadToEndAsync();
                }
            }
        }

        [GeneratedRegex(@"<!-- Invoice Line Start -->[\s\S]*<!-- Invoice Line End -->", RegexOptions.Compiled)]
        private static partial Regex CreateInvoiceLineRegex();
    }
}
