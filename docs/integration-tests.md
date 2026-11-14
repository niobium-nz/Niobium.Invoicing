# Integration tests (B07)

The separate `Niobium.Invoicing.Integration.Tests` project references Server and
uses the existing MSTest SDK and Moq versions. It does not upgrade Niobium packages.
All data, identities, signing keys and destinations are synthetic.

`HttpBoundaryTests` parses signed tokens with the installed Niobium PrincipalParser,
executes NewInvoice results through ASP.NET, tests claim mismatches and invalid
invoice dates, confirms issuance notifications are disabled, and inspects the compiled
manual email route for Function-level key authorization. `EmailInvoiceTests`
checks flow completion, empty 201 responses, propagated failures and cancellation. These are component integration tests;
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

## Lifecycle checkpoint compatibility preparation

The deployed harness independently recognizes the existing preparation checkpoints
and the reviewed additive `lifecycle-v1` representation in `LifecycleData` version
1. It still requires the exact expected preparation stage and an active invoice
without cancellation evidence. Unknown versions,
malformed data and mismatched legacy/new columns fail verification. The synthetic
saved-link fixture preserves lifecycle evidence instead of replacing its checkpoint
with an older representation. Fourteen local harness cases exercise both supported
formats, rejection boundaries and preservation of unrelated fixture fields.

This change prepares the immutable-main verifier for the subsequent cancellation
implementation. It does not introduce application lifecycle behavior or change
pipeline definitions, production routes, credentials or review protections. The
preparation PR must pass the existing candidate and trusted deployed suites and
merge through the normal process before a lifecycle implementation can be certified.

## Cancellation and historical-payment verification

The lifecycle implementation adds 35 unit cases and ten component/storage cases.
With the installed checkpoint verifier, the local suites contain 158 unit and 75
integration cases. The latter opt into actual loopback Table storage and include
the pinned prior-writer controls described in [invoice lifecycle](invoice-lifecycle.md).
The combined authored application coverage is 93.23%; both solution and deployed
harness builds pass without warnings/errors.

Seven candidate deployed cases extend the existing 28 cases: four identity modes
exercise all three lifecycle routes, then owned fixtures verify legacy-history
reconciliation/cancellation/saved links, immutable historical-payment evidence and
competing cancellation operations. Existing test cleanup removes only these owned
records. No additional notification is sent by these cases. The trusted suite from
main retains its reviewed active-invoice checks. Production still uses only its
five existing GET-only checks; local evidence is not a deployed pass.

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

`Niobium.Invoicing.Deployment.Tests` is outside the solution and runs explicitly after test deployment. Missing required configuration fails instead of skipping. It uses the existing MSTest SDK and installed Azure/Niobium versions.

Feature CI runs the candidate suite, then a second copy from immutable main on a fresh runner. The trusted runner compiles only main's harness and its project references and uses main's credential helper and read-only client. It never executes candidate source or downloads candidate artifacts, but targets the same deployed feature SHA and real test resources. Both runs share the workflow-wide environment lock. This lets new candidate tests evolve while certification still requires previously reviewed checks. Initial installation has no main harness yet; that trusted step is explicitly skipped and cannot certify the feature. Its administrative activation dependency is recorded in the delivery guide.

The suite uses the **existing environment's real services and configuration**. It does not create an Azure test stack, tables, a notification sink or a substitute consumer. The workflow reads test resource metadata and credentials with its configured OIDC identity. `TEST_ENVIRONMENT=test` and the actual test host/account/namespace are validated before writes. These tests are never run against production.

```sh
dotnet test --project src/Niobium.Invoicing.Deployment.Tests/Niobium.Invoicing.Deployment.Tests.csproj --configuration Release --report-trx --results-directory artifacts/deployed
```

Fifteen cases cover four HTTP authentication/ownership rejection cases; successful issuance with persistence, validation, exact replay and saved-token HTML; ingestion and update through the real incoming queue; enqueue through the real Notification service to the approved mailbox; safe fixture-ID collision rejection; reference retention after successful issuance and cleanup after rejected issuance; and manual email behavior: missing/invalid keys and bearer-only calls rejected, valid-key notification enqueue, and a valid-key missing-recipient skip. The health response must first identify the exact feature commit. Notification tests leave the downstream queue to its real consumer; they do not receive/complete unrelated messages or assert provider delivery from an enqueue acknowledgement.

