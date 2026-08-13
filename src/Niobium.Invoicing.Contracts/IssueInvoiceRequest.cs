using Niobium.Finance;

namespace Niobium.Invoicing
{
    public class IssueInvoiceRequest
    {
        public long InvoiceID { get; set; }

        public required Guid Tenant { get; set; }

        public required Guid BillerID { get; set; }

        public required Guid BilleeID { get; set; }

        public string? Particulars { get; set; }

        public string? Reference { get; set; }

        public int InvoiceCycle { get; set; }

        public DateTimeOffset? BillingPeriodStartDay { get; set; }

        public DateTimeOffset? BillingPeriodEndDay { get; set; }

        public DateTimeOffset? DueBy { get; set; }

        public string? Terms { get; set; }

        public string? PaymentInstructions { get; set; }

        public required List<InvoiceItem> InvoiceItems { get; set; }

        public Amount Settled { get; set; } = Amount.Zero;

        public bool NotifyBillee { get; set; }
    }
}
