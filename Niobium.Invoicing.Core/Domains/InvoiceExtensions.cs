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
                { nameof(invoice.BillerName).ToSnakeCaseUpper(), invoice.BillerName },
                { nameof(invoice.BillerBusinessID).ToSnakeCaseUpper(), invoice.BillerBusinessID ?? string.Empty },
                { nameof(invoice.BillerTaxID).ToSnakeCaseUpper(), invoice.BillerTaxID ?? string.Empty },
                { nameof(invoice.BillerAddressLine1).ToSnakeCaseUpper(), invoice.BillerAddressLine1 ?? string.Empty },
                { nameof(invoice.BillerAddressLine2).ToSnakeCaseUpper(), invoice.BillerAddressLine2 ?? string.Empty },
                { nameof(invoice.BillerAddressSuburb).ToSnakeCaseUpper(), invoice.BillerAddressSuburb ?? string.Empty },
                { nameof(invoice.BillerAddressCity).ToSnakeCaseUpper(), invoice.BillerAddressCity ?? string.Empty },
                { nameof(invoice.BillerAddressState).ToSnakeCaseUpper(), invoice.BillerAddressState ?? string.Empty },
                { nameof(invoice.BillerAddressCountry).ToSnakeCaseUpper(), !string.IsNullOrWhiteSpace(invoice.BillerAddressCountry) ? Country.Parse(invoice.BillerAddressCountry).ToString() : string.Empty },
                { nameof(invoice.BillerAddressZipcode).ToSnakeCaseUpper(), invoice.BillerAddressZipcode ?? string.Empty },
                { nameof(invoice.BilleeName).ToSnakeCaseUpper(), invoice.BilleeName },
                { nameof(invoice.BilleeBusinessID).ToSnakeCaseUpper(), invoice.BilleeBusinessID ?? string.Empty },
                { nameof(invoice.BilleeAddressLine1).ToSnakeCaseUpper(), invoice.BilleeAddressLine1 ?? string.Empty },
                { nameof(invoice.BilleeAddressLine2).ToSnakeCaseUpper(), invoice.BilleeAddressLine2 ?? string.Empty },
                { nameof(invoice.BilleeAddressSuburb).ToSnakeCaseUpper(), invoice.BilleeAddressSuburb ?? string.Empty },
                { nameof(invoice.BilleeAddressCity).ToSnakeCaseUpper(), invoice.BilleeAddressCity ?? string.Empty },
                { nameof(invoice.BilleeAddressState).ToSnakeCaseUpper(), invoice.BilleeAddressState ?? string.Empty },
                { nameof(invoice.BilleeAddressCountry).ToSnakeCaseUpper(), !string.IsNullOrWhiteSpace(invoice.BilleeAddressCountry) ? Country.Parse(invoice.BilleeAddressCountry).ToString() : string.Empty },
                { nameof(invoice.BilleeAddressZipcode).ToSnakeCaseUpper(), invoice.BilleeAddressZipcode ?? string.Empty },
                { nameof(invoice.ContactName).ToSnakeCaseUpper(), invoice.ContactName ?? string.Empty },
                { nameof(invoice.PaymentInstructions).ToSnakeCaseUpper(), invoice.PaymentInstructions ?? string.Empty },
                { nameof(invoice.Particulars).ToSnakeCaseUpper(), invoice.Particulars ?? string.Empty },
                { nameof(invoice.Reference).ToSnakeCaseUpper(), invoice.Reference ?? string.Empty },
                { nameof(invoice.ContactPhoneNumber).ToSnakeCaseUpper(), invoice.ContactPhoneNumber ?? string.Empty },
                { nameof(invoice.ContactEmailAddress).ToSnakeCaseUpper(), invoice.ContactEmailAddress ?? string.Empty },
                { "SUBTOTAL", Currency.Parse(invoice.SubtotalCurrency).ToDisplayLocal(invoice.SubtotalCents / 100d) },
                { "TAX_AMOUNT", Currency.Parse(invoice.TaxCurrency).ToDisplayLocal(invoice.TaxCents / 100d) },
                { "TAX_RATE", invoice.TaxRatePercentile == invoice.TaxRatePercentile / 100 * 100 ? $"{invoice.TaxRatePercentile / 100}%" : string.Format("{0:N2}%", invoice.TaxRatePercentile / 100d) },
                { "GRAND_TOTAL", Currency.Parse(invoice.GrandTotalCurrency).ToDisplayLocal(invoice.GrandTotalCents / 100d) },
                { "SETTLED", new Amount(invoice.SettledCents, invoice.GrandTotalCurrency).ToString() },
                { nameof(invoice.BillerLogo).ToSnakeCaseUpper(), invoice.BillerLogo ?? string.Empty },
                { nameof(invoice.Terms).ToSnakeCaseUpper(), invoice.Terms ?? string.Empty },
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

            var due = invoice.GrandTotalCents - invoice.SettledCents;
            if (due < 0)
            {
                due = 0;
            }
            parameters.Add("DUE", new Amount(due, invoice.GrandTotalCurrency).ToString());

            string dueBy = string.Empty;
            if (invoice.DueBy != null)
            {
                dueBy = $"Payment is due by: {invoice.DueBy.Value.ToLocal(timeZone).ToYearMonthDayInNames(culture)}";
            }
            parameters.Add("DUE_BY", dueBy);

            return parameters;
        }
    }
}
