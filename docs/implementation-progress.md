# Research plan implementation

## Baseline and source

The supplied `invoicing-plan.md` is readable in full: 175 lines, 34,257 bytes,
SHA-256 `1321b7712c4c9e10b477d03b230e12fce5485fbde3375d94c374fc96290313eb`.
Its recommendations are task input; the user's instructions and recorded owner
decisions govern implementation. The report's citation markers are not new evidence.

The selected managed cloud environment is Niobium.Invoicing. Its workspace is
`/workspace/Niobium.Invoicing`, with origin
`https://github.com/niobium-nz/Niobium.Invoicing.git`. The initial checkout was clean.
The user corrected the requested baseline from master to main on 2026-10-30
(Pacific/Auckland). The implementation baseline is therefore
`e2f7269676359d85cc7e4308d4babfd1f6f0b39c`, on feature branch
`codex/invoicing-reliability-main`.

The superseded master baseline was `275beb0b326c3fa5daf43533ec0a6778409828ac`.
Its restore/build and 26 tests passed. Those results do not describe main.
[PR #3](https://github.com/niobium-nz/Niobium.Invoicing/pull/3) was created separately
with no file changes and instructions to preserve master ancestry. At final
verification GitHub reports it merged, the remote master ref is absent, and main
still resolves to the baseline above. This agent did not merge or delete branches.

Applicable `AGENTS.md`, project documentation, workflow triggers and package
references were inspected. Main uses the 4.0.60/4.0.61 package graph and Notification
Contracts 2.0.7 recorded in [framework provenance](niobium-integration.md).
Matched source commits `1fa044968f504ef575021720c728a17dc72bb067` and
`c2462746614ee6c768d725c99e3c54df2e3848c8` were inspected as read-only references.
No framework changes or package upgrades are part of this branch.

## Baseline verification

Commands run from the repository root with .NET SDK 10.0.403. This workspace's
reviewed `/workspace/.niobium-tools/env.sh` supplies the SDK and package cache.

| Command | Main baseline result |
| --- | --- |
| `dotnet restore Niobium.Invoicing.slnx --nologo` | Passed using the configured package cache and feeds. Not a clean-cache certification. |
| `dotnet build Niobium.Invoicing.slnx --configuration Release --no-restore --nologo` | Failed with 408 pre-existing style errors after the baseline `.editorconfig` change. |
| `dotnet build Niobium.Invoicing.slnx --configuration Release --no-restore --nologo -p:EnforceCodeStyleInBuild=false` | Diagnostic build passed, zero warnings/errors. This does not replace the failing normal build. No project suppression was added. |
| `dotnet test --project src/Niobium.Invoicing.Core.Tests/Niobium.Invoicing.Core.Tests.csproj --configuration Release --no-build --no-restore` | Passed: 60 tests, zero skipped, against the diagnostic build. |

A synthetic probe against a task-local Azurite Table instance confirmed that
Niobium.Database.StorageTable 4.0.60 reads ETags and rejects a stale update with 412,
but drops ETags from create/update return values. This narrows U08; it does not
establish aggregate concurrency or live Azure behavior.

## Plan reconciliation and acceptance ledger

The report's IDs are retained. Partial work is not completion of the entire epic.

| ID | Acceptance criteria retained from the report | Baseline reconciliation and work status |
| --- | --- | --- |
| B01 | Load persisted aggregate; preserve identity/token/snapshots; conditional create/update; distinguish exact replay from edits; freeze at enqueue; reject stale/concurrent edits; propagate item failures and recover on retry. | Implemented for cooperative Core writers under Q21: load/snapshot preservation, conditional preparation/freeze, exact replay, ETag conflicts, verified item read-back, definitive-failure recovery and conservative blocking of uncertain storage writes. Unit and real-adapter local tests pass. No transaction, direct-writer protection or automatic abandoned-writer takeover is claimed. |
| B03 | Shared Core validation for HTTP/queue; nonempty identities; ownership/item linkage; supported currencies/cycles; unique nonnull lines; due-date default and ordering; checked arithmetic; settlement currency while allowing overpayment. | Implemented and tested in shared Core. U01 negative/zero/credit/rounding policy remains unchanged. Q19 resolves the default zero-USD ambiguity: zero is currency-neutral; nonzero settlement must match. |
| B02 | Stable outgoing identity/content on exact replay; downstream concurrency-safe inbox/claim/provider outcome and duplicate/crash recovery tests. | Invoicing responsibility implemented: frozen content/token/endpoint, stable ordering/formatting, exact replay serialization tested. Downstream inbox belongs to Niobium.Notification, outside the target repository; provider recovery protocol remains unresolved. Invoicing must continue republishing replays. |
| B07 | Separate opt-in integration project; isolated data/cleanup; real DI, ETag/stale writes, partial failure, HTTP auth/public token view, queue ingestion/emission and replay coverage. | Partially implemented: separate opt-in project, isolated/cleaned local tables, real registration and adapters, ETag conflicts, partial checkpoint repair, public token HTML and HTTP component checks. Deployed Functions/middleware, Service Bus and controlled Azure sink remain blocked by unavailable setup and the no-deployment boundary. |
| B04 | Build/unit -> test deploy -> integration -> production dependency; PR never deploys production; docs-only changes do not deploy; debug email route absent/disabled in production. | Partially implemented: PR production eligibility removed; verify -> test deployment -> production dependency added; docs-only automatic events filtered; debug function removed and compiled-route tested. Local integration runs before deployment. Full post-deployment promotion gate remains blocked on B07. |
| B06 | Explicit Delivering/Delivered lifecycle; idempotent confirmations; cancellation with zero settlement; concurrency tests and legacy-record interpretation. | Blocked by U05: confirmation event/authentication and provider outcomes are undefined. Cancellation during Delivering and legacy migration semantics need decisions. No new public contract or migration will be invented. |
| B09 | Decide Particulars/Reference precedence, null-versus-empty semantics and Template support; test request -> persistence -> rendering. | The decided display subset is implemented and tested. Q20 resolves precedence: nonnull trimmed values override defaults, null inherits and empty clears. Implemented with persistence/rendering tests; Template selection remains deferred. |
| B08 | Append-only historical versions/actor/reason across material mutations and soft delete; five-year retention; tested backup/restore and RPO/RTO. | Blocked by version-store/legacy compatibility and U04 retention clock, purge/privacy, backup and recovery decisions. No deletion or production migration. |
| B05 | Reopen key-isolation decision; compatible schema, migration/dual reads/backfill/rollback and ownership tests. | Explicitly deferred under Q16. Ownership entitlement correction already completed under Q15; do not change invoice-item keys. |
| B10 | MCP over Core flows with approved tool/auth contracts; payment attempts/idempotency, mandates, exact remaining-balance charge and reconciliation. | Separate product epics blocked by U05 contracts/provider/credentials/scheduling decisions. No accounting scope. |

## Delivery boundaries

No deployment, package publication, workflow dispatch, real queue consumption,
production data access or production migration is authorized. Submission commits
use `[skip ci]` while the existing workflow can deploy on PR events. Local checks
and unexecuted workflow changes are reported separately from deployed evidence.

Implementation changes, final commands/results and remaining decisions follow.

## B03 and B09 changes

`InvoiceValidation` runs in the shared UpsertFlow and validates again at the domain mutation boundary. Checked contract arithmetic prevents silent line/grand-total wraparound. Command sanitation only fills a missing customer ID, allowing inconsistent supplied IDs to be rejected. Domain mapping preserves the due-date default and implements Q19/Q20. The eight original upsert scenarios now use valid synthetic NZD parties and a reusable repository fixture.

Verification: `dotnet test --project src/Niobium.Invoicing.Core.Tests/Niobium.Invoicing.Core.Tests.csproj --configuration Release --no-restore -p:EnforceCodeStyleInBuild=false` passed 97 cases (zero skipped). The normal-build style failure remains pre-existing. `InvoiceValidationTests` covers invalid nested inputs, identity/ownership, currencies, dates, checked arithmetic, zero/negative quantity compatibility, command sanitation, and request-to-persisted-to-rendered display values.


## Completed milestones and evidence

| Commit | Plan items | Changes and acceptance evidence |
| --- | --- | --- |
| `ba0ec7c` | B03, decided B09 subset | Shared validation, checked arithmetic, safe command sanitation, due default, zero settlement and null/empty display precedence. InvoiceValidationTests plus the existing upsert cases cover the accepted rules and rendering. |
| `5f97799` | B01, Invoicing part of B02 | Conditional create/update, persisted preparation/freeze state, snapshot/default/token/endpoint preservation, exact replay, stale/conflicting edits, definite item failure repair and unknown-write blocking. InvoiceReplayTests and updated EmailFlowTests exercise failure ordering and stable serialized messages; JSON hides storage checkpoints. |
| `52b5df9` | Local B07, debug-route part of B04 | Separate integration project with real DI/Table adapters, ETag/stale/concurrent checks and known partial checkpoint repair. Installed token parser and ASP.NET results are executed; invalid IDs return 400 and mismatches 403. Debug EmailInvoice function removed and compiled assembly checked. |
| `bb11cf4` | Independent B04 safeguards | Verification before test deployment; production depends on successful test deployment and main push/manual dispatch; no PR production; Markdown/docs-only automatic triggers filtered. Local checks do not replace the missing deployed integration gate. |

Q21 resolves preparation/freeze storage compatibility for this implementation.
Seven nullable Table columns are additive and excluded from invoice JSON. Old
records are read without a backfill. [Recovery and compatibility](invoice-recovery.md)
describes conservative legacy freezing, failure states, direct-client/mixed-server
risks and the absence of automatic takeover for an unknown writer. No schema keys,
framework code or existing NuGet dependency versions were changed.

## Original verification before the main formatting update

All commands ran from `/workspace/Niobium.Invoicing` after sourcing the reviewed
`/workspace/.niobium-tools/env.sh`. Logs remain in the task workspace at
`/workspace/scratch/invoicing-evidence/`; they are not repository artifacts.

| Exact command | Result |
| --- | --- |
| `dotnet restore Niobium.Invoicing.slnx --nologo` | Passed with the configured cache/feeds; clean-cache restore not established. |
| `dotnet build Niobium.Invoicing.slnx --configuration Release --no-restore --nologo` | Failed with the same 408 Contracts style errors as main baseline (IDE0055/IDE0161), zero warnings. No unrelated style cleanup or project suppression added. |
| `dotnet build Niobium.Invoicing.slnx --configuration Release --no-restore --nologo -p:EnforceCodeStyleInBuild=false` | Passed, zero warnings and errors; diagnostic qualification remains explicit. |
| `dotnet test --project src/Niobium.Invoicing.Core.Tests/Niobium.Invoicing.Core.Tests.csproj --configuration Release --no-build --no-restore` | Passed 114 cases, zero failures/skips, against the diagnostic build. |
| `NIOBIUM_INVOICING_TEST_TABLE_ENDPOINT=http://127.0.0.1:18482/devstoreaccount1 dotnet test --project src/Niobium.Invoicing.Integration.Tests/Niobium.Invoicing.Integration.Tests.csproj --configuration Release --no-build --no-restore` | Passed 16 cases, zero failures/skips: four local Table/flow and twelve HTTP component cases. |
| `/workspace/scratch/invoicing-evidence/actionlint -shellcheck= -pyflakes= .github/workflows/azure-deploy.yml .github/workflows/package-publish.yml` | Passed with actionlint 1.7.7. Shellcheck/pyflakes were not run. |
| `git diff --check e2f7269676359d85cc7e4308d4babfd1f6f0b39c` | Passed for the complete implementation diff. |

Additional local inspection validated the workflow dependency graph, six
production event/ref combinations (including PRs with a main ref), path filters
and changed-document relative links. No workflow was dispatched to validate these
changes. The full code diff and matched framework persistence/batch paths were
reviewed. Local test tables are removed by fixture cleanup, and the disposable
in-memory Azurite process is stopped after verification.

## Blockers, remaining decisions and limits

| Item | Specific work or decision still required |
| --- | --- |
| B02 | Implement the concurrency-safe inbox in Niobium.Notification, with provider acceptance/timeout/crash recovery rules and tests. That repository is outside the target scope. Repeated publication remains intentional. |
| B07 / final B04 | The follow-up below authorizes and implements test deployment/host/queue verification and reviewed promotion. Confirm its exact pushed CI result; direct-resource entitlement and external-service/recovery integration remain outside this suite. |
| B06 / U05 | Approve confirmation event identity, authentication, provider outcome mapping, duplicate/out-of-order rules, cancellation while Delivering, and legacy delivery-state interpretation. No confirmation or cancellation contract invented. |
| B08 / U04 | Decide audit/version schema and consumer compatibility, retention start/exception/purge policy, backup/restore ownership and RPO/RTO. No append-only version store, soft-delete migration, automated deletion or restore claim implemented. |
| B05 / Q16 | Explicitly deferred. Reopen key migration/dual-read/backfill/rollback compatibility before changing InvoiceItem partitions. Existing cross-biller collision risk remains. |
| B09 | Template selection stays deferred as agreed; decide resource lookup, compatibility and fallback semantics before implementation. Decided display-field precedence is complete. |
| B10 / U05 | Approve MCP tool/auth contracts and payment-provider/mandate/webhook/scheduling/credential ownership. These remain separate product work. |
| U01 | Negative/credit/fractional quantity and rounding policy remains unchanged; no new business rule was inferred. |
| Rollout | Review/quiesce legacy servers and direct table writers that bypass checkpoints. Unknown abandoned storage writers require authorized operator recovery; no unsafe automatic takeover is supplied. |

The branch is ready for code review with these limits; the complete research
roadmap is not finished. The implementation was submitted as PR #4 to main with skip-CI commits,
not a deployment approval. No production data, production migration, package
publication, real queue consumption or notification sending was performed.


## PR #4 conflict-resolution follow-up

Integrated main `9ea7280b2ef1271313555f6bee6425ccd4352421` (the enforced C#
formatting commit) into `codex/invoicing-reliability-main`, preserving the original
implementation baseline and PR history. Eight files conflicted. Resolutions retain
the PR's validation, preparation/freeze, replay, token and notification changes
while adopting main's file-scoped namespaces, formatting and CRLF convention.

A temporary Roslyn syntax check compared all 24 C# files changed by the upstream
commit and all 24 resolved files against their previous versions. Compiler tokens
match after normalizing namespace syntax and using order; no implementation logic
was changed by the conflict resolution. No unresolved merge markers remain.

Verification in the same cloud workspace, after sourcing the reviewed SDK setup:

| Command | Result |
| --- | --- |
| `dotnet build Niobium.Invoicing.slnx --configuration Release --no-restore --nologo` | Passed with zero warnings/errors and style enforcement enabled. The earlier 408-error baseline issue is resolved by the main formatting commit. |
| `dotnet test --project src/Niobium.Invoicing.Core.Tests/Niobium.Invoicing.Core.Tests.csproj --configuration Release --no-build --no-restore` | 114 passed, zero failures/skips, against the normal Release build. |
| `NIOBIUM_INVOICING_TEST_TABLE_ENDPOINT=http://127.0.0.1:18483/devstoreaccount1 dotnet test --project src/Niobium.Invoicing.Integration.Tests/Niobium.Invoicing.Integration.Tests.csproj --configuration Release --no-build --no-restore` | 16 passed, zero failures/skips, using a disposable local Azurite instance. |
| `git -c core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol diff --check origin/main` | Passed, retaining whitespace checks while recognizing main's CRLF line endings. |

Logs and the temporary syntax checker are in
`/workspace/scratch/invoicing-merge-review/`. Test tables are cleaned by the suite
and the local emulator is stopped after verification. The merge commit uses
`[skip ci]` to avoid deployment/publication side effects on submission. Remaining
roadmap decisions and deployed integration gaps above are unchanged.


## Feature delivery policy follow-up (2026-10-30)

Owner instruction Q22 supersedes the earlier prohibition on test deployment and skip-CI submission: push feature changes directly, confirm the latest commit's complete CI/CD result after every change, deploy and integration-test test before creating a requested PR, and require review before production. No PR is created automatically. Production deployment, production data and migrations remain outside this feature task.

The branch `codex/test-deployment-gates` starts at conflict-resolution commit `0bdf34b894a8109285001ea28d22c8838d830ddd`, which already contains main `9ea7280b2ef1271313555f6bee6425ccd4352421`. The original implementation baseline remains `e2f7269676359d85cc7e4308d4babfd1f6f0b39c`. The owner subsequently merged PR #4 as `a14aca1de948589e1e47b44e179e8a84a8f0537a`; this feature is based on that current main commit. The agent did not merge it.

| Plan / policy item | Change and acceptance evidence | Remaining limits |
| --- | --- | --- |
| B04 / Q22 | All feature pushes, including docs and slash branches, run local checks, deploy test and run deployed integration under one environment lock. Main production and package publication require the reviewed PR head's prior successful deployment and identical source tree. Manual/PR deployment and feature package publication are removed. | Required status/review branch protection needs repository administration. Workflows cannot prevent PR object creation. Listener activates when merged to the default branch. |
| B07 / Q22 | Standalone deployed suite checks exact binary SHA, middleware/HTTP status, saved records, replay/HTML/token freeze, real incoming/outgoing Service Bus and missing debug route. Notifications use a controlled test sink. | Actual deployed outcome must be read from the pushed commit's workflow; external profile, direct-resource entitlement, Notification provider and operational recovery remain separate. |
| B03 / test quality | Seven added public calculation/construction cases, seven transport boundary cases and stronger real-storage HTML verification. Coverage gate is 90% combined / 80% per module. Ten Node and seven Python tests exercise the delivery policy/tooling. | Coverage is authored line coverage, not proof of every branch or deployed behavior. |
| Documentation | AGENTS, delivery, integration, API and testing pages record the standing policy, exact gates, safe fixture boundaries and external settings. | No production review/merge authority is granted to the agent. |

Local verification from the repository root after the reviewed SDK setup:

| Exact command | Result |
| --- | --- |
| `dotnet build Niobium.Invoicing.slnx --configuration Release --no-restore --nologo` | Passed with zero warnings/errors. |
| `dotnet build src/Niobium.Invoicing.Deployment.Tests/Niobium.Invoicing.Deployment.Tests.csproj --configuration Release --no-restore --nologo` | Compiles separately; no Azure deployment is inferred from compilation. |
| `dotnet test --project src/Niobium.Invoicing.Core.Tests/Niobium.Invoicing.Core.Tests.csproj --configuration Release --no-build --no-restore --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml --results-directory /workspace/scratch/invoicing-delivery-evidence/unit` | 121 passed, zero skipped. |
| `NIOBIUM_INVOICING_TEST_TABLE_ENDPOINT=http://127.0.0.1:18484/devstoreaccount1 dotnet test --project src/Niobium.Invoicing.Integration.Tests/Niobium.Invoicing.Integration.Tests.csproj --configuration Release --no-build --no-restore --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml --results-directory /workspace/scratch/invoicing-delivery-evidence/local-integration` | 23 passed, zero skipped. |
| `python scripts/check-coverage.py /workspace/scratch/invoicing-delivery-evidence/unit/coverage.cobertura.xml /workspace/scratch/invoicing-delivery-evidence/local-integration/coverage.cobertura.xml` | 697/762 authored lines, 91.47%; Contracts 100%, Core 90.72%, Server 84.81%; gate passed. |
| `node --test scripts/delivery-policy.test.cjs` | 10 passed. |
| `python -m unittest discover -s scripts -p 'test_*.py'` | 7 passed. |

Live evidence must be attached to the final delivery response with the exact commit and workflow URL. Local logs are under `/workspace/scratch/invoicing-delivery-evidence/`; uploaded CI reports are the durable per-commit evidence. Never label a pending, failed or skipped deployment run successful.

The first feature run [41037648966](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/41037648966) built cleanly, passed 121 unit and 23 local integration cases with zero skips, and measured 701/766 authored lines (91.51%; Server 85.54%). Azure package/provision/deploy succeeded. Credential preparation then rejected an empty/unusable `az functionapp show` hostname; deployed tests did not run and the overall workflow failed. The follow-up reads the explicit ARM hostname, refreshes the test signing-key reference, and adds regression coverage for test-resource selection. Production jobs were skipped. A subsequent run must pass before this branch is eligible for PR creation.

Run [41039036249](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/41039036249) for commit `1fffc481cf27c22eb31b0ae6b7127829b88a6cac` completed **successfully** on 2026-10-30. Build/local verification and test deployment/integration jobs both succeeded; production jobs were correctly skipped. The deployed command `dotnet test --project src/Niobium.Invoicing.Deployment.Tests/Niobium.Invoicing.Deployment.Tests.csproj --configuration Release --report-trx --results-directory artifacts/deployed` passed all seven cases, zero failed/skipped. Local coverage and deployed TRX artifacts were uploaded for that exact SHA. The ARM lookup and explicit test Key Vault refresh passed.

The final evidence/documentation follow-up also changes failed replay comparisons to report only a boolean mismatch, keeping link tokens and notification payloads out of assertion output. It must receive its own successful full feature run; the final delivery response records that latest-head result rather than reusing the older commit's success. No PR, production deployment, package publication or production migration was performed by this feature task. External main-branch protection administration and the remaining B07 integration areas above are still outstanding.


## Owner delivery corrections (2026-10-31, Pacific/Auckland)

Q23 replaces the earlier Q22 interpretation: automatically open the PR after successful feature deployment/tests, handle every actionable review comment, obtain current-head approval, then merge and monitor production. Main accepts no direct pushes. A merged PR immediately triggers production deployment and read-only deployed tests, with **no independent production gate**. Deployed tests use the actual environment resources; the separate notification sink and per-run signing-key rotation are removed. Q24 requires **one** approval from any approver GitHub permits, without an application policy excluding the PR owner.

The owner approved a test notification mailbox supplied through the environment secret `TEST_NOTIFICATION_RECIPIENT`. Production uses variables `TEST_INVOICE_BILLER`, `TEST_INVOICE_ID` and secret `TEST_INVOICE_TOKEN`. The owner requested generation of a persistent invoice in the real test environment so its IDs can be configured there; this is distinct from production, where fixture creation is prohibited.

Starting feature head: `a79abe2a2e7061b4e40f3649450aa14a4f8190b3`; main baseline remains `a14aca1de948589e1e47b44e179e8a84a8f0537a`. The prior green run validates the old implementation, not this correction.

| Item | Correction | Acceptance / remaining evidence |
| --- | --- | --- |
| B04 / Q23 | Feature push runs tests/deployment, then opens/reuses a ready PR. Agent fixes reviews, waits for one current-head approval and merges without asking again. Main-merge event starts production and read-only tests directly. | Confirm new feature CI, automatic PR creation, real review, merge and the resulting production run. External branch protection remains unverified. |
| B07 / Q23 | Tests use real test storage/queues/Notification namespace; no sink or table creation. Add persistent test reference fixture and real Notification enqueue case. | Provide generated test IDs from the uploaded artifact. Enqueue acknowledgement is not proof of provider delivery. |
| Production read-only | Five allowlisted GET checks use the existing configured production fixture; no mutation/setup/cleanup code or write-capable clients. | Local guard tests and feature-environment read-only checks run before review. Production can be verified only after the approved merge deploys. |
| Q24 | Remove author exclusion; one valid current-head approval suffices. | Tests cover author approval, another approver, stale/dismissed approvals and pending changes requests. GitHub's native permissions remain authoritative. |

Initial local checks: eleven Node policy tests and thirteen Python tests pass. Final build/CI results, generated test fixture identifiers, PR/review state and production outcome must be recorded from actual runs; they are not inferred from workflow declarations.
