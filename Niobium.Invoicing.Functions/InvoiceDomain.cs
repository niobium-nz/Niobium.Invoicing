using Cod;
using Cod.Finance;
using Cod.Platform.Notification.Email;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Niobium.Invoicing.Functions
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
        private const string InvoiceTemplateResourceName = "Niobium.Invoicing.Functions.InvoiceTemplate.html";
        private const string EmailTemplateResourceName = "Niobium.Invoicing.Functions.EmailTemplate.html";

        public async Task UpdateAsync(Invoice invoice, IEnumerable<InvoiceItem> invoiceItems, CancellationToken cancellationToken)
        {
            var existingInvoice = await GetEntityAsync(cancellationToken) ?? throw new Cod.ApplicationException(InternalError.NotFound, "Invoice not found.") { Reference = Invoice.BuildFullID(PartitionKey, RowKey) };
            if (existingInvoice.Delivered.HasValue)
            {
                throw new Cod.ApplicationException(InternalError.Conflict, "Invoice has been delivered.") { Reference = existingInvoice.GetFullID() };
            }

            var existingInvoiceItems = await itemRepo.Value.GetAsync(InvoiceItem.BuildPartitionKey(existingInvoice.GetID()), cancellationToken: cancellationToken)
                .ToArrayAsync(cancellationToken: cancellationToken);
            if (existingInvoiceItems.Length > 0)
            {
                await itemRepo.Value.DeleteAsync(existingInvoiceItems, cancellationToken: cancellationToken);
            }

            foreach (var item in invoiceItems)
            {
                item.LineTotalCents = item.FigureLineTotalCents();
            }

            invoice.Terms = invoice.Terms?.Trim();
            invoice.PaymentInstructions = invoice.PaymentInstructions?.Trim();
            invoice.BillingPeriodKind = invoice.BillingPeriodKind;
            invoice.BillingPeriodStartDay = invoice.BillingPeriodStartDay;
            invoice.BillingPeriodEndDay = invoice.BillingPeriodEndDay;
            invoice.DueBy = invoice.DueBy;
            invoice.SubtotalCents = invoiceItems.FigureSubTotalCents();
            invoice.TaxCents = invoice.FigureTaxTotalCents(invoiceItems);
            invoice.GrandTotalCents = invoice.FigureGrandTotalCents(invoiceItems);

            await Repository.UpdateAsync(invoice, cancellationToken: cancellationToken);
            await itemRepo.Value.CreateAsync(invoiceItems, cancellationToken: cancellationToken);
        }

        public async Task<string> GetHTMLOutputAsync(string token, CancellationToken cancellationToken)
        {
            var invoice = await GetEntityAsync(cancellationToken) ?? throw new Cod.ApplicationException(InternalError.NotFound, "Invoice not found.") { Reference = Invoice.BuildFullID(PartitionKey, RowKey) };
            if (!string.IsNullOrWhiteSpace(invoice.Token) && !invoice.Token.Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                throw new Cod.ApplicationException(InternalError.Forbidden, "Invalid token.") { Reference = invoice.GetFullID() };
            }

            var items = await itemRepo.Value.GetAsync(InvoiceItem.BuildPartitionKey(invoice.GetID()), cancellationToken: cancellationToken)
                .ToArrayAsync(cancellationToken: cancellationToken);

            TimeZoneInfo timezone = TimeZoneInfoHelper.ParseTimeZoneFromIANA(invoice.TimeZone);
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
            var invoice = await GetEntityAsync(cancellationToken) ?? throw new Cod.ApplicationException(InternalError.NotFound, "Invoice not found.") { Reference = Invoice.BuildFullID(PartitionKey, RowKey) };
            if (invoice.RecipientEmail == null)
            {
                return false;
            }
            var items = await itemRepo.Value.GetAsync(InvoiceItem.BuildPartitionKey(invoice.GetID()), cancellationToken: cancellationToken).ToArrayAsync(cancellationToken: cancellationToken);
            var token = BuildAccessToken(invoice, items, config.Value.InvoiceTokenSecretSalt);

            var email = await GetHTMLEmailAsync(token, cancellationToken);
            var result = await sender.SendAsync(
                new EmailAddress { DisplayName = invoice.ContactName ?? invoice.BillerName, Address = config.Value.InvoiceEmailSenderAddress },
                [invoice.RecipientEmail],
                $"Invoice {invoice.GetID()} from {invoice.BillerName} for {invoice.BilleeName}",
                email,
                cancellationToken);
            if (result)
            {
                invoice.Delivered = DateTimeOffset.UtcNow;
                invoice.Token = token;
                await SaveAsync(cancellationToken: cancellationToken);
            }

            return result;
        }

        private async Task<string> GetHTMLEmailAsync(string token, CancellationToken cancellationToken)
        {
            var invoice = await GetEntityAsync(cancellationToken) ?? throw new Cod.ApplicationException(InternalError.NotFound, "Invoice not found.") { Reference = Invoice.BuildFullID(PartitionKey, RowKey) };
            emailTemplate ??= await GetEmbededResourceAsStringAsync(EmailTemplateResourceName) ?? throw new Cod.ApplicationException(InternalError.InternalServerError, "Missing email template.") { Reference = invoice.GetFullID() };

            TimeZoneInfo timezone = TimeZoneInfoHelper.ParseTimeZoneFromIANA(invoice.TimeZone);
            CultureInfo culture = CultureInfo.GetCultureInfo(invoice.Culture, true);

            var result = BuildInvoiceHtml(emailTemplate, invoice, timezone, culture);
            var invoiceURL = $"{config.Value.GetInvoiceEndpoint}/{invoice.Biller}/invoices/{invoice.GetID()}?token={token}";
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

            var issuer = invoice.Biller.ToString("N");
            var invoiceID = invoice.GetID().ToString().PadLeft(12, '0');
            var secret = $"{salt[..4]}{issuer.Substring(8, 16)}{invoiceID[..12]}";

            return SHA.SHA256Hash(data.ToString(), secret, 16);
        }

        private static string BuildInvoiceLineHtml(string itemTemplate, InvoiceItem item)
        {
            return itemTemplate.Replace("{{Subject}}", item.Subject)
                                .Replace("{{Description}}", item.Description)
                                .Replace("{{UnitPrice}}", Currency.Parse(item.UnitPriceCurrency).ToDisplayLocal(item.UnitPriceCents / 100d))
                                .Replace("{{Quantity}}", item.Quantity.ToString())
                                .Replace("{{LineTotal}}", Currency.Parse(item.LineTotalCurrency).ToDisplayLocal(item.LineTotalCents / 100d));
        }

        private static string BuildInvoiceHtml(string template, Invoice invoice, TimeZoneInfo timeZone, CultureInfo culture)
        {
            var result = template.Replace("{{BillDate}}", invoice.GetBillDate(timeZone).ToYearMonthDayInNames(culture))
                            .Replace("{{BillerName}}", invoice.BillerName)
                            .Replace("{{BillerBusinessID}}", invoice.BillerBusinessID)
                            .Replace("{{BillerTaxID}}", invoice.BillerTaxID)
                            .Replace("{{BillerAddressLine1}}", invoice.BillerAddressLine1)
                            .Replace("{{BillerAddressCity}}", invoice.BillerAddressCity)
                            .Replace("{{BillerAddressZipcode}}", invoice.BillerAddressZipcode)
                            .Replace("{{BilleeName}}", invoice.BilleeName)
                            .Replace("{{BilleeBusinessID}}", invoice.BilleeBusinessID)
                            .Replace("{{BilleeAddressLine1}}", invoice.BilleeAddressLine1)
                            .Replace("{{BilleeAddressLine2}}", invoice.BilleeAddressLine2)
                            .Replace("{{BilleeAddressCity}}", invoice.BilleeAddressCity)
                            .Replace("{{BilleeAddressZipcode}}", invoice.BilleeAddressZipcode)
                            .Replace("{{ContactName}}", invoice.ContactName)
                            .Replace("{{PaymentInstructions}}", invoice.PaymentInstructions)
                            .Replace("{{Particulars}}", invoice.Particulars)
                            .Replace("{{Reference}}", invoice.Reference)
                            .Replace("{{ContactPhoneNumber}}", invoice.ContactPhoneNumber)
                            .Replace("{{ContactEmailAddress}}", invoice.ContactEmailAddress)
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

            result = invoice.Terms != null
                ? result.Replace("{{Terms}}", $"{invoice.Terms}<br>")
                : result.Replace("{{Terms}}", string.Empty);

            string billingPeriod = string.Empty;
            switch ((BillingPeriodKind)invoice.BillingPeriodKind)
            {
                case BillingPeriodKind.Daily:
                    if (invoice.BillingPeriodStartDay.HasValue)
                    {
                        billingPeriod = invoice.BillingPeriodStartDay.Value.ToLocal(timeZone).ToYearMonthDayInNames(culture);
                    }
                    break;
                case BillingPeriodKind.Monthly:
                    if (invoice.BillingPeriodStartDay.HasValue)
                    {
                        billingPeriod = invoice.BillingPeriodStartDay.Value.ToLocal(timeZone).ToYearMonth(culture);
                    }
                    break;
                case BillingPeriodKind.Anually:
                    if (invoice.BillingPeriodStartDay.HasValue)
                    {
                        billingPeriod = invoice.BillingPeriodStartDay.Value.ToLocal(timeZone).Year.ToString();
                    }
                    break;
                case BillingPeriodKind.Range:
                    if (invoice.BillingPeriodStartDay.HasValue && invoice.BillingPeriodEndDay.HasValue)
                    {
                        var start = invoice.BillingPeriodStartDay.Value.ToLocal(timeZone).ToYearMonthDayInNames(culture);
                        var end = invoice.BillingPeriodEndDay.Value.ToLocal(timeZone).ToYearMonthDayInNames(culture);
                        billingPeriod = $"{start} - {end}";
                    }
                    break;
                default:
                    break;
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
