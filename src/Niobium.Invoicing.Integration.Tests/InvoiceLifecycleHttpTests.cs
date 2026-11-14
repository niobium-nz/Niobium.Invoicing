using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using Niobium.Invoicing.Domains;
using Niobium.Invoicing.Flows;
using Niobium.Invoicing.Options;
using Niobium.Invoicing.Server;

namespace Niobium.Invoicing.Integration.Tests;

[TestClass]
public sealed class InvoiceLifecycleHttpTests
{
    [TestMethod]
    [DataRow("missing", 401)]
    [DataRow("unauthenticated-claims", 401)]
    [DataRow("missing-group", 401)]
    [DataRow("wrong-biller", 403)]
    [DataRow("invalid-invoice", 400)]
    public async Task AllLifecycleRoutes_RejectInvalidIdentityBeforeStorage(string scenario, int status)
    {
        Guid biller = Guid.NewGuid(), tenant = Guid.NewGuid();
        Mock<IDomainRepository<InvoiceDomain, Invoice>> repository = new(MockBehavior.Strict);
        InvoiceLifecycleFunctions handler = new(new(repository.Object, TimeProvider.System));
        HttpRequest request = await Request(biller, tenant, scenario);
        long invoice = scenario == "invalid-invoice" ? 0 : Invoice.ParseID(DateTimeOffset.UtcNow);
        IActionResult[] results =
        [
            await handler.GetInvoiceStatus(request, biller, invoice, default),
            await handler.CancelInvoice(request, biller, invoice, new(), default),
            await handler.ReconcileInvoicePaymentHistory(request, biller, invoice, new(), default),
        ];
        foreach (IActionResult result in results)
            Assert.AreEqual(status, ((StatusCodeResult)result).StatusCode);
        repository.VerifyNoOtherCalls();
    }

    [TestMethod]
    public async Task AuthenticatedOwner_ExecutesRealReconciliationAndCancellationFlow_AndStatusIsNotCached()
    {
        Guid biller = Guid.NewGuid(), tenant = Guid.NewGuid();
        long invoice = Invoice.ParseID(DateTimeOffset.UtcNow);
        Invoice saved = Invoice.BuildNew(invoice,
            new Biller { PartitionKey = tenant, RowKey = biller, BusinessName = "Synthetic", Currency = "NZD", Culture = "en-NZ", TimeZone = "Pacific/Auckland" },
            new Billee { Biller = biller, ID = Guid.NewGuid(), Name = "Synthetic", Currency = "NZD", Culture = "en-NZ", TimeZone = "Pacific/Auckland" });
        saved.ETag = "initial";
        int writes = 0;
        Mock<IRepository<Invoice>> storage = new(MockBehavior.Strict);
        storage.Setup(r => r.RetrieveAsync(Invoice.BuildPartitionKey(biller), Invoice.BuildRowKey(invoice), It.IsAny<IList<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => InvoiceContent.Copy(saved));
        storage.Setup(r => r.UpdateAsync(It.IsAny<IEnumerable<Invoice>>(), true, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<Invoice> values, bool _, bool _, CancellationToken _) =>
            {
                saved = InvoiceContent.Copy(values.Single());
                saved.ETag = (++writes).ToString(System.Globalization.CultureInfo.InvariantCulture);
                return new[] { InvoiceContent.Copy(saved) };
            });
        Lazy<IRepository<Invoice>> lazy = new(() => storage.Object);
        GenericDomainRepository<InvoiceDomain, Invoice> domains = new(() => new(Microsoft.Extensions.Options.Options.Create(new BillingOptions { InvoiceTokenSecretSalt = "synthetic-test-salt", GetInvoiceEndpoint = "https://invoice.example.test" }), lazy, []), lazy);
        InvoiceLifecycleFunctions handler = new(new(domains, TimeProvider.System));
        HttpRequest request = await Request(biller, tenant);
        Assert.AreEqual(InvoicePaymentHistory.Unknown, ((InvoiceLifecycleStatus)((OkObjectResult)await handler.GetInvoiceStatus(request, biller, invoice, default)).Value!).PaymentHistory);
        Assert.AreEqual("no-store", request.HttpContext.Response.Headers.CacheControl.ToString());
        Assert.IsInstanceOfType<BadRequestResult>(await handler.CancelInvoice(request, biller, invoice, null, default));
        Assert.IsInstanceOfType<BadRequestResult>(await handler.ReconcileInvoicePaymentHistory(request, biller, invoice, null, default));
        var reconciled = (OkObjectResult)await handler.ReconcileInvoicePaymentHistory(request, biller, invoice,
            new()
            {
                Operation = Guid.NewGuid(),
                Finding = InvoicePaymentHistory.NoPayments,
                Reason = "Synthetic review",
                Evidence = "synthetic-evidence"
            }, default);
        Assert.IsTrue(((InvoiceLifecycleStatus)reconciled.Value!).CanCancel);
        var cancelled = (OkObjectResult)await handler.CancelInvoice(request, biller, invoice,
            new()
            {
                Operation = Guid.NewGuid(),
                Reason = "Synthetic cancellation"
            }, default);
        Assert.IsTrue(((InvoiceLifecycleStatus)cancelled.Value!).Cancelled);
        Assert.AreEqual(2, writes);
        HttpRequest foreignGroup = await Request(biller, Guid.NewGuid());
        ApplicationException forbidden = await Assert.ThrowsExactlyAsync<ApplicationException>(() => handler.GetInvoiceStatus(foreignGroup, biller, invoice, default));
        Assert.AreEqual(403, forbidden.ErrorCode);
    }

    private static async Task<HttpRequest> Request(Guid biller, Guid tenant, string scenario = "valid")
    {
        List<Claim> claims = [new(ClaimTypes.Sid, (scenario == "wrong-biller" ? Guid.NewGuid() : biller).ToString()), new(ClaimTypes.GroupSid, tenant.ToString())];
        if (scenario == "missing-group")
            claims.RemoveAt(1);
        DefaultHttpContext context = new();
        context.User = scenario switch
        {
            "missing" => new(),
            "unauthenticated-claims" => new(new ClaimsIdentity(claims)),
            _ => await HttpBoundaryTests.Parser().ParseAsync(HttpBoundaryTests.Token(claims)),
        };
        return context.Request;
    }
}
