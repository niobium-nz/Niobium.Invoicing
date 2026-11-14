using System.Text.Json.Nodes;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Niobium.Invoicing.Flows;
using Niobium.Invoicing.Options;
using PriorInvoice = Niobium.Invoicing.Integration.Tests.LegacyLifecycleV1.Invoice;
using PriorDomain = Niobium.Invoicing.Integration.Tests.LegacyLifecycleV1.Domains.InvoiceDomain;

namespace Niobium.Invoicing.Integration.Tests;

public sealed partial class StorageFlowTests
{
    [TestMethod]
    public async Task Actual_prior_lifecycle_writer_erases_an_unfenced_reference_but_rejects_schema_two_before_writing()
    {
        IssueInvoiceRequest request = await NewRequest();
        await host!.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, default);
        IRepository<Invoice> currentRepository = host.Services.GetRequiredService<IRepository<Invoice>>();
        string partition = Invoice.BuildPartitionKey(request.BillerID), id = Invoice.BuildRowKey(request.InvoiceID);
        Invoice guarded = (await currentRepository.RetrieveAsync(partition, id))!;
        string original = guarded.LifecycleData!;
        TableClient table = tableService!.GetTableClient(prefix + nameof(Invoice));
        TableEntity unsafeControl = (await table.GetEntityAsync<TableEntity>(partition, id)).Value;
        JsonObject data = JsonNode.Parse(original)!.AsObject();
        data["Version"] = 1; // Deliberately unsafe, only in the disposable emulator.
        unsafeControl[nameof(Invoice.LifecycleData)] = data.ToJsonString();
        await table.UpdateEntityAsync(unsafeControl, unsafeControl.ETag, TableUpdateMode.Replace);
        PriorDomain old = LifecycleV1Domain(partition, id);
        await old.CancelAsync(request.Tenant, request.BillerID,
            new() { Operation = Guid.NewGuid(), Reason = "Synthetic unfenced control" }, DateTimeOffset.UtcNow);
        Invoice erased = (await currentRepository.RetrieveAsync(partition, id))!;
        Assert.IsNull(erased.ReadLifecycle()!.HistoryRevision, "The real old replace writer must demonstrate the unfenced loss.");

        guarded.ETag = erased.ETag;
        await currentRepository.UpdateAsync([guarded]);
        Invoice before = (await currentRepository.RetrieveAsync(partition, id))!;
        old = LifecycleV1Domain(partition, id);
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => old.CancelAsync(request.Tenant, request.BillerID,
            new() { Operation = Guid.NewGuid(), Reason = "Synthetic old cancellation" }, DateTimeOffset.UtcNow));
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => old.ReconcilePaymentHistoryAsync(request.Tenant, request.BillerID,
            new() { Operation = Guid.NewGuid(), Finding = LegacyLifecycleV1.InvoicePaymentHistory.PaymentOccurred,
                Reason = "Synthetic review", Evidence = "owned-emulator" }, DateTimeOffset.UtcNow));
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => old.UpdateAsync(request, request.InvoiceItems, default));
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => old.FreezeAsync(request.InvoiceItems, "synthetic-secret", default));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => old.OnDeliveredAsync("synthetic-token", default));
        Invoice after = (await currentRepository.RetrieveAsync(partition, id))!;
        Assert.AreEqual(before.ETag, after.ETag);
        Assert.AreEqual(original, after.LifecycleData);
        Assert.AreEqual(before.ReadLifecycle()!.HistoryRevision, after.ReadLifecycle()!.HistoryRevision);
    }

    private static PriorDomain LifecycleV1Domain(string partition, string id)
    {
        PriorDomain result = new(host!.Services.GetRequiredService<IOptions<BillingOptions>>(),
            new(() => host.Services.GetRequiredService<IRepository<PriorInvoice>>()), []);
        result.Initialize(partition, id);
        return result;
    }
}
