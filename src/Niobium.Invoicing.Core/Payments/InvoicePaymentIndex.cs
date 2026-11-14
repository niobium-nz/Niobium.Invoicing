using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Niobium.Invoicing.Payments;

/// <summary>An immutable operation lookup node. Only the invoice's committed root grants membership.</summary>
public sealed class InvoicePaymentIndex
{
    private const int CurrentVersion = 1;
    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdef");

    [EntityKey(EntityKeyKind.PartitionKey)]
    public Guid Biller
    {
        get; private init;
    }
    [EntityKey(EntityKeyKind.RowKey)] public string ID { get; private init; } = String.Empty;
    [EntityKey(EntityKeyKind.ETag)]
    public string? ETag
    {
        get; private init;
    }
    [EntityKey(EntityKeyKind.Timestamp)]
    public DateTimeOffset? Timestamp
    {
        get; private init;
    }
    public int Version
    {
        get; private init;
    }
    public Guid Tenant
    {
        get; private init;
    }
    public DateTimeOffset InvoiceCreated
    {
        get; private init;
    }
    public int Depth
    {
        get; private init;
    }
    public long Entries
    {
        get; private init;
    }
    public string? EntryData
    {
        get; private init;
    }
    public string? ChildrenData
    {
        get; private init;
    }

    public sealed record Reference(string ID, long Entries);
    public sealed record Prepared(InvoicePaymentIndex Root, IReadOnlyList<InvoicePaymentIndex> Nodes);
    private sealed record Entry(Guid Operation, string Payment, string PaymentDigest, long ReversedCents);
    private sealed record Child(int Slot, string ID, long Entries);
    private sealed record Contents(int Version, Guid Biller, Guid Tenant, DateTimeOffset InvoiceCreated,
        int Depth, long Entries, string? EntryData, string? ChildrenData);

    public bool Describes(Invoice invoice)
    {
        _ = Read();
        return Biller == invoice.Biller && Tenant == invoice.Tenant && InvoiceCreated == invoice.Created;
    }

    public Reference ReadReference()
    {
        _ = Read();
        return new(ID, Entries);
    }

    public Guid? ReadOperation() => Read().Entry?.Operation;
    public long ReadReversedCents() => Read().Entry?.ReversedCents ?? throw Invalid();
    public StorageKey GetPaymentKey() => new(Biller.ToString(), Read().Entry?.Payment ?? throw Invalid());
    public StorageKey GetKey() => Key(Biller, ID);
    public static StorageKey Key(Guid biller, string id)
        => biller != Guid.Empty && ValidID(id) ? new(biller.ToString(), id) : throw Invalid();

    public bool References(InvoicePayment receipt)
    {
        Entry entry = Read().Entry ?? throw Invalid();
        _ = receipt.Read();
        return receipt.Biller == Biller && receipt.Tenant == Tenant && receipt.InvoiceCreated == InvoiceCreated
            && entry.Operation == receipt.Operation && entry.Payment == receipt.ID && entry.PaymentDigest == receipt.ContentDigest;
    }

    public Reference? Select(Guid operation)
    {
        if (operation == Guid.Empty)
            throw Invalid();
        (Entry? entry, Child[] children) = Read();
        if (entry != null)
            return null;
        Child? child = children.FirstOrDefault(child => child.Slot == Slot(operation, Depth));
        return child == null ? null : new(child.ID, child.Entries);
    }

