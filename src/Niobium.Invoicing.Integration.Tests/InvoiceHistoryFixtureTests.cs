using Azure;
using Azure.Data.Tables;
using Moq;
using Niobium.Invoicing.Testing;

namespace Niobium.Invoicing.Integration.Tests;

[TestClass]
public sealed class InvoiceHistoryFixtureTests
{
    private readonly Guid biller = Guid.NewGuid(), customer = Guid.NewGuid(), revision = Guid.NewGuid();
    private readonly long invoice = Invoice.ParseID(DateTimeOffset.UtcNow);

    private TableEntity Owned() => new(Invoice.BuildPartitionKey(biller), Invoice.BuildRowKey(invoice))
    {
        ETag = new ETag("owned-invoice-version"),
        [nameof(Invoice.Billee)] = customer
    };

    private TableEntity Row(bool part) => new(biller.ToString(), part ? "opaque-model-owned-part-id" : revision.ToString())
    {
        ETag = new ETag("owned-history-version"),
        ["Version"] = 1,
        ["InvoiceCreated"] = Invoice.ParseID(invoice),
        ["Revision"] = revision
    };

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
    public async Task Cleanup_scopes_both_queries_to_the_owned_invoice_and_uses_observed_versions()
    {
        TableEntity root = Row(false), part = Row(true);
        Mock<TableClient> roots = Table(root), parts = Table(part);
        await InvoiceHistoryFixture.RemoveAsync(roots.Object, parts.Object, Owned(), biller, invoice, customer);
        foreach (Mock<TableClient> table in new[] { roots, parts })
            table.Verify(t => t.QueryAsync<TableEntity>(It.Is<string>(filter => filter.Contains(biller.ToString()) && filter.Contains("InvoiceCreated eq")),
                100, It.Is<IEnumerable<string>>(fields => !fields.Contains("Content")), It.IsAny<CancellationToken>()), Times.Once);
        roots.Verify(t => t.DeleteEntityAsync(root.PartitionKey, root.RowKey, root.ETag, It.IsAny<CancellationToken>()), Times.Once);
        parts.Verify(t => t.DeleteEntityAsync(part.PartitionKey, part.RowKey, part.ETag, It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    [DataRow("biller")]
    [DataRow("invoice")]
    [DataRow("version")]
    [DataRow("etag")]
    [DataRow("revision")]
    public async Task Unowned_or_unsupported_child_prevents_all_history_deletions(string corruption)
    {
        TableEntity root = Row(false), part = Row(true);
        switch (corruption)
        {
            case "biller":
                part.PartitionKey = Guid.NewGuid().ToString();
                break;
            case "invoice":
                part["InvoiceCreated"] = Invoice.ParseID(invoice).AddDays(1);
                break;
            case "version":
                part["Version"] = 2;
                break;
            case "etag":
                part.ETag = ETag.All;
                break;
            case "revision":
                part["Revision"] = Guid.Empty;
                break;
        }
        Mock<TableClient> roots = Table(root), parts = Table(part);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => InvoiceHistoryFixture.RemoveAsync(roots.Object, parts.Object, Owned(), biller, invoice, customer));
        roots.Verify(t => t.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()), Times.Never);
        parts.Verify(t => t.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Absent_history_tables_are_tolerated_only_before_the_history_rollout()
    {
        Mock<TableClient> absent = new(MockBehavior.Strict);
        absent.Setup(t => t.QueryAsync<TableEntity>(It.IsAny<string>(), 100, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Throws(new RequestFailedException(404, "Synthetic table absence", "TableNotFound", null));
        await InvoiceHistoryFixture.RemoveAsync(absent.Object, absent.Object, Owned(), biller, invoice, customer);
        TableEntity versioned = Owned();
        versioned[nameof(Invoice.IssuanceState)] = "lifecycle-v1";
        versioned["LifecycleData"] = "{\"Version\":2,\"Preparation\":3,\"WriteID\":\"" + Guid.NewGuid().ToString("N")
            + "\",\"HistoryRevision\":\"" + revision.ToString() + "\"}";
        await Assert.ThrowsExactlyAsync<RequestFailedException>(() => InvoiceHistoryFixture.RemoveAsync(absent.Object, absent.Object, versioned, biller, invoice, customer));
    }

    [TestMethod]
    public async Task Permission_failure_or_missing_owner_evidence_is_not_treated_as_absence()
    {
        Mock<TableClient> denied = new(MockBehavior.Strict);
        denied.Setup(t => t.QueryAsync<TableEntity>(It.IsAny<string>(), 100, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Throws(new RequestFailedException(403, "Synthetic denial"));
        await Assert.ThrowsExactlyAsync<RequestFailedException>(() => InvoiceHistoryFixture.RemoveAsync(denied.Object, denied.Object, Owned(), biller, invoice, customer));
        Mock<TableClient> untouched = new(MockBehavior.Strict);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => InvoiceHistoryFixture.RemoveAsync(untouched.Object, untouched.Object, Owned(), biller, invoice, Guid.NewGuid()));
        untouched.VerifyNoOtherCalls();
    }
}
