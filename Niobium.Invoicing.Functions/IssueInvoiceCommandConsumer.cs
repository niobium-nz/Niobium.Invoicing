using Azure.Messaging.ServiceBus;
using Cod;
using Cod.Messaging.ServiceBus;
using Cod.Profile;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Niobium.Invoicing.Functions;

public class IssueInvoiceCommandConsumer(
    IDomainRepository<InvoiceDomain, Invoice> repo,
    IProfileService<Biller> profileService,
    ILogger<IssueInvoiceCommandConsumer> logger)
{
    [Function(nameof(IssueInvoiceCommandConsumer))]
    public async Task Run(
        [ServiceBusTrigger("issueinvoicecommand", AutoCompleteMessages = true, Connection = nameof(ServiceBusOptions))]
        ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        if (!message.TryParse(out IssueInvoiceCommand? command, out var rawBody))
        {
            var err = $"Failed to parse message {message.MessageId}";
            logger.LogError(err);
            throw new InvalidOperationException(err);
        }

        command.TryValidate(out var validationState);
        if (!validationState.IsValid)
        {
            var err = $"Validation failed for command {command.ID}: {validationState}";
            logger.LogError(err);
            throw new InvalidOperationException(err);
        }

        var biller = await profileService.RetrieveAsync(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Biller does not exist.");

        var invoice = Invoice.BuildNew(command.ID, biller, command.Billee);
        var domain = await repo.BuildAsync(invoice, cancellationToken);
        await domain.UpdateAsync(invoice, command.InvoiceItems, cancellationToken);
    }
}