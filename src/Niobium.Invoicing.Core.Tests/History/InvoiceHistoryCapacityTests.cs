using Niobium.Invoicing.Core.Tests.Support;
using Niobium.Invoicing.History;

namespace Niobium.Invoicing.Core.Tests.History;

[TestClass]
public sealed class InvoiceHistoryCapacityTests
{
    [TestMethod]
    [DataRow("line")]
    [DataRow("invoice")]
    [DataRow("escaped")]
    [DataRow("unicode")]
    [DataRow("surrogate")]
    [DataRow("combined")]
    [DataRow("many")]
    public void Oversized_content_is_rejected_without_materializing_large_json_copies(string shape)
    {
        // Warm serializer metadata before measuring this synchronous model call.
        InvoiceRevisionTests.Example();
        IssuanceScenario scenario = new();
        Invoice invoice = Invoice.BuildNew(scenario.Request.InvoiceID, scenario.Biller, scenario.Billee);
        InvoiceItem[] lines = scenario.Request.InvoiceItems.ToArray();
        switch (shape)
        {
            case "line":
                lines[0].Description = new String('x', 17 * 1024 * 1024);
                break;
            case "invoice":
                invoice.Terms = new String('x', 17 * 1024 * 1024);
                break;
            case "escaped":
                lines[0].Description = new String('\u0001', 3 * 1024 * 1024);
                break;
            case "unicode":
                lines[0].Description = new String('\u4e00', 3 * 1024 * 1024);
                break;
            case "surrogate":
                lines[0].Description = new String('\ud800', 3 * 1024 * 1024);
                break;
            case "combined":
                lines[0].Description = new String('x', 9 * 1024 * 1024);
                InvoiceItem second = IssuanceScenario.Copy(lines[0]);
                second.ID++;
                lines = [lines[0], second];
                break;
            case "many":
                lines = Enumerable.Range(0, 100_000).Select(index => new InvoiceItem
                {
                    Invoice = invoice.Created,
                    ID = index,
                    Subject = String.Empty,
                    UnitPriceCurrency = "NZD",
                    LineTotalCurrency = "NZD"
                }).ToArray();
                break;
        }
        InvoiceChange change = new(InvoiceChangeKind.PreparationStarted,
            InvoiceChangeAuthority.AuthenticatedBiller, invoice.Biller.ToString(),
            "Synthetic capacity boundary", DateTimeOffset.UtcNow);
        long before = GC.GetAllocatedBytesForCurrentThread();
        InvoiceRevision.Prepared? result = InvoiceRevision.CaptureWithinCapacity(invoice, lines,
            InvoiceLineEvidence.Proposed, change);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.IsNull(result);
        Assert.IsLessThan(2L * 1024 * 1024, allocated,
            "Reject known oversized content before allocating full JSON strings, copies or part buffers.");
    }

    [TestMethod]
    public void Capacity_precheck_does_not_charge_excluded_credentials_storage_metadata_or_checkpoints()
    {
        IssuanceScenario scenario = new();
        Invoice invoice = Invoice.BuildNew(scenario.Request.InvoiceID, scenario.Biller, scenario.Billee);
        string excluded = new('x', 17 * 1024 * 1024);
        invoice.Token = excluded;
        invoice.ETag = excluded;
        invoice.IssuanceFingerprint = excluded;
        invoice.IssuanceDefaultPaymentInstructions = excluded;
        scenario.Request.InvoiceItems[0].ETag = excluded;
        Assert.IsFalse(InvoiceRevision.ExceedsCapacity(invoice, scenario.Request.InvoiceItems));
    }

    [TestMethod]
    public void Exact_envelope_limit_remains_authoritative_and_accepts_the_previous_boundary()
    {
        const int limit = 16 * 1024 * 1024;
        IssuanceScenario scenario = new();
        Invoice invoice = Invoice.BuildNew(scenario.Request.InvoiceID, scenario.Biller, scenario.Billee);
        InvoiceItem line = scenario.Request.InvoiceItems[0];
        line.Description = String.Empty;
        InvoiceChange change = new(InvoiceChangeKind.PreparationStarted,
            InvoiceChangeAuthority.AuthenticatedBiller, invoice.Biller.ToString(),
            "Synthetic exact boundary", new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero));
        InvoiceRevision.Prepared empty = InvoiceRevision.Capture(invoice, [line], InvoiceLineEvidence.Proposed, change);
        int overhead = empty.Parts.Sum(part => Convert.FromBase64String(part.Content).Length);
        line.Description = new String('x', limit - overhead);
        InvoiceRevision.Prepared? boundary = InvoiceRevision.CaptureWithinCapacity(invoice, [line], InvoiceLineEvidence.Proposed, change);
        Assert.IsNotNull(boundary);
        Assert.AreEqual(limit, boundary.Parts.Sum(part => Convert.FromBase64String(part.Content).Length));
        line.Description += "x";
        Assert.IsFalse(InvoiceRevision.ExceedsCapacity(invoice, [line]),
            "The lower bound does not count every envelope byte.");
        Assert.IsNull(InvoiceRevision.CaptureWithinCapacity(invoice, [line], InvoiceLineEvidence.Proposed, change));
    }
}
