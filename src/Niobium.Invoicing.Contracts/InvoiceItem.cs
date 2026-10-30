namespace Niobium.Invoicing;

/// <summary>
/// Persisted invoice line keyed by the parent issue timestamp and a line identifier.
/// </summary>
public class InvoiceItem
{
    /// <summary>
    /// Parent invoice issue instant, mapped to the reverse-timestamp partition key; it contains no biller
    /// or management-group identifier.
    /// </summary>
    [EntityKey(EntityKeyKind.PartitionKey)]
    public required DateTimeOffset Invoice
    {
        get; set;
    }

    /// <summary>
    /// Line identifier mapped to the row key; BuildNew uses current ordinary Unix milliseconds.
    /// </summary>
    [EntityKey(EntityKeyKind.RowKey)]
    public required long ID
    {
        get; set;
    }

    /// <summary>
    /// Storage modification timestamp for the line.
    /// </summary>
    [EntityKey(EntityKeyKind.Timestamp)]
    public DateTimeOffset? UpdatedAt
    {
        get; set;
    }

    /// <summary>
    /// Optional storage ETag for precondition-aware operations.
    /// </summary>
    [EntityKey(EntityKeyKind.ETag)]
    public string? ETag
    {
        get; set;
    }

    /// <summary>
    /// Line subject shown in invoice and notification templates.
    /// </summary>
    public required string Subject
    {
        get; set;
    }

    /// <summary>
    /// Optional line description.
    /// </summary>
    public string? Description
    {
        get; set;
    }

    /// <summary>
    /// Currency code for unit-price display; compatibility with the invoice is not validated.
    /// </summary>
    public required string UnitPriceCurrency
    {
        get; set;
    }

    /// <summary>
    /// Unit price in cents used by line arithmetic before invoice-level tax.
    /// </summary>
    public long UnitPriceCents
    {
        get; set;
    }

    /// <summary>
    /// Integer number of units; BuildNew sets one, with no range validator here.
    /// </summary>
    public int Quantity
    {
        get; set;
    }

    /// <summary>
    /// Currency code for stored line-total display; BuildNew copies the supplied currency.
    /// </summary>
    public required string LineTotalCurrency
    {
        get; set;
    }

    /// <summary>
    /// Stored unit-price cents times quantity, recalculated by InvoiceDomain.UpdateAsync.
    /// </summary>
    public long LineTotalCents
    {
        get; set;
    }

    /// <summary>
    /// Encodes the parent issue instant as its invoice identifier.
    /// </summary>
    /// <returns>The parent reverse-millisecond invoice identifier.</returns>
    public long GetInvoiceID()
    {
        return Invoicing.Invoice.ParseID(Invoice);
    }

    /// <summary>
    /// Formats the parent invoice identifier as the item-table partition key.
    /// </summary>
    /// <param name="invoiceID">Parent invoice identifier in reverse Unix milliseconds.</param>
    /// <seealso cref="Niobium.Invoicing.Invoice.BuildRowKey(long)"/>
    /// <returns>The padded reverse-timestamp key, without a biller or tenant component.</returns>
    public static string BuildPartitionKey(long invoiceID)
    {
        return Invoicing.Invoice.BuildRowKey(invoiceID);
    }

    /// <summary>
    /// Formats the supplied line identifier as decimal row-key text.
    /// </summary>
    /// <param name="id">Line identifier to format.</param>
    /// <returns>The identifier text.</returns>
    /// <remarks>
    /// This helper accepts int, while the persisted ID property is long.
    /// </remarks>
    public static string BuildRowKey(int id)
    {
        return id.ToString();
    }

    /// <summary>
    /// Constructs an unsaved line with one unit and zero monetary values.
    /// </summary>
    /// <param name="invoiceID">Parent invoice identifier in reverse Unix milliseconds.</param>
    /// <param name="currency">Currency code assigned to both line currency fields.</param>
    /// <returns>A line with an empty subject, supplied currency and current Unix-millisecond line ID.</returns>
    /// <remarks>
    /// The parent ID uses reverse milliseconds; the new line ID uses ordinary Unix milliseconds. No
    /// cross-call uniqueness guarantee is provided.
    /// </remarks>
    public static InvoiceItem BuildNew(long invoiceID, string currency)
    {
        return new()
        {
            Subject = string.Empty,
            Quantity = 1,
            Invoice = Invoicing.Invoice.ParseID(invoiceID),
            ID = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            LineTotalCurrency = currency,
            UnitPriceCurrency = currency,
        };
    }
}
