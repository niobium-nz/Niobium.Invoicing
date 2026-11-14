using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Niobium.Invoicing.Testing;

namespace Niobium.Invoicing.Deployment.Tests;

public sealed partial class DeployedInvoiceTests
{
    [TestMethod]
    [DataRow("missing", 401)]
    [DataRow("invalid", 401)]
    [DataRow("wrong-biller", 403)]
    [DataRow("wrong-tenant", 403)]
    public async Task Lifecycle_routes_enforce_real_biller_and_group_identity_without_mutation(string mode, int expected)
    {
        long id = await SeedInvoice(recipient: String.Empty);
        string before = (await Read(id)).ETag.ToString();
        string? credential = mode == "missing" ? null : mode == "invalid" ? "invalid-test-token"
            : Token(mode == "wrong-biller" ? Guid.NewGuid() : biller, mode == "wrong-tenant" ? Guid.NewGuid() : tenant);
        foreach ((string suffix, object? body) in new (string, object?)[]
        {
            ("status", null),
            ("history", null),
            ("cancel", new CancelInvoiceRequest { Operation = Guid.NewGuid(), Reason = "Synthetic cancellation" }),
            ("payment-history/reconcile", PaymentReview(InvoicePaymentHistory.NoPayments)),
        })
        {
            using HttpResponseMessage response = await LifecycleRequest(id, suffix, body, credential);
            Assert.AreEqual(expected, (int)response.StatusCode);
            Assert.AreEqual(before, (await Read(id)).ETag.ToString(), "Rejected lifecycle calls must not change the invoice.");
        }
    }

