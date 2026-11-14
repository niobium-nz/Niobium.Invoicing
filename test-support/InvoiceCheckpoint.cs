using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Data.Tables;

namespace Niobium.Invoicing.Testing;

// This independent verifier deliberately does not call the candidate's model codec.
// It accepts only reviewed legacy checkpoints and lifecycle schema versions 1/2/3.
internal enum PreparationCheckpoint
{
    Draft = 0, Preparing = 1, Retryable = 2, Ready = 3, Frozen = 4
}

internal static class InvoiceCheckpoint
{
    private const string LifecycleColumn = "LifecycleData";
    private const string LifecycleCheckpoint = "lifecycle-v1";

    // Read-only DTO materialization follows independent representation validation;
    // never invoke a candidate model's codec or use this DTO to rewrite storage.
    internal static InvoiceLifecycleSnapshot ReadEvidence(TableEntity invoice)
        => JsonSerializer.Deserialize<InvoiceLifecycleSnapshot>(ReadLifecycle(invoice).ToJsonString()) ?? throw Invalid();

    internal static PreparationCheckpoint ReadActive(TableEntity invoice) => ReadPreparation(invoice, true);

    internal static Guid? ReadHistoryReference(TableEntity invoice)
    {
        _ = ReadPreparation(invoice, false); // Cancelled fixtures still require complete representation validation.
        if (invoice.GetString(nameof(Invoice.IssuanceState)) != LifecycleCheckpoint)
            return null;
        JsonObject lifecycle = ReadLifecycle(invoice);
        return lifecycle["Version"]!.GetValue<int>() is 2 or 3
            ? Guid.ParseExact(lifecycle["HistoryRevision"]!.GetValue<string>(), "D") : null;
    }

    internal sealed record PaymentPosition(string Head, long Sequence, Guid Operation, string Currency,
        long OpeningSettledCents, long SettledCents, string Index);

    internal static PaymentPosition? ReadPaymentPosition(TableEntity invoice)
    {
        _ = ReadHistoryReference(invoice);
        if (invoice.GetString(nameof(Invoice.IssuanceState)) != LifecycleCheckpoint)
            return null;
        JsonObject lifecycle = ReadLifecycle(invoice);
        return lifecycle["Version"]!.GetValue<int>() == 3 ? ReadPaymentPosition(invoice, lifecycle) : null;
    }

    private static PaymentPosition ReadPaymentPosition(TableEntity invoice, JsonObject lifecycle)
    {
        if (lifecycle["Payments"] is not JsonObject payments || lifecycle["PaymentHistory"]?.GetValue<int>() != 2
            || lifecycle["PaymentObservedAt"] is not JsonValue observed || !observed.TryGetValue<DateTimeOffset>(out DateTimeOffset at)
            || at == default || payments["Head"] is not JsonValue headValue || !headValue.TryGetValue<string>(out string? head)
            || !InvoicePaymentFixture.ValidID(head)
            || payments["Index"] is not JsonValue indexValue || !indexValue.TryGetValue<string>(out string? index)
            || !InvoicePaymentFixture.ValidID(index)
            || payments["Sequence"] is not JsonValue sequenceValue || !sequenceValue.TryGetValue<long>(out long sequence) || sequence <= 0
            || !Guid.TryParseExact(payments["Operation"]?.GetValue<string>(), "D", out Guid operation) || operation == Guid.Empty
            || payments["Currency"] is not JsonValue currencyValue || !currencyValue.TryGetValue<string>(out string? currency)
            || currency is not ("NZD" or "AUD") || currency != invoice.GetString(nameof(Invoice.GrandTotalCurrency))
            || payments["OpeningSettledCents"] is not JsonValue openingValue || !openingValue.TryGetValue<long>(out long opening)
            || opening < 0 || opening != invoice.GetInt64(nameof(Invoice.SettledCents))
            || payments["SettledCents"] is not JsonValue settledValue || !settledValue.TryGetValue<long>(out long settled) || settled < opening)
            throw Invalid();
        return new(head!, sequence, operation, currency!, opening, settled, index!);
    }

