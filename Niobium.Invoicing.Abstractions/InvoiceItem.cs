using Cod;

namespace Niobium.Invoicing
{
    public class InvoiceItem
    {
        [EntityKey(EntityKeyKind.PartitionKey)]
        public required DateTimeOffset Invoice { get; set; }

        [EntityKey(EntityKeyKind.RowKey)]
        public required long ID { get; set; }

        [EntityKey(EntityKeyKind.Timestamp)]
        public DateTimeOffset? UpdatedAt { get; set; }

        [EntityKey(EntityKeyKind.ETag)]
        public string? ETag { get; set; }

        public required string Subject { get; set; }

        public string? Description { get; set; }

        public required string UnitPriceCurrency { get; set; }

        public long UnitPriceCents { get; set; }

        public int Quantity { get; set; }

        public required string LineTotalCurrency { get; set; }

        public long LineTotalCents { get; set; }

        public long GetInvoiceID() => Invoicing.Invoice.ParseID(Invoice);

        public static string BuildPartitionKey(long invoiceID) => Invoicing.Invoice.BuildRowKey(invoiceID);

        public static string BuildRowKey(int id) => id.ToString();
    }
}
