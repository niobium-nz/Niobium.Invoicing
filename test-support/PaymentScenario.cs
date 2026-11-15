using Azure;
using Moq;
using Niobium.Invoicing.Payments;

namespace Niobium.Invoicing.Testing;

internal sealed class PaymentScenario
{
    internal Mock<IRepository<InvoicePayment>> Payments { get; } = new(MockBehavior.Strict);
    internal Mock<IRepository<InvoicePaymentPosting>> Postings { get; } = new(MockBehavior.Strict);
    internal Mock<IRepository<InvoicePaymentIndex>> Index { get; } = new(MockBehavior.Strict);
    internal Dictionary<(string, string), InvoicePaymentIndex> Nodes { get; } = [];
    internal int Reads { get; private set; }
    internal void ResetReads() => Reads = 0;
    internal Dictionary<(string, string), InvoicePayment> Receipts { get; } = [];
    internal Dictionary<(string, string), InvoicePaymentPosting> Links { get; } = [];
    internal bool LoseReceiptResponse { get; set; }
    internal bool LosePostingResponse { get; set; }
    internal bool LoseIndexResponse { get; set; }
    internal bool MissingIndexRead { get; set; }
    internal bool CorruptReceiptRead { get; set; }
    internal bool MissingPostingRead { get; set; }
    private int version;

    internal PaymentScenario()
    {
        Configure(Payments, Receipts, p => p.GetKey(), () => LoseReceiptResponse,
            p =>
            {
                if (CorruptReceiptRead)
                    typeof(InvoicePayment).GetProperty(nameof(InvoicePayment.ContentDigest))!.SetValue(p, "invalid");
                return p;
            });
        Configure(Postings, Links, p => p.GetKey(), () => LosePostingResponse, p => MissingPostingRead ? null : p);
        Configure(Index, Nodes, p => p.GetKey(), () => LoseIndexResponse, p => MissingIndexRead ? null : p);
    }

    private void Configure<T>(Mock<IRepository<T>> repository, Dictionary<(string, string), T> stored,
        Func<T, StorageKey> key, Func<bool> loseResponse, Func<T, T?> inspect) where T : class, new()
    {
        repository.Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string partition, string row, IList<string>? _, CancellationToken cancellation) =>
            {
                cancellation.ThrowIfCancellationRequested();
                Reads++;
                return stored.TryGetValue((partition, row), out T? value) ? inspect(Copy(value)) : null;
            });
        repository.Setup(r => r.CreateAsync(It.IsAny<IEnumerable<T>>(), false, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<T> values, bool _, DateTimeOffset? _, CancellationToken cancellation) =>
            {
                cancellation.ThrowIfCancellationRequested();
                T value = values.Single();
                StorageKey identity = key(value);
                T saved = Copy(value);
                typeof(T).GetProperty("ETag")!.SetValue(saved, (++version).ToString(System.Globalization.CultureInfo.InvariantCulture));
                bool created = stored.TryAdd((identity.PartitionKey, identity.RowKey), saved);
                if (!created)
                    throw new RequestFailedException(409, "Synthetic duplicate immutable payment record");
                if (loseResponse())
                    throw new TimeoutException("Synthetic uncertain immutable payment write");
                // The pinned adapter can omit write-response ETags. A domain
                // must read and validate the receipt/posting before committing.
                T response = Copy(saved);
                typeof(T).GetProperty("ETag")!.SetValue(response, null);
                return new[] { response };
            });
    }

    private static T Copy<T>(T value) where T : class, new()
    {
        T result = new();
        foreach (System.Reflection.PropertyInfo property in EntityMappingHelper.GetMapping(typeof(T)).Values)
            property.SetValue(result, property.GetValue(value));
        return result;
    }
}
