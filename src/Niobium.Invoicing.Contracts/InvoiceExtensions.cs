using Niobium;
using Niobium.Finance;

namespace Niobium.Invoicing
{
    /// <summary>
    /// Calculates line, subtotal, invoice tax and final tax-inclusive totals from supplied items.
    /// </summary>
    public static class InvoiceExtensions
    {
        /// <summary>
        /// Adds recomputed subtotal and rounded invoice tax in cents.
        /// </summary>
        /// <param name="invoice">Invoice supplying TaxRatePercentile for invoice-level tax.</param>
        /// <param name="items">Invoice lines used by the calculation.</param>
        /// <returns>The final total including tax, in cents.</returns>
        /// <remarks>
        /// Uses supplied prices/quantities rather than stored totals; currency compatibility and value ranges
        /// are not validated. The item sequence must support repeated enumeration.
        /// </remarks>
        public static long FigureGrandTotalCents(this Invoice invoice, IEnumerable<InvoiceItem> items)
        {
            long subtotal = items.FigureSubTotalCents();
            long tax = invoice.FigureTaxTotalCents(items);
            return subtotal + tax;
        }

        /// <summary>
        /// Adds recomputed subtotal and invoice tax in major currency units.
        /// </summary>
        /// <param name="invoice">Invoice supplying TaxRatePercentile for invoice-level tax.</param>
        /// <param name="items">Invoice lines used by the calculation.</param>
        /// <returns>The final total including tax, in major units.</returns>
        /// <remarks>
        /// Uses supplied prices/quantities rather than stored totals; currency compatibility and value ranges
        /// are not validated. The item sequence must support repeated enumeration.
        /// </remarks>
        public static decimal FigureGrandTotal(this Invoice invoice, IEnumerable<InvoiceItem> items)
        {
            decimal subtotal = items.FigureSubTotal();
            decimal tax = invoice.FigureTaxTotal(items);
            return subtotal + tax;
        }

        /// <summary>
        /// Formats the recomputed final total using the invoice grand-total currency.
        /// </summary>
        /// <param name="invoice">Invoice supplying the tax rate and GrandTotalCurrency.</param>
        /// <param name="items">Invoice lines used by the calculation.</param>
        /// <returns>The currency-formatted tax-inclusive total.</returns>
        /// <remarks>
        /// Uses supplied prices/quantities rather than stored totals; currency compatibility and value ranges
        /// are not validated. The item sequence must support repeated enumeration.
        /// </remarks>
        public static string ToDisplayGrandTotal(this Invoice invoice, IEnumerable<InvoiceItem> items)
        {
            decimal figure = invoice.FigureGrandTotal(items);
            Currency currency = Currency.Parse(invoice.GrandTotalCurrency);
            return currency.ToDisplayLocal(figure);
        }

        /// <summary>
        /// Computes invoice-level tax from the summed line cents and configured rate.
        /// </summary>
        /// <param name="invoice">Invoice supplying TaxRatePercentile for invoice-level tax.</param>
        /// <param name="items">Invoice lines used by the calculation.</param>
        /// <returns>Tax rounded to whole cents using midpoint-to-even.</returns>
        /// <remarks>
        /// Uses supplied prices/quantities rather than stored totals; currency compatibility and value ranges
        /// are not validated. TaxRatePercentile is divided by 10000; 1500 represents 15%.
        /// </remarks>
        public static long FigureTaxTotalCents(this Invoice invoice, IEnumerable<InvoiceItem> items)
        {
            long taxable = items.FigureSubTotalCents();
            return (long)Math.Round(taxable * (invoice.TaxRatePercentile / 10000m), 0);
        }

        /// <summary>
        /// Computes invoice-level tax from the subtotal in major units.
        /// </summary>
        /// <param name="invoice">Invoice supplying TaxRatePercentile for invoice-level tax.</param>
        /// <param name="items">Invoice lines used by the calculation.</param>
        /// <returns>Tax rounded to two decimal places using midpoint-to-even.</returns>
        /// <remarks>
        /// Uses supplied prices/quantities rather than stored totals; currency compatibility and value ranges
        /// are not validated. TaxRatePercentile is divided by 10000; 1500 represents 15%.
        /// </remarks>
        public static decimal FigureTaxTotal(this Invoice invoice, IEnumerable<InvoiceItem> items)
        {
            decimal taxable = items.FigureSubTotal();
            return Math.Round(taxable * (invoice.TaxRatePercentile / 10000m), 2);
        }

