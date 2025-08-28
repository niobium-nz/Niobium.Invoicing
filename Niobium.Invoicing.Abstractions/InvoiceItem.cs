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

        public long GetInvoiceID()
        {
            return Invoicing.Invoice.ParseID(Invoice);
        }

        public static string BuildPartitionKey(long invoiceID)
        {
            return Invoicing.Invoice.BuildRowKey(invoiceID);
        }

        public static string BuildRowKey(int id)
        {
            return id.ToString();
        }

        public static InvoiceItem BuildNew(long invoiceID, string currency)
        {
            return new()
            {
                Subject = string.Empty,
                Quantity = 1,
                Invoice = Invoicing.Invoice.ParseID(invoiceID),
                ID = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                LineTotalCurrency = currency,
                UnitPriceCurrency = currency,
            };
        }
    }
}
