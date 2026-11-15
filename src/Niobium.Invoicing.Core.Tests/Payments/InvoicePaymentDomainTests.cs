using System.Text.Json;
using System.Text.Json.Nodes;
using Niobium.Invoicing.Core.Tests.Support;
using Niobium.Invoicing.Domains;
using Niobium.Invoicing.History;
using Niobium.Invoicing.Payments;

namespace Niobium.Invoicing.Core.Tests.Payments;

[TestClass]
public sealed class InvoicePaymentDomainTests
{
    [TestMethod]
    public async Task Manual_overpayment_updates_live_balance_and_history_without_changing_frozen_notification()
    {
        IssuanceScenario scenario = new();
        scenario.Request.NotifyBillee = true;
        await scenario.RunAsync();
        Invoice original = IssuanceScenario.Copy(scenario.Stored!);
        string before = JsonMarshaller.Marshall(await scenario.Domain().BuildNotificationParametersAsync(original.Token!, default));
        RecordInvoicePaymentRequest request = Receipt(12000);
        InvoicePaymentBalance result = await Record(scenario, request);
        Assert.AreEqual(12000, result.SettledCents);
        Assert.AreEqual(0, result.OutstandingCents);
        Assert.AreEqual(500, result.OverpaidCents);
        Assert.AreEqual(0, scenario.Stored!.SettledCents);
        Assert.AreEqual(original.IssuanceFingerprint, scenario.Stored.IssuanceFingerprint);
        Assert.AreEqual(original.Token, scenario.Stored.Token);
        Assert.AreEqual(3, scenario.Stored.ReadLifecycle()!.Version);
        Assert.AreEqual(before, JsonMarshaller.Marshall(await scenario.Domain().BuildNotificationParametersAsync(original.Token!, default)));
        string html = await scenario.Domain().BuildHTMLAsync(original.Token!, default);
        StringAssert.Contains(html, "<span class=\"f-w-600\">NZD$0.00</span>");
        InvoiceRevision latest = scenario.History.Committed(scenario.Stored)[0];
        Assert.AreEqual(InvoiceChangeKind.PaymentReceived, latest.ReadChange().Kind);
        InvoiceRevisionPayments audit = scenario.History.Snapshot(latest).Lifecycle!.Payments!;
        Assert.AreEqual(request.Operation, audit.Operation);
        Assert.AreEqual(12000, audit.SettledCents);
        Assert.AreEqual(request.Operation, (await scenario.Domain().GetPaymentAsync(scenario.Request.Tenant,
            scenario.Request.BillerID, request.Operation)).Operation);
        Assert.AreEqual(1, scenario.Messages.Count, "Recording a payment cannot send a notification.");
        await scenario.RunAsync();
        Assert.AreEqual(2, scenario.Messages.Count, "The existing explicit issuance replay remains supported.");
        Assert.AreEqual(before, JsonMarshaller.Marshall(await scenario.Domain().BuildNotificationParametersAsync(original.Token!, default)));
    }

    [TestMethod]
    public async Task Exact_retry_has_one_effect_and_conflicting_operation_reuse_fails()
    {
        IssuanceScenario scenario = await Ready();
        RecordInvoicePaymentRequest request = Receipt();
        await Record(scenario, request);
        int writes = scenario.InvoiceWrites, history = scenario.History.Revisions.Count;
        InvoicePaymentBalance replay = await Record(scenario, request);
        Assert.AreEqual(1000, replay.SettledCents);
        Assert.AreEqual(1, replay.Entries);
        Assert.AreEqual(writes, scenario.InvoiceWrites);
        Assert.AreEqual(history, scenario.History.Revisions.Count);
        request.AmountCents++;
        ApplicationException conflict = await Assert.ThrowsExactlyAsync<ApplicationException>(() => Record(scenario, request));
        Assert.AreEqual(409, conflict.ErrorCode);
        Assert.AreEqual(writes, scenario.InvoiceWrites);
        Assert.AreEqual(1, scenario.PaymentRecords.Receipts.Count);
    }

