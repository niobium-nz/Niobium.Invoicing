using Cod;

namespace Niobium.Billing
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

        public string? BusinessID { get; set; }

        public string? Name { get; set; }

        public string? City { get; set; }

        public string? Culture { get; set; }

        public string? Currency { get; set; }

        public string? Email { get; set; }

        public string? Wechat { get; set; }

        public string? Phone { get; set; }

        public string? TimeZone { get; set; }

        public string? Zipcode { get; set; }

        public static string GetPartitionKey(Guid biller)
        {
            return biller.ToKey();
        }

        public static string GetRowKey(Guid billee)
        {
            return billee.ToKey();
        }
    }
}
