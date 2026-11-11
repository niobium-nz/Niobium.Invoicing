# Invoice queue measurements

B07 / Q28 #13 calls for initial queue-age and processing-latency measurements.
`IssueInvoiceCommandConsumer` now measures each invocation through
[`InvoiceQueueTelemetry`](../src/Niobium.Invoicing.Server/InvoiceQueueTelemetry.cs).
This observes the existing issuance flow; it does not change persistence, retries,
message identity, cancellation handling or notification delivery semantics.

| Instrument | Type / unit | Meaning | Attributes |
| --- | --- | --- | --- |
| `niobium.invoicing.queue.age` | Histogram / seconds | Start time minus Service Bus `EnqueuedTime`, sampled once per attempt | None |
| `niobium.invoicing.queue.processing.duration` | Histogram / seconds | Monotonic elapsed time through parsing, validation and awaited issuance | `outcome` |
| `niobium.invoicing.queue.processed` | Counter / attempts | Attempts that finished or threw, including repeated deliveries | `outcome` |

`outcome` is one of `succeeded`, `failed` or `cancelled`. Cancellation is recorded
only when processing throws `OperationCanceledException` and the invocation's
cancellation token is requested. Other exceptions, including an unrequested
cancellation exception, are failures. The original exception is rethrown so the
Functions host retains control of retry/dead-letter behavior.

Missing/default enqueue timestamps produce no age sample. Future timestamps are
clamped to zero because host/broker clocks can differ. Elapsed processing time uses
`TimeProvider` timestamps, so a wall-clock correction does not change the duration.
Redelivery samples age again from the message's original enqueue time and counts
another attempt. Counters do not count unique invoices, provider submissions or
recipient deliveries. A successful attempt may have notification disabled.

There are no invoice, biller, tenant, customer, message-ID, recipient, payload or
exception-text metric attributes. The fixed outcome set bounds cardinality.
The platform's existing service/version/environment resource attributes remain
separate from these application measurements.

## Framework integration

`AddInvoiceQueueTelemetry` registers a singleton observer and the
`Niobium.Invoicing.Queue` meter with the existing OpenTelemetry builder. The
container-owned `IMeterFactory` owns meter disposal. An existing `TimeProvider`
registration is preserved; production defaults to `TimeProvider.System`.

The installed Niobium.Platform **4.0.61**, source
[`c246274`](https://github.com/niobium-nz/Niobium/blob/c2462746614ee6c768d725c99e3c54df2e3848c8/Niobium.Platform/ServiceDefaultsExtensions.cs),
already configures metrics and exporters. Its Azure Monitor exporter is enabled
by `APPLICATIONINSIGHTS_CONNECTION_STRING`; its OTLP exporter is conditional on
`OTEL_EXPORTER_OTLP_ENDPOINT`. This change adds the meter to that pipeline and
introduces no exporter, destination, credential or package upgrade.
Functions composition remains on 4.0.60 at source
[`1fa0449`](https://github.com/niobium-nz/Niobium/blob/1fa044968f504ef575021720c728a17dc72bb067/Niobium.Platform.Functions/DependencyModule.cs).

## Evidence and limits

Nine component cases execute the actual consumer and collect through the actual
OpenTelemetry `MeterProvider`/reader with a local exporter. They cover awaiting
issuance, a wall-clock change, flow failures, requested and unrelated cancellation,
malformed transport, missing/future enqueue timestamps and repeated delivery.
They verify instrument names, units, values and the complete attribute set.
Repeated service registration does not duplicate observations.

Local collection is verified; Azure ingestion, dashboards and alert thresholds
are not. No capacity or support target is inferred from these measurements.
After an authorized test deployment, use the existing test queue fixtures and
configured telemetry destination to verify all three instruments and their units.
Do not use production messages or send additional notifications for this check.
Notification uncertain-submission/hold counters and confirmed-delivery failures
remain separate B02/B06 work; this consumer cannot infer those outcomes from an
enqueue acknowledgement.
