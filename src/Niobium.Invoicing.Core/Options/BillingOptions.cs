namespace Niobium.Invoicing.Options;

/// <summary>
/// Configuration for public invoice links and their token derivation.
/// </summary>
public class BillingOptions
{
    /// <summary>
    /// High-entropy secret used in full for new invoice-link tokens. Existing saved tokens are reused
    /// without this value being involved in validation.
    /// </summary>
    public required string InvoiceTokenSecretSalt
    {
        get; set;
    }
    /// <summary>
    /// Public base URL used to construct invoice links; combined with biller, invoice ID and token.
    /// </summary>
    public required string GetInvoiceEndpoint
    {
        get; set;
    }
}
