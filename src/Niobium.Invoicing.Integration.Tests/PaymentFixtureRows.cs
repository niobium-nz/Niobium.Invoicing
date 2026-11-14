using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure;
using Azure.Data.Tables;

namespace Niobium.Invoicing.Integration.Tests;

// Synthetic raw wire fixtures, independent of the future application model.
internal sealed class PaymentFixtureRows
{
    internal Guid Biller { get; } = Guid.NewGuid();
    internal Guid Tenant { get; } = Guid.NewGuid();
    internal Guid Customer { get; } = Guid.NewGuid();
    internal Guid Operation { get; } = Guid.NewGuid();
    internal Guid Revision { get; } = Guid.NewGuid();
    internal DateTimeOffset Created { get; } = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    internal long Invoice => Invoicing.Invoice.ParseID(Created);
    internal TableEntity Owned
    {
        get;
    }
    internal TableEntity Receipt
    {
        get;
    }
    internal TableEntity Posting
    {
        get;
    }
    internal TableEntity Index
    {
        get;
    }

    internal PaymentFixtureRows()
    {
        DateTimeOffset occurred = Created.AddMinutes(1), recorded = Created.AddMinutes(2);
        string receiptID = "v1-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            FormattableString.Invariant($"Niobium.Invoicing.InvoicePayment/v1|{Biller:D}|{Invoice}|{Operation:D}"))));
        var entry = new
        {
            Operation,
            Kind = 1,
            AmountCents = 100L,
            Currency = "NZD",
            Reverses = (Guid?)null,
            OccurredAt = occurred,
            Actor = Biller,
            Reason = "Synthetic receipt",
            Evidence = "Synthetic bank reference",
            RecordedAt = recorded
        };
        string digest = Hash(new
        {
            Version = 1,
            Biller,
            Tenant,
            InvoiceCreated = Created,
            Entry = entry
        });
        Receipt = Row(receiptID);
        Receipt["Operation"] = Operation;
        Receipt["KindCode"] = 1;
        Receipt["AmountCents"] = 100L;
        Receipt["Currency"] = "NZD";
        Receipt["OccurredAt"] = occurred;
        Receipt["Actor"] = Biller;
        Receipt["Reason"] = entry.Reason;
        Receipt["Evidence"] = entry.Evidence;
        Receipt["RecordedAt"] = recorded;
        Receipt["ContentDigest"] = digest;

        string indexEntry = JsonSerializer.Serialize(new
        {
            Operation,
            Payment = receiptID,
            PaymentDigest = digest,
            ReversedCents = 0L
        });
        string indexID = "v1-" + Hash(new
        {
            Version = 1,
            Biller,
            Tenant,
            InvoiceCreated = Created,
            Depth = 0,
            Entries = 1L,
            EntryData = indexEntry,
            ChildrenData = (string?)null
        });
        Index = Row(indexID);
        Index["Depth"] = 0;
        Index["Entries"] = 1L;
        Index["EntryData"] = indexEntry;

        string postingID = "v1-" + Hash(new
        {
            Version = 1,
            Biller,
            Tenant,
            InvoiceCreated = Created,
            Operation,
            Payment = receiptID,
            PaymentDigest = digest,
            Previous = (string?)null,
            Sequence = 1L,
            Index = indexID,
            Currency = "NZD",
            OpeningSettledCents = 0L,
            SettledCents = 100L
        });
        Posting = Row(postingID);
        Posting["Operation"] = Operation;
        Posting["Payment"] = receiptID;
        Posting["PaymentDigest"] = digest;
        Posting["Sequence"] = 1L;
        Posting["Index"] = indexID;
        Posting["Currency"] = "NZD";
        Posting["OpeningSettledCents"] = 0L;
        Posting["SettledCents"] = 100L;

        Owned = new(Biller.ToString(), Invoicing.Invoice.BuildRowKey(Invoice))
        {
            ETag = new ETag("owned-invoice-version"),
            [nameof(Invoicing.Invoice.Billee)] = Customer,
            [nameof(Invoicing.Invoice.Tenant)] = Tenant,
            [nameof(Invoicing.Invoice.GrandTotalCurrency)] = "NZD",
            [nameof(Invoicing.Invoice.SettledCents)] = 0L,
            [nameof(Invoicing.Invoice.IssuanceState)] = "lifecycle-v1"
        };
        Owned["LifecycleData"] = new JsonObject
        {
            ["Version"] = 3,
            ["WriteID"] = Guid.NewGuid().ToString("N"),
            ["Preparation"] = 3,
            ["HistoryRevision"] = Revision.ToString(),
            ["PaymentHistory"] = 2,
            ["PaymentObservedAt"] = JsonValue.Create(recorded),
            ["Payments"] = new JsonObject
            {
                ["Head"] = postingID,
                ["Sequence"] = 1,
                ["Operation"] = Operation.ToString(),
                ["Currency"] = "NZD",
                ["OpeningSettledCents"] = 0,
                ["SettledCents"] = 100,
                ["Index"] = indexID
            }
        }.ToJsonString();
    }

    private TableEntity Row(string id) => new(Biller.ToString(), id)
    {
        ETag = new ETag("owned-payment-version"),
        ["Version"] = 1,
        ["Tenant"] = Tenant,
        ["InvoiceCreated"] = Created
    };
    private static string Hash<T>(T value) => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
