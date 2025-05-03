using Cod;

namespace Niobium.Billing
{
    public class Invoice
    {
        [EntityKey(EntityKeyKind.PartitionKey)]
        public required Guid Issuer { get; set; }

        [EntityKey(EntityKeyKind.RowKey)]
        public required DateTimeOffset CreatedAt { get; set; }

        [EntityKey(EntityKeyKind.Timestamp)]
        public DateTimeOffset? UpdatedAt { get; set; }

        [EntityKey(EntityKeyKind.ETag)]
        public string? ETag { get; set; }

        public string? BillerLogo { get; set; }

        public required string BillerName { get; set; }

        public required string BillerAddressLine1 { get; set; }

        public string? BillerAddressLine2 { get; set; }

        public required string BillerAddressCity { get; set; }

        public required string BillerAddressZipcode { get; set; }

        public string? BillerBusinessID { get; set; }

        public string? BillerTaxID { get; set; }

        public required string BilleeName { get; set; }

        public required string BilleeAddressLine1 { get; set; }

        public string? BilleeAddressLine2 { get; set; }

        public required string BilleeAddressCity { get; set; }

        public required string BilleeAddressZipcode { get; set; }

        public string? BilleeBusinessID { get; set; }

        public string? Particulars { get; set; }

        public string? Reference { get; set; }

        public DateTimeOffset? BillingPeriodStartDay { get; set; }

        public DateTimeOffset? BillingPeriodEndDay { get; set; }

        public required string SubtotalCurrency { get; set; }

        public long SubtotalCents { get; set; }

        public required string TaxCurrency { get; set; }

        public long TaxCents { get; set; }

        public int TaxRatePercentile { get; set; }

        public required string GrandTotalCurrency { get; set; }

        public long GrandTotalCents { get; set; }

        public DateTimeOffset? DueBy { get; set; }

        public required string ContactName { get; set; }

        public string? ContactPhoneNumber { get; set; }
        
        public string? ContactEmailAddress { get; set; }

        public string? Terms { get; set; }

        public required string PaymentInstructions { get; set; }

        public required string TimeZone { get; set; }

        public required string Culture { get; set; }

        public required string RecipientEmail { get; set; }

        public long SettledCents { get; set; }

        public string? Token { get; set; }

        public long GetID() => ParseID(CreatedAt);

        public DateTimeOffset GetBillDate(TimeZoneInfo timeZoneInfo) => CreatedAt.ToLocal(timeZoneInfo);

        public static long ParseID(DateTimeOffset created) => created.ToReverseUnixTimeMilliseconds();

        public static string BuildPartitionKey(Guid issuer) => issuer.ToString();

        public static string BuildRowKey(long id) => id.ToReverseUnixTimestamp();
    }
}
