namespace Niobium.Invoicing
{
    public class BillingOptions
    {
        public required string InvoiceTokenSecretSalt { get; set; }
        public required string GetInvoiceEndpoint { get; set; }
        public required string InvoiceEmailSenderAddress { get; set; }
    }
}
