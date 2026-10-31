using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Niobium.Invoicing.Flows;
using Niobium.Invoicing.Server;

namespace Niobium.Invoicing.Integration.Tests;

[TestClass]
public sealed class EmailInvoiceTests
{
    [TestMethod]
    public async Task Manual_email_waits_for_the_shared_flow_then_returns_an_empty_201()
    {
        Guid biller = Guid.NewGuid();
        long id = Invoice.ParseID(DateTimeOffset.UtcNow);
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<EmailFlow> flow = new(MockBehavior.Strict, null!, null!, null!, null!, null!);
        flow.Setup(x => x.RunAsync(biller, id, cancellation.Token)).Returns(completed.Task);
        ServiceCollection collection = new();
        collection.AddLogging();
        collection.AddControllers();
        using ServiceProvider services = collection.BuildServiceProvider();
        DefaultHttpContext context = new() { RequestServices = services };
        using MemoryStream body = new();
        context.Response.Body = body;

        Task<IActionResult> pending = new EmailInvoice(flow.Object).Run(context.Request, biller, id, cancellation.Token);
        Assert.IsFalse(pending.IsCompleted, "An in-progress send must not report success.");
        completed.SetResult();
        IActionResult result = await pending;
        await result.ExecuteResultAsync(new ActionContext(context, new RouteData(), new ActionDescriptor()));

        Assert.AreEqual(StatusCodes.Status201Created, context.Response.StatusCode);
        Assert.AreEqual(0L, body.Length);
        flow.Verify(x => x.RunAsync(biller, id, cancellation.Token), Times.Once);
        flow.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task Manual_email_propagates_flow_failure_instead_of_reporting_success()
    {
        Guid biller = Guid.NewGuid();
        long id = Invoice.ParseID(DateTimeOffset.UtcNow);
        InvalidOperationException failure = new("Synthetic enqueue failure");
        Mock<EmailFlow> flow = new(MockBehavior.Strict, null!, null!, null!, null!, null!);
        flow.Setup(x => x.RunAsync(biller, id, CancellationToken.None)).ThrowsAsync(failure);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new EmailInvoice(flow.Object).Run(new DefaultHttpContext().Request, biller, id, CancellationToken.None));

        Assert.AreSame(failure, actual);
        flow.Verify(x => x.RunAsync(biller, id, CancellationToken.None), Times.Once);
        flow.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task Manual_email_forwards_cancellation_without_reporting_success()
    {
        Guid biller = Guid.NewGuid();
        long id = Invoice.ParseID(DateTimeOffset.UtcNow);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Mock<EmailFlow> flow = new(MockBehavior.Strict, null!, null!, null!, null!, null!);
        flow.Setup(x => x.RunAsync(biller, id, cancellation.Token)).Returns(Task.FromCanceled(cancellation.Token));

        OperationCanceledException actual = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new EmailInvoice(flow.Object).Run(new DefaultHttpContext().Request, biller, id, cancellation.Token));

        Assert.AreEqual(cancellation.Token, actual.CancellationToken);
        flow.Verify(x => x.RunAsync(biller, id, cancellation.Token), Times.Once);
        flow.VerifyNoOtherCalls();
    }
}
