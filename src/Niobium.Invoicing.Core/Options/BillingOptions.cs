namespace Niobium.Invoicing.Options
{
    public class BillingOptions
    {
        public required string InvoiceTokenSecretSalt { get; set; }
        public required string GetInvoiceEndpoint { get; set; }
    }
}
