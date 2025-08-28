namespace Niobium.Invoicing
{
    public class Billable : ITrackable
    {
        public Guid Biller { get; set; }

        public Guid ID { get; set; }

        public DateTimeOffset? Timestamp { get; set; }

        public DateTimeOffset? Created { get; set; }

        public string? ETag { get; set; }

        public string? Subject { get; set; }

        public string? Description { get; set; }
    }
}
