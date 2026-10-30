using Niobium.Profile;

namespace Niobium.Invoicing
{
    /// <summary>
    /// Persisted invoice snapshot with biller/customer details, amounts and enqueue-related delivery
    /// metadata.
    /// </summary>
    public class Invoice
    {
        /// <summary>
        /// Maximum characters retained in generated payment particulars.
        /// </summary>
        private const int ParticularsMaxLength = 12;
        /// <summary>
        /// Maximum trailing characters retained in the generated payment reference.
        /// </summary>
        private const int ReferenceMaxLength = 12;

        /// <summary>
        /// Issuing business identifier and table partition key.
        /// </summary>
        [EntityKey(EntityKeyKind.PartitionKey)]
        public required Guid Biller { get; set; }

        /// <summary>
        /// Issue instant encoded in the consumer-supplied reverse-millisecond invoice ID; mapped to the row
        /// key.
        /// </summary>
        [EntityKey(EntityKeyKind.RowKey)]
        public required DateTimeOffset Created { get; set; }

        /// <summary>
        /// Storage modification timestamp, distinct from issue time.
        /// </summary>
        [EntityKey(EntityKeyKind.Timestamp)]
        public DateTimeOffset? Timestamp { get; set; }

        /// <summary>
        /// Optional storage ETag; force-save does not use it as an update precondition.
        /// </summary>
        [EntityKey(EntityKeyKind.ETag)]
        public string? ETag { get; set; }

        /// <summary>
        /// Invoicing management-group identifier copied from the biller profile; not part of the table key.
        /// </summary>
        public required Guid Tenant { get; set; }

        /// <summary>
        /// Biller logo reference captured when the invoice is built.
        /// </summary>
        public string? BillerLogo { get; set; }

        /// <summary>
        /// Biller business display name captured when the invoice is built.
        /// </summary>
        public required string BillerName { get; set; }

        /// <summary>
        /// Biller first address line snapshot copied when the invoice is built.
        /// </summary>
        public string? BillerAddressLine1 { get; set; }

        /// <summary>
        /// Biller second address line snapshot copied when the invoice is built.
        /// </summary>
        public string? BillerAddressLine2 { get; set; }

        /// <summary>
        /// Biller suburb/locality snapshot copied when the invoice is built.
        /// </summary>
        public string? BillerAddressSuburb { get; set; }

        /// <summary>
        /// Biller city snapshot copied when the invoice is built.
        /// </summary>
        public string? BillerAddressCity { get; set; }

        /// <summary>
        /// Biller state/region snapshot copied when the invoice is built.
        /// </summary>
        public string? BillerAddressState { get; set; }

        /// <summary>
        /// Biller country code snapshot copied when the invoice is built.
        /// </summary>
        public string? BillerAddressCountry { get; set; }

        /// <summary>
        /// Biller postal code snapshot copied when the invoice is built.
        /// </summary>
        public string? BillerAddressZipcode { get; set; }

        /// <summary>
        /// Optional biller business identifier snapshot.
        /// </summary>
        public string? BillerBusinessID { get; set; }

        /// <summary>
        /// Optional biller tax registration identifier snapshot.
        /// </summary>
        public string? BillerTaxID { get; set; }

        /// <summary>
        /// Customer identifier captured from the billee; no storage foreign key enforces this relationship.
        /// </summary>
        public required Guid Billee { get; set; }

        /// <summary>
        /// Customer display name snapshot used in the invoice and notification.
        /// </summary>
        public required string BilleeName { get; set; }

        /// <summary>
        /// Billee first address line snapshot copied when the invoice is built.
        /// </summary>
        public string? BilleeAddressLine1 { get; set; }

        /// <summary>
        /// Billee second address line snapshot copied when the invoice is built.
        /// </summary>
        public string? BilleeAddressLine2 { get; set; }

        /// <summary>
        /// Billee suburb/locality snapshot copied when the invoice is built.
        /// </summary>
        public string? BilleeAddressSuburb { get; set; }

        /// <summary>
        /// Billee city snapshot copied when the invoice is built.
        /// </summary>
        public string? BilleeAddressCity { get; set; }

        /// <summary>
        /// Billee state/region snapshot copied when the invoice is built.
        /// </summary>
        public string? BilleeAddressState { get; set; }

