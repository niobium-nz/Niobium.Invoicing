using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Niobium.Messaging.ServiceBus;
using Niobium.Platform.ServiceBus;
using Niobium.Profile;

namespace Niobium.Invoicing.Functions;

public class IssueInvoiceCommandConsumer(
    IDomainRepository<InvoiceDomain, Invoice> repo,
    IProfileService<Biller> profileService,
    ILogger<IssueInvoiceCommandConsumer> logger)
{
    [Function(nameof(IssueInvoiceCommandConsumer))]
    public async Task Run(
        [ServiceBusTrigger("issueinvoicecommand", AutoCompleteMessages = true, Connection = nameof(ServiceBusTriggerOptions))]
        ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        if (!message.TryParse(out IssueInvoiceCommand? command, out string? rawBody))
        {
            string err = $"Failed to parse message {message.MessageId}";
            logger.LogError(err);
            throw new InvalidOperationException(err);
        }

        command.TryValidate(out ValidationState? validationState);
        if (!validationState.IsValid)
        {
            string err = $"Validation failed for command {command.InvoiceID}: {validationState}";
            logger.LogError(err);
            throw new InvalidOperationException(err);
        }
        
        Biller biller = await profileService.RetrieveAsync(command.Tenant, command.BillerID, cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Biller does not exist.");

        Invoice invoice = Invoice.BuildNew(command.InvoiceID, biller, command.Billee);
        InvoiceDomain domain = await repo.BuildAsync(invoice, cancellationToken);
        await domain.UpdateAsync(command, command.InvoiceItems, cancellationToken);
    }
}