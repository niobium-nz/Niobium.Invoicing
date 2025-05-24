using Cod;

namespace Niobium.Billing
{
    public class Invoice
    {
        [EntityKey(EntityKeyKind.PartitionKey)]
        public required Guid Biller { get; set; }

        [EntityKey(EntityKeyKind.RowKey)]
        public required DateTimeOffset Created { get; set; }

        [EntityKey(EntityKeyKind.Timestamp)]
        public DateTimeOffset? Timestamp { get; set; }

        [EntityKey(EntityKeyKind.ETag)]
        public string? ETag { get; set; }

        public string? BillerLogo { get; set; }

        public required string BillerName { get; set; }

        public string? BillerAddressLine1 { get; set; }

        public string? BillerAddressLine2 { get; set; }

        public string? BillerAddressCity { get; set; }

        public string? BillerAddressZipcode { get; set; }

        public string? BillerBusinessID { get; set; }

        public string? BillerTaxID { get; set; }

        public required Guid Billee { get; set; }

        public required string BilleeName { get; set; }

        public string? BilleeAddressLine1 { get; set; }

        public string? BilleeAddressLine2 { get; set; }

        public string? BilleeAddressCity { get; set; }

        public string? BilleeAddressZipcode { get; set; }

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

        public string? ContactName { get; set; }

        public string? ContactPhoneNumber { get; set; }
        
        public string? ContactEmailAddress { get; set; }

        public string? Terms { get; set; }

        public string? PaymentInstructions { get; set; }

        public required string TimeZone { get; set; }

        public required string Culture { get; set; }

        public string? RecipientEmail { get; set; }

        public long SettledCents { get; set; }

        public string? Token { get; set; }

        public long GetID() => ParseID(Created);

        public DateTimeOffset GetBillDate(TimeZoneInfo timeZoneInfo) => Created.ToLocal(timeZoneInfo);

        public string GetFullID() => BuildFullID(Biller, GetID());

        public static string BuildFullID(Guid biller, long id) => BuildFullID(biller.ToString(), id.ToString());

        public static string BuildFullID(string partitionKey, string rowKey) => $"{rowKey}@{partitionKey}";

        public static long ParseID(DateTimeOffset created) => created.ToReverseUnixTimeMilliseconds();

        public static string BuildPartitionKey(Guid biller) => biller.ToString();

        public static string BuildRowKey(long id) => id.ToReverseUnixTimestamp();
    }
}
