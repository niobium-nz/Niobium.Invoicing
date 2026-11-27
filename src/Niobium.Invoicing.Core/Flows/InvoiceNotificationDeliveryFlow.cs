using Niobium.Invoicing.Delivery;
using Niobium.Invoicing.Domains;
using Niobium.Notification;

namespace Niobium.Invoicing.Flows;

public sealed class InvoiceNotificationDeliveryFlow(IDomainRepository<InvoiceDomain, Invoice> invoices,
    IDomainRepository<InvoiceNotificationDeliveryDomain, InvoiceNotificationDelivery> deliveries) : IFlow
{
    public async Task RunAsync(NotificationDeliveryEvent receipt, CancellationToken cancellationToken = default)
    {
        (Guid biller, long invoice) = InvoiceNotificationDeliveryDomain.VerifyReference(receipt);
        StorageKey key = InvoiceNotificationDelivery.BuildKey(biller, invoice);
        InvoiceDomain owner = await invoices.GetAsync(key.PartitionKey, key.RowKey, forceLoad: true, cancellationToken: cancellationToken);
        await owner.VerifyNotificationOperationAsync(receipt.NotificationID, biller, cancellationToken);

        InvoiceNotificationDeliveryDomain delivery = await deliveries.GetAsync(key.PartitionKey, key.RowKey,
            forceLoad: true, cancellationToken: cancellationToken);
        await delivery.RecordAsync(receipt, biller, invoice, cancellationToken);
    }
}
