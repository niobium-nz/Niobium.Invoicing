using Niobium.Profile;

namespace Niobium.Invoicing
{
    public class Biller : IProfile
    {
        public Guid PartitionKey { get; set; }

        public Guid RowKey { get; set; }

        public DateTimeOffset? Timestamp { get; set; }

        public string? ETag { get; set; }

        public string? AddressLine1 { get; set; }

        public string? AddressLine2 { get; set; }

        public string? Suburb { get; set; }

        public string? BusinessID { get; set; }

        public required string BusinessName { get; set; }

        public string? City { get; set; }

        public string? State { get; set; }

        public string? Country { get; set; }

        public string? Email { get; set; }

        public string? Logo { get; set; }

        public string? PaymentInstructions { get; set; }

        public string? ContactName { get; set; }

        public string? Phone { get; set; }

        public string? TaxID { get; set; }

        public int TaxRatePercentile { get; set; }

        public int? TaxKind { get; set; }

        public string? Zipcode { get; set; }

        public string? Template { get; set; }

        public required string TimeZone { get; set; }

        public required string Currency { get; set; }

        public required string Culture { get; set; }
    }
}
