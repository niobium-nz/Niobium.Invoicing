using System.Text.Json.Nodes;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Niobium.Invoicing.Flows;
using Niobium.Invoicing.Options;
using PriorInvoice = Niobium.Invoicing.Integration.Tests.LegacyLifecycleV2.Invoice;
using PriorDomain = Niobium.Invoicing.Integration.Tests.LegacyLifecycleV2.Domains.InvoiceDomain;
using PriorHistory = Niobium.Invoicing.Integration.Tests.LegacyLifecycleV2.History.InvoiceHistoryDomain;
using PriorRevision = Niobium.Invoicing.Integration.Tests.LegacyLifecycleV2.History.InvoiceRevision;
using PriorPart = Niobium.Invoicing.Integration.Tests.LegacyLifecycleV2.History.InvoiceRevisionPart;

namespace Niobium.Invoicing.Integration.Tests;

public sealed partial class StorageFlowTests
{
    [TestMethod]
    public async Task Actual_schema_two_writer_erases_unfenced_payment_state_but_rejects_schema_three_before_writing()
    {
        IssueInvoiceRequest request = await NewRequest();
        await host!.Services.GetRequiredService<IssueFlow>().RunAsync(request, null, default);
        string partition = Invoice.BuildPartitionKey(request.BillerID), id = Invoice.BuildRowKey(request.InvoiceID);
        TableClient table = tableService!.GetTableClient(prefix + nameof(Invoice));
        TableEntity raw = (await table.GetEntityAsync<TableEntity>(partition, id)).Value;
        JsonObject data = JsonNode.Parse(raw.GetString(nameof(Invoice.LifecycleData))!)!.AsObject();
        JsonNode position = JsonNode.Parse(new PaymentFixtureRows().Owned.GetString("LifecycleData")!)!["Payments"]!.DeepClone();
        data["Payments"] = position.DeepClone();
        data["Version"] = 2; // Deliberately unsafe, only in owned loopback storage.
        raw[nameof(Invoice.LifecycleData)] = data.ToJsonString();
        await table.UpdateEntityAsync(raw, raw.ETag, TableUpdateMode.Replace);
        await LifecycleV2Domain(partition, id).ReconcilePaymentHistoryAsync(request.Tenant, request.BillerID,
            new()
            {
                Operation = Guid.NewGuid(),
                Finding = LegacyLifecycleV2.InvoicePaymentHistory.PaymentOccurred,
                Reason = "Synthetic prior writer",
                Evidence = "Owned emulator control"
            }, DateTimeOffset.UtcNow);
        raw = (await table.GetEntityAsync<TableEntity>(partition, id)).Value;
        data = JsonNode.Parse(raw.GetString(nameof(Invoice.LifecycleData))!)!.AsObject();
        Assert.IsNull(data["Payments"], "The actual old domain and replace adapter must demonstrate unfenced loss.");
        data["Version"] = 3;
        data["Payments"] = position.DeepClone();
        raw[nameof(Invoice.LifecycleData)] = data.ToJsonString();
        await table.UpdateEntityAsync(raw, raw.ETag, TableUpdateMode.Replace);
        TableEntity before = (await table.GetEntityAsync<TableEntity>(partition, id)).Value;
        Func<Task>[] operations =
        [
            () => LifecycleV2Domain(partition, id).GetLifecycleStatusAsync(request.Tenant, request.BillerID),
            () => LifecycleV2Domain(partition, id).CancelAsync(request.Tenant, request.BillerID,
                new() { Operation = Guid.NewGuid(), Reason = "Synthetic old cancellation" }, DateTimeOffset.UtcNow),
            () => LifecycleV2Domain(partition, id).ReconcilePaymentHistoryAsync(request.Tenant, request.BillerID,
                new() { Operation = Guid.NewGuid(), Finding = LegacyLifecycleV2.InvoicePaymentHistory.PaymentOccurred,
                    Reason = "Synthetic old reconciliation", Evidence = "Owned emulator" }, DateTimeOffset.UtcNow),
            () => LifecycleV2Domain(partition, id).UpdateAsync(request, request.InvoiceItems, default),
            () => LifecycleV2Domain(partition, id).FreezeAsync(request.InvoiceItems, "synthetic-salt", default),
            () => LifecycleV2Domain(partition, id).EnsureAccessTokenAsync("synthetic-salt", default)
        ];
        foreach (Func<Task> operation in operations)
        {
            ApplicationException rejected = await Assert.ThrowsExactlyAsync<ApplicationException>(operation);
            Assert.AreEqual(409, rejected.ErrorCode);
        }
        TableEntity after = (await table.GetEntityAsync<TableEntity>(partition, id)).Value;
        Assert.AreEqual(before.ETag, after.ETag);
        Assert.AreEqual(before.GetString(nameof(Invoice.LifecycleData)), after.GetString(nameof(Invoice.LifecycleData)));
    }

    private static PriorDomain LifecycleV2Domain(string partition, string id)
    {
        IServiceProvider services = host!.Services;
        GenericDomainRepository<PriorHistory, PriorRevision> histories = new(
            () => new(new(() => services.GetRequiredService<IRepository<PriorRevision>>()),
                new(() => services.GetRequiredService<IRepository<PriorPart>>()), []),
            new(() => services.GetRequiredService<IRepository<PriorRevision>>()));
        PriorDomain result = new(services.GetRequiredService<IOptions<BillingOptions>>(),
            new(() => services.GetRequiredService<IRepository<PriorInvoice>>()), [], histories,
            services.GetRequiredService<IRepository<InvoiceItem>>(), TimeProvider.System);
        result.Initialize(partition, id);
        return result;
    }
}
