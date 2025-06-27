using Cod;
using Cod.Finance;

namespace Niobium.Invoicing
{
    public static class InvoiceExtensions
    {
        public static long FigureGrandTotalCents(this Invoice invoice, IEnumerable<InvoiceItem> items)
        {
            var subtotal = items.FigureSubTotalCents();
            var tax = invoice.FigureTaxTotalCents(items);
            return subtotal + tax;
        }

        public static decimal FigureGrandTotal(this Invoice invoice, IEnumerable<InvoiceItem> items)
        {
            var subtotal = items.FigureSubTotal();
            var tax = invoice.FigureTaxTotal(items);
            return subtotal + tax;
        }

        public static string ToDisplayGrandTotal(this Invoice invoice, IEnumerable<InvoiceItem> items)
        {
            var figure = invoice.FigureGrandTotal(items);
            var currency = Currency.Parse(invoice.GrandTotalCurrency);
            return currency.ToDisplayLocal(figure);
        }

        public static long FigureTaxTotalCents(this Invoice invoice, IEnumerable<InvoiceItem> items)
        {
            var taxable = items.FigureSubTotalCents();
            return (long)Math.Round(taxable * (invoice.TaxRatePercentile / 10000m), 0);
        }

        public static decimal FigureTaxTotal(this Invoice invoice, IEnumerable<InvoiceItem> items)
        {
            var taxable = items.FigureSubTotal();
            return Math.Round(taxable * (invoice.TaxRatePercentile / 10000m), 2);
        }

        public static string ToDisplayTaxTotal(this Invoice invoice, IEnumerable<InvoiceItem> items)
        {
            var figure = invoice.FigureTaxTotal(items);
            var currency = Currency.Parse(invoice.TaxCurrency);
            return currency.ToDisplayLocal(figure);
        }

        public static long FigureSubTotalCents(this IEnumerable<InvoiceItem> items)
        {
            return items.Sum(item => item.FigureLineTotalCents());
        }

        public static decimal FigureSubTotal(this IEnumerable<InvoiceItem> items)
        {
            return items.Sum(item => item.FigureLineTotal());
        }

        public static string ToDisplaySubTotal(this Invoice invoice, IEnumerable<InvoiceItem> items)
        {
            var figure = items.FigureSubTotal();
            var currency = Currency.Parse(invoice.SubtotalCurrency);
            return currency.ToDisplayLocal(figure);
        }

        public static long FigureLineTotalCents(this InvoiceItem item)
        {
            return item.UnitPriceCents * item.Quantity;
        }

        public static decimal FigureLineTotal(this InvoiceItem item)
        {
            return Math.Round(item.FigureLineTotalCents() / 100m, 2);
        }

        public static string ToDisplayLineTotal(this InvoiceItem item)
        {
            var figure = item.FigureLineTotal();
            var currency = Currency.Parse(item.LineTotalCurrency);
            return currency.ToDisplayLocal(figure);
        }
    }
}
