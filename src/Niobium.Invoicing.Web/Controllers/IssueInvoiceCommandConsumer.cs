using Dapr;
using Microsoft.AspNetCore.Mvc;
using Niobium.Invoicing.Flows;

namespace Niobium.Invoicing.Web.Controllers;

[ApiController]
[Route(DaprComponents.MessageRoute)]
public class IssueInvoiceCommandConsumer(IssueFlow flow, ILogger<IssueInvoiceCommandConsumer> logger) : ControllerBase
{
    [Topic(DaprComponents.ServiceBusPubSub, QueueNames.IssueInvoiceCommand, enableRawPayload: true)]
    [HttpPost(QueueNames.IssueInvoiceCommand)]
    public async Task Run(IssueInvoiceCommand message, CancellationToken cancellationToken)
    {
        message.TryValidate(out ValidationState? validationState);
        if (!validationState.IsValid)
        {
            string err = $"Validation failed for command {message.InvoiceID}: {validationState}";
            logger.LogError(err);
            throw new InvalidOperationException(err);
        }

        await flow.RunAsync(message, message.Billee, cancellationToken);
    }
}