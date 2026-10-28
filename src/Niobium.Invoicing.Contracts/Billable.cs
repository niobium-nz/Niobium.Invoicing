namespace Niobium.Invoicing
{
    /// <summary>
    /// Reusable biller catalog data; current invoice flows do not perform a catalog lookup.
    /// </summary>
    public class Billable : ITrackable
    {
        /// <summary>
        /// Biller identity associated with this catalog entry; this type has no explicit table-key mapping.
        /// </summary>
        public Guid Biller { get; set; }

        /// <summary>
        /// Catalog-entry identifier; this type has no explicit row-key mapping.
        /// </summary>
        public Guid ID { get; set; }

        /// <summary>
        /// Optional storage modification time supplied by infrastructure.
        /// </summary>
        public DateTimeOffset? Timestamp { get; set; }

        /// <summary>
        /// Optional creation time; the framework fills it on create for ITrackable entities when absent.
        /// </summary>
        public DateTimeOffset? Created { get; set; }

        /// <summary>
        /// Optional storage concurrency metadata; not a business version number.
        /// </summary>
        public string? ETag { get; set; }

        /// <summary>
        /// Optional reusable line subject; no catalog-to-invoice lookup is implemented here.
        /// </summary>
        public string? Subject { get; set; }

        /// <summary>
        /// Optional reusable line description.
        /// </summary>
        public string? Description { get; set; }
    }
}