        /// <summary>
        /// Billee country code snapshot copied when the invoice is built.
        /// </summary>
        public string? BilleeAddressCountry { get; set; }

        /// <summary>
        /// Billee postal code snapshot copied when the invoice is built.
        /// </summary>
        public string? BilleeAddressZipcode { get; set; }

        /// <summary>
        /// Optional customer business identifier snapshot.
        /// </summary>
        public string? BilleeBusinessID { get; set; }

        /// <summary>
        /// Payment particulars generated from the customer name, uppercased with LIMITED/spaces removed and
        /// truncated to 12 characters; request override is not applied.
        /// </summary>
        public string? Particulars { get; set; }

        /// <summary>
        /// Payment reference derived from the invoice ID, retaining its last 12 characters; request override is
        /// not applied.
        /// </summary>
        public string? Reference { get; set; }

        /// <summary>
        /// Numeric InvoiceCycle used only for billing-period presentation; it does not schedule recurring
        /// invoices.
        /// </summary>
        public int InvoiceCycle { get; set; }

        /// <summary>
        /// Optional start instant used for daily, monthly, annual or range presentation.
        /// </summary>
        public DateTimeOffset? BillingPeriodStartDay { get; set; }

        /// <summary>
        /// Optional end instant used only for custom-range presentation.
        /// </summary>
        public DateTimeOffset? BillingPeriodEndDay { get; set; }

        /// <summary>
        /// Currency code for displaying the subtotal, initially copied from the billee.
        /// </summary>
        public required string SubtotalCurrency { get; set; }

        /// <summary>
        /// Stored sum of unit-price cents multiplied by integer quantities, before invoice-level tax.
        /// </summary>
        public long SubtotalCents { get; set; }

        /// <summary>
        /// Currency code for displaying tax, initially copied from the billee.
        /// </summary>
        public required string TaxCurrency { get; set; }

        /// <summary>
        /// Stored invoice-level tax rounded to whole cents using midpoint-to-even.
        /// </summary>
        public long TaxCents { get; set; }

        /// <summary>
        /// Biller tax rate in hundredths of one percent; 1500 means 15%.
        /// </summary>
        public int TaxRatePercentile { get; set; }

        /// <summary>
        /// Optional numeric Niobium.Finance.TaxKind used as a display label.
        /// </summary>
        public int? TaxKind { get; set; }

        /// <summary>
        /// Currency code for the grand total, settlement and due display, initially copied from the billee.
        /// </summary>
        public required string GrandTotalCurrency { get; set; }

        /// <summary>
        /// Stored subtotal plus tax in cents; the displayed final total includes tax.
        /// </summary>
        public long GrandTotalCents { get; set; }

        /// <summary>
        /// Optional due instant in the current contract. BuildNew defaults to UTC now plus seven days;
        /// UpdateAsync can overwrite it with null.
        /// </summary>
        public DateTimeOffset? DueBy { get; set; }

        /// <summary>
        /// Biller contact person snapshot.
        /// </summary>
        public string? ContactName { get; set; }

        /// <summary>
        /// Biller contact telephone snapshot.
        /// </summary>
        public string? ContactPhoneNumber { get; set; }

        /// <summary>
        /// Biller contact email snapshot, not the notification destination.
        /// </summary>
        public string? ContactEmailAddress { get; set; }

        /// <summary>
        /// Optional request terms, trimmed during update.
        /// </summary>
        public string? Terms { get; set; }

        /// <summary>
        /// Optional payment text; UpdateAsync replaces the profile default with the trimmed request value,
        /// including null.
        /// </summary>
        public string? PaymentInstructions { get; set; }

        /// <summary>
        /// Billee IANA timezone captured for invoice date presentation.
        /// </summary>
        public required string TimeZone { get; set; }

        /// <summary>
        /// Billee culture name captured for invoice date presentation.
        /// </summary>
        public required string Culture { get; set; }

        /// <summary>
        /// Captured customer email address; a blank value skips notification enqueueing.
        /// </summary>
        public string? RecipientEmail { get; set; }

        /// <summary>
        /// Reported amount already settled in cents; only request Settled.Cents is copied, without checking its
        /// currency.
        /// </summary>
        public long SettledCents { get; set; }

