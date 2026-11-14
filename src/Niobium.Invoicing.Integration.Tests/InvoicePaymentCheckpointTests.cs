using System.Text.Json.Nodes;
using Niobium.Invoicing.Testing;

namespace Niobium.Invoicing.Integration.Tests;

[TestClass]
public sealed class InvoicePaymentCheckpointTests
{
    [TestMethod]
    public void Reviewed_payment_position_preserves_history_and_cannot_be_rewritten_for_viewing()
    {
        PaymentFixtureRows rows = new();
        string before = rows.Owned.GetString("LifecycleData")!;
        Assert.AreEqual(PreparationCheckpoint.Ready, InvoiceCheckpoint.ReadActive(rows.Owned));
        Assert.AreEqual(rows.Revision, InvoiceCheckpoint.ReadHistoryReference(rows.Owned));
        InvoiceCheckpoint.PaymentPosition position = InvoiceCheckpoint.ReadPaymentPosition(rows.Owned)!;
        Assert.AreEqual(rows.Operation, position.Operation);
        Assert.AreEqual(rows.Posting.RowKey, position.Head);
        Assert.AreEqual(rows.Index.RowKey, position.Index);
        Assert.AreEqual(100, position.SettledCents);
        Assert.ThrowsExactly<InvalidOperationException>(() => InvoiceCheckpoint.FreezeOwnedFixture(rows.Owned, "synthetic-token"));
        Assert.AreEqual(before, rows.Owned.GetString("LifecycleData"));
    }

    [TestMethod]
    [DataRow("history")]
    [DataRow("payments")]
    [DataRow("head")]
    [DataRow("index")]
    [DataRow("operation")]
    [DataRow("count")]
    [DataRow("currency")]
    [DataRow("opening")]
    [DataRow("settled")]
    [DataRow("evidence")]
    [DataRow("observed")]
    [DataRow("older-version")]
    [DataRow("future-version")]
    public void Missing_incompatible_or_unanchored_payment_positions_fail_before_fixture_mutation(string corruption)
    {
        PaymentFixtureRows rows = new();
        JsonObject data = JsonNode.Parse(rows.Owned.GetString("LifecycleData")!)!.AsObject();
        JsonObject payments = data["Payments"]!.AsObject();
        switch (corruption)
        {
            case "history":
                data["HistoryRevision"] = null;
                break;
            case "payments":
                data["Payments"] = null;
                break;
            case "head":
                payments["Head"] = "unexpected";
                break;
            case "index":
                payments.Remove("Index");
                break;
            case "operation":
                payments["Operation"] = Guid.Empty.ToString();
                break;
            case "count":
                payments["Sequence"] = 0;
                break;
            case "currency":
                payments["Currency"] = "AUD";
                break;
            case "opening":
                payments["OpeningSettledCents"] = 1;
                break;
            case "settled":
                payments["SettledCents"] = -1;
                break;
            case "evidence":
                data["PaymentHistory"] = 1;
                break;
            case "observed":
                data["PaymentObservedAt"] = null;
                break;
            case "older-version":
                data["Version"] = 2;
                break;
            default:
                data["Version"] = 4;
                break;
        }
        string original = data.ToJsonString();
        rows.Owned["LifecycleData"] = original;
        Assert.ThrowsExactly<InvalidOperationException>(() => InvoiceCheckpoint.ReadActive(rows.Owned));
        Assert.ThrowsExactly<InvalidOperationException>(() => InvoiceCheckpoint.ReadHistoryReference(rows.Owned));
        Assert.ThrowsExactly<InvalidOperationException>(() => InvoiceCheckpoint.ReadPaymentPosition(rows.Owned));
        Assert.ThrowsExactly<InvalidOperationException>(() => InvoiceCheckpoint.FreezeOwnedFixture(rows.Owned, "synthetic-token"));
        Assert.AreEqual(original, rows.Owned.GetString("LifecycleData"));
        Assert.IsFalse(rows.Owned.ContainsKey(nameof(Invoice.Token)));
    }
}
