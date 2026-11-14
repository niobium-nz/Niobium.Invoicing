using Azure;
using Moq;
using Niobium.Invoicing.History;

namespace Niobium.Invoicing.Core.Tests.History;

[TestClass]
public sealed class InvoiceHistoryDomainTests
{
    private sealed class Storage
    {
        internal readonly Mock<IRepository<InvoiceRevision>> Revisions = new(MockBehavior.Strict);
        internal readonly Mock<IRepository<InvoiceRevisionPart>> Parts = new(MockBehavior.Strict);
        internal readonly Dictionary<string, InvoiceRevisionPart> StoredParts = [];
        internal InvoiceRevision? StoredRevision;
        internal bool LosePartResponse;
        internal bool LoseRevisionResponse;
        internal bool DropPart;
        internal bool ChangeReadback;
        internal int RevisionCreates;

        internal Storage()
        {
            Parts.Setup(p => p.CreateAsync(It.IsAny<IEnumerable<InvoiceRevisionPart>>(), false, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync((IEnumerable<InvoiceRevisionPart> values, bool _, DateTimeOffset? __, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    InvoiceRevisionPart[] result = values.ToArray();
                    foreach (InvoiceRevisionPart part in result)
                    {
                        if (!StoredParts.TryAdd(part.ID, part)) throw new RequestFailedException(409, "Synthetic duplicate history part");
                        if (LosePartResponse) throw new TimeoutException("Synthetic unknown history-part write");
                    }
                    return result;
                });
            Revisions.Setup(p => p.CreateAsync(It.IsAny<IEnumerable<InvoiceRevision>>(), false, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync((IEnumerable<InvoiceRevision> values, bool _, DateTimeOffset? __, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    if (StoredRevision != null) throw new RequestFailedException(409, "Synthetic duplicate revision");
                    StoredRevision = InvoiceRevisionTests.Change(values.Single(), "ETag", "observed-revision-version");
                    RevisionCreates++;
                    if (LoseRevisionResponse) throw new TimeoutException("Synthetic unknown revision write");
                    return new[] { StoredRevision };
                });
            Revisions.Setup(p => p.RetrieveAsync(It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string __, IList<string>? ___, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return ChangeReadback && StoredRevision != null
                        ? InvoiceRevisionTests.Change(StoredRevision, "Reason", "A different change") : StoredRevision;
                });
            Parts.Setup(p => p.RetrieveAsync(It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, string id, IList<string>? __, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    return DropPart ? null : StoredParts.GetValueOrDefault(id);
                });
        }

        internal InvoiceHistoryDomain Domain(InvoiceRevision revision)
        {
            InvoiceHistoryDomain domain = new(new(() => Revisions.Object), new(() => Parts.Object), []);
            domain.Initialize(revision);
            return domain;
        }
    }

    [TestMethod]
    public async Task Conditional_history_creation_reads_back_all_parts_without_update_or_upsert()
    {
        Storage storage = new();
        InvoiceRevision.Prepared prepared = InvoiceRevisionTests.Example(60000);
        InvoiceHistoryDomain domain = storage.Domain(prepared.Revision);
        await domain.RecordAsync(prepared);
        InvoiceRevisionSnapshot result = await domain.ReadSnapshotAsync();
        Assert.AreEqual(60000, result.Lines[0].Description!.Length);
        Assert.AreEqual(1, storage.RevisionCreates);
        Assert.AreEqual(prepared.Parts.Count, storage.StoredParts.Count);
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => domain.RecordAsync(prepared));
        Assert.AreEqual(1, storage.RevisionCreates);
    }

    [TestMethod]
    public async Task An_unknown_part_write_never_creates_the_revision_manifest()
    {
        Storage storage = new() { LosePartResponse = true };
        InvoiceRevision.Prepared prepared = InvoiceRevisionTests.Example(60000);
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => storage.Domain(prepared.Revision).RecordAsync(prepared));
        Assert.AreEqual(1, storage.StoredParts.Count);
        Assert.AreEqual(0, storage.RevisionCreates);
        Assert.IsNull(storage.StoredRevision);
    }

    [TestMethod]
    public async Task An_unknown_manifest_write_does_not_report_a_verified_revision()
    {
        Storage storage = new() { LoseRevisionResponse = true };
        InvoiceRevision.Prepared prepared = InvoiceRevisionTests.Example();
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => storage.Domain(prepared.Revision).RecordAsync(prepared));
        Assert.IsNotNull(storage.StoredRevision);
        Assert.AreEqual(1, storage.RevisionCreates);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Missing_parts_or_changed_readback_never_report_a_verified_revision(bool missing)
    {
        Storage storage = new() { DropPart = missing, ChangeReadback = !missing };
        InvoiceRevision.Prepared prepared = InvoiceRevisionTests.Example();
        ApplicationException error = await Assert.ThrowsExactlyAsync<ApplicationException>(() => storage.Domain(prepared.Revision).RecordAsync(prepared));
        Assert.AreEqual((int)InternalError.Conflict, error.ErrorCode);
    }

    [TestMethod]
    public async Task A_mismatched_draft_or_cancelled_operation_never_writes_history()
    {
        Storage storage = new();
        InvoiceRevision.Prepared prepared = InvoiceRevisionTests.Example();
        await Assert.ThrowsExactlyAsync<ApplicationException>(() => storage.Domain(prepared.Revision).RecordAsync(InvoiceRevisionTests.Example()));
        using CancellationTokenSource source = new();
        source.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => storage.Domain(prepared.Revision).RecordAsync(prepared, source.Token));
        Assert.AreEqual(0, storage.StoredParts.Count);
        Assert.AreEqual(0, storage.RevisionCreates);
    }
}
