using Cod;

namespace Niobium.Billing
{
    public class Customer : ITrackable
    {
        public Guid Biller { get; set; }

        public Guid ID { get; set; }

        public DateTimeOffset? Timestamp { get; set; }

        public DateTimeOffset? Created { get; set; }

        public string? ETag { get; set; }

        public string? AddressLine1 { get; set; }

        public string? AddressLine2 { get; set; }

        public string? BusinessID { get; set; }

        public string? BusinessName { get; set; }

        public string? City { get; set; }

        public string? Culture { get; set; }

        public string? Currency { get; set; }

        public string? Email { get; set; }

        public string? Wechat { get; set; }

        public string? Phone { get; set; }

        public string? TimeZone { get; set; }

        public string? Zipcode { get; set; }
    }
}
