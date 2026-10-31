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

Cleanup tracks an invoice only after its creation succeeds, checks its run-specific customer identity before deletion, and uses its current ETag. A rejected or uncertain creation never schedules an existing row for deletion. The collision case verifies HTTP 409 from real Table storage, an unchanged invoice ETag and no additional cleanup claim.

A newly seeded reference is tracked as temporary until HTTP issuance returns success. Failed issuance immediately cleans that owned seed even if class initialization fails before framework cleanup. Only a successfully issued reference is removed from temporary tracking. Two deployed cases verify the retained/removed records through real Table storage, then remove their own temporary reference-case data. The outer deployed-test step permits 30 minutes: the health, two queue-processing, notification and read-only waits alone can consume about 15 minutes, so the former 10-minute timeout could terminate the process before cleanup. Abrupt runner termination can still prevent cleanup; this is not an atomic fixture lifecycle.

Store the approved mailbox in the test environment secret `TEST_NOTIFICATION_RECIPIENT`. `TEST_INVOICE_BILLER` and `TEST_INVOICE_ID` can select the existing reference invoice used to identify the test biller. When both are absent, the owner-authorized fixture setup retains a reference invoice in real test storage and writes its IDs to `artifacts/deployed/test-invoice-fixture.json`. It reuses an existing test biller when available, or a synthetic biller snapshot when the test table is empty. A working downstream Invoice template for that biller is still necessary for actual email delivery; this suite does not invent a Notification template. Tests use synthetic recipient/customer data and snapshot fixtures; external profile provisioning and provider receipt handling are not covered.

Cleanup deletes only the suite's temporary invoice/item records; it verifies item ownership markers. It retains the reference fixture requested by the owner and never deletes shared tables. Abrupt termination may leave temporary test records. Credentials, tokens, notification payloads and fixture response bodies must not be logged.

## Read-only production suite

Every merged PR starts production deployment followed by the separate Python read-only suite. There is no independent production gate. The agent that merged the PR must monitor the terminal result and immediately investigate/fix failures.

Production environment configuration is **required**: variables `TEST_INVOICE_BILLER`, `TEST_INVOICE_ID`, and secret `TEST_INVOICE_TOKEN` identify the existing production test invoice. The test does not generate one, copy a test invoice into production, seed or clean up any data, call a write endpoint, send/receive queue messages, or send notifications.

```sh
python scripts/read-only-deployment.py --require-fixture --report artifacts/production-read-only.json
```

The route allowlist permits only GET health and invoice HTML reads on the configured service origin. Redirects, all other routes and all non-GET methods are rejected before network access. Five checks require the exact deployed merge SHA, missing-invoice behavior, missing-token behavior, successful existing test-invoice HTML and wrong-token rejection. Reports omit tokens, response content and customer fields. All five checks from the same client run inside the feature HTTP integration case, while its valid temporary test invoice still exists; local tests exercise write/route/redirect rejection and missing required fixture configuration.

## Remaining coverage and evidence

See [delivery policy](delivery-and-operations.md) for author-created PRs after successful verification, the designated reviewer request, one current-head approval, review fixes, merging and production monitoring. Q25 assigns PR creation to the human or Codex branch author; the workflow only publishes readiness. Earlier owner corrections removed the extra production gate, independent-reviewer restriction and separate sink.

Real direct-resource entitlement grants, external profile provisioning, provider receipt/inbox behavior, delivery confirmation and operational recovery remain separate integration work. B07's full research acceptance is not claimed solely from these cases. [Progress](implementation-progress.md) distinguishes historical verified runs from the current changed code and its pending CI evidence.
