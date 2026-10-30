using Niobium.Profile;

namespace Niobium.Invoicing;

/// <summary>
/// External biller profile containing issuer identity, contact, tax and presentation defaults.
/// </summary>
public class Biller : IProfile
{
    /// <summary>
    /// Management-group identifier exposed by IProfile.GetTenant; profile storage is external.
    /// </summary>
    public Guid PartitionKey
    {
        get; set;
    }

    /// <summary>
    /// Biller/user identifier exposed by IProfile.GetUser; copied to Invoice.Biller.
    /// </summary>
    public Guid RowKey
    {
        get; set;
    }

    /// <summary>
    /// Optional upstream profile modification timestamp.
    /// </summary>
    public DateTimeOffset? Timestamp
    {
        get; set;
    }

    /// <summary>
    /// Optional upstream profile concurrency metadata.
    /// </summary>
    public string? ETag
    {
        get; set;
    }

    /// <summary>
    /// First postal-address line.
    /// </summary>
    public string? AddressLine1
    {
        get; set;
    }

    /// <summary>
    /// Optional second postal-address line.
    /// </summary>
    public string? AddressLine2
    {
        get; set;
    }

    /// <summary>
    /// Optional postal suburb or locality.
    /// </summary>
    public string? Suburb
    {
        get; set;
    }

    /// <summary>
    /// Optional business identifier copied into invoices.
    /// </summary>
    public string? BusinessID
    {
        get; set;
    }

    /// <summary>
    /// Business display name copied into the issued invoice snapshot.
    /// </summary>
    public required string BusinessName
    {
        get; set;
    }

    /// <summary>
    /// Optional postal city.
    /// </summary>
    public string? City
    {
        get; set;
    }

    /// <summary>
    /// Optional postal state or region.
    /// </summary>
    public string? State
    {
        get; set;
    }

    /// <summary>
    /// Optional country code; invoice rendering parses it to a country name.
    /// </summary>
    public string? Country
    {
        get; set;
    }

    /// <summary>
    /// Biller contact email copied to the invoice, not the notification destination.
    /// </summary>
    public string? Email
    {
        get; set;
    }

    /// <summary>
    /// Optional logo reference copied into the invoice template parameters.
    /// </summary>
    public string? Logo
    {
        get; set;
    }

    /// <summary>
    /// Default payment text copied by BuildNew; UpdateAsync subsequently assigns the request value,
    /// including null.
    /// </summary>
    public string? PaymentInstructions
    {
        get; set;
    }

    /// <summary>
    /// Biller contact person copied into the invoice.
    /// </summary>
    public string? ContactName
    {
        get; set;
    }

    /// <summary>
    /// Biller contact telephone copied into the invoice.
    /// </summary>
    public string? Phone
    {
        get; set;
    }

    /// <summary>
    /// Tax registration identifier copied into the invoice; no jurisdictional validation is implemented.
    /// </summary>
    public string? TaxID
    {
        get; set;
    }

    /// <summary>
    /// Tax rate in hundredths of one percent: 1500 means 15%; copied to invoices. Zero represents exemption
    /// in the product.
    /// </summary>
    public int TaxRatePercentile
    {
        get; set;
    }

    /// <summary>
    /// Optional numeric Niobium.Finance.TaxKind value used as a display label.
    /// </summary>
    public int? TaxKind
    {
        get; set;
    }

    /// <summary>
    /// Optional postal code.
    /// </summary>
    public string? Zipcode
    {
        get; set;
    }

    /// <summary>
    /// Optional template reference copied to invoices; current HTML rendering uses the embedded resource
    /// instead.
    /// </summary>
    public string? Template
    {
        get; set;
    }

    /// <summary>
    /// Biller timezone profile value; BuildNew currently selects the billee timezone for the invoice.
    /// </summary>
    public required string TimeZone
    {
        get; set;
    }

    /// <summary>
    /// Biller currency profile value; BuildNew currently selects the billee currency for totals.
    /// </summary>
    public required string Currency
    {
        get; set;
    }

    /// <summary>
    /// Biller culture profile value; BuildNew currently selects the billee culture for the invoice.
    /// </summary>
    public required string Culture
    {
        get; set;
    }
}
