using Niobium.Finance;
using System.Globalization;
using System.Text;

namespace Niobium.Invoicing.Domains
{
    internal static class InvoiceExtensions
    {
        public static string BuildAccessToken(this Invoice invoice, IEnumerable<InvoiceItem> items, string salt)
        {
            StringBuilder data = new();
            data.Append(invoice.GrandTotalCents);
            data.Append(invoice.GrandTotalCurrency);
            foreach (InvoiceItem item in items)
            {
                data.Append(item.LineTotalCents);
                data.Append(item.LineTotalCurrency);
            }

            string issuer = invoice.Biller.ToString("N");
            string invoiceID = invoice.GetID().ToString().PadLeft(12, '0');
            string secret = $"{salt[..4]}{issuer.Substring(8, 16)}{invoiceID[..12]}";

            return SHA.SHA256Hash(data.ToString(), secret, 16);
        }

        public static IReadOnlyDictionary<string, string> BuildTemplateParameters(this Invoice invoice, TimeZoneInfo timeZone, CultureInfo culture)
        {
            var parameters = new Dictionary<string, string>
            {
                { "INVOICE_ID", invoice.GetID().ToString() },
                { "BILL_DATE", invoice.GetCreated(timeZone).ToYearMonthDayInNames(culture) },
                { ToSnakeCase(nameof(invoice.BillerName)), invoice.BillerName },
                { ToSnakeCase(nameof(invoice.BillerBusinessID)), invoice.BillerBusinessID ?? string.Empty },
                { ToSnakeCase(nameof(invoice.BillerTaxID)), invoice.BillerTaxID ?? string.Empty },
                { ToSnakeCase(nameof(invoice.BillerAddressLine1)), invoice.BillerAddressLine1 ?? string.Empty },
                { ToSnakeCase(nameof(invoice.BillerAddressLine1)), invoice.BillerAddressLine1 ?? string.Empty },
                { ToSnakeCase(nameof(invoice.BillerAddressSuburb)), invoice.BillerAddressSuburb ?? string.Empty },
                { ToSnakeCase(nameof(invoice.BillerAddressCity)), invoice.BillerAddressCity ?? string.Empty },
                { ToSnakeCase(nameof(invoice.BillerAddressState)), invoice.BillerAddressState ?? string.Empty },
                { ToSnakeCase(nameof(invoice.BillerAddressCountry)), !string.IsNullOrWhiteSpace(invoice.BillerAddressCountry) ? Country.Parse(invoice.BillerAddressCountry).ToString() : string.Empty },
                { ToSnakeCase(nameof(invoice.BillerAddressZipcode)), invoice.BillerAddressZipcode ?? string.Empty },
                { ToSnakeCase(nameof(invoice.BilleeName)), invoice.BilleeName },
                { ToSnakeCase(nameof(invoice.BilleeBusinessID)), invoice.BilleeBusinessID ?? string.Empty },
                { ToSnakeCase(nameof(invoice.BilleeAddressLine1)), invoice.BilleeAddressLine1 ?? string.Empty },
                { ToSnakeCase(nameof(invoice.BilleeAddressLine2)), invoice.BilleeAddressLine2 ?? string.Empty },
                { ToSnakeCase(nameof(invoice.BilleeAddressSuburb)), invoice.BilleeAddressSuburb ?? string.Empty },
                { ToSnakeCase(nameof(invoice.BilleeAddressCity)), invoice.BilleeAddressCity ?? string.Empty },
                { ToSnakeCase(nameof(invoice.BilleeAddressState)), invoice.BilleeAddressState ?? string.Empty },
                { ToSnakeCase(nameof(invoice.BilleeAddressCountry)), !string.IsNullOrWhiteSpace(invoice.BilleeAddressCountry) ? Country.Parse(invoice.BilleeAddressCountry).ToString() : string.Empty },
                { ToSnakeCase(nameof(invoice.BilleeAddressZipcode)), invoice.BilleeAddressZipcode ?? string.Empty },
                { ToSnakeCase(nameof(invoice.ContactName)), invoice.ContactName ?? string.Empty },
                { ToSnakeCase(nameof(invoice.PaymentInstructions)), invoice.PaymentInstructions ?? string.Empty },
                { ToSnakeCase(nameof(invoice.Particulars)), invoice.Particulars ?? string.Empty },
                { ToSnakeCase(nameof(invoice.Reference)), invoice.Reference ?? string.Empty },
                { ToSnakeCase(nameof(invoice.ContactPhoneNumber)), invoice.ContactPhoneNumber ?? string.Empty },
                { ToSnakeCase(nameof(invoice.ContactEmailAddress)), invoice.ContactEmailAddress ?? string.Empty },
                { "SUBTOTAL", Currency.Parse(invoice.SubtotalCurrency).ToDisplayLocal(invoice.SubtotalCents / 100d) },
                { "TAX_AMOUNT", Currency.Parse(invoice.TaxCurrency).ToDisplayLocal(invoice.TaxCents / 100d) },
                { "TAX_RATE", invoice.TaxRatePercentile == invoice.TaxRatePercentile / 100 * 100 ? $"{invoice.TaxRatePercentile / 100}%" : string.Format("{0:N2}%", invoice.TaxRatePercentile / 100d) },
                { "GRAND_TOTAL", Currency.Parse(invoice.GrandTotalCurrency).ToDisplayLocal(invoice.GrandTotalCents / 100d) },
                { ToSnakeCase(nameof(invoice.BillerLogo)), invoice.BillerLogo != null ? $"<img src=\"{invoice.BillerLogo}\" class=\"biller-logo\" />" : string.Empty },
                { ToSnakeCase(nameof(invoice.Terms)), invoice.Terms ?? string.Empty },
            };

            string billingPeriod = string.Empty;
            switch ((InvoiceCycle)invoice.InvoiceCycle)
            {
                case InvoiceCycle.Daily:
                    if (invoice.BillingPeriodStartDay.HasValue)
                    {
                        billingPeriod = invoice.BillingPeriodStartDay.Value.ToLocal(timeZone).ToYearMonthDayInNames(culture);
                    }
                    break;
                case InvoiceCycle.Monthly:
                    if (invoice.BillingPeriodStartDay.HasValue)
                    {
                        billingPeriod = invoice.BillingPeriodStartDay.Value.ToLocal(timeZone).ToYearMonth(culture);
                    }
                    break;
                case InvoiceCycle.Anually:
                    if (invoice.BillingPeriodStartDay.HasValue)
                    {
                        billingPeriod = invoice.BillingPeriodStartDay.Value.ToLocal(timeZone).Year.ToString();
                    }
                    break;
                case InvoiceCycle.Range:
                    if (invoice.BillingPeriodStartDay.HasValue && invoice.BillingPeriodEndDay.HasValue)
                    {
                        string start = invoice.BillingPeriodStartDay.Value.ToLocal(timeZone).ToYearMonthDayInNames(culture);
                        string end = invoice.BillingPeriodEndDay.Value.ToLocal(timeZone).ToYearMonthDayInNames(culture);
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

            parameters.Add("BILLING_PERIOD", billingPeriod);

            string due = string.Empty;
            if (invoice.DueBy != null)
            {
                due = $"Payment is due by: {invoice.DueBy.Value.ToLocal(timeZone).ToYearMonthDayInNames(culture)}";
            }
            parameters.Add("DUE", due);

            return parameters;
        }

        private static string ToSnakeCase(string text)
        {
            if (text.Length < 2)
            {
                return text.ToUpperInvariant();
            }

            StringBuilder sb = new();
            sb.Append(char.ToUpperInvariant(text[0]));
            for (int i = 1; i < text.Length; ++i)
            {
                char c = text[i];
                if (char.IsUpper(c))
                {
                    sb.Append('_');
                    sb.Append(char.ToUpperInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
