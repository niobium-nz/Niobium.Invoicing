using System.Security.Cryptography;
using System.Text.Json;
using Azure.Data.Tables;

namespace Niobium.Invoicing.Testing;

// Independent raw verifier for owned test fixtures. This code intentionally
// validates the reviewed representation without loading a candidate model.
internal sealed class InvoicePaymentIndexFixture
{
    internal const string Table = "InvoicePaymentIndex";
    internal static readonly string[] Fields = ["PartitionKey", "RowKey", "Version", "Tenant", "InvoiceCreated", "Depth", "Entries", "EntryData", "ChildrenData"];
    internal sealed record Entry(Guid Operation, string Payment, string PaymentDigest, long ReversedCents);
    private sealed record Child(int Slot, string ID, long Entries);
    private sealed record Contents(int Version, Guid Biller, Guid Tenant, DateTimeOffset InvoiceCreated,
        int Depth, long Entries, string? EntryData, string? ChildrenData);
    private sealed record Node(int Depth, long Entries, Entry? Entry, Child[] Children);
    private readonly Dictionary<string, Node> nodes = [];

    internal InvoicePaymentIndexFixture(IReadOnlyList<TableEntity> rows, Guid biller, Guid tenant, DateTimeOffset invoice,
        IReadOnlyDictionary<string, TableEntity> receipts)
    {
        try
        {
            foreach (TableEntity row in rows)
            {
                int depth = row.GetInt32("Depth") ?? throw Invalid();
                long count = row.GetInt64("Entries") ?? throw Invalid();
                string? entryData = row.GetString("EntryData"), childrenData = row.GetString("ChildrenData");
                Contents contents = new(1, biller, tenant, invoice, depth, count, entryData, childrenData);
                string id = "v1-" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(contents)));
                if (row.RowKey != id || row.GetGuid("Tenant") != tenant || depth is < 0 or > 32 || count <= 0)
                    throw Invalid();
                Entry? entry = null;
                Child[] children = [];
                if (entryData != null)
                {
                    if (childrenData != null || entryData.Length > 512 || count != 1)
                        throw Invalid();
                    entry = JsonSerializer.Deserialize<Entry>(entryData) ?? throw Invalid();
                    if (entry.Operation == Guid.Empty || entry.ReversedCents < 0 || entryData != JsonSerializer.Serialize(entry)
                        || !receipts.TryGetValue(entry.Payment, out TableEntity? receipt)
                        || receipt.GetGuid("Operation") != entry.Operation || receipt.GetString("ContentDigest") != entry.PaymentDigest
                        || entry.ReversedCents > (receipt.GetInt32("KindCode") == 1 ? receipt.GetInt64("AmountCents") : 0))
                        throw Invalid();
                }
                else
                {
                    if (depth == 32 || childrenData == null || childrenData.Length > 4096)
                        throw Invalid();
                    children = JsonSerializer.Deserialize<Child[]>(childrenData) ?? throw Invalid();
                    if (children.Length is < 1 or > 16 || children.Any(child => child == null || child.Slot is < 0 or > 15
                        || !InvoicePaymentFixture.ValidID(child.ID) || child.Entries <= 0)
                        || children.Select(child => child.Slot).Distinct().Count() != children.Length
                        || !children.SequenceEqual(children.OrderBy(child => child.Slot))
                        || children.Sum(child => child.Entries) != count || childrenData != JsonSerializer.Serialize(children))
                        throw Invalid();
                }
                bool unique = nodes.TryAdd(id, new(depth, count, entry, children));
                if (!unique)
                    throw Invalid();
            }
            foreach (Node node in nodes.Values)
                foreach (Child child in node.Children)
                    if (!nodes.TryGetValue(child.ID, out Node? target) || target.Depth != node.Depth + 1 || target.Entries != child.Entries)
                        throw Invalid();
        }
        catch (JsonException) { throw Invalid(); }
        catch (OverflowException) { throw Invalid(); }
    }

    internal void RequireRoot(string id, long entries)
    {
        if (!nodes.TryGetValue(id, out Node? root) || root.Depth != 0 || root.Entries != entries)
            throw Invalid();
    }

    internal Dictionary<Guid, Entry> ReadCommitted(string id, long entries)
    {
        RequireRoot(id, entries);
        Dictionary<Guid, Entry> result = [];
        Visit(id, String.Empty, result);
        if (result.Count != entries)
            throw Invalid();
        return result;
    }

    private void Visit(string id, string route, Dictionary<Guid, Entry> result)
    {
        Node node = nodes[id];
        if (node.Depth != route.Length)
            throw Invalid();
        if (node.Entry != null)
        {
            bool unique = result.TryAdd(node.Entry.Operation, node.Entry);
            if (!unique || !node.Entry.Operation.ToString("N").StartsWith(route, StringComparison.Ordinal))
                throw Invalid();
            return;
        }
        foreach (Child child in node.Children)
            Visit(child.ID, route + child.Slot.ToString("x", System.Globalization.CultureInfo.InvariantCulture), result);
    }

    private static InvalidOperationException Invalid() => new("Invoice payment index fixture ownership or representation could not be verified.");
}