        /// <summary>
        /// Formats recomputed tax using the invoice tax currency.
        /// </summary>
        /// <param name="invoice">Invoice supplying the tax rate and TaxCurrency.</param>
        /// <param name="items">Invoice lines used by the calculation.</param>
        /// <returns>The currency-formatted tax amount.</returns>
        /// <remarks>
        /// Uses supplied prices/quantities rather than stored totals; currency compatibility and value ranges
        /// are not validated.
        /// </remarks>
        public static string ToDisplayTaxTotal(this Invoice invoice, IEnumerable<InvoiceItem> items)
        {
            decimal figure = invoice.FigureTaxTotal(items);
            Currency currency = Currency.Parse(invoice.TaxCurrency);
            return currency.ToDisplayLocal(figure);
        }

        /// <summary>
        /// Sums unit-price cents multiplied by quantity across all supplied lines.
        /// </summary>
        /// <param name="items">Invoice lines used by the calculation.</param>
        /// <returns>The subtotal in cents, before invoice-level tax.</returns>
        /// <remarks>
        /// Uses supplied prices/quantities rather than stored totals; currency compatibility and value ranges
        /// are not validated.
        /// </remarks>
        public static long FigureSubTotalCents(this IEnumerable<InvoiceItem> items)
        {
            return items.Sum(item => item.FigureLineTotalCents());
        }

        /// <summary>
        /// Sums calculated line amounts in major currency units.
        /// </summary>
        /// <param name="items">Invoice lines used by the calculation.</param>
        /// <returns>The subtotal in major units, before invoice-level tax.</returns>
        /// <remarks>
        /// Uses supplied prices/quantities rather than stored totals; currency compatibility and value ranges
        /// are not validated.
        /// </remarks>
        public static decimal FigureSubTotal(this IEnumerable<InvoiceItem> items)
        {
            return items.Sum(item => item.FigureLineTotal());
        }

        /// <summary>
        /// Formats recomputed subtotal using the invoice subtotal currency.
        /// </summary>
        /// <param name="invoice">Invoice supplying SubtotalCurrency.</param>
        /// <param name="items">Invoice lines used by the calculation.</param>
        /// <returns>The currency-formatted subtotal.</returns>
        /// <remarks>
        /// Uses supplied prices/quantities rather than stored totals; currency compatibility and value ranges
        /// are not validated.
        /// </remarks>
        public static string ToDisplaySubTotal(this Invoice invoice, IEnumerable<InvoiceItem> items)
        {
            decimal figure = items.FigureSubTotal();
            Currency currency = Currency.Parse(invoice.SubtotalCurrency);
            return currency.ToDisplayLocal(figure);
        }

        /// <summary>
        /// Multiplies unit-price cents by integer quantity.
        /// </summary>
        /// <param name="item">Line whose calculated amount is required.</param>
        /// <returns>The line total in cents, before invoice-level tax.</returns>
        /// <remarks>
        /// Uses supplied prices/quantities rather than stored totals; currency compatibility and value ranges
        /// are not validated.
        /// </remarks>
        public static long FigureLineTotalCents(this InvoiceItem item)
        {
            return item.UnitPriceCents * item.Quantity;
        }

        /// <summary>
        /// Converts the calculated line cents to major units rounded to two places.
        /// </summary>
        /// <param name="item">Line whose calculated amount is required.</param>
        /// <returns>The calculated line total in major units.</returns>
        /// <remarks>
        /// Uses supplied prices/quantities rather than stored totals; currency compatibility and value ranges
        /// are not validated.
        /// </remarks>
        public static decimal FigureLineTotal(this InvoiceItem item)
        {
            return Math.Round(item.FigureLineTotalCents() / 100m, 2);
        }

        /// <summary>
        /// Formats the recomputed line total using its line-total currency.
        /// </summary>
        /// <param name="item">Line whose calculated amount is required.</param>
        /// <returns>The currency-formatted line total.</returns>
        /// <remarks>
        /// Uses supplied prices/quantities rather than stored totals; currency compatibility and value ranges
        /// are not validated.
        /// </remarks>
        public static string ToDisplayLineTotal(this InvoiceItem item)
        {
            decimal figure = item.FigureLineTotal();
            Currency currency = Currency.Parse(item.LineTotalCurrency);
            return currency.ToDisplayLocal(figure);
        }
    }
}
