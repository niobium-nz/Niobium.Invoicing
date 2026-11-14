using System.Text.Json.Nodes;
using Azure.Data.Tables;
using Niobium.Invoicing.Testing;

namespace Niobium.Invoicing.Integration.Tests;

[TestClass]
public sealed class InvoiceCheckpointTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void KnownRepresentations_PreserveStateAssertionsAndOwnedFixtureEvidence(bool lifecycle)
    {
        TableEntity invoice = Fixture(lifecycle);
        Assert.AreEqual(PreparationCheckpoint.Ready, InvoiceCheckpoint.ReadActive(invoice));
        string? before = invoice.GetString("LifecycleData");
        InvoiceCheckpoint.FreezeOwnedFixture(invoice, "synthetic-link-token");
        Assert.AreEqual(PreparationCheckpoint.Frozen, InvoiceCheckpoint.ReadActive(invoice));
        Assert.AreEqual("synthetic-link-token", invoice.GetString(nameof(Invoice.Token)));
        Assert.AreEqual("unchanged", invoice.GetString("UnrelatedColumn"));
        if (lifecycle)
        {
            JsonObject original = JsonNode.Parse(before!)!.AsObject();
            JsonObject updated = JsonNode.Parse(invoice.GetString("LifecycleData")!)!.AsObject();
            Assert.AreNotEqual(original["WriteID"]!.GetValue<string>(), updated["WriteID"]!.GetValue<string>());
            original.Remove("WriteID");
            updated.Remove("WriteID");
            original["Preparation"] = 4;
            Assert.IsTrue(JsonNode.DeepEquals(original, updated), "Only preparation and the write identity may change in the synthetic fixture.");
            Assert.AreEqual("lifecycle-v1", invoice.GetString(nameof(Invoice.IssuanceState)));
        }
    }

    [TestMethod]
    [DataRow("unknown-checkpoint")]
    [DataRow("future-version")]
    [DataRow("unknown-preparation")]
    [DataRow("missing-data")]
    [DataRow("bad-json")]
    [DataRow("missing-write")]
    [DataRow("bad-shape")]
    [DataRow("oversized")]
    [DataRow("legacy-with-new-data")]
    public void MalformedOrUnreviewedRepresentations_CannotPassVerification(string corruption)
    {
        TableEntity invoice = Fixture(true);
        JsonObject data = JsonNode.Parse(invoice.GetString("LifecycleData")!)!.AsObject();
        switch (corruption)
        {
            case "unknown-checkpoint":
                invoice[nameof(Invoice.IssuanceState)] = "lifecycle-v2";
                break;
            case "future-version":
                data["Version"] = 3;
                break;
            case "unknown-preparation":
                data["Preparation"] = 99;
                break;
            case "missing-data":
                invoice.Remove("LifecycleData");
                break;
            case "bad-json":
                invoice["LifecycleData"] = "{";
                break;
            case "missing-write":
                data.Remove("WriteID");
                break;
            case "bad-shape":
                data["Version"] = "1";
                break;
            case "oversized":
                invoice["LifecycleData"] = new string(' ', 32769);
                break;
            case "legacy-with-new-data":
                invoice[nameof(Invoice.IssuanceState)] = "ready-v1";
                break;
        }
        if (corruption is "future-version" or "unknown-preparation" or "missing-write" or "bad-shape")
            invoice["LifecycleData"] = data.ToJsonString();
        Assert.ThrowsExactly<InvalidOperationException>(() => InvoiceCheckpoint.ReadActive(invoice));
        Assert.ThrowsExactly<InvalidOperationException>(() => InvoiceCheckpoint.FreezeOwnedFixture(invoice, "synthetic-link-token"));
        Assert.IsFalse(invoice.ContainsKey(nameof(Invoice.Token)));
    }

    [TestMethod]
    [DataRow(3)]
    [DataRow(4)]
    public void CancelledReadyOrFrozenRow_CannotSatisfyActiveIssuanceAssertions(int preparation)
    {
        TableEntity invoice = Fixture(true);
        JsonObject data = JsonNode.Parse(invoice.GetString("LifecycleData")!)!.AsObject();
        data["Preparation"] = preparation;
        data["Cancellation"] = new JsonObject
        {
            ["Operation"] = Guid.NewGuid().ToString(),
            ["Actor"] = Guid.NewGuid().ToString(),
            ["Reason"] = "Synthetic cancellation",
            ["At"] = "2026-11-14T12:00:00Z",
        };
        invoice["LifecycleData"] = data.ToJsonString();
        Assert.ThrowsExactly<InvalidOperationException>(() => InvoiceCheckpoint.ReadActive(invoice));
        Assert.ThrowsExactly<InvalidOperationException>(() => InvoiceCheckpoint.FreezeOwnedFixture(invoice, "synthetic-link-token"));
        Assert.IsFalse(invoice.ContainsKey(nameof(Invoice.Token)));
    }

    [TestMethod]
    public void NonReadyOrCancelledFixture_CannotBeRewrittenForViewing()
    {
        TableEntity invoice = Fixture(true);
        JsonObject data = JsonNode.Parse(invoice.GetString("LifecycleData")!)!.AsObject();
        data["Cancellation"] = new JsonObject { ["Reason"] = "Synthetic cancelled fixture" };
        invoice["LifecycleData"] = data.ToJsonString();
        Assert.ThrowsExactly<InvalidOperationException>(() => InvoiceCheckpoint.FreezeOwnedFixture(invoice, "synthetic-link-token"));
        invoice = Fixture(false);
        invoice[nameof(Invoice.IssuanceState)] = "preparing-v1";
        Assert.ThrowsExactly<InvalidOperationException>(() => InvoiceCheckpoint.FreezeOwnedFixture(invoice, "synthetic-link-token"));
        Assert.IsFalse(invoice.ContainsKey(nameof(Invoice.Token)));
    }

    [TestMethod]
    public void Reviewed_history_is_readable_but_cannot_be_frozen_without_its_history_transition()
    {
        TableEntity invoice = Fixture(true);
        JsonObject data = JsonNode.Parse(invoice.GetString("LifecycleData")!)!.AsObject();
        Guid revision = Guid.NewGuid();
        data["Version"] = 2;
        data["HistoryRevision"] = revision.ToString();
        string raw = data.ToJsonString();
        invoice["LifecycleData"] = raw;
        Assert.AreEqual(PreparationCheckpoint.Ready, InvoiceCheckpoint.ReadActive(invoice));
        Assert.AreEqual(revision, InvoiceCheckpoint.ReadHistoryReference(invoice));
        Assert.AreEqual(InvoicePreparationStage.Ready, InvoiceCheckpoint.ReadEvidence(invoice).Preparation);
        int columns = invoice.Count;
        Assert.ThrowsExactly<InvalidOperationException>(() => InvoiceCheckpoint.FreezeOwnedFixture(invoice, "synthetic-token"));
        Assert.AreEqual(columns, invoice.Count);
        Assert.AreEqual(raw, invoice.GetString("LifecycleData"));
        Assert.IsFalse(invoice.ContainsKey(nameof(Invoice.Token)));
    }

    [TestMethod]
    [DataRow(1, "unexpected")]
    [DataRow(2, "missing")]
    [DataRow(2, "empty")]
    [DataRow(2, "malformed")]
    [DataRow(2, "wrong-type")]
    public void HistoryRepresentation_RejectsMissingOrIncompatibleReferences(int version, string corruption)
    {
        TableEntity invoice = Fixture(true);
        JsonObject data = JsonNode.Parse(invoice.GetString("LifecycleData")!)!.AsObject();
        data["Version"] = version;
        if (corruption != "missing")
            data["HistoryRevision"] = corruption switch
            {
                "empty" => JsonValue.Create(Guid.Empty.ToString()),
                "malformed" => JsonValue.Create("not-a-revision"),
                "wrong-type" => JsonValue.Create(42),
                _ => JsonValue.Create(Guid.NewGuid().ToString())
            };
        invoice["LifecycleData"] = data.ToJsonString();
        Assert.ThrowsExactly<InvalidOperationException>(() => InvoiceCheckpoint.ReadActive(invoice));
        Assert.ThrowsExactly<InvalidOperationException>(() => InvoiceCheckpoint.ReadEvidence(invoice));
        Assert.ThrowsExactly<InvalidOperationException>(() => InvoiceCheckpoint.FreezeOwnedFixture(invoice, "synthetic-token"));
    }

    private static TableEntity Fixture(bool lifecycle)
    {
        TableEntity invoice = new("synthetic-biller", "synthetic-invoice")
        {
            [nameof(Invoice.IssuanceState)] = lifecycle ? "lifecycle-v1" : "ready-v1",
            ["UnrelatedColumn"] = "unchanged",
        };
        if (lifecycle)
            invoice["LifecycleData"] = new JsonObject
            {
                ["Version"] = 1,
                ["WriteID"] = Guid.NewGuid().ToString("N"),
                ["Preparation"] = 3,
                ["PaymentHistory"] = 1,
                ["Delivery"] = 1,
                ["Reconciliations"] = new JsonArray(new JsonObject { ["Evidence"] = "synthetic-review-reference" }),
                ["Cancellation"] = null,
            }.ToJsonString();
        return invoice;
    }
}
