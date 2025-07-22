using Cod;
using Cod.Profile;

namespace Niobium.Invoicing
{
    public class Invoice
    {
        private const int ParticularsMaxLength = 12;
        private const int ReferenceMaxLength = 12;

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

        public int BillingPeriodKind { get; set; }

        public DateTimeOffset? BillingPeriodStartDay { get; set; }

        public DateTimeOffset? BillingPeriodEndDay { get; set; }

        public required string SubtotalCurrency { get; set; }

        public long SubtotalCents { get; set; }

        public required string TaxCurrency { get; set; }

        public long TaxCents { get; set; }

        public int TaxRatePercentile { get; set; }

        public string? TaxKind { get; set; }

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

        public DateTimeOffset? Delivered { get; set; }

        public string? Token { get; set; }

        public string? Template { get; set; }

        public long GetID() => ParseID(Created);

        public DateTimeOffset GetBillDate(TimeZoneInfo timeZoneInfo) => Created.ToLocal(timeZoneInfo);

        public string GetFullID() => BuildFullID(Biller, GetID());

        public static string BuildFullID(Guid biller, long id) => BuildFullID(biller.ToString(), id.ToString());

        public static string BuildFullID(string partitionKey, string rowKey) => $"{rowKey}@{partitionKey}";

        public static long ParseID(DateTimeOffset created) => created.ToReverseUnixTimeMilliseconds();

        public static DateTimeOffset ParseID(long id) => DateTimeOffsetExtensions.FromReverseUnixTimeMilliseconds(id);

        public static string BuildPartitionKey(Guid biller) => biller.ToString();

        public static string BuildRowKey(long id) => id.ToReverseUnixTimestamp();

        public static Invoice BuildNew(long id, Biller biller, Billee billee)
        {
            var result = new Invoice
            {
                Created = Invoice.ParseID(id),

                Biller = biller.GetUser(),
                BillerAddressCity = biller.City,
                BillerAddressLine1 = biller.AddressLine1,
                BillerAddressLine2 = biller.AddressLine2,
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
                BilleeAddressCity = billee.City,
                BilleeAddressLine1 = billee.AddressLine1,
                BilleeAddressLine2 = billee.AddressLine2,
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