        /// <summary>
        /// UTC time recorded after notification enqueueing succeeds and this invoice is saved; not
        /// recipient-delivery confirmation.
        /// </summary>
        public DateTimeOffset? Delivered { get; set; }

        /// <summary>
        /// Stored invoice-link token required for public viewing and reused on resends. New tokens are saved
        /// before notification enqueueing; no explicit expiry field is present.
        /// </summary>
        public string? Token { get; set; }

        /// <summary>
        /// Copied profile template reference; current HTML rendering ignores it in favor of the embedded
        /// template.
        /// </summary>
        public string? Template { get; set; }

        /// <summary>Optional versioned Table Storage preparation state; excluded from JSON.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string? IssuanceState
        {
            get;
            set;
        }

        /// <summary>Fingerprint of the prepared or frozen invoice and items; excluded from JSON.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string? IssuanceFingerprint
        {
            get;
            set;
        }

        /// <summary>Owner of an in-progress item preparation; excluded from JSON.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string? IssuanceOperation
        {
            get;
            set;
        }

        /// <summary>Original particulars default used for null request values; excluded from JSON.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string? IssuanceDefaultParticulars
        {
            get;
            set;
        }

        /// <summary>Original reference default used for null request values; excluded from JSON.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string? IssuanceDefaultReference
        {
            get;
            set;
        }

        /// <summary>Original payment instructions default used for null request values; excluded from JSON.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string? IssuanceDefaultPaymentInstructions
        {
            get;
            set;
        }

        /// <summary>Invoice link endpoint captured before enqueue to keep replays stable; excluded from JSON.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string? IssuanceNotificationEndpoint
        {
            get;
            set;
        }

        /// <summary>
        /// Encodes this invoice issue instant as its reverse-millisecond identifier.
        /// </summary>
        /// <seealso cref="ParseID(DateTimeOffset)"/>
        /// <returns>The identifier derived from Created.</returns>
        public long GetID()
        {
            return ParseID(Created);
        }

        /// <summary>
        /// Converts the stored issue instant to the requested display timezone.
        /// </summary>
        /// <param name="timeZoneInfo">Timezone for presenting the stored issue instant.</param>
        /// <returns>The same instant expressed in the supplied timezone.</returns>
        public DateTimeOffset GetCreated(TimeZoneInfo timeZoneInfo)
        {
            return Created.ToLocal(timeZoneInfo);
        }

        /// <summary>
        /// Combines this invoice identifier and biller for message identity and references.
        /// </summary>
        /// <returns>The invoiceID@biller string.</returns>
        public string GetFullID()
        {
            return BuildFullID(Biller, GetID());
        }

        /// <summary>
        /// Combines the invoice identifier and biller into a full reference.
        /// </summary>
        /// <param name="biller">Issuing biller identifier.</param>
        /// <param name="id">Invoice identifier in reverse Unix milliseconds.</param>
        /// <returns>The invoiceID@biller string.</returns>
        public static string BuildFullID(Guid biller, long id)
        {
            return BuildFullID(biller.ToString(), id.ToString());
        }

        /// <summary>
        /// Combines string keys into the full invoice reference format.
        /// </summary>
        /// <param name="partitionKey">Biller partition-key text.</param>
        /// <param name="rowKey">Invoice identifier text.</param>
        /// <returns>The rowKey@partitionKey string; inputs are not validated or normalized.</returns>
        public static string BuildFullID(string partitionKey, string rowKey)
        {
            return $"{rowKey}@{partitionKey}";
        }

        /// <summary>
        /// Encodes an issue instant using Niobium reverse Unix milliseconds.
        /// </summary>
        /// <param name="created">Issue instant to encode.</param>
        /// <returns>The active reverse-timestamp anchor minus the instant in Unix milliseconds.</returns>
        /// <remarks>
        /// The matched default anchor is 2100-01-01 UTC; this is not an ordinary Unix timestamp.
        /// </remarks>
        public static long ParseID(DateTimeOffset created)
        {
            return created.ToReverseUnixTimeMilliseconds();
        }

