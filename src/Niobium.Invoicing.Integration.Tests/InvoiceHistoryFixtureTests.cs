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
        bool parentDeleted = false;
        await InvoiceHistoryFixture.RemoveAsync(roots.Object, parts.Object, Anchored(), biller, invoice, customer, () =>
        {
            roots.Verify(t => t.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()), Times.Never);
            parts.Verify(t => t.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()), Times.Never);
            parentDeleted = true;
            return Task.CompletedTask;
        });
        Assert.IsTrue(parentDeleted);
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
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => InvoiceHistoryFixture.RemoveAsync(roots.Object, parts.Object, Owned(), biller, invoice, customer, () => Task.CompletedTask));
        roots.Verify(t => t.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()), Times.Never);
        parts.Verify(t => t.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Absent_history_tables_are_tolerated_only_before_the_history_rollout()
    {
        Mock<TableClient> absent = new(MockBehavior.Strict);
        absent.Setup(t => t.QueryAsync<TableEntity>(It.IsAny<string>(), 100, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Throws(new RequestFailedException(404, "Synthetic table absence", "TableNotFound", null));
        await InvoiceHistoryFixture.RemoveAsync(absent.Object, absent.Object, Owned(), biller, invoice, customer, () => Task.CompletedTask);
        TableEntity versioned = Owned();
        versioned[nameof(Invoice.IssuanceState)] = "lifecycle-v1";
        versioned["LifecycleData"] = "{\"Version\":2,\"Preparation\":3,\"WriteID\":\"" + Guid.NewGuid().ToString("N")
            + "\",\"HistoryRevision\":\"" + revision.ToString() + "\"}";
        await Assert.ThrowsExactlyAsync<RequestFailedException>(() => InvoiceHistoryFixture.RemoveAsync(absent.Object, absent.Object, versioned, biller, invoice, customer, () => Task.CompletedTask));
    }

    private TableEntity Anchored()
    {
        TableEntity owned = Owned();
        owned[nameof(Invoice.IssuanceState)] = "lifecycle-v1";
        owned["LifecycleData"] = "{\"Version\":2,\"Preparation\":3,\"WriteID\":\"" + Guid.NewGuid().ToString("N")
            + "\",\"HistoryRevision\":\"" + revision.ToString() + "\"}";
        return owned;
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Empty_or_miskeyed_history_cannot_delete_a_parent_that_references_a_revision(bool wrongRevision)
    {
        TableEntity root = Row(false);
        root.RowKey = Guid.NewGuid().ToString();
        Mock<TableClient> roots = wrongRevision ? Table(root) : Table();
        Mock<TableClient> parts = Table();
        bool deleted = false;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => InvoiceHistoryFixture.RemoveAsync(
            roots.Object, parts.Object, Anchored(), biller, invoice, customer, () => { deleted = true; return Task.CompletedTask; }));
        Assert.IsFalse(deleted);
        roots.Verify(t => t.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()), Times.Never);
        parts.Verify(t => t.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    [DataRow("unknown-checkpoint")]
    [DataRow("legacy-with-lifecycle")]
    [DataRow("missing-checkpoint-with-lifecycle")]
    [DataRow("empty-checkpoint")]
    public async Task Unsupported_root_prevents_all_queries_and_the_parent_deletion_callback(string corruption)
    {
        TableEntity owned = Owned();
        if (corruption != "missing-checkpoint-with-lifecycle")
            owned[nameof(Invoice.IssuanceState)] = corruption == "legacy-with-lifecycle" ? "ready-v1"
                : corruption == "empty-checkpoint" ? "" : "future-checkpoint";
        if (corruption.Contains("lifecycle", StringComparison.Ordinal))
            owned["LifecycleData"] = "{}";
        Mock<TableClient> untouched = new(MockBehavior.Strict);
        bool deleted = false;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => InvoiceHistoryFixture.RemoveAsync(
            untouched.Object, untouched.Object, owned, biller, invoice, customer, () => { deleted = true; return Task.CompletedTask; }));
        Assert.IsFalse(deleted);
        untouched.VerifyNoOtherCalls();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task A_failed_or_uncertain_parent_delete_preserves_all_history(bool uncertain)
    {
        Mock<TableClient> roots = Table(Row(false)), parts = Table(Row(true));
        Exception failure = uncertain ? new TimeoutException("Synthetic unknown parent delete")
            : new RequestFailedException(412, "Synthetic changed parent");
        Exception actual = await Assert.ThrowsAsync<Exception>(() => InvoiceHistoryFixture.RemoveAsync(
            roots.Object, parts.Object, Anchored(), biller, invoice, customer, () => Task.FromException(failure)));
        Assert.AreSame(failure, actual);
        roots.Verify(t => t.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()), Times.Never);
        parts.Verify(t => t.DeleteEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ETag>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Permission_failure_or_missing_owner_evidence_is_not_treated_as_absence()
    {
        Mock<TableClient> denied = new(MockBehavior.Strict);
        denied.Setup(t => t.QueryAsync<TableEntity>(It.IsAny<string>(), 100, It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Throws(new RequestFailedException(403, "Synthetic denial"));
        await Assert.ThrowsExactlyAsync<RequestFailedException>(() => InvoiceHistoryFixture.RemoveAsync(denied.Object, denied.Object, Owned(), biller, invoice, customer, () => Task.CompletedTask));
        Mock<TableClient> untouched = new(MockBehavior.Strict);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => InvoiceHistoryFixture.RemoveAsync(untouched.Object, untouched.Object, Owned(), biller, invoice, Guid.NewGuid(), () => Task.CompletedTask));
        untouched.VerifyNoOtherCalls();
    }
}