    [TestMethod]
    public async Task Legacy_history_requires_audited_reconciliation_and_cancelled_saved_link_stays_usable()
    {
        long id = await SeedInvoice(recipient: String.Empty, legacyFrozen: true);
        CancelInvoiceRequest cancellation = new()
        {
            Operation = Guid.NewGuid(),
            Reason = "Synthetic service cancelled"
        };
        using (HttpResponseMessage rejected = await LifecycleRequest(id, "cancel", cancellation, Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.Conflict, rejected.StatusCode);
        InvoiceLifecycleStatus unknown = await LifecycleStatus(id);
        Assert.AreEqual(InvoiceCancellationBlock.PaymentHistoryUnknown, unknown.CancellationBlock);
        ReconcileInvoicePaymentHistoryRequest review = PaymentReview(InvoicePaymentHistory.NoPayments);
        using (HttpResponseMessage reconciled = await LifecycleRequest(id, "payment-history/reconcile", review, Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.OK, reconciled.StatusCode);

        // Reconciliation/cancellation use the real application on a fully seeded
        // legacy frozen snapshot. Never rewrite a history-linked fixture directly.
        IRepository<Invoice> repository = services.GetRequiredService<IRepository<Invoice>>();
        Invoice before = (await repository.RetrieveAsync(Invoice.BuildPartitionKey(biller), Invoice.BuildRowKey(id)))!;
        using (HttpResponseMessage response = await LifecycleRequest(id, "cancel", cancellation, Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Invoice cancelled = (await repository.RetrieveAsync(Invoice.BuildPartitionKey(biller), Invoice.BuildRowKey(id)))!;
        InvoiceLifecycleSnapshot evidence = InvoiceCheckpoint.ReadEvidence(await Read(id));
        Assert.AreEqual(cancellation.Operation, evidence.Cancellation!.Operation);
        Assert.AreEqual(biller, evidence.Cancellation.Actor);
        Assert.AreEqual(cancellation.Reason, evidence.Cancellation.Reason);
        Assert.AreEqual(review.Evidence, evidence.Reconciliations.Single().Evidence);
        Assert.AreEqual(before.GrandTotalCents, cancelled.GrandTotalCents);
        Assert.AreEqual(before.IssuanceFingerprint, cancelled.IssuanceFingerprint);
        Assert.IsTrue(before.Token == cancelled.Token, "Cancellation must preserve the saved token.");
        using (HttpResponseMessage html = await http.GetAsync($"/{biller}/invoices/{id}?token={cancelled.Token}"))
        {
            Assert.AreEqual(HttpStatusCode.OK, html.StatusCode);
            string content = await html.Content.ReadAsStringAsync();
            Assert.IsTrue(content.Contains("Cancelled", StringComparison.Ordinal) && content.Contains("Not payable", StringComparison.Ordinal));
        }
        using (HttpResponseMessage replay = await LifecycleRequest(id, "cancel", cancellation, Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        Assert.AreEqual(cancelled.ETag, (await Read(id)).ETag.ToString());
        using (HttpResponseMessage edit = await Post(Request(id), Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.Conflict, edit.StatusCode);
        Assert.IsTrue((await LifecycleStatus(id)).Cancelled);

        ReconcileInvoicePaymentHistoryRequest laterPayment = PaymentReview(InvoicePaymentHistory.PaymentOccurred);
        using (HttpResponseMessage later = await LifecycleRequest(id, "payment-history/reconcile", laterPayment, Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.OK, later.StatusCode);
        InvoiceLifecycleStatus corrected = await LifecycleStatus(id);
        Assert.IsTrue(corrected.Cancelled);
        Assert.AreEqual(InvoicePaymentHistory.PaymentOccurred, corrected.PaymentHistory);
        Invoice correctedInvoice = (await repository.RetrieveAsync(Invoice.BuildPartitionKey(biller), Invoice.BuildRowKey(id)))!;
        InvoiceLifecycleSnapshot correctedEvidence = InvoiceCheckpoint.ReadEvidence(await Read(id));
        Assert.AreEqual(cancellation.Operation, correctedEvidence.Cancellation!.Operation);
        CollectionAssert.AreEqual(new[] { review.Operation, laterPayment.Operation },
            correctedEvidence.Reconciliations.Select(x => x.Operation).ToArray());
        using (HttpResponseMessage replay = await LifecycleRequest(id, "payment-history/reconcile", laterPayment, Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        Assert.AreEqual(correctedInvoice.ETag, (await Read(id)).ETag.ToString());
    }

    [TestMethod]
    [DataRow(InvoicePaymentHistory.NoPayments)]
    [DataRow(InvoicePaymentHistory.PaymentOccurred)]
    public async Task Reconciliation_defers_during_an_owned_active_preparation(InvoicePaymentHistory finding)
    {
        long id = await SeedInvoice(recipient: String.Empty);
        IRepository<Invoice> repository = services.GetRequiredService<IRepository<Invoice>>();
        Invoice owned = (await repository.RetrieveAsync(Invoice.BuildPartitionKey(biller), Invoice.BuildRowKey(id)))!;
        owned.SetPreparationStage(InvoicePreparationStage.Preparing);
        owned.IssuanceOperation = Guid.NewGuid().ToString("N");
        await repository.UpdateAsync([owned]);
        string version = (await Read(id)).ETag.ToString();

        using HttpResponseMessage response = await LifecycleRequest(id, "payment-history/reconcile", PaymentReview(finding), Token(biller, tenant));

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        Assert.AreEqual(version, (await Read(id)).ETag.ToString());
        Invoice unchanged = (await repository.RetrieveAsync(Invoice.BuildPartitionKey(biller), Invoice.BuildRowKey(id)))!;
        Assert.AreEqual(owned.IssuanceOperation, unchanged.IssuanceOperation);
        Assert.IsEmpty(InvoiceCheckpoint.ReadEvidence(await Read(id)).Reconciliations);
    }

    [TestMethod]
    public async Task Recorded_historical_payment_blocks_cancellation_even_when_current_settlement_is_zero()
    {
        long id = await SeedInvoice(recipient: String.Empty);
        ReconcileInvoicePaymentHistoryRequest review = PaymentReview(InvoicePaymentHistory.PaymentOccurred);
        using (HttpResponseMessage saved = await LifecycleRequest(id, "payment-history/reconcile", review, Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode);
        string version = (await Read(id)).ETag.ToString();
        using (HttpResponseMessage cancellation = await LifecycleRequest(id, "cancel",
            new CancelInvoiceRequest { Operation = Guid.NewGuid(), Reason = "Synthetic cancellation attempt" }, Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.Conflict, cancellation.StatusCode);
        using (HttpResponseMessage erase = await LifecycleRequest(id, "payment-history/reconcile", PaymentReview(InvoicePaymentHistory.NoPayments), Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.Conflict, erase.StatusCode);
        Assert.AreEqual(version, (await Read(id)).ETag.ToString());
        InvoiceLifecycleStatus status = await LifecycleStatus(id);
        Assert.AreEqual(InvoicePaymentHistory.PaymentOccurred, status.PaymentHistory);
        Assert.AreEqual(InvoiceCancellationBlock.PaymentOccurred, status.CancellationBlock);
        Assert.IsFalse(status.Cancelled);
    }

    [TestMethod]
    public async Task Competing_cancellation_operations_record_exactly_one_winner()
    {
        long id = await SeedInvoice(recipient: String.Empty);
        using (HttpResponseMessage review = await LifecycleRequest(id, "payment-history/reconcile", PaymentReview(InvoicePaymentHistory.NoPayments), Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.OK, review.StatusCode);
        CancelInvoiceRequest[] actions =
        [
            new() { Operation = Guid.NewGuid(), Reason = "Synthetic cancellation A" },
            new() { Operation = Guid.NewGuid(), Reason = "Synthetic cancellation B" },
        ];
        HttpResponseMessage[] responses = await Task.WhenAll(actions.Select(action => LifecycleRequest(id, "cancel", action, Token(biller, tenant))));
        try
        {
            Assert.AreEqual(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.AreEqual(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            int winner = Array.FindIndex(responses, r => r.StatusCode == HttpStatusCode.OK);
            Invoice stored = (await services.GetRequiredService<IRepository<Invoice>>().RetrieveAsync(Invoice.BuildPartitionKey(biller), Invoice.BuildRowKey(id)))!;
            InvoiceLifecycleSnapshot evidence = InvoiceCheckpoint.ReadEvidence(await Read(id));
            Assert.AreEqual(actions[winner].Operation, evidence.Cancellation!.Operation);
            Assert.AreEqual(actions[winner].Reason, evidence.Cancellation!.Reason);
        }
        finally
        {
            foreach (HttpResponseMessage response in responses)
                response.Dispose();
        }
    }

    private static ReconcileInvoicePaymentHistoryRequest PaymentReview(InvoicePaymentHistory finding) => new()
    {
        Operation = Guid.NewGuid(),
        Finding = finding,
        Reason = "Synthetic historical review",
        Evidence = "owned-ci-review-reference",
    };

    private static async Task<InvoiceLifecycleStatus> LifecycleStatus(long id)
    {
        using HttpResponseMessage response = await LifecycleRequest(id, "status", null, Token(biller, tenant));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Headers.CacheControl?.NoStore == true);
        return JsonSerializer.Deserialize<InvoiceLifecycleStatus>(await response.Content.ReadAsStringAsync(), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Invoice lifecycle status was missing.");
    }

    private static async Task<HttpResponseMessage> LifecycleRequest(long id, string suffix, object? body, string? credential)
    {
        using HttpRequestMessage request = new(body == null ? HttpMethod.Get : HttpMethod.Post, $"/{biller}/invoices/{id}/{suffix}");
        if (body != null)
            request.Content = new StringContent(JsonMarshaller.Marshall(body), Encoding.UTF8, "application/json");
        if (credential != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        return await http.SendAsync(request);
    }
}
