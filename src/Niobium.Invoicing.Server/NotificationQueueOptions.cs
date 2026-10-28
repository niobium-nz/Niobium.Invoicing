using Niobium.Messaging.ServiceBus;

namespace Niobium.Invoicing.Server
{
    /// <summary>
    /// Named configuration section for the downstream NotifyCommand broker; inherits Service Bus options.
    /// </summary>
    internal class NotificationQueueOptions : ServiceBusOptions
    {
    }
}
