using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Niobium.Invoicing.Domains;
using Niobium.Invoicing.Options;
using Niobium.Messaging;
using Niobium.Notification;

namespace Niobium.Invoicing.Flows
{
    /// <summary>
    /// Freezes persisted invoice content, queues its notification, then records enqueue completion.
    /// </summary>
    /// <param name="repo">Repository for loading invoice domains by biller and invoice keys.</param>
    /// <param name="itemRepo">Repository for reading persisted invoice lines.</param>
    /// <param name="broker">Downstream NotifyCommand broker.</param>
    /// <param name="config">Bound billing configuration for invoice links.</param>
    /// <param name="logger">Logger for missing-recipient skips.</param>
    public class EmailFlow(
        IDomainRepository<InvoiceDomain, Invoice> repo,
        IRepository<InvoiceItem> itemRepo,
        IMessagingBroker<NotifyCommand> broker,
        IOptions<BillingOptions> config,
        ILogger<EmailFlow> logger) : IFlow
    {
        /// <summary>
        /// Channel used to select the downstream sender-specific invoice template.
        /// </summary>
        private const string NotificationInvoiceChannel = "Invoice";

        /// <summary>
        /// Loads an invoice and queues its notification unless recipient email is blank.
        /// </summary>
        /// <param name="issuer">Issuing biller identifier.</param>
        /// <param name="invoice">Invoice identifier in reverse Unix milliseconds.</param>
        /// <param name="cancellationToken">Token forwarded to supported asynchronous operations.</param>
        /// <returns>A task completing after token persistence if needed, enqueue and status save, or the missing-recipient skip.</returns>
        /// <remarks>
        /// Freezes verified persisted content with an existing or newly saved token before enqueueing. Builds notification
        /// parameters from persisted data and uses invoiceID@biller for message identity.
        /// It does not suppress replays, confirm recipient delivery or make enqueue/status persistence atomic.
        /// Cancellation or failure can leave earlier side effects completed.
        /// </remarks>
        public virtual async Task RunAsync(Guid issuer, long invoice, CancellationToken cancellationToken)
        {
            InvoiceDomain domain = await repo.GetAsync(
                Invoice.BuildPartitionKey(issuer),
                Invoice.BuildRowKey(invoice), forceLoad: true, cancellationToken: cancellationToken);
            Invoice entity = await domain.GetEntityAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(entity.RecipientEmail))
            {
                logger.LogWarning("Invoice {Invoice} has no recipient email, skipping notification.", entity.GetFullID());
                return;
            }

            InvoiceItem[] items = await itemRepo.GetAsync(InvoiceItem.BuildPartitionKey(entity.GetID()), cancellationToken: cancellationToken).ToArrayAsync(cancellationToken: cancellationToken);
            items = InvoiceContent.Order(items);
            string token = await domain.FreezeAsync(items, config.Value.InvoiceTokenSecretSalt, cancellationToken);
            entity = await domain.GetEntityAsync(cancellationToken);
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
