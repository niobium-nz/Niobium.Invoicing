using Niobium.Finance;

namespace Niobium.Invoicing.Domains
{
    internal static class InvoiceItemExtensions
    {
        public static string BuildHTML(this InvoiceItem invoiceItem, string template)
        {
            return template.Replace("{{Subject}}", invoiceItem.Subject)
                .Replace("{{Description}}", invoiceItem.Description)
                .Replace("{{UnitPrice}}", Currency.Parse(invoiceItem.UnitPriceCurrency).ToDisplayLocal(invoiceItem.UnitPriceCents / 100d))
                .Replace("{{Quantity}}", invoiceItem.Quantity.ToString())
                .Replace("{{LineTotal}}", Currency.Parse(invoiceItem.LineTotalCurrency).ToDisplayLocal(invoiceItem.LineTotalCents / 100d));
        }
    }
}
