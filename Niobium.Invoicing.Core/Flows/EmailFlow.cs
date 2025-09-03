using Microsoft.Extensions.Options;
using Niobium.Invoicing.Domains;
using Niobium.Invoicing.Options;
using Niobium.Platform.Notification.Email;

namespace Niobium.Invoicing.Flows
{
    public class EmailFlow(
        IDomainRepository<InvoiceDomain, Invoice> repo,
        IRepository<InvoiceItem> itemRepo,
        IEmailNotificationClient sender,
        IOptions<BillingOptions> config) : IFlow
    {
        public async Task<bool> RunAsync(Guid issuer, long invoice, CancellationToken cancellationToken)
        {
            InvoiceDomain domain = await repo.GetAsync(
                Invoice.BuildPartitionKey(issuer),
                Invoice.BuildRowKey(invoice), cancellationToken: cancellationToken);
            Invoice entity = await domain.GetEntityAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(entity.RecipientEmail))
            {
                return false;
            }

            InvoiceItem[] items = await itemRepo.GetAsync(InvoiceItem.BuildPartitionKey(entity.GetID()), cancellationToken: cancellationToken).ToArrayAsync(cancellationToken: cancellationToken);
            string token = entity.BuildAccessToken(items, config.Value.InvoiceTokenSecretSalt);
            string email = await domain.BuildEmailAsync(token, cancellationToken);

            bool result = await sender.SendAsync(
                new EmailAddress { DisplayName = entity.ContactName ?? entity.BillerName, Address = config.Value.InvoiceEmailSenderAddress },
                [entity.RecipientEmail],
                $"Invoice {entity.GetID()} from {entity.BillerName} for {entity.BilleeName}",
                email,
                cancellationToken);

            if (result)
            {
                await domain.OnDeliveredAsync(token, cancellationToken);
            }

            return result;
        }
    }
}
