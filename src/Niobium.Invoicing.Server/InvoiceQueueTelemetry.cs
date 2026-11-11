using System.Diagnostics.Metrics;

namespace Niobium.Invoicing.Server;

/// <summary>
/// Measures queue processing attempts without recording invoice or recipient data.
/// Successful processing does not establish downstream email delivery.
/// </summary>
public sealed class InvoiceQueueTelemetry
{
    public const string MeterName = "Niobium.Invoicing.Queue";

    private readonly TimeProvider clock;
    private readonly Histogram<double> queueAge;
    private readonly Histogram<double> processingDuration;
    private readonly Counter<long> processed;

    public InvoiceQueueTelemetry(IMeterFactory meterFactory, TimeProvider clock)
    {
        this.clock = clock;
        Meter meter = meterFactory.Create(MeterName);
        queueAge = meter.CreateHistogram<double>("niobium.invoicing.queue.age", "s",
            "Time since Service Bus enqueue at the start of each processing attempt.");
        processingDuration = meter.CreateHistogram<double>("niobium.invoicing.queue.processing.duration", "s",
            "Elapsed time for a queue processing attempt, including parsing and issuance.");
        processed = meter.CreateCounter<long>("niobium.invoicing.queue.processed", "{attempt}",
            "Completed processing attempts, including failures and redeliveries; not unique invoices or delivered emails.");
    }

    /// <summary>
    /// Observes processing while preserving its exceptions and cancellation behavior.
    /// </summary>
    public async Task MeasureAsync(DateTimeOffset enqueuedAt, Func<Task> process, CancellationToken cancellationToken)
    {
        if (enqueuedAt != default)
        {
            queueAge.Record(Math.Max(0, (clock.GetUtcNow() - enqueuedAt).TotalSeconds));
        }

        long started = clock.GetTimestamp();
        ProcessingOutcome outcome = ProcessingOutcome.Failed;
        try
        {
            await process();
            outcome = ProcessingOutcome.Succeeded;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = ProcessingOutcome.Cancelled;
            throw;
        }
        finally
        {
            KeyValuePair<string, object?> tag = new("outcome", outcome.ToString().ToLowerInvariant());
            processingDuration.Record(clock.GetElapsedTime(started).TotalSeconds, tag);
            processed.Add(1, tag);
        }
    }

    private enum ProcessingOutcome
    {
        Succeeded,
        Failed,
        Cancelled
    }
}
