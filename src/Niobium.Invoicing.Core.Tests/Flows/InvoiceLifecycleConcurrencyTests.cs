using Niobium.Invoicing.Core.Tests.Support;
using Niobium.Invoicing.Domains;

namespace Niobium.Invoicing.Core.Tests.Flows;

public sealed partial class InvoiceLifecycleTests
{
    [TestMethod]
    [DataRow(InvoicePreparationStage.Ready)]
    [DataRow(InvoicePreparationStage.Preparing)]
    public async Task LaterHistoricalPayment_PreservesCancellationAndBothFindings(InvoicePreparationStage stage)
    {
        IssuanceScenario s = new();
        await s.RunAsync();
        s.Stored = JsonMarshaller.Unmarshall<Invoice>(JsonMarshaller.Marshall(s.Stored!));
        ReconcileInvoicePaymentHistoryRequest first = Reconciliation(InvoicePaymentHistory.NoPayments);
        await s.Domain().ReconcilePaymentHistoryAsync(s.Request.Tenant, s.Request.BillerID, first, Now);
        s.Stored.SetPreparationStage(stage);
        await s.Domain().CancelAsync(s.Request.Tenant, s.Request.BillerID, Cancellation(), Now);
        InvoiceCancellation cancellation = s.Stored.ReadLifecycle()!.Cancellation!;
        ReconcileInvoicePaymentHistoryRequest payment = Reconciliation(InvoicePaymentHistory.PaymentOccurred);

        InvoiceLifecycleStatus result = await s.Domain().ReconcilePaymentHistoryAsync(s.Request.Tenant, s.Request.BillerID, payment, Now.AddMinutes(1));

        Assert.IsTrue(result.Cancelled);
        Assert.AreEqual(InvoicePaymentHistory.PaymentOccurred, result.PaymentHistory);
        Assert.AreEqual(cancellation, s.Stored.ReadLifecycle()!.Cancellation);
        Assert.HasCount(2, s.Stored.ReadLifecycle()!.Reconciliations);
        string? version = s.Stored.ETag;
        InvoiceLifecycleStatus replay = await s.Domain().ReconcilePaymentHistoryAsync(s.Request.Tenant, s.Request.BillerID, first, Now.AddMinutes(2));
        Assert.AreEqual(InvoicePaymentHistory.PaymentOccurred, replay.PaymentHistory);
        await s.Domain().ReconcilePaymentHistoryAsync(s.Request.Tenant, s.Request.BillerID, payment, Now.AddMinutes(2));
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => s.Domain().ReconcilePaymentHistoryAsync(
            s.Request.Tenant, s.Request.BillerID, Reconciliation(InvoicePaymentHistory.NoPayments), Now));
        Assert.AreEqual(version, s.Stored.ETag);
    }

    [TestMethod]
    [DataRow(InvoicePaymentHistory.NoPayments)]
    [DataRow(InvoicePaymentHistory.PaymentOccurred)]
    public async Task ActivePreparation_DefersReconciliationWithoutInvalidatingItsOwner(InvoicePaymentHistory finding)
    {
        IssuanceScenario s = new();
        await s.RunAsync();
        s.Stored = JsonMarshaller.Unmarshall<Invoice>(JsonMarshaller.Marshall(s.Stored!));
        InvoiceDomain preparer = s.Domain();
        await preparer.UpdateAsync(s.Request, s.Items, default);
        string? version = s.Stored.ETag;
        string? operation = s.Stored.IssuanceOperation;
        int writes = s.InvoiceWrites;

        ApplicationException rejection = await Assert.ThrowsExactlyAsync<ApplicationException>(() =>
            s.Domain().ReconcilePaymentHistoryAsync(s.Request.Tenant, s.Request.BillerID, Reconciliation(finding), Now));

        Assert.AreEqual(409, rejection.ErrorCode);
        Assert.AreEqual(version, s.Stored.ETag);
        Assert.AreEqual(operation, s.Stored.IssuanceOperation);
        Assert.AreEqual(writes, s.InvoiceWrites);
        await preparer.CompletePreparationAsync(s.Items, default);
        Assert.AreEqual(InvoicePreparationStage.Ready, s.Stored.GetPreparationStage());
        await s.Domain().ReconcilePaymentHistoryAsync(s.Request.Tenant, s.Request.BillerID, Reconciliation(finding), Now);
        Assert.AreEqual(finding, s.Stored.ReadLifecycle()!.PaymentHistory);
    }

    [TestMethod]
    public async Task CancellationAfterBrokerAcceptance_SenderRecordsAcknowledgementAndKeepsAudit()
    {
        IssuanceScenario s = new();
        s.Request.NotifyBillee = true;
        CancelInvoiceRequest cancellation = Cancellation();
        s.AfterEnqueue = async () =>
            await s.Domain().CancelAsync(s.Request.Tenant, s.Request.BillerID, cancellation, Now);

        await s.RunAsync();

        Assert.HasCount(1, s.Messages);
        Assert.AreEqual(cancellation.Operation, s.Stored!.ReadLifecycle()!.Cancellation!.Operation);
        Assert.AreEqual(s.Stored.Delivered, s.Stored.ReadLifecycle()!.QueuedAt);
        Assert.IsNotNull(s.Stored.Delivered);
        Assert.IsNull(s.Stored.ReadLifecycle()!.ConfirmedDeliveredAt);
        Assert.IsFalse(await s.Domain().CanQueueNotificationAsync(s.Stored.Token!));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AcknowledgementWriteConflict_MergesCancellationWithBoundedConditionalAttempts(bool continuous)
    {
        IssuanceScenario s = new();
        s.Request.NotifyBillee = true;
        Guid operation = Guid.NewGuid();
        int attempts = 0;
        s.AfterEnqueue = () =>
        {
            s.BeforeInvoiceUpdate = () =>
            {
                attempts++;
                if (attempts == 1 || continuous)
                {
                    s.Stored!.StoreLifecycle(s.Stored.ReadLifecycle()! with
                    {
                        Cancellation = new(operation, s.Request.BillerID, "Concurrent cancellation", Now)
                    });
                    s.Stored.ETag = "concurrent-write-" + attempts;
                }
            };
            return Task.CompletedTask;
        };

        if (continuous)
        {
            ApplicationException failure = await Assert.ThrowsExactlyAsync<ApplicationException>(() => s.RunAsync());
            Assert.AreEqual(409, failure.ErrorCode);
            Assert.AreEqual(3, attempts);
            Assert.IsNull(s.Stored!.Delivered);
        }
        else
        {
            await s.RunAsync();
            Assert.AreEqual(2, attempts);
            Assert.IsNotNull(s.Stored!.Delivered);
            Assert.AreEqual(s.Stored.Delivered, s.Stored.ReadLifecycle()!.QueuedAt);
        }
        Assert.HasCount(1, s.Messages);
        Assert.AreEqual(operation, s.Stored.ReadLifecycle()!.Cancellation!.Operation);
    }

    [TestMethod]
    [DataRow("token")]
    [DataRow("fingerprint")]
    [DataRow("endpoint")]
    public async Task AcknowledgementReload_RejectsChangedFrozenIdentity(string changed)
    {
        IssuanceScenario s = new();
        s.Request.NotifyBillee = true;
        s.AfterEnqueue = () =>
        {
            if (changed == "token") s.Stored!.Token = "different-saved-token";
            else if (changed == "fingerprint") s.Stored!.IssuanceFingerprint = "different-content";
            else s.Stored!.IssuanceNotificationEndpoint = "https://different.example.invalid";
            s.Stored!.ETag = "different-version";
            return Task.CompletedTask;
        };
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => s.RunAsync());
        Assert.IsNull(s.Stored!.Delivered);
        Assert.HasCount(1, s.Messages);
    }

    [TestMethod]
    public async Task AcknowledgementUnknownWriteOutcome_DoesNotRetry()
    {
        IssuanceScenario s = new();
        s.Request.NotifyBillee = true;
        int attempts = 0;
        s.AfterEnqueue = () =>
        {
            s.BeforeInvoiceUpdate = () => { attempts++; throw new TimeoutException("Synthetic unknown acknowledgement write"); };
            return Task.CompletedTask;
        };
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => s.RunAsync());
        Assert.AreEqual(1, attempts);
        Assert.HasCount(1, s.Messages);
    }
}
