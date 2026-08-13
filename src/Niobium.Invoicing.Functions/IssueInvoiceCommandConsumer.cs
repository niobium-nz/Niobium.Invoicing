using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Niobium.Invoicing.Flows;
using Niobium.Messaging.ServiceBus;
using Niobium.Platform.ServiceBus;

namespace Niobium.Invoicing.Functions;

public class IssueInvoiceCommandConsumer(IssueFlow flow, ILogger<IssueInvoiceCommandConsumer> logger)
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

        await flow.RunAsync(command, command.Billee, cancellationToken);
    }
}