namespace Niobium.Invoicing
{
    public class IssueInvoiceCommand
    {
        public required Invoice Invoice { get; set; }

        public required InvoiceItem[] InvoiceItems { get; set; }

        public bool NotifyBillee { get; set; }
    }
}
