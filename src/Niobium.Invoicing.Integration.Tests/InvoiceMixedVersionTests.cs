using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Niobium.Invoicing.Domains;
using Niobium.Invoicing.Options;
using OldInvoice = Niobium.Invoicing.Integration.Tests.Legacy.Invoice;
using OldDomain = Niobium.Invoicing.Integration.Tests.Legacy.Domains.InvoiceDomain;

namespace Niobium.Invoicing.Integration.Tests;

public sealed partial class StorageFlowTests
{
    [TestMethod]
    public async Task PriorWriter_ControlErasesUnfencedEvidence_ButLifecycleCheckpointPreventsItsWrites()
    {
        IssueInvoiceRequest request = await NewRequest();
        await host!.Services.GetRequiredService<Flows.IssueFlow>().RunAsync(request, null, default);
        IRepository<Invoice> currentStorage = host.Services.GetRequiredService<IRepository<Invoice>>();
        IRepository<OldInvoice> oldStorage = host.Services.GetRequiredService<IRepository<OldInvoice>>();
        string partition = Invoice.BuildPartitionKey(request.BillerID), row = Invoice.BuildRowKey(request.InvoiceID);
        Invoice current = (await currentStorage.RetrieveAsync(partition, row))!;
        OldInvoice oldShape = (await oldStorage.RetrieveAsync(partition, row))!;
        Assert.AreEqual(InvoiceContent.Fingerprint(current, request.InvoiceItems),
            Legacy.Domains.InvoiceContent.Fingerprint(oldShape, request.InvoiceItems),
            "Adding lifecycle state must preserve the prior writer's v1 content fingerprint.");
        string evidence = current.LifecycleData!;
        // Unsafe control exists only in this owned emulator: expose new data under
        // the old checkpoint to prove the old replace writer really erases it.
        current.IssuanceState = "ready-v1";
        await currentStorage.UpdateAsync([current]);
        OldDomain control = PriorDomain(oldStorage, partition, row);
        request.InvoiceItems[0].Quantity = 2;
        Assert.IsTrue(await control.UpdateAsync(request, request.InvoiceItems, default));
        Invoice erased = (await currentStorage.RetrieveAsync(partition, row))!;
        Assert.IsNull(erased.LifecycleData);
        Assert.AreEqual("preparing-v1", erased.IssuanceState);

        // Restore the coherent owned invoice before testing the real fence.
        current.ETag = erased.ETag;
        typeof(Invoice).GetProperty(nameof(Invoice.LifecycleData))!.SetValue(current, evidence);
        current.IssuanceState = "lifecycle-v1";
        current.Token = "synthetic-frozen-link";
        current.SetPreparationStage(InvoicePreparationStage.Frozen);
        await currentStorage.UpdateAsync([current]);
        InvoiceDomain domain = new(host.Services.GetRequiredService<IOptions<BillingOptions>>(), new(() => currentStorage), []);
        domain.Initialize(partition, row);
        await domain.CancelAsync(request.Tenant, request.BillerID,
            new()
            {
                Operation = Guid.NewGuid(),
                Reason = "Synthetic cancellation"
            }, DateTimeOffset.UtcNow);
        Invoice guarded = (await currentStorage.RetrieveAsync(partition, row))!;
        OldDomain prior = PriorDomain(oldStorage, partition, row);
        ApplicationException update = await Assert.ThrowsExactlyAsync<ApplicationException>(() => prior.UpdateAsync(request, request.InvoiceItems, default));
        Assert.AreEqual(409, update.ErrorCode);
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => prior.FreezeAsync(request.InvoiceItems, "synthetic-salt", default));
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => prior.OnDeliveredAsync(guarded.Token!, default));
        Invoice final = (await currentStorage.RetrieveAsync(partition, row))!;
        Assert.AreEqual(guarded.ETag, final.ETag);
        Assert.AreEqual(guarded.LifecycleData, final.LifecycleData);
        Assert.IsNotNull(final.ReadLifecycle()!.Cancellation);
    }

    [TestMethod]
    public async Task PriorWriterHoldingLegacyVersion_CannotReplaceNewCancellationEvidence()
    {
        IssueInvoiceRequest request = await NewRequest();
        await host!.Services.GetRequiredService<Flows.IssueFlow>().RunAsync(request, null, default);
        IRepository<Invoice> storage = host.Services.GetRequiredService<IRepository<Invoice>>();
        string partition = Invoice.BuildPartitionKey(request.BillerID), row = Invoice.BuildRowKey(request.InvoiceID);
        Invoice legacy = (await storage.RetrieveAsync(partition, row))!;
        // Arrange a prior-version row only in this disposable emulator.
        typeof(Invoice).GetProperty(nameof(Invoice.LifecycleData))!.SetValue(legacy, null);
        legacy.IssuanceState = "ready-v1";
        await storage.UpdateAsync([legacy]);
        OldDomain inFlight = PriorDomain(host.Services.GetRequiredService<IRepository<OldInvoice>>(), partition, row);
        await inFlight.GetEntityAsync();
        InvoiceDomain current = new(host.Services.GetRequiredService<IOptions<BillingOptions>>(), new(() => storage), []);
        current.Initialize(partition, row);
        await current.ReconcilePaymentHistoryAsync(request.Tenant, request.BillerID, new()
        {
            Operation = Guid.NewGuid(),
            Finding = InvoicePaymentHistory.NoPayments,
            Reason = "Synthetic review",
            Evidence = "owned-emulator-review",
        }, DateTimeOffset.UtcNow);
        await current.CancelAsync(request.Tenant, request.BillerID, new()
        {
            Operation = Guid.NewGuid(),
            Reason = "Synthetic cancellation",
        }, DateTimeOffset.UtcNow);
        Invoice guarded = (await storage.RetrieveAsync(partition, row))!;
        request.InvoiceItems[0].Quantity = 2;
        ApplicationException conflict = await Assert.ThrowsExactlyAsync<ApplicationException>(() => inFlight.UpdateAsync(request, request.InvoiceItems, default));
        Assert.AreEqual(409, conflict.ErrorCode);
        Invoice final = (await storage.RetrieveAsync(partition, row))!;
        Assert.AreEqual(guarded.ETag, final.ETag);
        Assert.AreEqual(guarded.LifecycleData, final.LifecycleData);
    }

    private static OldDomain PriorDomain(IRepository<OldInvoice> storage, string partition, string row)
    {
        OldDomain domain = new(host!.Services.GetRequiredService<IOptions<BillingOptions>>(), new(() => storage), []);
        domain.Initialize(partition, row);
        return domain;
    }
}
