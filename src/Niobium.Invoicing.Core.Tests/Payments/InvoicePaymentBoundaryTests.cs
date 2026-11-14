using Niobium.Finance;
using Niobium.Invoicing.Core.Tests.Support;
using Niobium.Invoicing.Domains;

namespace Niobium.Invoicing.Core.Tests.Payments;

[TestClass]
public sealed class InvoicePaymentBoundaryTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Unverified_receipt_or_posting_cannot_change_the_invoice(bool receipt)
    {
        IssuanceScenario scenario = await Ready();
        scenario.PaymentRecords.CorruptReceiptRead = receipt;
        scenario.PaymentRecords.MissingPostingRead = !receipt;
        string lifecycle = scenario.Stored!.LifecycleData!;
        int writes = scenario.InvoiceWrites, history = scenario.History.Revisions.Count;
        ApplicationException rejected = await Assert.ThrowsExactlyAsync<ApplicationException>(() => Record(scenario, Receipt()));
        Assert.AreEqual(409, rejected.ErrorCode);
        Assert.AreEqual(lifecycle, scenario.Stored.LifecycleData);
        Assert.AreEqual(writes, scenario.InvoiceWrites);
        Assert.AreEqual(history, scenario.History.Revisions.Count);
        Assert.IsNull(scenario.Stored.ReadLifecycle()!.Payments);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Missing_or_corrupted_committed_records_reject_reads_and_further_payments(bool receipt)
    {
        IssuanceScenario scenario = await Ready();
        await Record(scenario, Receipt());
        scenario.PaymentRecords.CorruptReceiptRead = receipt;
        scenario.PaymentRecords.MissingPostingRead = !receipt;
        int writes = scenario.InvoiceWrites, count = scenario.PaymentRecords.Receipts.Count;
        ApplicationException read = await Assert.ThrowsExactlyAsync<ApplicationException>(() =>
            scenario.Domain().GetPaymentBalanceAsync(scenario.Request.Tenant, scenario.Request.BillerID));
        Assert.AreEqual(409, read.ErrorCode);
        ApplicationException mutation = await Assert.ThrowsExactlyAsync<ApplicationException>(() => Record(scenario, Receipt()));
        Assert.AreEqual(409, mutation.ErrorCode);
        Assert.AreEqual(writes, scenario.InvoiceWrites);
        Assert.AreEqual(count, scenario.PaymentRecords.Receipts.Count);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Payment_and_cancellation_races_preserve_the_committed_winner(bool paymentWins)
    {
        IssuanceScenario scenario = await Ready();
        InvoiceDomain payment = scenario.Domain(), cancellation = scenario.Domain();
        _ = await payment.GetEntityAsync();
        _ = await cancellation.GetEntityAsync();
        RecordInvoicePaymentRequest receipt = Receipt();
        CancelInvoiceRequest cancel = new()
        {
            Operation = Guid.NewGuid(),
            Reason = "Synthetic cancellation"
        };
        if (paymentWins)
        {
            await payment.RecordPaymentAsync(scenario.Request.Tenant, scenario.Request.BillerID, receipt);
            ApplicationException stale = await Assert.ThrowsExactlyAsync<ApplicationException>(() =>
                cancellation.CancelAsync(scenario.Request.Tenant, scenario.Request.BillerID, cancel, DateTimeOffset.UtcNow));
            Assert.AreEqual(409, stale.ErrorCode);
            ApplicationException fresh = await Assert.ThrowsExactlyAsync<ApplicationException>(() =>
                scenario.Domain().CancelAsync(scenario.Request.Tenant, scenario.Request.BillerID, cancel, DateTimeOffset.UtcNow));
            Assert.AreEqual(409, fresh.ErrorCode);
            Assert.IsNull(scenario.Stored!.ReadLifecycle()!.Cancellation);
        }
        else
        {
            await cancellation.CancelAsync(scenario.Request.Tenant, scenario.Request.BillerID, cancel, DateTimeOffset.UtcNow);
            ApplicationException stale = await Assert.ThrowsExactlyAsync<ApplicationException>(() =>
                payment.RecordPaymentAsync(scenario.Request.Tenant, scenario.Request.BillerID, receipt));
            Assert.AreEqual(409, stale.ErrorCode);
            await Record(scenario, receipt);
            Assert.AreEqual(cancel.Operation, scenario.Stored!.ReadLifecycle()!.Cancellation!.Operation);
        }
        InvoicePaymentBalance balance = await scenario.Domain().GetPaymentBalanceAsync(scenario.Request.Tenant, scenario.Request.BillerID);
        Assert.AreEqual(1000, balance.SettledCents);
        Assert.AreEqual(1, balance.Entries);
        Assert.AreEqual(InvoicePaymentHistory.PaymentOccurred, scenario.Stored!.ReadLifecycle()!.PaymentHistory);
    }

    [TestMethod]
    [DataRow("tenant")]
    [DataRow("actor")]
    public async Task Payment_operations_require_the_invoice_owner(string mismatch)
    {
        IssuanceScenario scenario = await Ready();
        Guid tenant = mismatch == "tenant" ? Guid.NewGuid() : scenario.Request.Tenant;
        Guid actor = mismatch == "actor" ? Guid.NewGuid() : scenario.Request.BillerID;
        int writes = scenario.InvoiceWrites;
        ApplicationException rejected = await Assert.ThrowsExactlyAsync<ApplicationException>(() =>
            scenario.Domain().RecordPaymentAsync(tenant, actor, Receipt()));
        Assert.AreEqual(403, rejected.ErrorCode);
        ApplicationException hidden = await Assert.ThrowsExactlyAsync<ApplicationException>(() =>
            scenario.Domain().GetPaymentBalanceAsync(tenant, actor));
        Assert.AreEqual(403, hidden.ErrorCode);
        Assert.AreEqual(writes, scenario.InvoiceWrites);
        Assert.AreEqual(0, scenario.PaymentRecords.Receipts.Count);
    }

    [TestMethod]
    public async Task Monetary_overflow_is_rejected_before_reserving_a_new_operation()
    {
        IssuanceScenario scenario = await Ready();
        await Record(scenario, Receipt(Int64.MaxValue));
        int writes = scenario.InvoiceWrites;
        ApplicationException rejected = await Assert.ThrowsExactlyAsync<ApplicationException>(() => Record(scenario, Receipt(1)));
        Assert.AreEqual(400, rejected.ErrorCode);
        Assert.AreEqual(writes, scenario.InvoiceWrites);
        Assert.AreEqual(1, scenario.PaymentRecords.Receipts.Count);
        InvoicePaymentBalance balance = await scenario.Domain().GetPaymentBalanceAsync(scenario.Request.Tenant, scenario.Request.BillerID);
        Assert.AreEqual(Int64.MaxValue, balance.SettledCents);
        Assert.AreEqual(Int64.MaxValue - 11500, balance.OverpaidCents);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Invoice_edits_cannot_rewrite_the_ledgers_opening_settlement_or_currency(bool settlement)
    {
        IssuanceScenario scenario = await Ready();
        await Record(scenario, Receipt());
        int writes = scenario.InvoiceWrites;
        if (settlement)
            scenario.Request.Settled = new Amount(100, "NZD");
        else
        {
            scenario.Biller.Currency = scenario.Billee.Currency = "AUD";
            scenario.Biller.Country = scenario.Billee.Country = "AU";
            scenario.Request.InvoiceItems[0].UnitPriceCurrency = "AUD";
            scenario.Request.InvoiceItems[0].LineTotalCurrency = "AUD";
        }
        ApplicationException rejected = await Assert.ThrowsExactlyAsync<ApplicationException>(() => scenario.RunAsync());
        Assert.AreEqual(settlement ? 409 : 400, rejected.ErrorCode);
        Assert.AreEqual(writes, scenario.InvoiceWrites);
        Assert.AreEqual(0, scenario.Stored!.SettledCents);
        Assert.AreEqual("NZD", scenario.Stored.GrandTotalCurrency);
    }

    [TestMethod]
    public async Task Reversal_evidence_cannot_predate_the_original_receipt()
    {
        IssuanceScenario scenario = await Ready();
        RecordInvoicePaymentRequest receipt = Receipt();
        await Record(scenario, receipt);
        ReverseInvoicePaymentRequest reversal = new()
        {
            Operation = Guid.NewGuid(),
            Reverses = receipt.Operation,
            AmountCents = 100,
            Currency = "NZD",
            OccurredAt = receipt.OccurredAt.AddDays(-1),
            Reason = "Synthetic reversal",
            Evidence = "Synthetic evidence"
        };
        ApplicationException rejected = await Assert.ThrowsExactlyAsync<ApplicationException>(() =>
            scenario.Domain().ReversePaymentAsync(scenario.Request.Tenant, scenario.Request.BillerID, reversal));
        Assert.AreEqual(400, rejected.ErrorCode);
        Assert.AreEqual(1, scenario.PaymentRecords.Receipts.Count);
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
            Reason = "Synthetic receipt",
            Evidence = "Synthetic evidence"
        };

    private static Task<InvoicePaymentBalance> Record(IssuanceScenario scenario, RecordInvoicePaymentRequest request)
        => scenario.Domain().RecordPaymentAsync(scenario.Request.Tenant, scenario.Request.BillerID, request);
}
