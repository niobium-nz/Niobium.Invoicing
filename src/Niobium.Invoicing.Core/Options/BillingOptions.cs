namespace Niobium.Invoicing.Options
{
    /// <summary>
    /// Configuration for public invoice links and their token derivation.
    /// </summary>
    public class BillingOptions
    {
        /// <summary>
        /// Secret input used when deriving invoice-link tokens; required by the email flow.
        /// </summary>
        public required string InvoiceTokenSecretSalt { get; set; }
        /// <summary>
        /// Public base URL used to construct invoice links; combined with biller, invoice ID and token.
        /// </summary>
        public required string GetInvoiceEndpoint { get; set; }
    }
}
