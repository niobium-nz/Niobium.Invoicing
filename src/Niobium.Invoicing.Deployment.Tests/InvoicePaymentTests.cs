using System.Net;
using System.Text.Json;
using Niobium.Invoicing.History;
using Niobium.Invoicing.Testing;

namespace Niobium.Invoicing.Deployment.Tests;

public sealed partial class DeployedInvoiceTests
{
    [TestMethod]
    [DataRow("missing", 401)]
    [DataRow("invalid", 401)]
    [DataRow("wrong-biller", 403)]
    [DataRow("wrong-tenant", 403)]
    public async Task Payment_routes_require_real_biller_and_group_identity_before_any_write(string mode, int expected)
    {
        long id = await SeedInvoice(recipient: String.Empty);
        string before = (await Read(id)).ETag.ToString();
        string? credential = mode == "missing" ? null : mode == "invalid" ? "invalid-test-token"
            : Token(mode == "wrong-biller" ? Guid.NewGuid() : biller, mode == "wrong-tenant" ? Guid.NewGuid() : tenant);
        RecordInvoicePaymentRequest receipt = ManualReceipt(100);
        foreach ((string suffix, object? body) in new (string, object?)[]
        {
            ("payments/balance", null), ("payments/" + receipt.Operation, null),
            ("payments", receipt), ("payments/reversals", ManualReversal(receipt, 100))
        })
        {
            using HttpResponseMessage response = await LifecycleRequest(id, suffix, body, credential);
            Assert.AreEqual(expected, (int)response.StatusCode);
            Assert.AreEqual(before, (await Read(id)).ETag.ToString());
        }
    }

