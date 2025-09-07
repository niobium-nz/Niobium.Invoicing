using Niobium.Finance;

namespace Niobium.Invoicing.Domains
{
    internal static class InvoiceItemExtensions
    {
        public static IReadOnlyDictionary<string, string> BuildTemplateParameters(this InvoiceItem invoiceItem)
        {
            return new Dictionary<string, string>
            {
                { nameof(invoiceItem.Subject).ToSnakeCaseUpper(), invoiceItem.Subject },
                { nameof(invoiceItem.Description).ToSnakeCaseUpper(), invoiceItem.Description ?? string.Empty },
                { "UNIT_PRICE", Currency.Parse(invoiceItem.UnitPriceCurrency).ToDisplayLocal(invoiceItem.UnitPriceCents / 100d) },
                { nameof(invoiceItem.Quantity).ToSnakeCaseUpper(), invoiceItem.Quantity.ToString() },
                { "LINE_TOTAL", Currency.Parse(invoiceItem.LineTotalCurrency).ToDisplayLocal(invoiceItem.LineTotalCents / 100d) },
            };
        }
    }
}
