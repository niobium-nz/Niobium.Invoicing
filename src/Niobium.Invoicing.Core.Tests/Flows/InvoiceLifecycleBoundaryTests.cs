using System.Text.Json.Nodes;
using Niobium.Invoicing.Core.Tests.Support;

namespace Niobium.Invoicing.Core.Tests.Flows;

public sealed partial class InvoiceLifecycleTests
{
    [TestMethod]
    [DataRow("checkpoint")]
    [DataRow("missing-data")]
    [DataRow("malformed-json")]
    [DataRow("oversized")]
    [DataRow("version")]
    [DataRow("preparation")]
    [DataRow("write-id")]
    [DataRow("payment-history")]
    [DataRow("null-audit")]
    [DataRow("null-audit-entry")]
    [DataRow("delivery")]
    [DataRow("unconfirmed-delivered")]
    [DataRow("invalid-cancellation")]
    public async Task UnsupportedOrMalformedStoredLifecycle_FailsClosedWithoutWriting(string corruption)
    {
        IssuanceScenario s = new();
        await s.RunAsync();
        JsonObject data = JsonNode.Parse(s.Stored!.LifecycleData!)!.AsObject();
        string? raw = s.Stored.LifecycleData;
        switch (corruption)
        {
            case "checkpoint":
                s.Stored.IssuanceState = "future-version";
                break;
            case "missing-data":
                raw = null;
                break;
            case "malformed-json":
                raw = "{";
                break;
            case "oversized":
                raw = new string(' ', 32769);
                break;
            case "version":
                data["Version"] = InvoiceLifecycleSnapshot.CurrentVersion + 1;
                break;
            case "preparation":
                data["Preparation"] = 99;
                break;
            case "write-id":
                data["WriteID"] = "";
                break;
            case "payment-history":
                data["PaymentHistory"] = 99;
                break;
            case "null-audit":
                data["Reconciliations"] = null;
                break;
            case "null-audit-entry":
                data["Reconciliations"] = new JsonArray((JsonNode?)null);
                break;
            case "delivery":
                data["Delivery"] = 99;
                break;
            case "unconfirmed-delivered":
                data["Delivery"] = (int)InvoiceDeliveryState.Delivered;
                break;
            case "invalid-cancellation":
                data["Cancellation"] = JsonNode.Parse("{}");
                break;
        }
        if (corruption is not ("checkpoint" or "missing-data" or "malformed-json" or "oversized"))
            raw = data.ToJsonString();
        // Deliberately corrupt only this synthetic storage fixture, including the private mapped setter.
        typeof(Invoice).GetProperty(nameof(Invoice.LifecycleData))!.SetValue(s.Stored, raw);
        int before = s.InvoiceWrites;
        ApplicationException status = await Assert.ThrowsExactlyAsync<ApplicationException>(() => s.Domain().GetLifecycleStatusAsync(s.Request.Tenant, s.Request.BillerID));
        Assert.AreEqual(409, status.ErrorCode);
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => s.Domain().CancelAsync(s.Request.Tenant, s.Request.BillerID, Cancellation(), Now));
        Assert.AreEqual(before, s.InvoiceWrites);
    }

    [TestMethod]
    [DataRow(InvoiceDeliveryState.NotRequested)]
    [DataRow(InvoiceDeliveryState.Delivering)]
    [DataRow(InvoiceDeliveryState.Delivered)]
    [DataRow(InvoiceDeliveryState.DeliveryProblem)]
    public async Task VerifiedUnpaidInvoice_CanCancelAtEveryDeliveryStage(InvoiceDeliveryState delivery)
    {
        IssuanceScenario s = new();
        await s.RunAsync();
        s.Stored!.StoreLifecycle(s.Stored.ReadLifecycle()! with
        {
            Delivery = delivery,
            ConfirmedDeliveredAt = delivery == InvoiceDeliveryState.Delivered ? Now.AddMinutes(-1) : null,
        });
        InvoiceLifecycleStatus result = await s.Domain().CancelAsync(s.Request.Tenant, s.Request.BillerID, Cancellation(), Now);
        Assert.IsTrue(result.Cancelled);
        Assert.AreEqual(delivery, result.Delivery);
        Assert.AreEqual(delivery == InvoiceDeliveryState.Delivered ? Now.AddMinutes(-1) : (DateTimeOffset?)null, result.ConfirmedDeliveredAt);
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => s.Domain().EnsureAccessTokenAsync("synthetic-salt", default));
        Assert.IsNull(s.Stored.Token);
    }

    [TestMethod]
    public async Task ReconcilingLegacyDraft_PreservesItsHistoricalDefaultsOnLaterPreparation()
    {
        IssuanceScenario s = new();
        await s.RunAsync();
        s.Stored = JsonMarshaller.Unmarshall<Invoice>(JsonMarshaller.Marshall(s.Stored!));
        s.Stored.Particulars = "Legacy particulars";
        s.Stored.Reference = "Legacy reference";
        s.Stored.PaymentInstructions = "Legacy bank transfer";
        await s.Domain().ReconcilePaymentHistoryAsync(s.Request.Tenant, s.Request.BillerID, Reconciliation(InvoicePaymentHistory.NoPayments), Now);
        Assert.AreEqual(InvoicePreparationStage.Draft, s.Stored.GetPreparationStage());
        await s.RunAsync();
        Assert.AreEqual("Legacy particulars", s.Stored.Particulars);
        Assert.AreEqual("Legacy reference", s.Stored.Reference);
        Assert.AreEqual("Legacy bank transfer", s.Stored.PaymentInstructions);
        Assert.AreEqual(InvoicePaymentHistory.NoPayments, s.Stored.ReadLifecycle()!.PaymentHistory);
        Assert.HasCount(1, s.Stored.ReadLifecycle()!.Reconciliations);
    }

    [TestMethod]
    public async Task CancellationCommittedAfterFreeze_IsObservedBeforeBrokerEnqueue()
    {
        IssuanceScenario s = new();
        s.Request.NotifyBillee = true;
        Guid operation = Guid.NewGuid();
        s.BeforeInvoiceRead = () =>
        {
            if (s.Stored?.GetPreparationStage() == InvoicePreparationStage.Frozen
                && s.Stored.ReadLifecycle()!.Cancellation == null)
            {
                // A different request has committed cancellation between this
                // flow's freeze and final read; use a distinct persisted ETag.
                s.Stored.StoreLifecycle(s.Stored.ReadLifecycle()! with
                {
                    Cancellation = new(operation, s.Request.BillerID, "Synthetic concurrent cancellation", Now)
                });
                s.Stored.ETag = "concurrent-cancellation";
            }
        };
        await s.RunAsync();
        Assert.IsEmpty(s.Messages);
        Assert.AreEqual(operation, s.Stored!.ReadLifecycle()!.Cancellation!.Operation);
        Assert.IsNull(s.Stored.Delivered);
        Assert.AreEqual("concurrent-cancellation", s.Stored.ETag);
    }

    [TestMethod]
    public async Task LegacyEnqueueTimestamp_IsNotDeliveryConfirmationOrPaymentEvidence()
    {
        IssuanceScenario s = new();
        await s.RunAsync();
        s.Stored = JsonMarshaller.Unmarshall<Invoice>(JsonMarshaller.Marshall(s.Stored!));
        s.Stored.Delivered = Now.AddDays(-1);
        InvoiceLifecycleStatus result = await s.Domain().GetLifecycleStatusAsync(s.Request.Tenant, s.Request.BillerID);
        Assert.AreEqual(InvoiceDeliveryState.Unknown, result.Delivery);
        Assert.AreEqual(s.Stored.Delivered, result.QueuedAt);
        Assert.IsNull(result.ConfirmedDeliveredAt);
        Assert.AreEqual(InvoiceCancellationBlock.PaymentHistoryUnknown, result.CancellationBlock);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("*")]
    public async Task UnconditionalVersion_CannotCancelOrReconcile(string? version)
    {
        IssuanceScenario s = new();
        await s.RunAsync();
        s.Stored!.ETag = version;
        int before = s.InvoiceWrites;
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => s.Domain().CancelAsync(s.Request.Tenant, s.Request.BillerID, Cancellation(), Now));
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => s.Domain().ReconcilePaymentHistoryAsync(s.Request.Tenant, s.Request.BillerID, Reconciliation(InvoicePaymentHistory.PaymentOccurred), Now));
        Assert.AreEqual(before, s.InvoiceWrites);
    }
}
