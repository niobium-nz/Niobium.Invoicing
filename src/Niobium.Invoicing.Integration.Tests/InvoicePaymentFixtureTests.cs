using Azure;
using Azure.Data.Tables;
using Moq;
using Niobium.Invoicing.Testing;

namespace Niobium.Invoicing.Integration.Tests;

[TestClass]
public sealed class InvoicePaymentFixtureTests
{
    private static Mock<TableClient> Table(params TableEntity[] rows)
    {
        Mock<TableClient> client = new(MockBehavior.Strict);
        client.Setup(t => t.QueryAsync<TableEntity>(It.IsAny<string>(), 100, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(AsyncPageable<TableEntity>.FromPages([Page<TableEntity>.FromValues(rows, null, Mock.Of<Response>())]));
        foreach (TableEntity row in rows)
            client.Setup(t => t.DeleteEntityAsync(row.PartitionKey, row.RowKey, row.ETag, It.IsAny<CancellationToken>())).ReturnsAsync(Mock.Of<Response>());
        return client;
    }

    [TestMethod]
    public async Task Cleanup_validates_all_owned_records_before_conditionally_deleting_parent_then_children()
    {
        PaymentFixtureRows rows = new();
        Mock<TableClient> receipts = Table(rows.Receipt), postings = Table(rows.Posting), indexes = Table(rows.Index);
        bool parentDeleted = false;
        await InvoicePaymentFixture.RemoveAsync(receipts.Object, postings.Object, indexes.Object, rows.Owned,
            rows.Biller, rows.Invoice, rows.Customer, () =>
            {
                foreach (Mock<TableClient> table in new[] { receipts, postings, indexes })
                    VerifyNoDeletes(table);
                parentDeleted = true;
                return Task.CompletedTask;
            });
        Assert.IsTrue(parentDeleted);
        foreach ((Mock<TableClient> table, TableEntity row) in new[] { (receipts, rows.Receipt), (postings, rows.Posting), (indexes, rows.Index) })
        {
            table.Verify(t => t.QueryAsync<TableEntity>(It.Is<string>(filter => filter.Contains(rows.Biller.ToString()) && filter.Contains("InvoiceCreated eq")),
                100, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()), Times.Once);
            table.Verify(t => t.DeleteEntityAsync(row.PartitionKey, row.RowKey, row.ETag, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [TestMethod]
    [DataRow("foreign-biller")]
    [DataRow("foreign-invoice")]
    [DataRow("foreign-tenant")]
    [DataRow("receipt-evidence")]
    [DataRow("receipt-version")]
    [DataRow("receipt-etag")]
    [DataRow("posting-balance")]
    [DataRow("posting-index")]
    [DataRow("posting-version")]
    [DataRow("index-count")]
    [DataRow("index-entry")]
    [DataRow("index-version")]
    [DataRow("missing-receipt")]
    [DataRow("missing-posting")]
    [DataRow("missing-index")]
    public async Task Unowned_missing_or_altered_evidence_prevents_parent_and_all_child_deletions(string corruption)
    {
        PaymentFixtureRows rows = new();
        switch (corruption)
        {
            case "foreign-biller":
                rows.Receipt.PartitionKey = Guid.NewGuid().ToString();
                break;
            case "foreign-invoice":
                rows.Receipt["InvoiceCreated"] = rows.Created.AddDays(1);
                break;
            case "foreign-tenant":
                rows.Receipt["Tenant"] = Guid.NewGuid();
                break;
            case "receipt-evidence":
                rows.Receipt["Evidence"] = "changed evidence";
                break;
            case "receipt-version":
                rows.Receipt["Version"] = 2;
                break;
            case "receipt-etag":
                rows.Receipt.ETag = ETag.All;
                break;
            case "posting-balance":
                rows.Posting["SettledCents"] = 101L;
                break;
            case "posting-index":
                rows.Posting["Index"] = "v1-" + new string('a', 64);
                break;
            case "posting-version":
                rows.Posting["Version"] = 2;
                break;
            case "index-count":
                rows.Index["Entries"] = 2L;
                break;
            case "index-entry":
                rows.Index["EntryData"] = "{}";
                break;
            case "index-version":
                rows.Index["Version"] = 2;
                break;
        }
        Mock<TableClient> receipts = Table(corruption == "missing-receipt" ? [] : [rows.Receipt]);
        Mock<TableClient> postings = Table(corruption == "missing-posting" ? [] : [rows.Posting]);
        Mock<TableClient> indexes = Table(corruption == "missing-index" ? [] : [rows.Index]);
        bool parentDeleted = false;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => InvoicePaymentFixture.RemoveAsync(
            receipts.Object, postings.Object, indexes.Object, rows.Owned, rows.Biller, rows.Invoice, rows.Customer,
            () => { parentDeleted = true; return Task.CompletedTask; }));
        Assert.IsFalse(parentDeleted);
        foreach (Mock<TableClient> table in new[] { receipts, postings, indexes })
            VerifyNoDeletes(table);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Stale_or_unknown_parent_deletion_leaves_all_payment_evidence(bool unknown)
    {
        PaymentFixtureRows rows = new();
        Mock<TableClient> receipts = Table(rows.Receipt), postings = Table(rows.Posting), indexes = Table(rows.Index);
        Exception failure = unknown ? new TimeoutException("Synthetic unknown parent delete") : new RequestFailedException(412, "Synthetic stale parent");
        Exception actual = await Assert.ThrowsAsync<Exception>(() => InvoicePaymentFixture.RemoveAsync(
            receipts.Object, postings.Object, indexes.Object, rows.Owned, rows.Biller, rows.Invoice, rows.Customer,
            () => Task.FromException(failure)));
        Assert.AreSame(failure, actual);
        foreach (Mock<TableClient> table in new[] { receipts, postings, indexes })
            VerifyNoDeletes(table);
    }

    [TestMethod]
    public async Task Missing_payment_tables_are_tolerated_only_for_an_invoice_without_payment_position()
    {
        PaymentFixtureRows rows = new();
        Mock<TableClient> absent = new(MockBehavior.Strict);
        absent.Setup(t => t.QueryAsync<TableEntity>(It.IsAny<string>(), 100, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Throws(new RequestFailedException(404, "Synthetic missing table", "TableNotFound", null));
        await Assert.ThrowsExactlyAsync<RequestFailedException>(() => InvoicePaymentFixture.RemoveAsync(absent.Object, absent.Object,
            absent.Object, rows.Owned, rows.Biller, rows.Invoice, rows.Customer, () => Task.CompletedTask));
        rows.Owned.Remove("LifecycleData");
        rows.Owned.Remove(nameof(Invoice.IssuanceState));
        await InvoicePaymentFixture.RemoveAsync(absent.Object, absent.Object, absent.Object, rows.Owned,
            rows.Biller, rows.Invoice, rows.Customer, () => Task.CompletedTask);
    }

    private static void VerifyNoDeletes(Mock<TableClient> table)
        => table.Verify(t => t.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()), Times.Never);
}