    private static PreparationCheckpoint ReadPreparation(TableEntity invoice, bool requireActive)
    {
        string? checkpoint = invoice.GetString(nameof(Invoice.IssuanceState));
        if (checkpoint == LifecycleCheckpoint)
        {
            JsonObject lifecycle = ReadLifecycle(invoice);
            if (requireActive && lifecycle["Cancellation"] != null)
                throw Invalid();
            return (PreparationCheckpoint)lifecycle["Preparation"]!.GetValue<int>();
        }
        if (invoice.ContainsKey(LifecycleColumn))
            throw Invalid();
        return checkpoint switch
        {
            null => PreparationCheckpoint.Draft,
            "preparing-v1" => PreparationCheckpoint.Preparing,
            "retryable-v1" => PreparationCheckpoint.Retryable,
            "ready-v1" => PreparationCheckpoint.Ready,
            "frozen-v1" => PreparationCheckpoint.Frozen,
            _ => throw Invalid(),
        };
    }

    // Only unanchored synthetic legacy fixtures can be arranged without a history
    // transition. Never alter a schema-2 row or detach its committed revision.
    internal static void FreezeOwnedFixture(TableEntity invoice, string token)
    {
        Guid? history = ReadHistoryReference(invoice);
        if (history.HasValue || ReadActive(invoice) != PreparationCheckpoint.Ready || String.IsNullOrWhiteSpace(token))
            throw Invalid();
        if (invoice.GetString(nameof(Invoice.IssuanceState)) == LifecycleCheckpoint)
        {
            JsonObject lifecycle = ReadLifecycle(invoice);
            lifecycle["Preparation"] = (int)PreparationCheckpoint.Frozen;
            lifecycle["WriteID"] = Guid.NewGuid().ToString("N");
            invoice[LifecycleColumn] = lifecycle.ToJsonString();
        }
        else
            invoice[nameof(Invoice.IssuanceState)] = "frozen-v1";
        invoice[nameof(Invoice.Token)] = token;
    }

    private static JsonObject ReadLifecycle(TableEntity invoice)
    {
        if (invoice.GetString(nameof(Invoice.IssuanceState)) != LifecycleCheckpoint)
            throw Invalid();
        string? raw = invoice.GetString(LifecycleColumn);
        if (String.IsNullOrWhiteSpace(raw) || Encoding.UTF8.GetByteCount(raw) > 32 * 1024)
            throw Invalid();
        try
        {
            JsonObject? lifecycle = JsonNode.Parse(raw, documentOptions: new JsonDocumentOptions { MaxDepth = 16 }) as JsonObject;
            if (lifecycle == null || lifecycle["Version"] is not JsonValue schema || !schema.TryGetValue<int>(out int version)
                || version is not (1 or 2 or 3)
                || lifecycle["Preparation"] is not JsonValue stage || !stage.TryGetValue<int>(out int preparation)
                || !Enum.IsDefined((PreparationCheckpoint)preparation)
                || !Guid.TryParseExact(lifecycle["WriteID"]?.GetValue<string>(), "N", out Guid write) || write == Guid.Empty)
                throw Invalid();
            if (version == 1 && lifecycle["HistoryRevision"] != null)
                throw Invalid();
            if (version is 2 or 3 && (!Guid.TryParseExact(lifecycle["HistoryRevision"]?.GetValue<string>(), "D", out Guid revision)
                || revision == Guid.Empty))
                throw Invalid();
            if (version < 3 && lifecycle["Payments"] != null)
                throw Invalid();
            if (version == 3)
                _ = ReadPaymentPosition(invoice, lifecycle);
            return lifecycle;
        }
        catch (JsonException) { throw Invalid(); }
        catch (InvalidOperationException) { throw Invalid(); }
        catch (FormatException) { throw Invalid(); }
    }

    private static InvalidOperationException Invalid() => new("Unsupported invoice preparation checkpoint in test storage.");
}
