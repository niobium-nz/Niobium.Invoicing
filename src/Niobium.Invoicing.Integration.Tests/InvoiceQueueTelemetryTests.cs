using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Niobium.Invoicing.Flows;
using Niobium.Invoicing.Server;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Niobium.Invoicing.Integration.Tests;

[TestClass]
[DoNotParallelize]
public sealed class InvoiceQueueTelemetryTests
{
    [TestMethod]
    [DataRow("Synthetic.Invoice.Service", "1.2.345-prerelease")]
    [DataRow("Synthetic.Invoice.Service", null)]
    [DataRow(null, "1.2.345-prerelease")]
    [DataRow(" ", "")]
    public void Configured_service_identity_survives_the_installed_Functions_resource_detector(string? name, string? version)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SERVICE_NAME"] = name,
            ["SERVICE_VERSION"] = version
        }).Build();
        KeyValuePair<string, object>[] platformIdentity =
            [new("service.name", name ?? "unknown-service"), new("service.version", version ?? "1.0.0-prerelease")];
        Dictionary<string, object> detected = ResourceBuilder.CreateDefault().AddAttributes(platformIdentity)
            .AddDetector(new FunctionsResourceDetector()).Build().Attributes.ToDictionary();
        ServiceCollection registrations = new();
        registrations.AddOpenTelemetry().ConfigureResource(resource => resource.AddAttributes(platformIdentity)).UseFunctionsWorkerDefaults();
        registrations.AddInvoiceQueueTelemetry(configuration);
        using ServiceProvider services = registrations.BuildServiceProvider();
        Resource[] resources = [services.GetRequiredService<MeterProvider>().GetResource(), services.GetRequiredService<TracerProvider>().GetResource()];
        foreach (Resource resource in resources)
        {
            Dictionary<string, object> attributes = resource.Attributes.ToDictionary();
            Assert.AreEqual(string.IsNullOrWhiteSpace(name) ? detected["service.name"] : name, attributes["service.name"]);
            Assert.AreEqual(string.IsNullOrWhiteSpace(version) ? detected["service.version"] : version, attributes["service.version"]);
            Assert.IsTrue(attributes.ContainsKey("process.pid"), "Keep the Functions resource metadata.");
        }
    }

    [TestMethod]
    public async Task Consumer_waits_for_issuance_and_exports_monotonic_duration_with_bounded_tags()
    {
        using Fixture fixture = new();
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Upsert.Setup(x => x.RunAsync(It.IsAny<IssueInvoiceRequest>(), It.IsAny<Billee?>(), It.IsAny<CancellationToken>()))
            .Returns(completion.Task);

        Task processing = fixture.Consumer.Run(fixture.Message(), CancellationToken.None);
        Assert.IsFalse(processing.IsCompleted);
        Assert.IsFalse(fixture.Collect().Any(x => x.Name == "niobium.invoicing.queue.processed"));
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        fixture.Clock.UtcNow -= TimeSpan.FromDays(1);
        completion.SetResult();
        await processing;

        List<Measurement> measurements = fixture.Collect();
        AssertMeasurement(measurements, "niobium.invoicing.queue.age", "s", 1, 20);
        AssertMeasurement(measurements, "niobium.invoicing.queue.processing.duration", "s", 1, 2, "succeeded");
        AssertMeasurement(measurements, "niobium.invoicing.queue.processed", "{attempt}", 1, 1, "succeeded");
        Assert.AreEqual(3, measurements.Count);
        fixture.Upsert.Verify(x => x.RunAsync(It.IsAny<IssueInvoiceRequest>(), It.IsAny<Billee?>(), It.IsAny<CancellationToken>()), Times.Once);
        fixture.Email.VerifyNoOtherCalls();
    }

    [TestMethod]
    [DataRow(false, false, "failed")]
    [DataRow(true, false, "failed")]
    [DataRow(true, true, "cancelled")]
    public async Task Consumer_preserves_flow_failures_and_distinguishes_requested_cancellation(bool cancellationException, bool cancelToken, string outcome)
    {
        using Fixture fixture = new();
        using CancellationTokenSource source = new();
        if (cancelToken)
        {
            source.Cancel();
        }
        Exception expected = cancellationException ? new OperationCanceledException(source.Token) : new InvalidOperationException("synthetic failure");
        fixture.Upsert.Setup(x => x.RunAsync(It.IsAny<IssueInvoiceRequest>(), It.IsAny<Billee?>(), source.Token))
            .Returns(() =>
            {
                fixture.Clock.Advance(TimeSpan.FromSeconds(3));
                return Task.FromException(expected);
            });

        Exception? actual = null;
        try
        {
            await fixture.Consumer.Run(fixture.Message(), source.Token);
        }
        catch (Exception error)
        {
            actual = error;
        }
        Assert.AreSame(expected, actual);
        List<Measurement> measurements = fixture.Collect();
        AssertMeasurement(measurements, "niobium.invoicing.queue.processing.duration", "s", 1, 3, outcome);
        AssertMeasurement(measurements, "niobium.invoicing.queue.processed", "{attempt}", 1, 1, outcome);
        fixture.Upsert.Verify(x => x.RunAsync(It.IsAny<IssueInvoiceRequest>(), It.IsAny<Billee?>(), source.Token), Times.Once);
        fixture.Email.VerifyNoOtherCalls();
    }

    [TestMethod]
    [DataRow("text/plain", "{}")]
    [DataRow("application/json", "invalid-json")]
    public async Task Invalid_transport_is_counted_without_running_business_flows(string contentType, string body)
    {
        using Fixture fixture = new();
        ServiceBusReceivedMessage message = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData(body), contentType: contentType, messageId: "not-a-metric-label", enqueuedTime: fixture.Clock.UtcNow.AddSeconds(-5));

        if (contentType == "application/json")
        {
            await Assert.ThrowsAsync<JsonException>(() => fixture.Consumer.Run(message, CancellationToken.None));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Consumer.Run(message, CancellationToken.None));
        }
        List<Measurement> measurements = fixture.Collect();
        AssertMeasurement(measurements, "niobium.invoicing.queue.age", "s", 1, 5);
        AssertMeasurement(measurements, "niobium.invoicing.queue.processed", "{attempt}", 1, 1, "failed");
        fixture.Upsert.VerifyNoOtherCalls();
        fixture.Email.VerifyNoOtherCalls();
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Missing_enqueue_time_is_omitted_and_future_time_is_clamped(bool future)
    {
        using Fixture fixture = new();
        fixture.Upsert.Setup(x => x.RunAsync(It.IsAny<IssueInvoiceRequest>(), It.IsAny<Billee?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        await fixture.Consumer.Run(fixture.Message(future ? fixture.Clock.UtcNow.AddSeconds(30) : default(DateTimeOffset)), CancellationToken.None);
        List<Measurement> measurements = fixture.Collect();
        if (future)
        {
            AssertMeasurement(measurements, "niobium.invoicing.queue.age", "s", 1, 0);
        }
        else
        {
            Assert.IsFalse(measurements.Any(x => x.Name == "niobium.invoicing.queue.age"));
        }
        AssertMeasurement(measurements, "niobium.invoicing.queue.processed", "{attempt}", 1, 1, "succeeded");
    }

    [TestMethod]
    public async Task Redelivery_counts_attempts_and_retains_the_original_enqueue_age()
    {
        using Fixture fixture = new();
        fixture.Upsert.Setup(x => x.RunAsync(It.IsAny<IssueInvoiceRequest>(), It.IsAny<Billee?>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                fixture.Clock.Advance(TimeSpan.FromSeconds(3));
                return Task.CompletedTask;
            });
        ServiceBusReceivedMessage message = fixture.Message();
        await fixture.Consumer.Run(message, CancellationToken.None);
        await fixture.Consumer.Run(message, CancellationToken.None);
        List<Measurement> measurements = fixture.Collect();
        AssertMeasurement(measurements, "niobium.invoicing.queue.age", "s", 2, 43);
        AssertMeasurement(measurements, "niobium.invoicing.queue.processing.duration", "s", 2, 6, "succeeded");
        AssertMeasurement(measurements, "niobium.invoicing.queue.processed", "{attempt}", 2, 2, "succeeded");
    }

    private static void AssertMeasurement(List<Measurement> measurements, string name, string unit, long count, double sum, string? outcome = null)
    {
        Measurement measurement = measurements.Single(x => x.Name == name);
        Assert.AreEqual(unit, measurement.Unit);
        Assert.AreEqual(count, measurement.Count);
        Assert.AreEqual(sum, measurement.Sum, 0.0001);
        Assert.AreEqual(outcome is null ? 0 : 1, measurement.Tags.Count);
        if (outcome is not null)
        {
            Assert.AreEqual(outcome, measurement.Tags["outcome"]);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public TestClock Clock { get; } = new();
        public Mock<UpsertFlow> Upsert { get; } = new(MockBehavior.Strict, null!, null!, null!, null!);
        public Mock<EmailFlow> Email { get; } = new(MockBehavior.Strict, null!, null!, null!, null!, null!);
        public IssueInvoiceCommandConsumer Consumer { get; }
        private readonly CaptureExporter exporter = new();
        private readonly ServiceProvider services;
        private readonly MeterProvider provider;

        public Fixture()
        {
            ServiceCollection registrations = new();
            registrations.AddLogging();
            registrations.AddSingleton<TimeProvider>(Clock);
            IConfiguration configuration = new ConfigurationBuilder().Build();
            registrations.AddInvoiceQueueTelemetry(configuration);
            registrations.AddInvoiceQueueTelemetry(configuration); // Repeated composition must not duplicate instruments.
            registrations.AddOpenTelemetry().WithMetrics(metrics => metrics.AddReader(new CaptureReader(exporter)));
            services = registrations.BuildServiceProvider();
            provider = services.GetRequiredService<MeterProvider>();
            Consumer = new(new IssueFlow(Upsert.Object, Email.Object), NullLogger<IssueInvoiceCommandConsumer>.Instance,
                services.GetRequiredService<InvoiceQueueTelemetry>());
        }

        public List<Measurement> Collect()
        {
            Assert.IsTrue(provider.ForceFlush());
            return exporter.Measurements.ToList();
        }

        public ServiceBusReceivedMessage Message(DateTimeOffset? enqueuedAt = null)
        {
            Guid biller = Guid.NewGuid();
            Billee customer = new() { Biller = biller, ID = Guid.NewGuid(), Name = "Synthetic", Currency = "NZD", Culture = "en-NZ", TimeZone = "Pacific/Auckland" };
            IssueInvoiceCommand command = new()
            {
                BillerID = biller, BilleeID = customer.ID, Tenant = Guid.NewGuid(),
                InvoiceID = Invoice.ParseID(Clock.UtcNow), Billee = customer, InvoiceItems = [], NotifyBillee = false
            };
            return ServiceBusModelFactory.ServiceBusReceivedMessage(body: new BinaryData(JsonMarshaller.Marshall(command)),
                contentType: "application/json", messageId: "private-operation-identity", enqueuedTime: enqueuedAt ?? Clock.UtcNow.AddSeconds(-20));
        }

        public void Dispose() => services.Dispose();
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 11, 0, 0, 0, TimeSpan.Zero);
        private long timestamp;
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public override long GetTimestamp() => timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan elapsed)
        {
            UtcNow += elapsed;
            timestamp += elapsed.Ticks;
        }
    }

    private sealed record Measurement(string Name, string Unit, long Count, double Sum, Dictionary<string, object?> Tags);

    private sealed class CaptureReader(BaseExporter<Metric> exporter) : BaseExportingMetricReader(exporter);

    private sealed class CaptureExporter : BaseExporter<Metric>
    {
        public List<Measurement> Measurements { get; } = [];

        public override ExportResult Export(in Batch<Metric> batch)
        {
            Measurements.Clear();
            foreach (Metric metric in batch)
            {
                foreach (ref readonly MetricPoint point in metric.GetMetricPoints())
                {
                    Dictionary<string, object?> tags = [];
                    foreach (KeyValuePair<string, object?> tag in point.Tags)
                    {
                        tags.Add(tag.Key, tag.Value);
                    }
                    bool histogram = metric.MetricType == MetricType.Histogram;
                    Measurements.Add(new(metric.Name, metric.Unit,
                        histogram ? point.GetHistogramCount() : point.GetSumLong(),
                        histogram ? point.GetHistogramSum() : point.GetSumLong(), tags));
                }
            }
            return ExportResult.Success;
        }
    }
}
