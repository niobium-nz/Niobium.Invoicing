using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Niobium.Invoicing.Flows;
using Niobium.Invoicing.Server;

namespace Niobium.Invoicing.Integration.Tests;

[TestClass]
public sealed class TransportEntryTests
{
    [TestMethod]
    public void Health_response_identifies_the_binary_without_credentials_or_runtime_settings()
    {
        OkObjectResult result = (OkObjectResult)new DeploymentInfo().Run(new DefaultHttpContext().Request);
        using JsonDocument body = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        Assert.AreEqual("ready", body.RootElement.GetProperty("status").GetString());
        Assert.IsTrue(body.RootElement.TryGetProperty("commit", out _));
        Assert.AreEqual(2, body.RootElement.EnumerateObject().Count());
    }

    [TestMethod]
    [DataRow("", 1L, false)]
    [DataRow("token", 0L, false)]
    [DataRow("token", 1L, true)]
    public async Task Public_view_rejects_missing_link_inputs_before_storage(string token, long id, bool emptyBiller)
    {
        HTMLFlow flow = new(null!, null!);
        IActionResult result = await new GetHTMLInvoice(flow).Run(new DefaultHttpContext().Request,
            emptyBiller ? Guid.Empty : Guid.NewGuid(), id, token, CancellationToken.None);
        Assert.IsInstanceOfType<NotFoundResult>(result);
    }

    [TestMethod]
    [DataRow("text/plain", "{}")]
    [DataRow("application/json", "invalid-json")]
    public async Task Queue_consumer_rejects_invalid_transport_payloads(string contentType, string body)
    {
        Mock<UpsertFlow> upsert = new(MockBehavior.Strict, null!, null!, null!, null!);
        Mock<EmailFlow> email = new(MockBehavior.Strict, null!, null!, null!, null!, null!);
        IssueInvoiceCommandConsumer consumer = new(new IssueFlow(upsert.Object, email.Object), NullLogger<IssueInvoiceCommandConsumer>.Instance);
        ServiceBusReceivedMessage message = ServiceBusModelFactory.ServiceBusReceivedMessage(body: new BinaryData(body), contentType: contentType, messageId: "synthetic");
        if (contentType == "application/json")
        {
            await Assert.ThrowsAsync<JsonException>(() => consumer.Run(message, CancellationToken.None));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => consumer.Run(message, CancellationToken.None));
        }
        upsert.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task Queue_consumer_parses_and_sanitizes_the_command_before_calling_shared_flows()
    {
        Guid biller = Guid.NewGuid();
        Billee customer = new()
        {
            Biller = biller,
            ID = Guid.NewGuid(),
            Name = "Synthetic",
            Currency = "NZD",
            Culture = "en-NZ",
            TimeZone = "Pacific/Auckland"
        };
        IssueInvoiceCommand command = new()
        {
            BillerID = biller,
            BilleeID = Guid.Empty,
            Tenant = Guid.NewGuid(),
            InvoiceID = Invoice.ParseID(DateTimeOffset.UtcNow),
            Billee = customer,
            InvoiceItems = []
        };
        Mock<UpsertFlow> upsert = new(MockBehavior.Strict, null!, null!, null!, null!);
        upsert.Setup(x => x.RunAsync(It.Is<IssueInvoiceRequest>(r => r.BilleeID == customer.ID), It.Is<Billee?>(b => b!.ID == customer.ID), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        Mock<EmailFlow> email = new(MockBehavior.Strict, null!, null!, null!, null!, null!);
        IssueInvoiceCommandConsumer consumer = new(new IssueFlow(upsert.Object, email.Object), NullLogger<IssueInvoiceCommandConsumer>.Instance);
        ServiceBusReceivedMessage message = ServiceBusModelFactory.ServiceBusReceivedMessage(body: new BinaryData(JsonMarshaller.Marshall(command)), contentType: "application/json", messageId: "synthetic");
        await consumer.Run(message, CancellationToken.None);
        upsert.Verify(x => x.RunAsync(It.IsAny<IssueInvoiceRequest>(), It.IsAny<Billee?>(), It.IsAny<CancellationToken>()), Times.Once);
        email.VerifyNoOtherCalls();
    }
}
