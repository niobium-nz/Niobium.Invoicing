using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Niobium.Invoicing.Domains;
using Niobium.Invoicing.Options;
using Niobium.Messaging;
using Niobium.Notification;

namespace Niobium.Invoicing.Flows
{
    public class EmailFlow(
        IDomainRepository<InvoiceDomain, Invoice> repo,
        IRepository<InvoiceItem> itemRepo,
        IMessagingBroker<NotifyCommand> broker,
        IOptions<BillingOptions> config,
        ILogger<EmailFlow> logger) : IFlow
    {
        private const string NotificationInvoiceChannel = "Invoice";

        public virtual async Task RunAsync(Guid issuer, long invoice, CancellationToken cancellationToken)
        {
            InvoiceDomain domain = await repo.GetAsync(
                Invoice.BuildPartitionKey(issuer),
                Invoice.BuildRowKey(invoice), cancellationToken: cancellationToken);
            Invoice entity = await domain.GetEntityAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(entity.RecipientEmail))
            {
                logger.LogWarning("Invoice {Invoice} has no recipient email, skipping notification.", entity.GetFullID());
                return;
            }

            InvoiceItem[] items = await itemRepo.GetAsync(InvoiceItem.BuildPartitionKey(entity.GetID()), cancellationToken: cancellationToken).ToArrayAsync(cancellationToken: cancellationToken);
            string token = entity.BuildAccessToken(items, config.Value.InvoiceTokenSecretSalt);
            var invoiceParameters = await domain.BuildNotificationParametersAsync(token, cancellationToken);

            var parameters = new Dictionary<string, object>();
            foreach (var key in invoiceParameters.Keys)
            {
                parameters.Add(key, invoiceParameters[key]);
            }

            parameters.Add(nameof(InvoiceItem).ToSnakeCaseUpper(), items.Select(item => item.BuildTemplateParameters().ToDictionary()).ToArray());

            await broker.EnqueueAsync(new MessagingEntry<NotifyCommand>
            {
                ID = entity.GetFullID(),
                Value = new NotifyCommand 
                {
                    ID = entity.GetFullID(),
                    Tenant = issuer,
                    Channel = NotificationInvoiceChannel,
                    Destination = entity.RecipientEmail,
                    DestinationDisplayName = entity.BilleeName,
                    Parameters = parameters,
                },
            }, cancellationToken: cancellationToken);
            await domain.OnDeliveredAsync(token, cancellationToken);
        }
    }
}
