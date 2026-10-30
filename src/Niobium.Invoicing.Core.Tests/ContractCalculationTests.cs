using Niobium.Invoicing.Core.Tests.Support;

namespace Niobium.Invoicing.Core.Tests;

[TestClass]
public sealed class ContractCalculationTests
{
    [TestMethod]
    [DataRow(10000L, 1, 1500, 10000L, 1500L, 11500L)]
    [DataRow(1L, 1, 5000, 1L, 0L, 1L)]
    [DataRow(3L, 1, 5000, 3L, 2L, 5L)]
    [DataRow(10000L, 0, 1500, 0L, 0L, 0L)]
    [DataRow(-3L, 1, 5000, -3L, -2L, -5L)]
    public void Public_contract_money_helpers_agree_in_cents_and_major_units(long unit, int quantity, int rate, long subtotal, long tax, long total)
    {
        IssuanceScenario fixture = new();
        Invoice invoice = Invoice.BuildNew(fixture.Request.InvoiceID, fixture.Biller, fixture.Billee);
        invoice.TaxRatePercentile = rate;
        InvoiceItem line = fixture.Request.InvoiceItems[0];
        line.UnitPriceCents = unit;
        line.Quantity = quantity;
        InvoiceItem[] lines = [line];
        Assert.AreEqual(subtotal, lines.FigureSubTotalCents());
        Assert.AreEqual(subtotal / 100m, lines.FigureSubTotal());
        Assert.AreEqual(tax, invoice.FigureTaxTotalCents(lines));
        Assert.AreEqual(tax / 100m, invoice.FigureTaxTotal(lines));
        Assert.AreEqual(total, invoice.FigureGrandTotalCents(lines));
        Assert.AreEqual(total / 100m, invoice.FigureGrandTotal(lines));
        StringAssert.Contains(invoice.ToDisplayGrandTotal(lines), Math.Abs(total / 100m).ToString("N2", System.Globalization.CultureInfo.InvariantCulture));
        StringAssert.Contains(invoice.ToDisplayTaxTotal(lines), Math.Abs(tax / 100m).ToString("N2", System.Globalization.CultureInfo.InvariantCulture));
        StringAssert.Contains(invoice.ToDisplaySubTotal(lines), Math.Abs(subtotal / 100m).ToString("N2", System.Globalization.CultureInfo.InvariantCulture));
        StringAssert.Contains(line.ToDisplayLineTotal(), Math.Abs(subtotal / 100m).ToString("N2", System.Globalization.CultureInfo.InvariantCulture));
    }

    [TestMethod]
    public void Public_contract_helpers_reject_line_and_grand_total_overflow()
    {
        IssuanceScenario fixture = new();
        Invoice invoice = Invoice.BuildNew(fixture.Request.InvoiceID, fixture.Biller, fixture.Billee);
        invoice.TaxRatePercentile = 10000;
        InvoiceItem line = fixture.Request.InvoiceItems[0];
        line.UnitPriceCents = long.MaxValue;
        line.Quantity = 2;
        Assert.Throws<OverflowException>(() => line.FigureLineTotalCents());
        line.Quantity = 1;
        Assert.Throws<OverflowException>(() => invoice.FigureGrandTotalCents([line]));
    }

    [TestMethod]
    public void New_line_preserves_parent_reverse_identifier_and_uses_ordinary_milliseconds_for_line_ID()
    {
        DateTimeOffset instant = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        long parent = Invoice.ParseID(instant);
        long before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        InvoiceItem item = InvoiceItem.BuildNew(parent, "NZD");
        Assert.AreEqual(instant, item.Invoice);
        Assert.AreEqual(parent, item.GetInvoiceID());
        Assert.IsTrue(item.ID >= before && item.ID <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Assert.AreEqual(1, item.Quantity);
        Assert.AreEqual(0L, item.FigureLineTotalCents());
        Assert.AreEqual("NZD", item.UnitPriceCurrency);
        Assert.AreEqual("NZD", item.LineTotalCurrency);
        Assert.AreEqual(Invoice.BuildRowKey(parent), InvoiceItem.BuildPartitionKey(parent));
        Assert.AreEqual("42", InvoiceItem.BuildRowKey(42));
    }
}