    // The model owns routing, serialized children and content-addressed identity.
    // The domain supplies a verified path and chooses the business reversal amount.
    public static Prepared Capture(Invoice invoice, IReadOnlyList<InvoicePaymentIndex> path,
        InvoicePayment receipt, long reversedCents)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(receipt);
        if (!receipt.Describes(invoice) || reversedCents < 0 || path.Count > 33)
            throw Invalid();
        Entry entry = new(receipt.Operation, receipt.ID, receipt.ContentDigest, reversedCents);
        Reference? expected = null;
        for (int i = 0; i < path.Count; i++)
        {
            InvoicePaymentIndex node = path[i];
            if (!node.Describes(invoice) || node.Depth != i || (expected != null && node.ReadReference() != expected)
                || (i > 0 && expected == null))
                throw Invalid();
            expected = node.Select(entry.Operation);
        }
        if (expected != null)
            throw Invalid();
        List<InvoicePaymentIndex> nodes = [];
        InvoicePaymentIndex current;
        int ancestor;
        if (path.Count == 0)
        {
            current = Leaf(invoice, 0, entry);
            nodes.Add(current);
            ancestor = -1;
        }
        else
        {
            InvoicePaymentIndex last = path[^1];
            (Entry? existing, Child[] children) = last.Read();
            if (existing == null)
            {
                current = Leaf(invoice, last.Depth + 1, entry);
                nodes.Add(current);
                current = ReplaceChild(invoice, last.Depth, children, entry.Operation, current);
                nodes.Add(current);
            }
            else if (existing.Operation == entry.Operation)
            {
                if (existing.Payment != entry.Payment || existing.PaymentDigest != entry.PaymentDigest)
                    throw Invalid();
                current = Leaf(invoice, last.Depth, entry);
                nodes.Add(current);
            }
            else
                current = Join(invoice, last.Depth, existing, entry, nodes);
            ancestor = path.Count - 2;
        }
        for (int i = ancestor; i >= 0; i--)
        {
            current = ReplaceChild(invoice, path[i].Depth, path[i].Read().Children, entry.Operation, current);
            nodes.Add(current);
        }
        return new(current, nodes);
    }

    private static InvoicePaymentIndex Join(Invoice invoice, int depth, Entry first, Entry second, List<InvoicePaymentIndex> nodes)
    {
        if (depth >= 32)
            throw Invalid();
        int a = Slot(first.Operation, depth), b = Slot(second.Operation, depth);
        InvoicePaymentIndex branch;
        if (a == b)
        {
            InvoicePaymentIndex child = Join(invoice, depth + 1, first, second, nodes);
            branch = Branch(invoice, depth, [new(a, child.ID, child.Entries)]);
        }
        else
        {
            InvoicePaymentIndex left = Leaf(invoice, depth + 1, first), right = Leaf(invoice, depth + 1, second);
            nodes.Add(left);
            nodes.Add(right);
            branch = Branch(invoice, depth, [new(a, left.ID, left.Entries), new(b, right.ID, right.Entries)]);
        }
        nodes.Add(branch);
        return branch;
    }

    private static InvoicePaymentIndex ReplaceChild(Invoice invoice, int depth, Child[] children, Guid operation, InvoicePaymentIndex child)
    {
        if (child.Depth != depth + 1)
            throw Invalid();
        int slot = Slot(operation, depth);
        return Branch(invoice, depth, [.. children.Where(item => item.Slot != slot), new(slot, child.ID, child.Entries)]);
    }

    private static InvoicePaymentIndex Leaf(Invoice invoice, int depth, Entry entry)
        => Create(invoice, depth, 1, JsonSerializer.Serialize(entry), null);

    private static InvoicePaymentIndex Branch(Invoice invoice, int depth, Child[] children)
    {
        Child[] ordered = children.OrderBy(child => child.Slot).ToArray();
        return Create(invoice, depth, ordered.Sum(child => child.Entries), null, JsonSerializer.Serialize(ordered));
    }

    private static InvoicePaymentIndex Create(Invoice invoice, int depth, long entries, string? entry, string? children)
    {
        Contents contents = new(CurrentVersion, invoice.Biller, invoice.Tenant, invoice.Created.ToUniversalTime(), depth, entries, entry, children);
        InvoicePaymentIndex result = new()
        {
            Version = contents.Version,
            Biller = contents.Biller,
            Tenant = contents.Tenant,
            InvoiceCreated = contents.InvoiceCreated,
            Depth = depth,
            Entries = entries,
            EntryData = entry,
            ChildrenData = children,
            ID = Identity(contents)
        };
        _ = result.Read();
        return result;
    }

    private (Entry? Entry, Child[] Children) Read()
    {
        if (Version != CurrentVersion || Biller == Guid.Empty || Tenant == Guid.Empty || InvoiceCreated == default
            || InvoiceCreated.Offset != TimeSpan.Zero || Depth is < 0 or > 32 || Entries <= 0
            || ID != Identity(new(Version, Biller, Tenant, InvoiceCreated, Depth, Entries, EntryData, ChildrenData)))
            throw Invalid();
        try
        {
            if (EntryData != null)
            {
                if (ChildrenData != null || EntryData.Length > 512 || Entries != 1)
                    throw Invalid();
                Entry entry = JsonSerializer.Deserialize<Entry>(EntryData) ?? throw Invalid();
                if (entry.Operation == Guid.Empty || !ValidID(entry.Payment) || entry.PaymentDigest == null
                    || entry.PaymentDigest.Length != 64 || entry.PaymentDigest.AsSpan().IndexOfAnyExcept(HexDigits) >= 0
                    || entry.ReversedCents < 0 || EntryData != JsonSerializer.Serialize(entry))
                    throw Invalid();
                return (entry, []);
            }
            if (ChildrenData == null || ChildrenData.Length > 4096 || Depth == 32)
                throw Invalid();
            Child[] children = JsonSerializer.Deserialize<Child[]>(ChildrenData) ?? throw Invalid();
            if (children.Length is < 1 or > 16 || children.Any(child => child == null || child.Slot is < 0 or > 15
                || !ValidID(child.ID) || child.Entries <= 0) || children.Select(child => child.Slot).Distinct().Count() != children.Length
                || !children.SequenceEqual(children.OrderBy(child => child.Slot)) || children.Sum(child => child.Entries) != Entries
                || ChildrenData != JsonSerializer.Serialize(children))
                throw Invalid();
            return (null, children);
        }
        catch (JsonException) { throw Invalid(); }
        catch (OverflowException) { throw Invalid(); }
    }

    private static int Slot(Guid operation, int depth)
    {
        if (operation == Guid.Empty || depth is < 0 or >= 32)
            throw Invalid();
        char digit = operation.ToString("N")[depth];
        return digit <= '9' ? digit - '0' : digit - 'a' + 10;
    }

    private static bool ValidID(string? id) => id != null && id.Length == 67 && id.StartsWith("v1-", StringComparison.Ordinal)
        && id.AsSpan(3).IndexOfAnyExcept(HexDigits) < 0;
    private static string Identity(Contents contents) => "v1-" + Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(contents)));
    private static InvalidOperationException Invalid() => new("Unsupported invoice payment index representation.");
}
