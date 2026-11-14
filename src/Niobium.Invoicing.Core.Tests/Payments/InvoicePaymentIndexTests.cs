using Niobium.Invoicing.Core.Tests.Support;
using Niobium.Invoicing.Payments;

namespace Niobium.Invoicing.Core.Tests.Payments;

[TestClass]
public sealed class InvoicePaymentIndexTests
{
    [TestMethod]
    public async Task Long_payment_history_keeps_balance_operation_lookup_and_exact_retry_reads_bounded()
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        RecordInvoicePaymentRequest first = Receipt();
        await Record(scenario, first);
        for (int i = 1; i < 256; i++)
            await Record(scenario, Receipt());
        scenario.PaymentRecords.ResetReads();
        InvoicePaymentBalance balance = await scenario.Domain().GetPaymentBalanceAsync(scenario.Request.Tenant, scenario.Request.BillerID);
        Assert.AreEqual(25600, balance.SettledCents);
        Assert.AreEqual(256, balance.Entries);
        Assert.IsTrue(scenario.PaymentRecords.Reads <= 36, "A balance read must not traverse the payment history.");
        scenario.PaymentRecords.ResetReads();
        InvoicePaymentEntry oldest = await scenario.Domain().GetPaymentAsync(scenario.Request.Tenant, scenario.Request.BillerID, first.Operation);
        Assert.AreEqual(first.Operation, oldest.Operation);
        Assert.IsTrue(scenario.PaymentRecords.Reads <= 70, "Membership lookup has a fixed maximum of two index paths.");
        int writes = scenario.InvoiceWrites;
        scenario.PaymentRecords.ResetReads();
        Assert.AreEqual(balance, await Record(scenario, first));
        Assert.AreEqual(writes, scenario.InvoiceWrites);
        Assert.IsTrue(scenario.PaymentRecords.Reads <= 71, "An old exact retry must not scan all predecessor postings.");
    }

    [TestMethod]
    public async Task Operations_sharing_thirty_one_digits_remain_distinct_at_the_maximum_depth()
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        RecordInvoicePaymentRequest first = Receipt(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        RecordInvoicePaymentRequest second = Receipt(Guid.Parse("11111111-1111-1111-1111-111111111112"));
        RecordInvoicePaymentRequest third = Receipt(Guid.Parse("11111111-1111-1111-1111-111111111113"));
        await Record(scenario, first);
        await Record(scenario, second);
        await Record(scenario, third);
        Assert.AreEqual(32, scenario.PaymentRecords.Nodes.Values.Max(node => node.Depth));
        scenario.PaymentRecords.ResetReads();
        InvoicePaymentEntry found = await scenario.Domain().GetPaymentAsync(scenario.Request.Tenant, scenario.Request.BillerID, first.Operation);
        Assert.AreEqual(first.Operation, found.Operation);
        Assert.IsTrue(scenario.PaymentRecords.Reads <= 70);
        InvoicePaymentBalance reversed = await scenario.Domain().ReversePaymentAsync(scenario.Request.Tenant, scenario.Request.BillerID,
            new()
            {
                Operation = Guid.Parse("11111111-1111-1111-1111-111111111114"),
                Reverses = first.Operation,
                AmountCents = 50,
                Currency = "NZD",
                OccurredAt = DateTimeOffset.UtcNow,
                Reason = "Synthetic partial reversal",
                Evidence = "Synthetic reversal evidence"
            });
        Assert.AreEqual(250, reversed.SettledCents);
        Assert.AreEqual(4, reversed.Entries);
        Assert.AreEqual(first.Operation, (await scenario.Domain().GetPaymentAsync(scenario.Request.Tenant, scenario.Request.BillerID, first.Operation)).Operation);
    }

    [TestMethod]
    public async Task Missing_index_readback_cannot_commit_a_payment_and_exact_retry_recovers_it()
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        RecordInvoicePaymentRequest receipt = Receipt();
        int writes = scenario.InvoiceWrites;
        scenario.PaymentRecords.MissingIndexRead = true;
        ApplicationException failure = await Assert.ThrowsExactlyAsync<ApplicationException>(() => Record(scenario, receipt));
        Assert.AreEqual(409, failure.ErrorCode);
        Assert.AreEqual(writes, scenario.InvoiceWrites);
        Assert.IsNull(scenario.Stored!.ReadLifecycle()!.Payments);
        scenario.PaymentRecords.MissingIndexRead = false;
        InvoicePaymentBalance result = await Record(scenario, receipt);
        Assert.AreEqual(100, result.SettledCents);
        Assert.AreEqual(1, result.Entries);
        Assert.AreEqual(1, scenario.PaymentRecords.Receipts.Count);
    }

    [TestMethod]
    [DataRow("Version")]
    [DataRow("Entries")]
    [DataRow("Depth")]
    [DataRow("Tenant")]
    [DataRow("ID")]
    [DataRow("EntryData")]
    [DataRow("ChildrenData")]
    public async Task Altered_committed_index_columns_reject_reads_and_new_payments(string property)
    {
        IssuanceScenario scenario = new();
        await scenario.RunAsync();
        await Record(scenario, Receipt());
        InvoicePaymentIndex root = scenario.PaymentRecords.Nodes.Values.Single();
        object value = property switch
        {
            "Version" => 2,
            "Entries" => 2L,
            "Depth" => 33,
            "Tenant" => Guid.NewGuid(),
            "ID" => "v1-" + new string('f', 64),
            _ => "{}"
        };
        typeof(InvoicePaymentIndex).GetProperty(property)!.SetValue(root, value);
        int writes = scenario.InvoiceWrites;
        ApplicationException read = await Assert.ThrowsExactlyAsync<ApplicationException>(() =>
            scenario.Domain().GetPaymentBalanceAsync(scenario.Request.Tenant, scenario.Request.BillerID));
        Assert.AreEqual(409, read.ErrorCode);
        ApplicationException mutation = await Assert.ThrowsExactlyAsync<ApplicationException>(() => Record(scenario, Receipt()));
        Assert.AreEqual(409, mutation.ErrorCode);
        Assert.AreEqual(writes, scenario.InvoiceWrites);
        Assert.AreEqual(1, scenario.PaymentRecords.Receipts.Count);
    }

    private static RecordInvoicePaymentRequest Receipt(Guid? operation = null)
        => new()
        {
            Operation = operation ?? Guid.NewGuid(),
            AmountCents = 100,
            Currency = "NZD",
            OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Reason = "Synthetic manual payment",
            Evidence = "Synthetic evidence"
        };
    private static Task<InvoicePaymentBalance> Record(IssuanceScenario scenario, RecordInvoicePaymentRequest request)
        => scenario.Domain().RecordPaymentAsync(scenario.Request.Tenant, scenario.Request.BillerID, request);
}
