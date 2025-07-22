namespace Niobium.Invoicing
{
    public class IssueInvoiceRequest
    {
        public long ID { get; set; }

        public required Guid BillerID { get; set; }

        public required Guid BilleeID { get; set; }

        public string? Particulars { get; set; }

        public string? Reference { get; set; }

        public int BillingPeriodKind { get; set; }

        public DateTimeOffset? BillingPeriodStartDay { get; set; }

        public DateTimeOffset? BillingPeriodEndDay { get; set; }

        public DateTimeOffset? DueBy { get; set; }

        public string? Terms { get; set; }

        public string? PaymentInstructions { get; set; }

        public required InvoiceItem[] InvoiceItems { get; set; }

        public bool NotifyBillee { get; set; }
    }
}
