using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Niobium.Invoicing.Flows;
using Niobium.Invoicing.History;
using Niobium.Messaging.ServiceBus;
using Niobium.Platform.ServiceBus;

namespace Niobium.Invoicing.Server;

/// <summary>
/// Consumes trusted private invoice commands from the issueinvoicecommand queue.
/// </summary>
/// <param name="flow">Application flow invoked by this trigger.</param>
/// <param name="logger">Logger for command parse/validation failures.</param>
/// <param name="telemetry">Queue attempt measurements in the existing telemetry pipeline.</param>
public class IssueInvoiceCommandConsumer(IssueFlow flow, ILogger<IssueInvoiceCommandConsumer> logger, InvoiceQueueTelemetry telemetry)
{
    /// <summary>
    /// Parses and normalizes a trusted invoice command, then runs issuance with its embedded customer.
    /// </summary>
    /// <param name="message">Received Service Bus message containing direct command JSON.</param>
    /// <param name="cancellationToken">Token forwarded to supported asynchronous operations.</param>
    /// <returns>A task completing after issuance.</returns>
    /// <remarks>
    /// Requires exact application/json content type. Does not validate an end-user principal or maintain an
    /// inbox. Exceptions are left to Functions message handling.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The message cannot be parsed by the helper or fails validation.</exception>
    [Function(nameof(IssueInvoiceCommandConsumer))]
    public Task Run(
        [ServiceBusTrigger("issueinvoicecommand")]
        ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
    {
        return telemetry.MeasureAsync(message.EnqueuedTime, () => ProcessAsync(message, cancellationToken), cancellationToken);
    }

    private async Task ProcessAsync(ServiceBusReceivedMessage message, CancellationToken cancellationToken)
    {
        bool parsed = message.TryParse(out IssueInvoiceCommand? command, out _);
        if (!parsed || command is null)
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

        await flow.RunAsync(command, command.Billee,
            new(InvoiceChangeAuthority.TrustedQueue, nameof(IssueInvoiceCommandConsumer)), cancellationToken);
    }
}
