namespace Niobium.Invoicing;

/// <summary>
/// Customer data scoped to a biller and copied into invoice snapshots.
/// </summary>
public class Billee : ITrackable
{
    /// <summary>
    /// Owning biller identifier, mapped to the table partition key.
    /// </summary>
    [EntityKey(EntityKeyKind.PartitionKey)]
    public Guid Biller
    {
        get; set;
    }

    /// <summary>
    /// Customer identifier within the biller partition, mapped to the row key.
    /// </summary>
    [EntityKey(EntityKeyKind.RowKey)]
    public Guid ID
    {
        get; set;
    }

    /// <summary>
    /// Storage modification timestamp, distinct from Created.
    /// </summary>
    [EntityKey(EntityKeyKind.Timestamp)]
    public DateTimeOffset? Timestamp
    {
        get; set;
    }

    /// <summary>
    /// Optional storage ETag for precondition-aware repository operations.
    /// </summary>
    [EntityKey(EntityKeyKind.ETag)]
    public string? ETag
    {
        get; set;
    }

    /// <summary>
    /// Optional creation time; populated by the framework on create when absent.
    /// </summary>
    public DateTimeOffset? Created
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
    /// Customer display name copied to new invoices and notification destination display name.
    /// </summary>
    public required string Name
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
    /// Optional invoice recipient address; blank causes EmailFlow to skip enqueueing.
    /// </summary>
    public string? Email
    {
        get; set;
    }

    /// <summary>
    /// Optional customer messaging contact; unused by current invoice flows.
    /// </summary>
    public string? InstantMessaging
    {
        get; set;
    }

    /// <summary>
    /// Optional customer telephone contact; not copied into invoice biller contact fields.
    /// </summary>
    public string? Phone
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
    /// Culture name used for invoice date formatting, for example en-NZ.
    /// </summary>
    public required string Culture
    {
        get; set;
    }

    /// <summary>
    /// Currency code copied to all invoice total-currency fields; no biller/item compatibility check is
    /// performed.
    /// </summary>
    public required string Currency
    {
        get; set;
    }

    /// <summary>
    /// IANA timezone used for invoice dates, for example Pacific/Auckland.
    /// </summary>
    public required string TimeZone
    {
        get; set;
    }

    /// <summary>
    /// Formats the owning biller as the customer-table partition key.
    /// </summary>
    /// <param name="biller">Issuing biller identifier.</param>
    /// <returns>The GUID key using the active Niobium GUID-format convention.</returns>
    public static string BuildPartitionKey(Guid biller)
    {
        return biller.ToKey();
    }

    /// <summary>
    /// Formats a customer identifier as its table row key.
    /// </summary>
    /// <param name="billee">Customer identifier within the owning biller partition.</param>
    /// <returns>The GUID key using the active Niobium GUID-format convention.</returns>
    public static string BuildRowKey(Guid billee)
    {
        return billee.ToKey();
    }
}
