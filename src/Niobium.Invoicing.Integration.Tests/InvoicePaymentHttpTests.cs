using System.Security.Claims;
using Azure.Data.Tables;
using Niobium.Invoicing.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Niobium.Invoicing.Flows;
using Niobium.Invoicing.History;
using Niobium.Invoicing.Server;

namespace Niobium.Invoicing.Integration.Tests;

public sealed partial class StorageFlowTests
{
    [TestMethod]
    public async Task Authenticated_payment_handlers_commit_real_records_and_preserve_retry_reversal_and_history_semantics()
    {
        IssueInvoiceRequest invoice = await NewRequest();
        await host!.Services.GetRequiredService<IssueFlow>().RunAsync(invoice, null, default);
        InvoiceLifecycleFunctions handler = new(host.Services.GetRequiredService<InvoiceLifecycleFlow>());
        DefaultHttpContext context = new();
        context.User = await HttpBoundaryTests.Parser().ParseAsync(HttpBoundaryTests.Token(
            [new Claim(ClaimTypes.Sid, invoice.BillerID.ToString()), new Claim(ClaimTypes.GroupSid, invoice.Tenant.ToString())]));
        HttpRequest request = context.Request;
        Assert.IsInstanceOfType<BadRequestResult>(await handler.RecordInvoicePayment(request, invoice.BillerID, invoice.InvoiceID, null, default));
        Assert.IsInstanceOfType<BadRequestResult>(await handler.ReverseInvoicePayment(request, invoice.BillerID, invoice.InvoiceID, null, default));
        Assert.AreEqual("no-store", context.Response.Headers.CacheControl.ToString());
        RecordInvoicePaymentRequest receipt = new()
        {
            Operation = Guid.NewGuid(),
            AmountCents = 12000,
            Currency = "NZD",
            OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Reason = "Synthetic manual payment",
            Evidence = "Synthetic bank reference"
        };
        InvoicePaymentBalance paid = (InvoicePaymentBalance)((OkObjectResult)await handler.RecordInvoicePayment(
            request, invoice.BillerID, invoice.InvoiceID, receipt, default)).Value!;
        Assert.AreEqual(12000, paid.SettledCents);
        Assert.AreEqual(500, paid.OverpaidCents);
        Assert.AreEqual(0, paid.OutstandingCents);
        InvoicePaymentBalance retry = (InvoicePaymentBalance)((OkObjectResult)await handler.RecordInvoicePayment(
            request, invoice.BillerID, invoice.InvoiceID, receipt, default)).Value!;
        Assert.AreEqual(paid, retry);
        InvoicePaymentEntry entry = (InvoicePaymentEntry)((OkObjectResult)await handler.GetInvoicePayment(
            request, invoice.BillerID, invoice.InvoiceID, receipt.Operation, default)).Value!;
        Assert.AreEqual(receipt.Operation, entry.Operation);
        Assert.AreEqual(invoice.BillerID, entry.Actor);
        Assert.AreEqual(receipt.Evidence, entry.Evidence);
        ReverseInvoicePaymentRequest reverse = new()
        {
            Operation = Guid.NewGuid(),
            Reverses = receipt.Operation,
            AmountCents = 12000,
            Currency = "NZD",
            OccurredAt = DateTimeOffset.UtcNow,
            Reason = "Synthetic reversal",
            Evidence = "Synthetic reversal reference"
        };
        InvoicePaymentBalance reversed = (InvoicePaymentBalance)((OkObjectResult)await handler.ReverseInvoicePayment(
            request, invoice.BillerID, invoice.InvoiceID, reverse, default)).Value!;
        Assert.AreEqual(0, reversed.SettledCents);
        Assert.AreEqual(11500, reversed.OutstandingCents);
        Assert.AreEqual(2, reversed.Entries);
        InvoicePaymentBalance read = (InvoicePaymentBalance)((OkObjectResult)await handler.GetInvoicePaymentBalance(
            request, invoice.BillerID, invoice.InvoiceID, default)).Value!;
        Assert.AreEqual(reversed, read);
        InvoiceLifecycleStatus status = (InvoiceLifecycleStatus)((OkObjectResult)await handler.GetInvoiceStatus(
            request, invoice.BillerID, invoice.InvoiceID, default)).Value!;
        Assert.AreEqual(InvoicePaymentHistory.PaymentOccurred, status.PaymentHistory);
        Assert.IsFalse(status.CanCancel);
        InvoiceHistoryPage history = (InvoiceHistoryPage)((OkObjectResult)await handler.GetInvoiceHistory(
            request, invoice.BillerID, invoice.InvoiceID, default)).Value!;
        Assert.AreEqual(InvoiceChangeKind.PaymentReversed, history.Entry!.Change.Kind);
        Assert.AreEqual(reverse.Operation, history.Entry.Snapshot.Lifecycle!.Payments!.Operation);
        Assert.IsEmpty(messages, "Manual payment evidence cannot send notifications.");

        context.User = await HttpBoundaryTests.Parser().ParseAsync(HttpBoundaryTests.Token(
            [new Claim(ClaimTypes.Sid, invoice.BillerID.ToString()), new Claim(ClaimTypes.GroupSid, Guid.NewGuid().ToString())]));
        ApplicationException denied = await Assert.ThrowsExactlyAsync<ApplicationException>(() => handler.GetInvoicePaymentBalance(
            request, invoice.BillerID, invoice.InvoiceID, default));
        Assert.AreEqual(403, denied.ErrorCode);
        Assert.AreEqual("no-store", context.Response.Headers.CacheControl.ToString());
        TableClient invoices = tableService!.GetTableClient(prefix + nameof(Invoice));
        TableEntity owned = (await invoices.GetEntityAsync<TableEntity>(Invoice.BuildPartitionKey(invoice.BillerID), Invoice.BuildRowKey(invoice.InvoiceID))).Value;
        await InvoicePaymentFixture.RemoveAsync(tableService.GetTableClient(prefix + InvoicePaymentFixture.PaymentsTable),
            tableService.GetTableClient(prefix + InvoicePaymentFixture.PostingsTable), tableService.GetTableClient(prefix + InvoicePaymentIndexFixture.Table),
            owned, invoice.BillerID, invoice.InvoiceID, invoice.BilleeID,
            () => InvoiceHistoryFixture.RemoveAsync(tableService.GetTableClient(prefix + InvoiceHistoryFixture.RevisionsTable),
                tableService.GetTableClient(prefix + InvoiceHistoryFixture.PartsTable), owned, invoice.BillerID, invoice.InvoiceID, invoice.BilleeID,
                async () => await invoices.DeleteEntityAsync(owned.PartitionKey, owned.RowKey, owned.ETag)));
        Assert.IsFalse((await invoices.GetEntityIfExistsAsync<TableEntity>(owned.PartitionKey, owned.RowKey)).HasValue,
            "The independent raw verifier must accept actual receipt, reversal, index and history representations before owned cleanup.");
    }
}
