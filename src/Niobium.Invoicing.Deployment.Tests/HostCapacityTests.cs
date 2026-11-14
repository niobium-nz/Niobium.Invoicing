using System.Net;
using Azure.Data.Tables;
using Niobium.Invoicing.History;

namespace Niobium.Invoicing.Deployment.Tests;

public sealed partial class DeployedInvoiceTests
{
    [TestMethod]
    public async Task Concurrent_oversized_line_collections_are_rejected_without_writes_and_the_host_remains_usable()
    {
        long[] ids = [await SeedInvoice(recipient: String.Empty), await SeedInvoice(recipient: String.Empty)];
        TableEntity[] before = [await Read(ids[0]), await Read(ids[1])];
        Task<HttpResponseMessage>[] requests = ids.Select(id =>
        {
            IssueInvoiceRequest input = Request(id);
            input.InvoiceItems = Enumerable.Range(0, 100_000).Select(index => new InvoiceItem
            {
                Invoice = Invoice.ParseID(id), ID = index, Subject = itemSubject,
                UnitPriceCurrency = "NZD", LineTotalCurrency = "NZD", Quantity = 1
            }).ToList();
            return Post(input, Token(biller, tenant));
        }).ToArray();
        try
        {
            HttpResponseMessage[] responses = await Task.WhenAll(requests);
            foreach (HttpResponseMessage response in responses)
                Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            foreach (Task<HttpResponseMessage> request in requests)
                if (request.IsCompletedSuccessfully)
                    request.Result.Dispose();
        }
        for (int i = 0; i < ids.Length; i++)
            await AssertRejectedRequestLeftNoHistory(ids[i], before[i]);

        // A normal real invoice write must still work after both rejected bodies.
        using HttpResponseMessage normal = await Post(Request(ids[0]), Token(biller, tenant));
        Assert.AreEqual(HttpStatusCode.OK, normal.StatusCode);
    }

    private static async Task AssertRejectedRequestLeftNoHistory(long id, TableEntity before)
    {
        TableEntity after = await Read(id);
        Assert.AreEqual(before.ETag, after.ETag);
        Assert.IsFalse(after.ContainsKey(nameof(Invoice.LifecycleData)));
        foreach (string table in new[] { nameof(InvoiceRevision), nameof(InvoiceRevisionPart) })
            await foreach (TableEntity unused in tables.GetTableClient(table).QueryAsync<TableEntity>(
                TableClient.CreateQueryFilter($"PartitionKey eq {biller.ToString()} and InvoiceCreated eq {Invoice.ParseID(id)}")))
                Assert.Fail("Rejected input must not leave a history revision or part.");
    }
}
