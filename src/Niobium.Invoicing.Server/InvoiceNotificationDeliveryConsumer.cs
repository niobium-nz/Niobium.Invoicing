using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Niobium.Invoicing.Flows;
using Niobium.Notification;
using Niobium.Platform.ServiceBus;

namespace Niobium.Invoicing.Server;

/// <summary>Consumes the invoice-only receipt destination in the Notification namespace.</summary>
public sealed class InvoiceNotificationDeliveryConsumer(InvoiceNotificationDeliveryFlow flow)
{
    [Function(nameof(InvoiceNotificationDeliveryConsumer))]
    public Task Run(
        [ServiceBusTrigger("notificationdeliveryevent-invoicing", Connection = "NotificationQueueOptions")]
        ServiceBusReceivedMessage message, CancellationToken cancellationToken)
    {
        NotificationDeliveryEvent receipt = ReadReceipt(message);
        return flow.RunAsync(receipt, cancellationToken);
    }

    private static NotificationDeliveryEvent ReadReceipt(ServiceBusReceivedMessage message)
    {
        try
        {
            if (message.Body.ToMemory().Length <= 4096
                && message.TryParse(out NotificationDeliveryEvent? receipt, out _)
                && message.MessageId == receipt.ID)
                return receipt;
        }
        catch (JsonException)
        {
            // The provider event and payload are not needed in exception logs.
        }
        throw new InvalidOperationException("Invalid invoice receipt envelope.");
    }
}
