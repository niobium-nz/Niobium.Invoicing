using Microsoft.Extensions.DependencyInjection;
using Niobium.Invoicing.Domains;
using Niobium.Invoicing.Flows;

namespace Niobium.Invoicing.Integration.Tests;

public sealed partial class StorageFlowTests
{
    [TestMethod]
    public async Task LifecycleColumnAndAudit_MaterializeThroughPinnedAdapter_AndRejectStaleCancellation()
    {
        IssueInvoiceRequest request = await NewRequest();
        request.NotifyBillee = true;
        await host!.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, default);
        IDomainRepository<InvoiceDomain, Invoice> domains = host.Services.GetRequiredService<IDomainRepository<InvoiceDomain, Invoice>>();
        InvoiceDomain first = await domains.GetAsync(Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID), forceLoad: true);
        InvoiceDomain stale = await domains.GetAsync(Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID), forceLoad: true);
        Invoice original = await first.GetEntityAsync();
        await stale.GetEntityAsync();
        string token = original.Token!;
        CancelInvoiceRequest action = new()
        {
            Operation = Guid.NewGuid(),
            Reason = "Synthetic cancellation"
        };
        InvoiceLifecycleStatus result = await first.CancelAsync(request.Tenant, request.BillerID, action, DateTimeOffset.UtcNow);
        Assert.IsTrue(result.Cancelled);
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => stale.CancelAsync(request.Tenant, request.BillerID, action, DateTimeOffset.UtcNow));
        Invoice loaded = (await host.Services.GetRequiredService<IRepository<Invoice>>().RetrieveAsync(
            Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID)))!;
        Assert.AreEqual(action.Operation, loaded.ReadLifecycle()!.Cancellation!.Operation);
        Assert.AreEqual(request.BillerID, loaded.ReadLifecycle()!.Cancellation!.Actor);
        Assert.AreNotEqual(original.ETag, loaded.ETag);
        Assert.AreEqual(token, loaded.Token);
        Assert.AreEqual(original.IssuanceFingerprint, loaded.IssuanceFingerprint);
        // These are the exact checkpoint values accepted by the prior writer.
        Assert.IsFalse(new string?[] { null, "preparing-v1", "retryable-v1", "ready-v1", "frozen-v1" }.Contains(loaded.IssuanceState));
        string html = await host.Services.GetRequiredService<HTMLFlow>().RunAsync(request.BillerID, request.InvoiceID, token, default);
        StringAssert.Contains(html, "Cancelled");
        Assert.HasCount(1, messages);
    }

    [TestMethod]
    public async Task LegacyReconciliationAndCancellation_PersistEvidenceTogetherAcrossRecreatedDomains()
    {
        IssueInvoiceRequest request = await NewRequest();
        await host!.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, default);
        IRepository<Invoice> repository = host.Services.GetRequiredService<IRepository<Invoice>>();
        Invoice existing = (await repository.RetrieveAsync(Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID)))!;
        // Synthetic legacy shape: normal invoice JSON deliberately excludes all
        // internal lifecycle/checkpoint columns. This fixture is loopback-only.
        Invoice legacy = JsonMarshaller.Unmarshall<Invoice>(JsonMarshaller.Marshall(existing));
        await repository.UpdateAsync([legacy]);
        InvoiceLifecycleFlow flow = host.Services.GetRequiredService<InvoiceLifecycleFlow>();
        InvoiceLifecycleStatus before = await flow.GetAsync(request.BillerID, request.InvoiceID, request.Tenant);
        Assert.AreEqual(InvoiceCancellationBlock.PaymentHistoryUnknown, before.CancellationBlock);
        ReconcileInvoicePaymentHistoryRequest evidence = new()
        {
            Operation = Guid.NewGuid(),
            Finding = InvoicePaymentHistory.NoPayments,
            Reason = "Synthetic historical review",
            Evidence = "owned-emulator-review-reference",
        };
        await flow.ReconcileAsync(request.BillerID, request.InvoiceID, request.Tenant, evidence);
        await flow.CancelAsync(request.BillerID, request.InvoiceID, request.Tenant, new()
        {
            Operation = Guid.NewGuid(),
            Reason = "Synthetic cancellation"
        });
        Invoice final = (await repository.RetrieveAsync(Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID)))!;
        Assert.AreEqual(evidence.Evidence, final.ReadLifecycle()!.Reconciliations.Single().Evidence);
        Assert.IsNotNull(final.ReadLifecycle()!.Cancellation);
        Assert.AreEqual(InvoicePaymentHistory.NoPayments, final.ReadLifecycle()!.PaymentHistory);
    }
}
