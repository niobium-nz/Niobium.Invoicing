using Microsoft.Extensions.DependencyInjection;
using Niobium.Invoicing.Flows;
using Niobium.Invoicing.History;

namespace Niobium.Invoicing.Integration.Tests;

public sealed partial class StorageFlowTests
{
    [TestMethod]
    public async Task Actual_adapter_history_pagination_returns_verified_previous_lines_without_writes()
    {
        IssueInvoiceRequest request = await NewRequest();
        IssueFlow issue = host!.Services.GetRequiredService<IssueFlow>();
        await issue.RunAsync(request, null, default);
        request.InvoiceItems[0].Quantity = 2;
        await issue.RunAsync(request, null, default);
        IRepository<Invoice> invoices = host.Services.GetRequiredService<IRepository<Invoice>>();
        Invoice before = (await invoices.RetrieveAsync(Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID)))!;
        InvoiceLifecycleFlow reader = host.Services.GetRequiredService<InvoiceLifecycleFlow>();
        List<InvoiceHistoryEntry> result = [];
        string? cursor = null;
        do
        {
            Assert.IsLessThan(10, result.Count, "Synthetic committed history must remain bounded.");
            InvoiceHistoryPage page = await reader.GetHistoryAsync(request.BillerID, request.InvoiceID, request.Tenant, cursor);
            Assert.IsNotNull(page.Entry);
            result.Add(page.Entry);
            cursor = page.Continuation;
        } while (cursor != null);
        Assert.AreEqual(4, result.Count);
        Assert.AreEqual(2, result[0].Snapshot.Lines.Single().Quantity);
        Assert.AreEqual(1, result[2].Snapshot.Lines.Single().Quantity);
        Assert.AreEqual(23000, result[0].Snapshot.Invoice.GrandTotalCents);
        Assert.AreEqual(11500, result[2].Snapshot.Invoice.GrandTotalCents);
        Assert.AreEqual(before.ReadLifecycle()!.HistoryRevision, result[0].ID);
        Assert.IsTrue(result.All(entry => entry.Snapshot.Invoice.Token == null));
        Invoice after = (await invoices.RetrieveAsync(Invoice.BuildPartitionKey(request.BillerID), Invoice.BuildRowKey(request.InvoiceID)))!;
        Assert.AreEqual(before.ETag, after.ETag);
        Assert.AreEqual(before.LifecycleData, after.LifecycleData);
    }
}