Q26 restores the historical key-guarded debug endpoint. The separately reviewed preparatory harness permits 404 or 401 so the absent route can transition to host authorization without changing protection settings. The restoration candidate strictly requires 401 for missing/invalid keys and bearer-only requests to an existing ready invoice, an unchanged invoice ETag on rejection, and 201 for valid-key calls. It uses the workflow's existing Azure CLI identity to read the test app's default host function key after matching its ARM hostname to the verified test endpoint. It does not create/rotate keys or use a master key. Credentials and CLI output are captured without logging; requests carry the key only in a header. The authorized-send case uses the approved mailbox and checks freeze, token, enqueue acknowledgement and public HTML; the empty-recipient case checks the historical skip without a write. Production's GET allowlist remains limited to health and invoice viewing; the manual email route is never part of read-only production tests.

Q27 adds six deployed debug-view cases: missing/invalid keys and bearer-only requests return 401; a valid existing test key renders tokenless drafts and frozen invoices without changing invoice/item ETags, token, issuance state or enqueue acknowledgement; missing invoices return 404. Responses must be HTML with `Cache-Control: no-store` and preserve HTML encoding. Tests verify that public token checks remain mandatory even with a function key, saved links still work, drafts stay editable and frozen updates remain rejected. These cases reuse the test-only key reader and existing resources; they do not send additional notifications. Local component tests also execute the handler results and exercise reads through the pinned Table adapter against disposable Azurite.

Cleanup tracks an invoice only after its creation succeeds, checks its run-specific customer identity before deletion, and uses its current ETag. A rejected or uncertain creation never schedules an existing row for deletion. The collision case verifies HTTP 409 from real Table storage, an unchanged invoice ETag and no additional cleanup claim.

A newly seeded reference is tracked as temporary until HTTP issuance returns success. Failed issuance immediately cleans that owned seed even if class initialization fails before framework cleanup. Only a successfully issued reference is removed from temporary tracking. Two deployed cases verify the retained/removed records through real Table storage, then remove their own temporary reference-case data. The outer deployed-test step permits 30 minutes: the health, two queue-processing, notification and read-only waits alone can consume about 15 minutes, so the former 10-minute timeout could terminate the process before cleanup. Abrupt runner termination can still prevent cleanup; this is not an atomic fixture lifecycle.

Store the approved mailbox in the test environment secret `TEST_NOTIFICATION_RECIPIENT`. `TEST_INVOICE_BILLER` and `TEST_INVOICE_ID` can select the existing reference invoice used to identify the test biller. When both are absent, the owner-authorized fixture setup retains a reference invoice in real test storage and writes its IDs to `artifacts/deployed/test-invoice-fixture.json`. It reuses an existing test biller when available, or a synthetic biller snapshot when the test table is empty. A working downstream Invoice template for that biller is still necessary for actual email delivery; this suite does not invent a Notification template. Tests use synthetic recipient/customer data and snapshot fixtures; external profile provisioning and provider receipt handling are not covered.

Cleanup deletes only the suite's temporary invoice/item records; it verifies item ownership markers. It retains the reference fixture requested by the owner and never deletes shared tables. Abrupt termination may leave temporary test records. Credentials, tokens, notification payloads and fixture response bodies must not be logged.

## Invoice-item resource-token verification (B07)

