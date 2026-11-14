using Niobium.Invoicing.Core.Tests.Support;
using Niobium.Invoicing.Domains;

namespace Niobium.Invoicing.Core.Tests.Flows;

[TestClass]
public sealed partial class InvoiceLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private static CancelInvoiceRequest Cancellation() => new() { Operation = Guid.NewGuid(), Reason = "Customer cancelled the service" };
    private static ReconcileInvoicePaymentHistoryRequest Reconciliation(InvoicePaymentHistory finding) => new()
    {
        Operation = Guid.NewGuid(),
        Finding = finding,
        Reason = "Reviewed historical bank records",
        Evidence = "synthetic-reconciliation-reference",
    };

    [TestMethod]
    [DataRow(InvoicePreparationStage.Preparing)]
    [DataRow(InvoicePreparationStage.Retryable)]
    [DataRow(InvoicePreparationStage.Ready)]
    [DataRow(InvoicePreparationStage.Frozen)]
    public async Task UnpaidInvoice_CanCancelAtEveryPreparationStage_WithAtomicAudit(InvoicePreparationStage preparation)
    {
        IssuanceScenario s = new();
        await s.RunAsync();
        s.Stored!.SetPreparationStage(preparation);
        string fingerprint = InvoiceContent.Fingerprint(s.Stored, s.Items);
        CancelInvoiceRequest request = Cancellation();
        int before = s.InvoiceWrites;
        InvoiceLifecycleStatus result = await s.Domain().CancelAsync(s.Request.Tenant, s.Request.BillerID, request, Now);
        Assert.IsTrue(result.Cancelled);
        Assert.IsFalse(result.CanCancel);
        Assert.AreEqual(InvoiceCancellationBlock.AlreadyCancelled, result.CancellationBlock);
        Assert.AreEqual(before + 1, s.InvoiceWrites);
        InvoiceCancellation audit = s.Stored!.ReadLifecycle()!.Cancellation!;
        Assert.AreEqual(request.Operation, audit.Operation);
        Assert.AreEqual(s.Request.BillerID, audit.Actor);
        Assert.AreEqual(request.Reason, audit.Reason);
        Assert.AreEqual(Now, audit.At);
        Assert.AreEqual(fingerprint, InvoiceContent.Fingerprint(s.Stored, s.Items));
        string? version = s.Stored.ETag;
        await s.Domain().CancelAsync(s.Request.Tenant, s.Request.BillerID, request, Now.AddMinutes(1));
        Assert.AreEqual(version, s.Stored.ETag);
        Assert.AreEqual(before + 1, s.InvoiceWrites);
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => s.Domain().CancelAsync(s.Request.Tenant, s.Request.BillerID, Cancellation(), Now));
    }

    [TestMethod]
    public async Task LegacyZeroSettlement_RequiresRecordedReconciliation_AndKeepsItsEvidence()
    {
        IssuanceScenario s = new();
        await s.RunAsync();
        s.Stored = JsonMarshaller.Unmarshall<Invoice>(JsonMarshaller.Marshall(s.Stored!));
        Assert.IsNull(s.Stored.ReadLifecycle());
        InvoiceLifecycleStatus unknown = await s.Domain().GetLifecycleStatusAsync(s.Request.Tenant, s.Request.BillerID);
        Assert.AreEqual(InvoicePaymentHistory.Unknown, unknown.PaymentHistory);
        Assert.AreEqual(InvoiceCancellationBlock.PaymentHistoryUnknown, unknown.CancellationBlock);
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => s.Domain().CancelAsync(s.Request.Tenant, s.Request.BillerID, Cancellation(), Now));
        ReconcileInvoicePaymentHistoryRequest request = Reconciliation(InvoicePaymentHistory.NoPayments);
        InvoiceLifecycleStatus reviewed = await s.Domain().ReconcilePaymentHistoryAsync(s.Request.Tenant, s.Request.BillerID, request, Now);
        Assert.IsTrue(reviewed.CanCancel);
        Assert.AreEqual(request.Evidence, s.Stored.ReadLifecycle()!.Reconciliations.Single().Evidence);
        string? version = s.Stored.ETag;
        await s.Domain().ReconcilePaymentHistoryAsync(s.Request.Tenant, s.Request.BillerID, request, Now.AddDays(1));
        Assert.AreEqual(version, s.Stored.ETag);
        request.Evidence = "different-evidence";
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => s.Domain().ReconcilePaymentHistoryAsync(s.Request.Tenant, s.Request.BillerID, request, Now));
        await s.Domain().CancelAsync(s.Request.Tenant, s.Request.BillerID, Cancellation(), Now.AddMinutes(1));
        Assert.HasCount(1, s.Stored.ReadLifecycle()!.Reconciliations);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task HistoricalPayment_PermanentlyPreventsCancellation_EvenAfterBalanceReturnsToZero(bool legacy)
    {
        IssuanceScenario s = new();
        s.Request.Settled = new Niobium.Finance.Amount { Currency = "NZD", Cents = 100 };
        await s.RunAsync();
        if (legacy)
            s.Stored = JsonMarshaller.Unmarshall<Invoice>(JsonMarshaller.Marshall(s.Stored!));
        s.Request.Settled = Niobium.Finance.Amount.Zero;
        await s.RunAsync();
        Assert.AreEqual(0, s.Stored!.SettledCents);
        InvoiceLifecycleStatus result = await s.Domain().GetLifecycleStatusAsync(s.Request.Tenant, s.Request.BillerID);
        Assert.AreEqual(InvoicePaymentHistory.PaymentOccurred, result.PaymentHistory);
        Assert.IsFalse(result.CanCancel);
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => s.Domain().CancelAsync(s.Request.Tenant, s.Request.BillerID, Cancellation(), Now));
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => s.Domain().ReconcilePaymentHistoryAsync(s.Request.Tenant, s.Request.BillerID, Reconciliation(InvoicePaymentHistory.NoPayments), Now));
    }

    [TestMethod]
    public async Task ReconciliationCanRecordEarlierPayment_ButNeverEraseItOrEarlierEvidence()
    {
        IssuanceScenario s = new();
        await s.RunAsync();
        s.Stored = JsonMarshaller.Unmarshall<Invoice>(JsonMarshaller.Marshall(s.Stored!));
        ReconcileInvoicePaymentHistoryRequest first = Reconciliation(InvoicePaymentHistory.NoPayments);
        await s.Domain().ReconcilePaymentHistoryAsync(s.Request.Tenant, s.Request.BillerID, first, Now);
        await s.Domain().ReconcilePaymentHistoryAsync(s.Request.Tenant, s.Request.BillerID, Reconciliation(InvoicePaymentHistory.PaymentOccurred), Now.AddMinutes(1));
        Assert.HasCount(2, s.Stored!.ReadLifecycle()!.Reconciliations);
        InvoiceLifecycleStatus replay = await s.Domain().ReconcilePaymentHistoryAsync(s.Request.Tenant, s.Request.BillerID, first, Now.AddMinutes(2));
        Assert.AreEqual(InvoicePaymentHistory.PaymentOccurred, replay.PaymentHistory);
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => s.Domain().ReconcilePaymentHistoryAsync(s.Request.Tenant, s.Request.BillerID, Reconciliation(InvoicePaymentHistory.NoPayments), Now));
    }

    [TestMethod]
    public async Task CancelledFrozenInvoice_KeepsItsLinkAndTotals_AndStopsLaterSends()
    {
        IssuanceScenario s = new();
        s.Request.NotifyBillee = true;
        await s.RunAsync();
        string token = s.Stored!.Token!;
        long total = s.Stored.GrandTotalCents;
        string fingerprint = s.Stored.IssuanceFingerprint!;
        await s.Domain().CancelAsync(s.Request.Tenant, s.Request.BillerID, Cancellation(), Now);
        string html = await s.Domain().BuildHTMLAsync(token, default);
        StringAssert.Contains(html, "Cancelled");
        StringAssert.Contains(html, "Not payable");
        Assert.IsFalse(html.Contains("Bank transfer", StringComparison.Ordinal));
        Assert.AreEqual(total, s.Stored.GrandTotalCents);
        Assert.AreEqual(fingerprint, s.Stored.IssuanceFingerprint);
        Assert.AreEqual(token, s.Stored.Token);
        Assert.IsFalse(await s.Domain().CanQueueNotificationAsync(token));
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => s.RunAsync());
        Assert.HasCount(1, s.Messages);
    }

    [TestMethod]
    public async Task LateQueueAcknowledgement_PreservesCancellation()
    {
        IssuanceScenario s = new();
        s.Request.NotifyBillee = true;
        s.FailEnqueue = true;
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => s.RunAsync());
        await s.Domain().CancelAsync(s.Request.Tenant, s.Request.BillerID, Cancellation(), Now);
        await s.Domain().OnDeliveredAsync(s.Stored!.Token!, default);
        Assert.IsNotNull(s.Stored.ReadLifecycle()!.Cancellation);
        Assert.IsNotNull(s.Stored.ReadLifecycle()!.QueuedAt);
        Assert.IsNull(s.Stored.ReadLifecycle()!.ConfirmedDeliveredAt);
    }

    [TestMethod]
    public async Task ConcurrentInvoiceChange_RejectsCancellationWithoutClaimingSuccess()
    {
        IssuanceScenario s = new();
        await s.RunAsync();
        s.BeforeInvoiceUpdate = () => s.Stored!.ETag = "competing-write";
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => s.Domain().CancelAsync(s.Request.Tenant, s.Request.BillerID, Cancellation(), Now));
        Assert.IsNull(s.Stored!.ReadLifecycle()!.Cancellation);
    }

    [TestMethod]
    public async Task OwnerAndEvidenceChecks_RejectMutation()
    {
        IssuanceScenario s = new();
        await s.RunAsync();
        int before = s.InvoiceWrites;
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => s.Domain().CancelAsync(Guid.NewGuid(), s.Request.BillerID, Cancellation(), Now));
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => s.Domain().CancelAsync(s.Request.Tenant, Guid.NewGuid(), Cancellation(), Now));
        foreach (CancelInvoiceRequest invalid in new[] { new CancelInvoiceRequest(), new CancelInvoiceRequest { Operation = Guid.NewGuid(), Reason = new string('a', 501) } })
            await Assert.ThrowsExactlyAsync<ApplicationException>(() => s.Domain().CancelAsync(s.Request.Tenant, s.Request.BillerID, invalid, Now));
        Assert.AreEqual(before, s.InvoiceWrites);
    }

    [TestMethod]
    public async Task LostAdapterVersion_ReadBackVerifiesLifecycleAndAtomicAudit()
    {
        IssuanceScenario s = new()
        {
            DropWriteEtags = true
        };
        await s.RunAsync();
        await s.Domain().CancelAsync(s.Request.Tenant, s.Request.BillerID, Cancellation(), Now);
        Assert.IsNotNull(s.Stored!.ReadLifecycle()!.Cancellation);
        Assert.IsFalse(String.IsNullOrWhiteSpace(s.Stored.ReadLifecycle()!.WriteID));
    }
}
