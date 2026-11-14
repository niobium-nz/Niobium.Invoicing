using System.Net;
using System.Text.Json;
using Niobium.Invoicing.History;

namespace Niobium.Invoicing.Deployment.Tests;

public sealed partial class DeployedInvoiceTests
{
    [TestMethod]
    public async Task Deployed_history_reader_preserves_previous_pages_across_edits_without_writing_or_cross_invoice_reuse()
    {
        long id = await SeedInvoice(recipient: String.Empty);
        InvoiceHistoryPage legacy = await HistoryPage(id);
        Assert.IsNull(legacy.Entry);
        Assert.IsNull(legacy.Continuation);
        IssueInvoiceRequest request = Request(id);
        using (HttpResponseMessage issued = await Post(request, Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.OK, issued.StatusCode);
        InvoiceHistoryPage first = await HistoryPage(id);
        Assert.AreEqual(InvoiceChangeKind.PreparationCompleted, first.Entry!.Change.Kind);
        Assert.IsNotNull(first.Continuation);
        request.InvoiceItems[0].Quantity = 3;
        using (HttpResponseMessage edited = await Post(request, Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.OK, edited.StatusCode);
        string version = (await Read(id)).ETag.ToString();
        List<InvoiceHistoryEntry> original = [first.Entry];
        string? cursor = first.Continuation;
        do
        {
            Assert.IsLessThan(5, original.Count, "The original owned chain has exactly three revisions.");
            InvoiceHistoryPage page = await HistoryPage(id, cursor);
            Assert.IsNotNull(page.Entry);
            original.Add(page.Entry);
            cursor = page.Continuation;
        } while (cursor != null);
        Assert.AreEqual(3, original.Count);
        Assert.AreEqual(1, original[0].Snapshot.Lines.Single().Quantity);
        Assert.AreEqual(InvoiceChangeKind.ObservedBaseline, original[^1].Change.Kind);
        Assert.IsTrue(original.All(entry => entry.Snapshot.Invoice.Token == null));
        InvoiceHistoryPage latest = await HistoryPage(id);
        Assert.AreNotEqual(first.Entry.ID, latest.Entry!.ID);
        Assert.AreEqual(3, latest.Entry.Snapshot.Lines.Single().Quantity);
        using (HttpResponseMessage malformed = await LifecycleRequest(id, "history?cursor=invalid", null, Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.BadRequest, malformed.StatusCode);
        long other = await SeedInvoice(recipient: String.Empty);
        string otherVersion = (await Read(other)).ETag.ToString();
        using (HttpResponseMessage wrongInvoice = await LifecycleRequest(other, "history?cursor=" + first.Continuation, null, Token(biller, tenant)))
            Assert.AreEqual(HttpStatusCode.BadRequest, wrongInvoice.StatusCode);
        Assert.AreEqual(version, (await Read(id)).ETag.ToString());
        Assert.AreEqual(otherVersion, (await Read(other)).ETag.ToString());
    }

    private static async Task<InvoiceHistoryPage> HistoryPage(long id, string? cursor = null)
    {
        using HttpResponseMessage response = await LifecycleRequest(id,
            "history" + (cursor == null ? String.Empty : "?cursor=" + Uri.EscapeDataString(cursor)), null, Token(biller, tenant));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsTrue(response.Headers.CacheControl?.NoStore == true);
        return JsonSerializer.Deserialize<InvoiceHistoryPage>(await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }
}
