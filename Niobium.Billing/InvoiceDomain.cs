using Cod;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Niobium.Billing
{
    public partial class InvoiceDomain(
        Lazy<IRepository<Invoice>> repo,
        Lazy<IRepository<InvoiceItem>> itemRepo,
        IEnumerable<IDomainEventHandler<IDomain<Invoice>>> eventHandlers)
          : GenericDomain<Invoice>(repo, eventHandlers)
    {
        private static readonly Regex InvoiceLineRegex = CreateInvoiceLineRegex();
        private const string TemplateResourceName = "Niobium.Billing.InvoiceTemplate.html";

        public async Task<bool> VerifyTokenAsync(string token)
        {
            var invoice = await GetEntityAsync() ?? throw new Cod.ApplicationException(InternalError.NotFound, "Invoice not found.");
            var json = JsonSerializer.SerializeObject(invoice, JsonSerializationFormat.PascalCase);
            var hash = SHA.SHA256Hash(json, 16);
            return hash.Equals(token, StringComparison.InvariantCultureIgnoreCase);
        }

        public async Task<string?> GetHtmlOutputAsync(string token)
        {
            var valid = await VerifyTokenAsync(token);
            if (!valid)
            {
                throw new Cod.ApplicationException(InternalError.Forbidden, "Invalid token.");
            }

            var invoice = await GetEntityAsync() ?? throw new Cod.ApplicationException(InternalError.NotFound, "Invoice not found.");
            TimeZoneInfo timezone = TimeZoneInfo.FindSystemTimeZoneById(invoice.TimeZone);
            CultureInfo culture = CultureInfo.GetCultureInfo(invoice.Culture, true);

            var template = await GetEmbededResourceAsStringAsync(TemplateResourceName) ?? throw new Cod.ApplicationException(InternalError.InternalServerError, "Missing invoice template.");
            var itemTemplateMatch = InvoiceLineRegex.Match(template);
            if (!itemTemplateMatch.Success)
            {
                throw new Cod.ApplicationException(InternalError.InternalServerError, "Missing invoice line template.");
            }

            var itemTemplate = itemTemplateMatch.Value;
            string result = BuildInvoiceHtml(template, invoice, timezone, culture);

            var items = itemRepo.Value.GetAsync(InvoiceItem.BuildPartitionKey(invoice.GetID()));
            var itemsHtml = new StringBuilder();
            await foreach (var item in items)
            {
                string itemHtml = BuildInvoiceLineHtml(itemTemplate, item);
                itemsHtml.Append(itemHtml);
            }
            result = result.Replace(itemTemplate, itemsHtml.ToString());

            return result;
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
                            .Replace("{{Tax}}", Currency.Parse(invoice.TaxCurrency).ToDisplayLocal(invoice.TaxCents / 100d))
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
                billingPeriod = invoice.BillingPeriodStartDay.Value.ToLocal(timeZone).ToMonthDay(culture);
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
            var assembly = typeof(GetHtmlInvoice).Assembly;
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
