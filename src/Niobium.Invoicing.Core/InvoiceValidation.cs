using Niobium.Finance;
using Niobium.Profile;

namespace Niobium.Invoicing;

/// <summary>Billing invariants shared by HTTP and private queue issuance.</summary>
internal static class InvoiceValidation
{
    internal readonly record struct Totals(long Subtotal, long Tax, long GrandTotal, DateTimeOffset DueBy);

    internal static void ValidateRequest(IssueInvoiceRequest request)
    {
        if (request == null || request.Tenant == Guid.Empty || request.BillerID == Guid.Empty || request.BilleeID == Guid.Empty)
        {
            throw Invalid("Tenant, biller and billee identifiers are required.");
        }

        if (request.InvoiceID <= 0)
        {
            throw Invalid("Invoice identifier must be a positive reverse Unix millisecond value.");
        }

        try
        {
            _ = Invoice.ParseID(request.InvoiceID);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Invalid("Invoice identifier is outside the supported date range.");
        }

        if (!Enum.IsDefined((InvoiceCycle)request.InvoiceCycle))
        {
            throw Invalid("Invoice cycle is not supported.");
        }

        if (request.BillingPeriodStartDay > request.BillingPeriodEndDay)
        {
            throw Invalid("Billing period end must not precede its start.");
        }

        if (request.InvoiceItems == null)
        {
            throw Invalid("Invoice items are required.");
        }

        ValidateItems(request.InvoiceID, request.InvoiceItems);
    }

    private static void ValidateItems(long invoiceID, IReadOnlyList<InvoiceItem> items)
    {
        HashSet<long> identifiers = [];
        foreach (InvoiceItem? item in items)
        {
            if (item == null || item.Subject == null)
            {
                throw Invalid("Invoice items and their subjects cannot be null.");
            }

            if (item.GetInvoiceID() != invoiceID)
            {
                throw Invalid("Each item must belong to the requested invoice.");
            }

            if (!identifiers.Add(item.ID))
            {
                throw Invalid("Invoice item identifiers must be unique within the invoice.");
            }
        }
    }

    internal static void ValidateParties(IssueInvoiceRequest request, Biller biller, Billee billee)
    {
        if (biller.GetTenant() != request.Tenant || biller.GetUser() != request.BillerID ||
            billee.Biller != request.BillerID || billee.ID != request.BilleeID)
        {
            throw Invalid("Biller and customer ownership must match the request.");
        }

        if (string.IsNullOrWhiteSpace(biller.BusinessName) || string.IsNullOrWhiteSpace(billee.Name))
        {
            throw Invalid("Biller and customer names are required.");
        }

        Currency currency = SupportedCurrency(biller.Currency);
        if (SupportedCurrency(billee.Currency) != currency)
        {
            throw Invalid("Biller and customer currencies must match.");
        }

        ValidateCountry(biller.Country);
        ValidateCountry(billee.Country);
    }

    internal static Totals ValidateAndCalculate(IssueInvoiceRequest request, Invoice invoice, IReadOnlyList<InvoiceItem> items)
    {
        ValidateRequest(request);
        ValidateItems(request.InvoiceID, items);
        if (invoice.Tenant != request.Tenant || invoice.Biller != request.BillerID ||
            invoice.Billee != request.BilleeID || invoice.GetID() != request.InvoiceID)
        {
            throw Invalid("Invoice identity must match the request.");
        }

        Currency currency = SupportedCurrency(invoice.GrandTotalCurrency);
        if (SupportedCurrency(invoice.SubtotalCurrency) != currency || SupportedCurrency(invoice.TaxCurrency) != currency)
        {
            throw Invalid("Invoice currencies must match.");
        }

        // Zero carries no settled value. This also preserves omitted Amount.Zero requests (USD).
        if (request.Settled.Cents != 0 && request.Settled.Currency != currency)
        {
            throw Invalid("Settlement currency must match the invoice currency.");
        }

        try
        {
            DateTimeOffset dueBy = request.DueBy ?? invoice.DueBy ?? invoice.Created.AddDays(7);
            if (dueBy < invoice.Created)
            {
                throw Invalid("Due date must not precede the invoice issue time.");
            }

            foreach (InvoiceItem item in items)
            {
                if (SupportedCurrency(item.UnitPriceCurrency) != currency || SupportedCurrency(item.LineTotalCurrency) != currency)
                {
                    throw Invalid("Item currencies must match the invoice currency.");
                }
            }

            // Preserve the existing midpoint-to-even tax calculation and whole-unit quantities.
            long subtotal = items.FigureSubTotalCents();
            long tax = invoice.FigureTaxTotalCents(items);
            long total = checked(subtotal + tax);
            _ = checked(total - request.Settled.Cents);
            return new Totals(subtotal, tax, total, dueBy);
        }
        catch (OverflowException)
        {
            throw Invalid("Invoice amounts exceed the supported integer-cent range.");
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Invalid("Due date is outside the supported date range.");
        }
    }

    private static Currency SupportedCurrency(string code)
    {
        if (!Currency.TryParse(code, out Currency currency) || (currency != Currency.NZD && currency != Currency.AUD))
        {
            throw Invalid("Stage-one invoicing supports NZD and AUD.");
        }

        return currency;
    }

    private static void ValidateCountry(string? country)
    {
        // Postal addresses remain optional. Do not infer tax rates or currency from residence.
        if (!string.IsNullOrWhiteSpace(country) &&
            !string.Equals(country.Trim(), "NZ", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(country.Trim(), "AU", StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("Stage-one invoicing supports New Zealand and Australia.");
        }
    }

    private static ApplicationException Invalid(string message) => new(InternalError.BadRequest, message);
}
