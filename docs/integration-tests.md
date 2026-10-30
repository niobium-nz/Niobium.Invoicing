# Integration tests (B07)

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

## Deployed test-environment suite

`Niobium.Invoicing.Deployment.Tests` is deliberately outside the solution and runs explicitly after test deployment. Missing configuration fails class initialization; it cannot silently skip. It uses the existing MSTest SDK and Server's resolved Azure/Niobium dependencies without upgrades.

The workflow obtains these settings through its test-environment OIDC identity and [`scripts/test-resources.py`](../scripts/test-resources.py): `TEST_ENVIRONMENT`, `TEST_API_URL`, `TEST_STORAGE_ACCOUNT`, `TEST_STORAGE_KEY`, `TEST_SERVICEBUS_CONNECTION`, `TEST_SIGNING_KEY`, `TEST_EXPECTED_COMMIT`, `TEST_TOKEN_ISSUER`, `TEST_TOKEN_AUDIENCE`. Do not copy values into local settings, logs or documentation. The suite rejects non-test environments, non-HTTPS/non-test Functions hosts, storage accounts and Service Bus namespaces outside the test naming convention. This guard complements the workflow's selected resources; it is not an authorization boundary for arbitrary user-supplied credentials.

```sh
dotnet test --project src/Niobium.Invoicing.Deployment.Tests/Niobium.Invoicing.Deployment.Tests.csproj --configuration Release --report-trx --results-directory artifacts/deployed
```

Run this only inside the authorized test deployment job. It first waits for `/health` to report the exact pushed SHA, then exercises seven deployed cases: four authentication/ownership rejection cases; successful HTTP issuance with durable storage, validation, exact replay and saved-token HTML; real Service Bus ingestion with stable notification content/token on retry; and absence of the debug email route. It uses synthetic biller/customer snapshots seeded through the matched Table adapter, so external profile lookup is intentionally outside this suite.

The incoming command uses `issueinvoicecommand`; outgoing messages go to `notifycommand` in Invoicing's **own test namespace**, which has no email consumer. The suite completes only messages matching its own synthetic invoice identity. Unrelated messages are abandoned during cleanup. It deletes only its own synthetic invoice/item records, never shared tables. Deployment plus testing is serialized across branches. Abrupt cancellation or a failed storage cleanup can leave synthetic fixtures; never delete unrelated records to repair a test run.

The deployed suite covers real Functions middleware/binding, HTTP error translation, storage and Service Bus paths that local component tests cannot establish. It does not test external profile provisioning, direct-resource entitlement issuance, downstream email/provider idempotency, confirmation events, production scale, backup/restore or abandoned-writer recovery. Passing tests are evidence for these scenarios and this exact deployment, not a blanket delivery guarantee.

## Remaining coverage and evidence

The owner authorized test deployment and required post-deployment verification on 2026-10-30. This supersedes the original task's test-deployment prohibition. [Delivery policy](delivery-and-operations.md) describes the gate, required review and external repository protection settings. [Progress](implementation-progress.md) records historical results and current verification; a source declaration alone is not a successful deployed run.

Direct-resource grants in Azure, the external profile/Notification services and operational recovery scenarios above remain separate integration work. B07's full research acceptance is not claimed solely from this suite.
