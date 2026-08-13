namespace Niobium.Invoicing
{
    public class Billee : ITrackable
    {
        [EntityKey(EntityKeyKind.PartitionKey)]
        public Guid Biller { get; set; }

        [EntityKey(EntityKeyKind.RowKey)]
        public Guid ID { get; set; }

        [EntityKey(EntityKeyKind.Timestamp)]
        public DateTimeOffset? Timestamp { get; set; }

        [EntityKey(EntityKeyKind.ETag)]
        public string? ETag { get; set; }

        public DateTimeOffset? Created { get; set; }

        public string? AddressLine1 { get; set; }

        public string? AddressLine2 { get; set; }

        public string? Suburb { get; set; }

        public string? BusinessID { get; set; }

        public required string Name { get; set; }

        public string? City { get; set; }

        public string? State { get; set; }

        public string? Country { get; set; }

        public string? Email { get; set; }

        public string? InstantMessaging { get; set; }

        public string? Phone { get; set; }

        public string? Zipcode { get; set; }

        public required string Culture { get; set; }

        public required string Currency { get; set; }

        public required string TimeZone { get; set; }

        public static string BuildPartitionKey(Guid biller)
        {
            return biller.ToKey();
        }

        public static string BuildRowKey(Guid billee)
        {
            return billee.ToKey();
        }
    }
}
