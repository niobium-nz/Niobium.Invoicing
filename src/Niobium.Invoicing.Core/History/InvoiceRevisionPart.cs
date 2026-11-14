using System.Globalization;

namespace Niobium.Invoicing.History;

/// <summary>A bounded part of a model-owned immutable historical snapshot.</summary>
public sealed class InvoiceRevisionPart
{
    [EntityKey(EntityKeyKind.PartitionKey)]
    public Guid Biller
    {
        get; private init;
    }
    [EntityKey(EntityKeyKind.RowKey)]
    public string ID { get; private init; } = String.Empty;
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
    public Guid Revision
    {
        get; private init;
    }
    public DateTimeOffset InvoiceCreated
    {
        get; private init;
    }
    public int Index
    {
        get; private init;
    }
    public string Content { get; private init; } = String.Empty;

    internal static InvoiceRevisionPart Capture(InvoiceRevision revision, int index, ReadOnlySpan<byte> content)
        => new()
        {
            Version = 1,
            Biller = revision.Biller,
            ID = PartID(revision.ID, index),
            Revision = revision.ID,
            InvoiceCreated = revision.InvoiceCreated,
            Index = index,
            Content = Convert.ToBase64String(content)
        };

    internal static StorageKey Key(Guid biller, Guid revision, int index)
        => new(biller.ToString(), PartID(revision, index));

    internal byte[] ReadContent(InvoiceRevision revision, int index, int maximumBytes)
    {
        if (Version != 1 || Biller != revision.Biller || Revision != revision.ID || InvoiceCreated != revision.InvoiceCreated || Index != index
            || ID != PartID(revision.ID, index) || String.IsNullOrEmpty(Content)
            || Content.Length > (maximumBytes + 2) / 3 * 4)
            throw InvoiceRevision.InvalidRepresentation();
        try
        {
            byte[] result = Convert.FromBase64String(Content);
            if (result.Length == 0 || result.Length > maximumBytes || Convert.ToBase64String(result) != Content)
                throw InvoiceRevision.InvalidRepresentation();
            return result;
        }
        catch (FormatException) { throw InvoiceRevision.InvalidRepresentation(); }
    }

    private static string PartID(Guid revision, int index)
    {
        if (revision == Guid.Empty || index < 0)
            throw InvoiceRevision.InvalidRepresentation();
        return revision.ToString("N") + "." + index.ToString("D4", CultureInfo.InvariantCulture);
    }
}
