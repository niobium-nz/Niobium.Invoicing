namespace Niobium.Billing.Functions
{
    public class BillingOptions
    {
        public required string InvoiceTokenSecret { get; set; }
        public required string GetInvoiceEndpoint { get; set; }
        public required string InvoiceEmailSenderAddress { get; set; }
        public bool IsGetInvoiceVerifyToken { get; set; }
    }
}
