using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Niobium.Invoicing.Flows;
using Niobium.Invoicing.Server;
using System.Reflection;

namespace Niobium.Invoicing.Integration.Tests;

[TestClass]
public sealed class DebugInvoiceTests
{
    [TestMethod]
    public void Debug_view_has_one_GET_route_protected_by_a_Functions_key()
    {
        MethodInfo method = typeof(DebugInvoice).GetMethod(nameof(DebugInvoice.Run))!;
        Assert.AreEqual("DebugInvoice", method.GetCustomAttribute<FunctionAttribute>()?.Name);
        HttpTriggerAttribute trigger = method.GetParameters()[0].GetCustomAttribute<HttpTriggerAttribute>()!;
        Assert.AreEqual(AuthorizationLevel.Function, trigger.AuthLevel);
        Assert.AreEqual("debug/{issuer}/invoices/{invoice}", trigger.Route);
        CollectionAssert.AreEqual(new[] { "get" }, trigger.Methods);
        int routes = typeof(DebugInvoice).Assembly.GetTypes().SelectMany(t => t.GetMethods())
            .SelectMany(m => m.GetParameters()).Select(p => p.GetCustomAttribute<HttpTriggerAttribute>())
            .Count(a => a?.Route?.StartsWith("debug/", StringComparison.Ordinal) == true);
        Assert.AreEqual(1, routes, "No additional debug route may bypass the protected trigger.");
    }

    [TestMethod]
    public async Task Debug_view_waits_for_rendering_and_executes_HTML_with_no_store()
    {
        Guid biller = Guid.NewGuid();
        long id = Invoice.ParseID(DateTimeOffset.UtcNow);
        using CancellationTokenSource cancellation = new();
        TaskCompletionSource<string> completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<HTMLFlow> flow = new(MockBehavior.Strict, null!, null!);
        flow.Setup(x => x.RunForDebugAsync(biller, id, cancellation.Token)).Returns(completed.Task);
        ServiceCollection collection = new();
        collection.AddLogging();
        collection.AddControllers();
        using ServiceProvider services = collection.BuildServiceProvider();
        DefaultHttpContext context = new() { RequestServices = services };
        using MemoryStream body = new();
        context.Response.Body = body;

        Task<IActionResult> pending = new DebugInvoice(flow.Object).Run(context.Request, biller, id, cancellation.Token);
        Assert.IsFalse(pending.IsCompleted);
        const string html = "<html><body>Synthetic invoice &amp; details</body></html>";
        completed.SetResult(html);
        await (await pending).ExecuteResultAsync(new ActionContext(context, new RouteData(), new ActionDescriptor()));

        Assert.AreEqual(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.AreEqual("text/html", context.Response.ContentType);
        Assert.AreEqual("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.AreEqual(html, System.Text.Encoding.UTF8.GetString(body.ToArray()));
        flow.Verify(x => x.RunForDebugAsync(biller, id, cancellation.Token), Times.Once);
        flow.VerifyNoOtherCalls();
    }

    [TestMethod]
    [DataRow(true, 1L)]
    [DataRow(false, 0L)]
    [DataRow(false, -1L)]
    public async Task Invalid_debug_identifiers_return_404_without_reading(bool emptyBiller, long id)
    {
        Mock<HTMLFlow> flow = new(MockBehavior.Strict, null!, null!);
        DefaultHttpContext context = new();
        IActionResult result = await new DebugInvoice(flow.Object).Run(context.Request,
            emptyBiller ? Guid.Empty : Guid.NewGuid(), id, CancellationToken.None);
        Assert.IsInstanceOfType<NotFoundResult>(result);
        Assert.AreEqual("no-store", context.Response.Headers.CacheControl.ToString());
        flow.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task Debug_view_propagates_missing_invoice_failure()
    {
        Guid biller = Guid.NewGuid();
        long id = Invoice.ParseID(DateTimeOffset.UtcNow);
        ApplicationException failure = new(InternalError.NotFound, "Synthetic missing invoice");
        Mock<HTMLFlow> flow = new(MockBehavior.Strict, null!, null!);
        flow.Setup(x => x.RunForDebugAsync(biller, id, CancellationToken.None)).ThrowsAsync(failure);
        ApplicationException actual = await Assert.ThrowsAsync<ApplicationException>(() =>
            new DebugInvoice(flow.Object).Run(new DefaultHttpContext().Request, biller, id, CancellationToken.None));
        Assert.AreSame(failure, actual);
    }

    [TestMethod]
    public async Task Debug_view_forwards_cancellation_without_reporting_success()
    {
        Guid biller = Guid.NewGuid();
        long id = Invoice.ParseID(DateTimeOffset.UtcNow);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Mock<HTMLFlow> flow = new(MockBehavior.Strict, null!, null!);
        flow.Setup(x => x.RunForDebugAsync(biller, id, cancellation.Token)).Returns(Task.FromCanceled<string>(cancellation.Token));
        OperationCanceledException actual = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new DebugInvoice(flow.Object).Run(new DefaultHttpContext().Request, biller, id, cancellation.Token));
        Assert.AreEqual(cancellation.Token, actual.CancellationToken);
    }
}
