using Cod.Profile;

namespace Niobium.Billing
{
    public class Biller : IProfile
    {
        public Guid PartitionKey { get; set; }

        public Guid RowKey { get; set; }

        public DateTimeOffset? Timestamp { get; set; }

        public string? ETag { get; set; }

        public string? AddressLine1 { get; set; }

        public string? AddressLine2 { get; set; }

        public string? BusinessID { get; set; }

        public string? BusinessName { get; set; }

        public string? City { get; set; }

        public string? Culture { get; set; }

        public string? Currency { get; set; }

        public string? Email { get; set; }

        public string? Logo { get; set; }

        public string? PaymentInstructions { get; set; }

        public string? Phone { get; set; }

        public string? TaxID { get; set; }

        public string? TimeZone { get; set; }

        public string? Zipcode { get; set; }
    }
}
