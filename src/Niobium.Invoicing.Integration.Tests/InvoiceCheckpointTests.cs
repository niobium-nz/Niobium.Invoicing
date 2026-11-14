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
        Assert.AreEqual(PreparationCheckpoint.Ready, InvoiceCheckpoint.Read(invoice));
        string? before = invoice.GetString("LifecycleData");
        InvoiceCheckpoint.FreezeOwnedFixture(invoice, "synthetic-link-token");
        Assert.AreEqual(PreparationCheckpoint.Frozen, InvoiceCheckpoint.Read(invoice));
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
                data["Version"] = 2;
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
        Assert.ThrowsExactly<InvalidOperationException>(() => InvoiceCheckpoint.Read(invoice));
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