Azure [Table SAS access ranges](https://learn.microsoft.com/en-us/rest/api/storageservices/create-service-sas#specify-table-access-ranges) filter queries to the authorized range. The deployed point-read assertion therefore expects 404 for the second, out-of-range invoice and independently checks with the fixture credential that its item still exists with the same ETag. A query-only credential must reject an insert with 403.

Four local cases use the real Server resource-control registration and the pinned Table adapter to check an owned invoice partition, another biller, an omitted partition and a missing invoice. Every applicable control is checked, so an alternative registered grant cannot silently bypass a rejection. The granted permission must be Query with equal start/end partition keys.

Six deployed cases exercise `GET /rsas` through the real test host: missing/invalid bearer credentials, another biller, an omitted partition and a missing invoice are denied. An owning biller receives a signed read-only credential for its invoice-item partition. The suite uses that credential against real test Table storage to read its own line, reject a read from another existing synthetic invoice partition, and reject an insert. The original line's ETag remains unchanged. The fixture's normal ownership-checked cleanup also covers the marked insert probe if a regression unexpectedly allows it.

The complete resource-token response is a credential. These tests never log it or the SAS, and storage-probe failures report only status codes. They reuse existing signing/storage configuration and synthetic invoices; they create no Azure resources or external entitlements and never call this route in production. Q16's same-ID cross-biller key collision remains deferred: the tests establish the existing ownership boundary for distinct invoice IDs, not a migrated key schema or an audit of historical tokens/external role grants.

The candidate deployed suite now contains 28 cases. Local component/storage integration contains 51 cases, including nine queue-measurement and four Functions resource-identity cases. A successful local build/test run does not establish the deployed result; the exact feature run must pass both candidate and trusted suites before PR creation.

## Queue metric export verification (B07)

The deployed metrics case sends a synthetic invoice with notification disabled,
waits for persistence, then requires all three queue instruments in the existing
test Application Insights destination. It reuses the verified test Function App,
resource group and Azure workflow identity. The current workflow `buildVersion`
must match the host's `SERVICE_VERSION` with its existing prerelease suffix.
Queries filter by that version, test service name and this case's start time.
The test permits six minutes for export/ingestion, fails on missing configuration
or query access, and never logs settings, credentials or raw telemetry. See
[queue measurements](queue-observability.md) for exact assertions and limits.
No workflow inputs, resources, notification sends or production checks are added.

## Read-only production suite

Use the [environment configuration inventory](environment-configuration.md) for the existing fixture settings, their exact runner mappings and why `TEST_INVOICE_TOKEN` must remain configured. The same guide records the service boundary: Invoicing owns invoice-specific integration tests, while Notification owns generic notification tests without biller/invoice fixtures.

Every merged PR starts production deployment followed by the separate Python read-only suite. There is no independent production gate. The agent that merged the PR must monitor the terminal result and immediately investigate/fix failures.

Production environment configuration is **required**: variables `TEST_INVOICE_BILLER`, `TEST_INVOICE_ID`, and secret `TEST_INVOICE_TOKEN` identify the existing production test invoice. The test does not generate one, copy a test invoice into production, seed or clean up any data, call a write endpoint, send/receive queue messages, or send notifications.

```sh
python scripts/read-only-deployment.py --require-fixture --report artifacts/production-read-only.json
```

The route allowlist permits only GET health and invoice HTML reads on the configured service origin. Redirects, all other routes and all non-GET methods are rejected before network access. Five checks require the exact deployed merge SHA, missing-invoice behavior, missing-token behavior, successful existing test-invoice HTML and wrong-token rejection. Reports omit tokens, response content and customer fields. All five checks from the same client run inside the feature HTTP integration case, while its valid temporary test invoice still exists; local tests exercise write/route/redirect rejection and missing required fixture configuration.

## Remaining coverage and evidence

See [delivery policy](delivery-and-operations.md) for author-created PRs after successful verification, the designated reviewer request, one current-head approval, review fixes, merging and production monitoring. Q25 assigns PR creation to the human or Codex branch author; the workflow only publishes readiness. Earlier owner corrections removed the extra production gate, independent-reviewer restriction and separate sink.

The resource-token cases above cover the application's invoice-item ownership grant for synthetic principals. External entitlement administration and profile provisioning, provider receipt/inbox behavior, delivery confirmation and operational recovery remain separate integration work. B07's full research acceptance is not claimed solely from these cases. [Progress](implementation-progress.md) distinguishes historical verified runs from the current changed code and its pending CI evidence.