    [TestMethod]
    [DataRow("receipt")]
    [DataRow("posting")]
    [DataRow("index")]
    [DataRow("invoice")]
    public async Task Unknown_write_outcomes_are_resolved_by_exact_retry_without_counting_twice(string stage)
    {
        IssuanceScenario scenario = await Ready();
        RecordInvoicePaymentRequest request = Receipt();
        scenario.PaymentRecords.LoseReceiptResponse = stage == "receipt";
        scenario.PaymentRecords.LosePostingResponse = stage == "posting";
        scenario.PaymentRecords.LoseIndexResponse = stage == "index";
        scenario.LoseInvoiceWriteResponse = stage == "invoice";
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => Record(scenario, request));
        scenario.PaymentRecords.LoseReceiptResponse = scenario.PaymentRecords.LosePostingResponse = false;
        scenario.PaymentRecords.LoseIndexResponse = false;
        scenario.LoseInvoiceWriteResponse = false;
        InvoicePaymentBalance result = await Record(scenario, request);
        Assert.AreEqual(1000, result.SettledCents);
        Assert.AreEqual(1, result.Entries);
        Assert.AreEqual(1, scenario.PaymentRecords.Receipts.Count);
        Assert.AreEqual(1, scenario.PaymentRecords.Links.Count);
    }

    [TestMethod]
    public async Task A_losing_payment_rebases_its_posting_and_uncommitted_candidates_stay_invisible()
    {
        IssuanceScenario scenario = await Ready();
        InvoiceDomain first = scenario.Domain(), second = scenario.Domain();
        _ = await first.GetEntityAsync();
        _ = await second.GetEntityAsync();
        RecordInvoicePaymentRequest a = Receipt(), b = Receipt(250);
        await first.RecordPaymentAsync(scenario.Request.Tenant, scenario.Request.BillerID, a);
        ApplicationException stale = await Assert.ThrowsExactlyAsync<ApplicationException>(() =>
            second.RecordPaymentAsync(scenario.Request.Tenant, scenario.Request.BillerID, b));
        Assert.AreEqual(409, stale.ErrorCode);
        ApplicationException invisible = await Assert.ThrowsExactlyAsync<ApplicationException>(() =>
            scenario.Domain().GetPaymentAsync(scenario.Request.Tenant, scenario.Request.BillerID, b.Operation));
        Assert.AreEqual(404, invisible.ErrorCode);
        InvoicePaymentBalance recovered = await Record(scenario, b);
        Assert.AreEqual(1250, recovered.SettledCents);
        Assert.AreEqual(2, recovered.Entries);
        Assert.AreEqual(2, scenario.PaymentRecords.Receipts.Count);
        Assert.AreEqual(3, scenario.PaymentRecords.Links.Count);
    }

    [TestMethod]
    public async Task Partial_and_full_reversals_preserve_permanent_payment_evidence()
    {
        IssuanceScenario scenario = await Ready();
        RecordInvoicePaymentRequest receipt = Receipt();
        await Record(scenario, receipt);
        await Reverse(scenario, Reversal(receipt, 300));
        ReverseInvoicePaymentRequest last = Reversal(receipt, 700);
        InvoicePaymentBalance result = await Reverse(scenario, last);
        Assert.AreEqual(0, result.SettledCents);
        Assert.AreEqual(3, result.Entries);
        Assert.AreEqual(result, await Reverse(scenario, last));
        Assert.AreEqual(InvoicePaymentHistory.PaymentOccurred, scenario.Stored!.ReadLifecycle()!.PaymentHistory);
        ApplicationException cancelled = await Assert.ThrowsExactlyAsync<ApplicationException>(() => scenario.Domain().CancelAsync(
            scenario.Request.Tenant, scenario.Request.BillerID, new()
            {
                Operation = Guid.NewGuid(),
                Reason = "Synthetic cancellation"
            }, DateTimeOffset.UtcNow));
        Assert.AreEqual(409, cancelled.ErrorCode);
        ApplicationException excessive = await Assert.ThrowsExactlyAsync<ApplicationException>(() => Reverse(scenario, Reversal(receipt, 1)));
        Assert.AreEqual(409, excessive.ErrorCode);
        Assert.AreEqual(3, scenario.PaymentRecords.Receipts.Count);
    }

    [TestMethod]
    public async Task Late_real_payment_evidence_preserves_cancellation()
    {
        IssuanceScenario scenario = await Ready();
        await scenario.Domain().CancelAsync(scenario.Request.Tenant, scenario.Request.BillerID,
            new()
            {
                Operation = Guid.NewGuid(),
                Reason = "Synthetic cancellation"
            }, DateTimeOffset.UtcNow);
        InvoiceCancellation cancellation = scenario.Stored!.ReadLifecycle()!.Cancellation!;
        await Record(scenario, Receipt());
        Assert.AreEqual(cancellation, scenario.Stored!.ReadLifecycle()!.Cancellation);
        Assert.AreEqual(InvoicePaymentHistory.PaymentOccurred, scenario.Stored.ReadLifecycle()!.PaymentHistory);
    }

    [TestMethod]
    public async Task First_legacy_payment_captures_opening_amount_and_history_before_storing_schema_three()
    {
        IssuanceScenario scenario = new();
        scenario.Stored = Invoice.BuildNew(scenario.Request.InvoiceID, scenario.Biller, scenario.Billee);
        scenario.Stored.SettledCents = 500;
        scenario.Stored.GrandTotalCents = 2000;
        scenario.Stored.ETag = "legacy";
        InvoicePaymentBalance result = await Record(scenario, Receipt(200));
        Assert.AreEqual(500, result.OpeningSettledCents);
        Assert.AreEqual(700, result.SettledCents);
        Assert.AreEqual(500, scenario.Stored!.SettledCents);
        Assert.IsNotNull(scenario.Stored.ReadLifecycle()!.HistoryRevision);
        Assert.AreEqual(3, scenario.Stored.ReadLifecycle()!.Version);
        IReadOnlyList<InvoiceRevision> history = scenario.History.Committed(scenario.Stored);
        Assert.AreEqual(2, history.Count);
        Assert.AreEqual(InvoiceChangeKind.ObservedBaseline, history[1].ReadChange().Kind);
        Assert.IsNull(scenario.History.Snapshot(history[1]).Lifecycle);
        Assert.AreEqual(700, scenario.History.Snapshot(history[0]).Lifecycle!.Payments!.SettledCents);
    }

    [TestMethod]
    [DataRow("zero")]
    [DataRow("negative")]
    [DataRow("currency")]
    [DataRow("future")]
    [DataRow("operation")]
    [DataRow("evidence")]
    public async Task Invalid_payment_requests_leave_all_persistence_unchanged(string field)
    {
        IssuanceScenario scenario = await Ready();
        RecordInvoicePaymentRequest request = Receipt();
        switch (field)
        {
            case "zero":
                request.AmountCents = 0;
                break;
            case "negative":
                request.AmountCents = -1;
                break;
            case "currency":
                request.Currency = "AUD";
                break;
            case "future":
                request.OccurredAt = DateTimeOffset.UtcNow.AddDays(1);
                break;
            case "operation":
                request.Operation = Guid.Empty;
                break;
            default:
                request.Evidence = " ";
                break;
        }
        int writes = scenario.InvoiceWrites, history = scenario.History.Revisions.Count;
        ApplicationException invalid = await Assert.ThrowsExactlyAsync<ApplicationException>(() => Record(scenario, request));
        Assert.AreEqual(400, invalid.ErrorCode);
        Assert.AreEqual(writes, scenario.InvoiceWrites);
        Assert.AreEqual(history, scenario.History.Revisions.Count);
        Assert.AreEqual(0, scenario.PaymentRecords.Receipts.Count);
        Assert.AreEqual(0, scenario.PaymentRecords.Links.Count);
    }

    [TestMethod]
    [DataRow("status")]
    [DataRow("history")]
    [DataRow("balance")]
    [DataRow("payment")]
    [DataRow("cancel")]
    [DataRow("token")]
    public async Task Persisted_schema_three_without_history_is_rejected_without_adoption_or_repair(string operation)
    {
        IssuanceScenario scenario = await Ready();
        InvoicePaymentEntry entry = new(Guid.NewGuid(), InvoicePaymentKind.Received, 1000, "NZD", null,
            DateTimeOffset.UtcNow.AddMinutes(-1), scenario.Request.BillerID, "Synthetic receipt", "Synthetic evidence", DateTimeOffset.UtcNow);
        InvoicePaymentPosting posting = InvoicePaymentPosting.Capture(InvoicePayment.Capture(scenario.Stored!, entry), null, 0, 1000, "v1-" + new string('a', 64));
        JsonObject raw = JsonNode.Parse(scenario.Stored!.LifecycleData!)!.AsObject();
        raw["Version"] = 3;
        raw["HistoryRevision"] = null;
        raw["Payments"] = JsonSerializer.SerializeToNode(posting.ReadPosition());
        typeof(Invoice).GetProperty(nameof(Invoice.LifecycleData))!.SetValue(scenario.Stored, raw.ToJsonString());
        Assert.ThrowsExactly<InvalidOperationException>(() => scenario.Stored.ReadLifecycle());
        int writes = scenario.InvoiceWrites, revisions = scenario.History.Revisions.Count;
        InvoiceDomain domain = scenario.Domain();
        Task action = operation switch
        {
            "status" => domain.GetLifecycleStatusAsync(scenario.Request.Tenant, scenario.Request.BillerID),
            "history" => domain.ReadHistoryAsync(scenario.Request.Tenant, scenario.Request.BillerID),
            "balance" => domain.GetPaymentBalanceAsync(scenario.Request.Tenant, scenario.Request.BillerID),
            "payment" => Record(scenario, Receipt()),
            "cancel" => domain.CancelAsync(scenario.Request.Tenant, scenario.Request.BillerID,
                new()
                {
                    Operation = Guid.NewGuid(),
                    Reason = "Synthetic cancellation"
                }, DateTimeOffset.UtcNow),
            _ => domain.EnsureAccessTokenAsync(scenario.Options.InvoiceTokenSecretSalt, default)
        };
        ApplicationException invalid = await Assert.ThrowsExactlyAsync<ApplicationException>(() => action);
        Assert.AreEqual(409, invalid.ErrorCode);
        Assert.AreEqual(writes, scenario.InvoiceWrites);
        Assert.AreEqual(revisions, scenario.History.Revisions.Count);
        Assert.AreEqual(0, scenario.PaymentRecords.Receipts.Count);
    }

    private static async Task<IssuanceScenario> Ready()
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        return scenario;
    }
    private static RecordInvoicePaymentRequest Receipt(long amount = 1000)
        => new()
        {
            Operation = Guid.NewGuid(),
            AmountCents = amount,
            Currency = "NZD",
            OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Reason = "Synthetic manual receipt",
            Evidence = "Synthetic bank reference"
        };
    private static ReverseInvoicePaymentRequest Reversal(RecordInvoicePaymentRequest receipt, long amount)
        => new()
        {
            Operation = Guid.NewGuid(),
            Reverses = receipt.Operation,
            AmountCents = amount,
            Currency = "NZD",
            OccurredAt = DateTimeOffset.UtcNow,
            Reason = "Synthetic reversal evidence",
            Evidence = "Synthetic reversal reference"
        };
    private static Task<InvoicePaymentBalance> Record(IssuanceScenario scenario, RecordInvoicePaymentRequest request)
        => scenario.Domain().RecordPaymentAsync(scenario.Request.Tenant, scenario.Request.BillerID, request);
    private static Task<InvoicePaymentBalance> Reverse(IssuanceScenario scenario, ReverseInvoicePaymentRequest request)
        => scenario.Domain().ReversePaymentAsync(scenario.Request.Tenant, scenario.Request.BillerID, request);
}
