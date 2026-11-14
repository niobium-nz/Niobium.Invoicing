using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.Data.Tables;

namespace Niobium.Invoicing.Testing;

// This independent verifier deliberately does not call the candidate's model codec.
// It accepts only the reviewed legacy and lifecycle-v1 preparation representations.
internal enum PreparationCheckpoint
{
    Draft = 0, Preparing = 1, Retryable = 2, Ready = 3, Frozen = 4
}

internal static class InvoiceCheckpoint
{
    private const string LifecycleColumn = "LifecycleData";
    private const string LifecycleCheckpoint = "lifecycle-v1";

    internal static PreparationCheckpoint Read(TableEntity invoice)
    {
        string? checkpoint = invoice.GetString(nameof(Invoice.IssuanceState));
        if (checkpoint == LifecycleCheckpoint)
            return (PreparationCheckpoint)ReadLifecycle(invoice)["Preparation"]!.GetValue<int>();
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

    // Used only on a newly created, owned synthetic test invoice. Preserve every
    // unrelated column and all lifecycle evidence while arranging a saved-link view.
    internal static void FreezeOwnedFixture(TableEntity invoice, string token)
    {
        if (Read(invoice) != PreparationCheckpoint.Ready || String.IsNullOrWhiteSpace(token))
            throw Invalid();
        if (invoice.GetString(nameof(Invoice.IssuanceState)) == LifecycleCheckpoint)
        {
            JsonObject lifecycle = ReadLifecycle(invoice);
            if (lifecycle["Cancellation"] != null)
                throw Invalid();
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
        string? raw = invoice.GetString(LifecycleColumn);
        if (String.IsNullOrWhiteSpace(raw) || Encoding.UTF8.GetByteCount(raw) > 32 * 1024)
            throw Invalid();
        try
        {
            JsonObject? lifecycle = JsonNode.Parse(raw, documentOptions: new JsonDocumentOptions { MaxDepth = 16 }) as JsonObject;
            if (lifecycle == null || lifecycle["Version"]?.GetValue<int>() != 1
                || lifecycle["Preparation"] is not JsonValue stage || !stage.TryGetValue<int>(out int preparation)
                || !Enum.IsDefined((PreparationCheckpoint)preparation)
                || !Guid.TryParseExact(lifecycle["WriteID"]?.GetValue<string>(), "N", out Guid write) || write == Guid.Empty)
                throw Invalid();
            return lifecycle;
        }
        catch (JsonException) { throw Invalid(); }
        catch (InvalidOperationException) { throw Invalid(); }
        catch (FormatException) { throw Invalid(); }
    }

    private static InvalidOperationException Invalid() => new("Unsupported invoice preparation checkpoint in test storage.");
}
