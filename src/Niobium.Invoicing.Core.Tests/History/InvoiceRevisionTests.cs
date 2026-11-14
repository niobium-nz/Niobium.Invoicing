using System.Reflection;
using System.Text;
using Niobium.Invoicing.Core.Tests.Support;
using Niobium.Invoicing.History;

namespace Niobium.Invoicing.Core.Tests.History;

[TestClass]
public sealed class InvoiceRevisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);

    internal static InvoiceRevision.Prepared Example(int descriptionLength = 0)
    {
        IssuanceScenario scenario = new();
        Invoice invoice = Invoice.BuildNew(scenario.Request.InvoiceID, scenario.Biller, scenario.Billee);
        invoice.Token = "synthetic-link-credential-never-retained";
        invoice.ETag = "synthetic-storage-version-never-retained";
        invoice.Timestamp = Now;
        invoice.StoreLifecycle(InvoiceLifecycleSnapshot.Create(InvoicePreparationStage.Ready) with
        {
            PaymentHistory = InvoicePaymentHistory.NoPayments
        });
        scenario.Request.InvoiceItems[0].Description = new String('x', descriptionLength);
        scenario.Request.InvoiceItems[0].ETag = "synthetic-item-version-never-retained";
        scenario.Request.InvoiceItems[0].UpdatedAt = Now;
        return InvoiceRevision.Capture(invoice, scenario.Request.InvoiceItems, InvoiceLineEvidence.Verified,
            new(InvoiceChangeKind.PreparationCompleted, InvoiceChangeAuthority.AuthenticatedBiller,
                invoice.Biller.ToString(), "Synthetic approved edit", Now));
    }

    internal static T Change<T>(T value, string property, object? replacement) where T : class, new()
    {
        T result = new();
        foreach (PropertyInfo field in typeof(T).GetProperties())
            field.SetValue(result, field.GetValue(value));
        typeof(T).GetProperty(property)!.SetValue(result, replacement);
        return result;
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(60000)]
    public void Snapshot_round_trip_keeps_business_history_and_excludes_link_credentials_and_storage_metadata(int size)
    {
        InvoiceRevision.Prepared prepared = Example(size);
        InvoiceRevisionSnapshot snapshot = prepared.Revision.ReadSnapshot(prepared.Parts);
        Assert.AreEqual(prepared.Revision.Biller, snapshot.Invoice.Biller);
        Assert.AreEqual(InvoicePreparationStage.Ready, snapshot.Preparation);
        Assert.AreEqual(InvoicePaymentHistory.NoPayments, snapshot.Lifecycle!.PaymentHistory);
        Assert.AreEqual(InvoiceLineEvidence.Verified, snapshot.LineEvidence);
        Assert.AreEqual(size, snapshot.Lines[0].Description!.Length);
        Assert.IsNull(snapshot.Invoice.Token);
        Assert.IsNull(snapshot.Invoice.ETag);
        Assert.IsNull(snapshot.Invoice.Timestamp);
        Assert.IsNull(snapshot.Lines[0].ETag);
        Assert.IsNull(snapshot.Lines[0].UpdatedAt);
        string raw = Encoding.UTF8.GetString(prepared.Parts.SelectMany(p => Convert.FromBase64String(p.Content)).ToArray());
        Assert.IsFalse(raw.Contains("synthetic-link-credential", StringComparison.Ordinal));
        Assert.IsFalse(raw.Contains("synthetic-storage-version", StringComparison.Ordinal));
        Assert.IsFalse(raw.Contains("synthetic-item-version", StringComparison.Ordinal));
        snapshot.Invoice.Terms = "caller mutation";
        snapshot.Lines[0].Subject = "caller mutation";
        InvoiceRevisionSnapshot reread = prepared.Revision.ReadSnapshot(prepared.Parts);
        Assert.AreNotEqual(snapshot.Invoice.Terms, reread.Invoice.Terms);
        Assert.AreNotEqual(snapshot.Lines[0].Subject, reread.Lines[0].Subject);
        Assert.AreEqual(prepared.Revision.GetKey(), InvoiceRevision.Key(prepared.Revision.Biller, prepared.Revision.ID));
        Assert.IsTrue(prepared.Revision.Describes(snapshot.Invoice.Biller, snapshot.Invoice.Created));
        Assert.IsFalse(prepared.Revision.Describes(Guid.NewGuid(), snapshot.Invoice.Created));
        Assert.AreEqual(prepared.Parts[0].ID, prepared.Revision.GetPartKey(0).RowKey);
    }

    [TestMethod]
    public void Capture_is_detached_from_its_mutable_inputs_and_binds_the_previous_revision()
    {
        IssuanceScenario scenario = new();
        Invoice invoice = Invoice.BuildNew(scenario.Request.InvoiceID, scenario.Biller, scenario.Billee);
        invoice.Token = "synthetic-original-token";
        Guid previous = Guid.NewGuid();
        InvoiceRevision.Prepared prepared = InvoiceRevision.Capture(invoice, scenario.Request.InvoiceItems,
            InvoiceLineEvidence.Proposed, new(InvoiceChangeKind.PreparationStarted,
                InvoiceChangeAuthority.TrustedQueue, "IssueInvoiceCommandConsumer", "Invoice preparation requested", Now), previous);
        invoice.BilleeName = "later customer";
        scenario.Request.InvoiceItems[0].Quantity = 999;
        Assert.AreEqual("synthetic-original-token", invoice.Token);
        InvoiceRevisionSnapshot captured = prepared.Revision.ReadSnapshot(prepared.Parts);
        Assert.AreEqual("Synthetic customer", captured.Invoice.BilleeName);
        Assert.AreEqual(1, captured.Lines[0].Quantity);
        Assert.IsNull(captured.Lifecycle);
        Assert.AreEqual(previous, prepared.Revision.Previous);
        Assert.AreEqual(InvoiceChangeAuthority.TrustedQueue, prepared.Revision.ReadChange().Authority);
    }

    [TestMethod]
    [DataRow("Version", 9)]
    [DataRow("PartCount", 0)]
    [DataRow("PartCount", Int32.MaxValue)]
    [DataRow("ChangeCode", 999)]
    [DataRow("AuthorityCode", 999)]
    [DataRow("Actor", " ")]
    [DataRow("Reason", " ")]
    [DataRow("ContentDigest", "invalid")]
    [DataRow("ContentDigest", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Corrupt_or_unknown_revision_metadata_never_becomes_a_valid_snapshot(string field, object value)
    {
        InvoiceRevision.Prepared prepared = Example();
        InvoiceRevision corrupt = Change(prepared.Revision, field, value);
        Assert.ThrowsExactly<InvalidOperationException>(() => corrupt.ReadSnapshot(prepared.Parts));
    }

    [TestMethod]
    public void Validly_shaped_but_changed_manifest_evidence_is_rejected()
    {
        InvoiceRevision.Prepared prepared = Example();
        foreach ((string field, object value) in new (string, object)[]
        {
            ("Biller", Guid.NewGuid()), ("ID", Guid.NewGuid()), ("Previous", Guid.NewGuid()),
            ("InvoiceCreated", prepared.Revision.InvoiceCreated.AddDays(-1)), ("RecordedAt", Now.AddDays(-1)),
            ("Reason", "Changed reason"), ("Actor", "another actor"),
            ("ChangeCode", (int)InvoiceChangeKind.Cancelled), ("AuthorityCode", (int)InvoiceChangeAuthority.FunctionKey)
        })
            Assert.ThrowsExactly<InvalidOperationException>(() => Change(prepared.Revision, field, value).ReadSnapshot(prepared.Parts), field);
        Assert.ThrowsExactly<InvalidOperationException>(() => Change(prepared.Revision, "Previous", prepared.Revision.ID).ReadChange());
    }

    [TestMethod]
    [DataRow("Version", 2)]
    [DataRow("Index", 1)]
    [DataRow("ID", "another-revision-part")]
    [DataRow("Content", "not base64")]
    [DataRow("Content", "")]
    [DataRow("Content", "eA==")]
    public void Malformed_or_changed_parts_are_rejected(string field, object value)
    {
        InvoiceRevision.Prepared prepared = Example();
        InvoiceRevisionPart corrupt = Change(prepared.Parts[0], field, value);
        Assert.ThrowsExactly<InvalidOperationException>(() => prepared.Revision.ReadSnapshot([corrupt]));
    }

    [TestMethod]
    public void Missing_reordered_foreign_and_oversized_parts_are_rejected()
    {
        InvoiceRevision.Prepared prepared = Example(60000);
        Assert.IsGreaterThan(1, prepared.Parts.Count);
        Assert.ThrowsExactly<InvalidOperationException>(() => prepared.Revision.ReadSnapshot(prepared.Parts.Skip(1).ToArray()));
        Assert.ThrowsExactly<InvalidOperationException>(() => prepared.Revision.ReadSnapshot(prepared.Parts.Reverse().ToArray()));
        foreach ((string field, object value) in new (string, object)[]
        {
            ("Biller", Guid.NewGuid()), ("Revision", Guid.NewGuid()),
            ("Content", new String('A', 64 * 1024)), ("Content", "eA=="), ("Content", prepared.Parts[0].Content + "\n")
        })
        {
            InvoiceRevisionPart[] parts = prepared.Parts.ToArray();
            parts[0] = Change(parts[0], field, value);
            Assert.ThrowsExactly<InvalidOperationException>(() => prepared.Revision.ReadSnapshot(parts), field);
        }
        InvoiceRevisionPart[] missing = prepared.Parts.ToArray();
        missing[0] = null!;
        Assert.ThrowsExactly<InvalidOperationException>(() => prepared.Revision.ReadSnapshot(missing));
        Assert.ThrowsExactly<InvalidOperationException>(() => prepared.Revision.GetPartKey(-1));
        Assert.ThrowsExactly<InvalidOperationException>(() => prepared.Revision.GetPartKey(prepared.Parts.Count));
        Assert.ThrowsExactly<InvalidOperationException>(() => InvoiceRevision.Key(Guid.Empty, prepared.Revision.ID));
    }

    [TestMethod]
    public void Capture_rejects_ambiguous_identity_and_unbounded_or_missing_actor_evidence()
    {
        IssuanceScenario scenario = new();
        Invoice invoice = Invoice.BuildNew(scenario.Request.InvoiceID, scenario.Biller, scenario.Billee);
        InvoiceChange valid = new(InvoiceChangeKind.PreparationStarted, InvoiceChangeAuthority.UnattributedCoreCaller,
            "Core", "Invoice preparation requested", Now);
        foreach (InvoiceChange change in new[]
        {
            valid with { Actor = "" }, valid with { Actor = new String('a', 257) },
            valid with { Reason = "" }, valid with { Reason = new String('a', 501) },
            valid with { Actor = " padded " }, valid with { Reason = " padded " },
            valid with { At = default }, valid with { Kind = (InvoiceChangeKind)999 }
        })
            Assert.ThrowsExactly<InvalidOperationException>(() => InvoiceRevision.Capture(invoice, scenario.Request.InvoiceItems, InvoiceLineEvidence.Proposed, change));
        Assert.ThrowsExactly<InvalidOperationException>(() => InvoiceRevision.Capture(invoice,
            [scenario.Request.InvoiceItems[0], scenario.Request.InvoiceItems[0]], InvoiceLineEvidence.Proposed, valid));
        scenario.Request.InvoiceItems[0].Invoice = invoice.Created.AddMinutes(1);
        Assert.ThrowsExactly<InvalidOperationException>(() => InvoiceRevision.Capture(invoice, scenario.Request.InvoiceItems, InvoiceLineEvidence.Proposed, valid));
    }
}
