using Niobium.Finance;

namespace Niobium.Invoicing.Domains
{
    /// <summary>
    /// Formats stored line values for HTML and notification templates.
    /// </summary>
    internal static class InvoiceItemExtensions
    {
        /// <summary>
        /// Formats stored subject, description, unit price, quantity and line total.
        /// </summary>
        /// <param name="invoiceItem">Line whose stored values are formatted.</param>
        /// <returns>The line template-key dictionary.</returns>
        /// <remarks>
        /// Uses stored LineTotalCents rather than recalculating it; unsupported display currencies can fail.
        /// </remarks>
        public static IReadOnlyDictionary<string, string> BuildTemplateParameters(this InvoiceItem invoiceItem)
        {
            return new Dictionary<string, string>
            {
                { nameof(invoiceItem.Subject).ToSnakeCaseUpper(), invoiceItem.Subject },
                { nameof(invoiceItem.Description).ToSnakeCaseUpper(), invoiceItem.Description ?? string.Empty },
                { "UNIT_PRICE", Currency.Parse(invoiceItem.UnitPriceCurrency).ToDisplayLocal(invoiceItem.UnitPriceCents / 100d) },
                { nameof(invoiceItem.Quantity).ToSnakeCaseUpper(), invoiceItem.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                { "LINE_TOTAL", Currency.Parse(invoiceItem.LineTotalCurrency).ToDisplayLocal(invoiceItem.LineTotalCents / 100d) },
            };
        }
    }
}
