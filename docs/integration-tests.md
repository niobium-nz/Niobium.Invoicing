# Local integration tests (B07)

The separate `Niobium.Invoicing.Integration.Tests` project references Server and
uses the existing MSTest SDK and Moq versions. It does not upgrade Niobium packages.
All data, identities, signing keys and destinations are synthetic.

`HttpBoundaryTests` parses signed tokens with the installed Niobium PrincipalParser,
executes NewInvoice results through ASP.NET, tests claim mismatches and invalid
invoice dates, confirms HTTP notifications are disabled, and inspects the compiled
server for the removed debug function. These are component integration tests;
they do not run the Functions HTTP host or its middleware pipeline.

`StorageFlowTests` opts in through `NIOBIUM_INVOICING_TEST_TABLE_ENDPOINT`. Only an
HTTP loopback emulator endpoint is allowed. It uses the published Azurite account
key and creates three tables with a random `InvTest` prefix. Cleanup deletes only
those tables. Real Server/Core registration and Niobium Table adapters are used;
profile lookup and outgoing notification transport are controlled test boundaries.
No shared Azure account, live customer data, queue or email provider is accessed.

The storage cases cover create/edit/read ETags, stale rejection, competing
preparation claims, repair of a known partial item set, frozen exact replay,
stable notification identity/content and saved-token public HTML rendering.
Unknown-outcome storage failures and definite rejection injection are covered by
the unit suite. The partial integration test explicitly creates an acknowledged
partial checkpoint; it does not simulate a network timeout or prove safe takeover.

## Run locally

Use a disposable Table-only Azurite process. The task used installed Azurite with
this command, and stopped it after verification:

```sh
azurite-table --tableHost 127.0.0.1 --tablePort 18482 --inMemoryPersistence --disableTelemetry --skipApiVersionCheck --silent
```

In another shell, from the repository root after restore/build:

```sh
NIOBIUM_INVOICING_TEST_TABLE_ENDPOINT=http://127.0.0.1:18482/devstoreaccount1 dotnet test --project src/Niobium.Invoicing.Integration.Tests/Niobium.Invoicing.Integration.Tests.csproj --configuration Release --no-build --no-restore
```

Omitting the variable runs the HTTP component cases and skips storage cases. A
skipped storage suite is not evidence that storage passed. The workflow sets the
variable and starts a disposable Azurite service for its local verification job.
The existing prepackage solution test invocation does not opt into Table tests.

After abrupt runner termination, cleanup might not execute. Stop the disposable
in-memory emulator to discard its test data. Do not point this suite at existing
resources or reuse application credentials. No local settings file is required.

## Outstanding deployed coverage

B07 remains partial. Still required: a reviewed isolated Azure test deployment,
real Functions host startup/middleware/binding, issued-resource authorization,
Service Bus command ingestion and controlled NotifyCommand sink, deployed retry
and failure handling, and a deployed check that the debug route is absent.
No such resources, transport credentials or deployment authorization were supplied.
The target task expressly forbids deployment and production data access.

These missing checks block B04's final post-deployment promotion gate. Local
Azurite checks occur before test deployment and must not be described as that gate.
See [progress](implementation-progress.md) for exact results and baseline failures.
