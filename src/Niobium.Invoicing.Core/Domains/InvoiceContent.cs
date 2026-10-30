using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Niobium.Invoicing.Domains;

internal static class InvoiceContent
{
    internal const string Preparing = "preparing-v1";
    internal const string Retryable = "retryable-v1";
    internal const string Ready = "ready-v1";
    internal const string Frozen = "frozen-v1";

    internal static bool IsFrozen(Invoice invoice) => invoice.IssuanceState == Frozen ||
        invoice.Delivered.HasValue || !string.IsNullOrWhiteSpace(invoice.Token);

    internal static Invoice Copy(Invoice source)
    {
        Invoice result = new()
        {
            Biller = source.Biller,
            Created = source.Created,
            Tenant = source.Tenant,
            BillerName = source.BillerName,
            Billee = source.Billee,
            BilleeName = source.BilleeName,
            SubtotalCurrency = source.SubtotalCurrency,
            TaxCurrency = source.TaxCurrency,
            GrandTotalCurrency = source.GrandTotalCurrency,
            TimeZone = source.TimeZone,
            Culture = source.Culture
        };
        // The installed framework maps these same public scalar properties, including JsonIgnore fields.
        foreach (PropertyInfo property in EntityMappingHelper.GetMapping(typeof(Invoice)).Values)
        {
            property.SetValue(result, property.GetValue(source));
        }
        return result;
    }

    internal static string Fingerprint(Invoice invoice, IEnumerable<InvoiceItem> items)
    {
        Invoice business = Copy(invoice);
        business.ETag = null;
        business.Timestamp = null;
        business.Token = null;
        business.Delivered = null;
        business.Created = business.Created.ToUniversalTime();
        business.DueBy = business.DueBy?.ToUniversalTime();
        business.BillingPeriodStartDay = business.BillingPeriodStartDay?.ToUniversalTime();
        business.BillingPeriodEndDay = business.BillingPeriodEndDay?.ToUniversalTime();
        string json = JsonMarshaller.Marshall(new
        {
            Invoice = business,
            Items = Order(items).Select(i => new
            {
                Invoice = i.GetInvoiceID(),
                i.ID,
                i.Subject,
                i.Description,
                i.UnitPriceCurrency,
                i.UnitPriceCents,
                i.Quantity,
                i.LineTotalCurrency,
                i.LineTotalCents
            }).ToArray()
        });
        return "v1:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    internal static InvoiceItem[] Order(IEnumerable<InvoiceItem> items) => items
        .OrderBy(i => i.ID.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal).ToArray();

    internal static bool SamePersistedContent(Invoice left, Invoice right) =>
        Fingerprint(left, []) == Fingerprint(right, []) && left.Token == right.Token && left.Delivered == right.Delivered &&
        left.IssuanceState == right.IssuanceState && left.IssuanceOperation == right.IssuanceOperation &&
        left.IssuanceFingerprint == right.IssuanceFingerprint &&
        left.IssuanceDefaultParticulars == right.IssuanceDefaultParticulars &&
        left.IssuanceDefaultReference == right.IssuanceDefaultReference &&
        left.IssuanceDefaultPaymentInstructions == right.IssuanceDefaultPaymentInstructions &&
        left.IssuanceNotificationEndpoint == right.IssuanceNotificationEndpoint;
}