    [TestMethod]
    public async Task Manual_overpayment_exact_retry_and_full_reversal_preserve_frozen_links_and_permanent_payment_history()
    {
        long id = await SeedInvoice(recipient: String.Empty, legacyFrozen: true);
        var original = await Read(id);
        string savedToken = original.GetString(nameof(Invoice.Token))!;
        await AssertDisplayedPaymentDue(id, savedToken, "NZD$115.00");
        RecordInvoicePaymentRequest receipt = ManualReceipt(12000);
        using (HttpResponseMessage posted = await LifecycleRequest(id, "payments", receipt, Token(biller, tenant)))
        {
            InvoicePaymentBalance paid = await PaymentResult(posted);
            Assert.AreEqual(12000, paid.SettledCents);
            Assert.AreEqual(500, paid.OverpaidCents);
            Assert.AreEqual(0, paid.OutstandingCents);
        }
        string committed = (await Read(id)).ETag.ToString();
        using (HttpResponseMessage replay = await LifecycleRequest(id, "payments", receipt, Token(biller, tenant)))
            Assert.AreEqual(1, (await PaymentResult(replay)).Entries);
        Assert.AreEqual(committed, (await Read(id)).ETag.ToString());
        using (HttpResponseMessage entry = await LifecycleRequest(id, "payments/" + receipt.Operation, null, Token(biller, tenant)))
        {
            Assert.AreEqual(HttpStatusCode.OK, entry.StatusCode);
            Assert.IsTrue(entry.Headers.CacheControl?.NoStore == true);
            InvoicePaymentEntry observed = JsonSerializer.Deserialize<InvoicePaymentEntry>(await entry.Content.ReadAsStringAsync(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Assert.AreEqual(receipt.Operation, observed.Operation);
            Assert.AreEqual(biller, observed.Actor);
            Assert.AreEqual(receipt.Evidence, observed.Evidence);
        }
        await AssertDisplayedPaymentDue(id, savedToken, "NZD$0.00");
        receipt.AmountCents++;
        using (HttpResponseMessage conflict = await LifecycleRequest(id, "payments", receipt, Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.Conflict, conflict.StatusCode);
        receipt.AmountCents--;
        Assert.AreEqual(committed, (await Read(id)).ETag.ToString());
        using (HttpResponseMessage partial = await LifecycleRequest(id, "payments/reversals", ManualReversal(receipt, 3000), Token(biller, tenant)))
            Assert.AreEqual(9000, (await PaymentResult(partial)).SettledCents);
        await AssertDisplayedPaymentDue(id, savedToken, "NZD$25.00");
        ReverseInvoicePaymentRequest final = ManualReversal(receipt, 9000);
        using (HttpResponseMessage reversed = await LifecycleRequest(id, "payments/reversals", final, Token(biller, tenant)))
        {
            InvoicePaymentBalance balance = await PaymentResult(reversed);
            Assert.AreEqual(0, balance.SettledCents);
            Assert.AreEqual(11500, balance.OutstandingCents);
            Assert.AreEqual(3, balance.Entries);
        }
        await AssertDisplayedPaymentDue(id, savedToken, "NZD$115.00");
        string afterReversal = (await Read(id)).ETag.ToString();
        using (HttpResponseMessage replay = await LifecycleRequest(id, "payments/reversals", final, Token(biller, tenant)))
            Assert.AreEqual(3, (await PaymentResult(replay)).Entries);
        using (HttpResponseMessage excessive = await LifecycleRequest(id, "payments/reversals", ManualReversal(receipt, 1), Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.Conflict, excessive.StatusCode);
        using (HttpResponseMessage cancellation = await LifecycleRequest(id, "cancel",
            new CancelInvoiceRequest { Operation = Guid.NewGuid(), Reason = "Synthetic cancellation attempt" }, Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.Conflict, cancellation.StatusCode);
        Assert.AreEqual(afterReversal, (await Read(id)).ETag.ToString());
        InvoiceLifecycleStatus status = await LifecycleStatus(id);
        Assert.AreEqual(InvoicePaymentHistory.PaymentOccurred, status.PaymentHistory);
        Assert.IsFalse(status.CanCancel);
        InvoiceHistoryPage history = await HistoryPage(id);
        Assert.AreEqual(InvoiceChangeKind.PaymentReversed, history.Entry!.Change.Kind);
        Assert.AreEqual(final.Operation, history.Entry.Snapshot.Lifecycle!.Payments!.Operation);
        var stored = await Read(id);
        Assert.IsTrue(original.GetString(nameof(Invoice.Token)) == stored.GetString(nameof(Invoice.Token)), "A payment must preserve the existing saved link.");
        Assert.AreEqual(original.GetString(nameof(Invoice.IssuanceFingerprint)), stored.GetString(nameof(Invoice.IssuanceFingerprint)));
        Assert.AreEqual(original.GetInt64(nameof(Invoice.SettledCents)), stored.GetInt64(nameof(Invoice.SettledCents)));
        Assert.IsNull(stored.GetDateTimeOffset(nameof(Invoice.Delivered)), "Manual records do not enqueue invoice notifications.");
    }

    [TestMethod]
    public async Task Competing_manual_payments_recover_with_exact_retries_and_each_has_one_committed_effect()
    {
        long id = await SeedInvoice(recipient: String.Empty, legacyFrozen: true);
        RecordInvoicePaymentRequest[] receipts = [ManualReceipt(100), ManualReceipt(250)];
        HttpResponseMessage[] responses = await Task.WhenAll(receipts.Select(receipt =>
            LifecycleRequest(id, "payments", receipt, Token(biller, tenant))));
        foreach (HttpResponseMessage response in responses)
        {
            using (response)
                Assert.IsTrue(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict);
        }
        foreach (RecordInvoicePaymentRequest receipt in receipts)
        {
            using HttpResponseMessage replay = await LifecycleRequest(id, "payments", receipt, Token(biller, tenant));
            Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        }
        using HttpResponseMessage result = await LifecycleRequest(id, "payments/balance", null, Token(biller, tenant));
        InvoicePaymentBalance balance = await PaymentResult(result);
        Assert.AreEqual(350, balance.SettledCents);
        Assert.AreEqual(2, balance.Entries);
        string version = (await Read(id)).ETag.ToString();
        foreach (RecordInvoicePaymentRequest receipt in receipts)
        {
            using HttpResponseMessage replay = await LifecycleRequest(id, "payments", receipt, Token(biller, tenant));
            Assert.AreEqual(balance, await PaymentResult(replay));
        }
        Assert.AreEqual(version, (await Read(id)).ETag.ToString());
    }

    [TestMethod]
    public async Task Payment_cancellation_race_retains_cancellation_when_it_won_and_records_late_payment_evidence()
    {
        long id = await SeedInvoice(recipient: String.Empty, legacyFrozen: true);
        using (HttpResponseMessage reviewed = await LifecycleRequest(id, "payment-history/reconcile",
            PaymentReview(InvoicePaymentHistory.NoPayments), Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.OK, reviewed.StatusCode);
        RecordInvoicePaymentRequest receipt = ManualReceipt(100);
        CancelInvoiceRequest cancellation = new() { Operation = Guid.NewGuid(), Reason = "Synthetic racing cancellation" };
        HttpResponseMessage[] responses = await Task.WhenAll(
            LifecycleRequest(id, "payments", receipt, Token(biller, tenant)),
            LifecycleRequest(id, "cancel", cancellation, Token(biller, tenant)));
        bool cancellationAcknowledged = responses[1].StatusCode == HttpStatusCode.OK;
        foreach (HttpResponseMessage response in responses)
        {
            using (response)
                Assert.IsTrue(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict);
        }
        using (HttpResponseMessage retry = await LifecycleRequest(id, "payments", receipt, Token(biller, tenant)))
            Assert.AreEqual(1, (await PaymentResult(retry)).Entries);
        var paid = await Read(id);
        InvoiceCancellation? cancelled = InvoiceCheckpoint.ReadEvidence(paid).Cancellation;
        if (cancellationAcknowledged)
            Assert.IsNotNull(cancelled, "A successful cancellation must survive a later payment.");
        if (cancelled != null)
        {
            Assert.AreEqual(cancellation.Operation, cancelled.Operation);
            Assert.AreEqual(biller, cancelled.Actor);
            Assert.AreEqual(cancellation.Reason, cancelled.Reason);
        }
        // The first response can be uncertain after a successful commit. Resolve
        // the same operation against durable evidence, without creating a new one.
        using (HttpResponseMessage replay = await LifecycleRequest(id, "cancel", cancellation, Token(biller, tenant)))
            Assert.AreEqual(cancelled == null ? HttpStatusCode.Conflict : HttpStatusCode.OK, replay.StatusCode);
        Assert.AreEqual(paid.ETag.ToString(), (await Read(id)).ETag.ToString());
        InvoiceLifecycleStatus status = await LifecycleStatus(id);
        Assert.AreEqual(cancelled != null, status.Cancelled);
        Assert.AreEqual(InvoicePaymentHistory.PaymentOccurred, status.PaymentHistory);
        Assert.IsFalse(status.CanCancel);
    }

    private async Task AssertDisplayedPaymentDue(long id, string savedToken, string expected)
    {
        using HttpResponseMessage response = await http.GetAsync($"/{biller}/invoices/{id}?token={savedToken}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        string html = await response.Content.ReadAsStringAsync();
        int start = html.IndexOf("<div class=\"invoice-price-right\">", StringComparison.Ordinal);
        Assert.IsTrue(start >= 0, "The invoice amount-due element is missing.");
        int end = html.IndexOf("</div>", start, StringComparison.Ordinal);
        Assert.IsTrue(end > start, "The invoice amount-due element is incomplete.");
        string due = html[start..end];
        Assert.IsTrue(due.Contains("<small>TOTAL AMOUNT DUE</small>", StringComparison.Ordinal));
        Assert.IsTrue(due.Contains($"<span class=\"f-w-600\">{expected}</span>", StringComparison.Ordinal),
            "The amount-due element must reflect the committed payment balance.");
    }

    private static RecordInvoicePaymentRequest ManualReceipt(long amount)
        => new() { Operation = Guid.NewGuid(), AmountCents = amount, Currency = "NZD", OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Reason = "Synthetic manual receipt", Evidence = "Synthetic bank evidence reference" };
    private static ReverseInvoicePaymentRequest ManualReversal(RecordInvoicePaymentRequest receipt, long amount)
        => new() { Operation = Guid.NewGuid(), Reverses = receipt.Operation, AmountCents = amount, Currency = "NZD",
            OccurredAt = DateTimeOffset.UtcNow.AddSeconds(-1), Reason = "Synthetic external reversal", Evidence = "Synthetic reversal evidence reference" };
    private static async Task<InvoicePaymentBalance> PaymentResult(HttpResponseMessage response)
    {
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Headers.CacheControl?.NoStore == true);
        return JsonSerializer.Deserialize<InvoicePaymentBalance>(await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidOperationException("Payment balance is missing.");
    }
}