        /// <summary>
        /// Decodes a reverse-millisecond invoice identifier into its issue instant.
        /// </summary>
        /// <param name="id">Invoice identifier in reverse Unix milliseconds.</param>
        /// <returns>The issue instant represented by the identifier.</returns>
        /// <remarks>
        /// Uses the active Niobium reverse-timestamp convention; no recency validation is performed here.
        /// </remarks>
        public static DateTimeOffset ParseID(long id)
        {
            return DateTimeOffsetExtensions.FromReverseUnixTimeMilliseconds(id);
        }

        /// <summary>
        /// Formats a biller as the invoice-table partition key.
        /// </summary>
        /// <param name="biller">Issuing biller identifier.</param>
        /// <returns>The standard hyphenated GUID string.</returns>
        public static string BuildPartitionKey(Guid biller)
        {
            return biller.ToString();
        }

        /// <summary>
        /// Formats an encoded invoice identifier for the table row key.
        /// </summary>
        /// <param name="id">Invoice identifier in reverse Unix milliseconds.</param>
        /// <returns>Decimal identifier text left-padded to at least 12 characters.</returns>
        public static string BuildRowKey(long id)
        {
            return id.ToReverseUnixTimestamp();
        }

        /// <summary>
        /// Builds an invoice snapshot from the supplied issuer and customer profiles.
        /// </summary>
        /// <param name="id">Invoice identifier in reverse Unix milliseconds.</param>
        /// <param name="biller">Issuer profile whose identity, contact and tax fields are copied.</param>
        /// <param name="billee">Customer snapshot whose identity and presentation settings are copied.</param>
        /// <returns>An unsaved invoice with generated payment references and a seven-day due-date default.</returns>
        /// <remarks>
        /// Copies tax/contact data from the biller and currency/culture/timezone from the billee. Totals and
        /// settlement remain defaults until update; a later update can overwrite DueBy and PaymentInstructions
        /// with null.
        /// </remarks>
        public static Invoice BuildNew(long id, Biller biller, Billee billee)
        {
            Invoice result = new()
            {
                Created = Invoice.ParseID(id),

                Biller = biller.GetUser(),
                Tenant = biller.GetTenant(),
                BillerAddressLine1 = biller.AddressLine1,
                BillerAddressLine2 = biller.AddressLine2,
                BillerAddressSuburb = biller.Suburb,
                BillerAddressCity = biller.City,
                BillerAddressState = biller.State,
                BillerAddressCountry = biller.Country,
                BillerAddressZipcode = biller.Zipcode,
                BillerName = biller.BusinessName,
                BillerBusinessID = biller.BusinessID,
                BillerLogo = biller.Logo,
                BillerTaxID = biller.TaxID,
                ContactName = biller.ContactName,
                ContactEmailAddress = biller.Email,
                ContactPhoneNumber = biller.Phone,
                PaymentInstructions = biller.PaymentInstructions,
                TaxKind = biller.TaxKind,
                TaxRatePercentile = biller.TaxRatePercentile,
                Template = biller.Template,

                Billee = billee.ID,
                BilleeName = billee.Name,
                BilleeBusinessID = billee.BusinessID,
                BilleeAddressLine1 = billee.AddressLine1,
                BilleeAddressLine2 = billee.AddressLine2,
                BilleeAddressSuburb = billee.Suburb,
                BilleeAddressState = billee.State,
                BilleeAddressCountry = billee.Country,
                BilleeAddressCity = billee.City,
                BilleeAddressZipcode = billee.Zipcode,
                RecipientEmail = billee.Email,
                GrandTotalCurrency = billee.Currency,
                SubtotalCurrency = billee.Currency,
                TaxCurrency = billee.Currency,
                TimeZone = billee.TimeZone,
                Culture = billee.Culture,

                Particulars = billee.Name.ToUpperInvariant()
                                .Replace("LIMITED", string.Empty)
                                .Replace(" ", string.Empty),

                DueBy = DateTimeOffset.UtcNow.AddDays(7),
            };

            result.Reference = result.GetID().ToString();

            if (result.Reference.Length > ReferenceMaxLength)
            {
                result.Reference = result.Reference.Substring(result.Reference.Length - ReferenceMaxLength, ReferenceMaxLength);
            }

            if (result.Particulars.Length > ParticularsMaxLength)
            {
                result.Particulars = result.Particulars[..ParticularsMaxLength];
            }

            return result;
        }
    }
}
