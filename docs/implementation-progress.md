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

Run [41053195543](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/41053195543), commit `030b805daa50647bd72118fc725ed5c72745ef1c`, passed 121 unit tests, 23 local integration tests, eleven Node tests, thirteen Python tests, all eight deployed cases and all five read-only checks against test. Application coverage was 701/766 lines (91.51%), with no authored-source exclusions. Both builds had zero warnings/errors. The persistent invoice was created and its non-secret IDs were supplied to the owner from `test-invoice-fixture.json`; recipient and tokens remain secret. The run **failed overall** because GitHub rejected automatic PR creation with HTTP 403: Actions was not permitted to create PRs.

The main ruleset audit confirmed one approval, stale-review dismissal, resolved threads and no bypass actors. It also identified the incorrect pre-merge `prod` deployment requirement and missing required status. The owner was asked to use `test`, require `Test deployment verified`, and enable Actions PR creation because the available connection cannot administer those settings. These are external configuration blockers, not reasons to bypass review or deploy production before merge.

Final source review found a cleanup edge case: fixture creation was registered for cleanup before Table storage acknowledged it. The follow-up registers only successful creations, checks the run-specific customer before deletion and uses the invoice ETag. A ninth deployed case exercises a real duplicate-key rejection and verifies that the existing row remains unchanged and is not additionally claimed for cleanup. This follow-up requires its own complete feature CI result before review and merge.

## PR #6 review follow-up (2026-10-31, Pacific/Auckland)

Run [41054903186 attempt 2](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/41054903186/attempts/2) succeeded for `b4cc04816679eb338e1124120f9cf1d169f5cd22`, including nine deployed cases and five read-only checks against test. It used the configured reference-invoice variables and masked recipient secret and automatically created [PR #6](https://github.com/niobium-nz/Niobium.Invoicing/pull/6). Main remained `a14aca1de948589e1e47b44e179e8a84a8f0537a`. The owner clarified that the agent must remain actively watching reviews rather than end work when the PR is ready; AGENTS and the delivery guide now state this explicitly.

| Review thread / plan item | Change | Acceptance evidence / remaining dependency |
| --- | --- | --- |
| 4429287868 / B04 | Remove feature-side status/PR write job; only `pr-readiness.yml` checks out its immutable main workflow commit and performs repository writes. | Node guards reject feature contexts before any write. Trusted listener must first be installed on main. |
| 4429287874 / B04 | Authenticate pipeline Git blobs against trusted main before accepting job/step success; bind workflow completion to its actual run ID. | Regression cases reject no-op/mismatched workflow/helpers, missing trusted files and reuse of an older green run. Pipeline changes deliberately need reviewed activation, not self-certification. |
| 4429288013 / delivery | Serialize package release jobs with `queue: max` and cancellation disabled. | Official GitHub syntax supports up to 100 pending jobs; unrelated merges cannot replace/cancel an existing publication within that queue. |
| 4429288017 / delivery | Inspect `filename` and `previous_filename` for Contracts changes. | Node cases cover additions, deletions, renames into/out of Contracts and unrelated paths. |
| 4429288020 / B07 | Keep reference seeds temporary until successful HTTP issuance; clean an acknowledged seed immediately on issuance failure. | Two deployed cases verify success retention and failed-issuance deletion through actual test resources. |
| 4429288023 / B07 | Increase deployed-test timeout from 10 to 30 minutes. | Accommodates the roughly 15-minute cumulative asynchronous/read-only wait budget and additional request/cleanup time. Abrupt process/runner termination remains an operational limit. |

The initial trusted-controller installation is an explicit unresolved administrative dependency: main currently lacks the listener, and GitHub only runs `workflow_run` listeners already on the default branch. This branch can run its real tests but must not issue its own required status to unlock itself. No protection bypass, direct-main push or fabricated verification status is authorized or performed. The exact reviewed head, new CI evidence and an owner decision about initial activation are required before merge. Production retains the requested merged-PR trigger and has no new independent gate.

Local review validation: both Release builds pass with zero warnings/errors; seventeen Node and thirteen Python cases pass; changed documentation links and CRLF-aware whitespace checks pass. Actionlint 1.7.12 validates the deployment and trusted-controller workflows. It does not yet recognise the officially documented `concurrency.queue` property, so the package workflow reports that single validator error; its remaining syntax passes on a temporary projection excluding only that property. The real workflow retains `queue: max`. This is a recorded validator limitation, not a claim that the full package workflow passed actionlint. No package publication was run to test the concurrency change.

### Second review of PR #6

Run [41147127986](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/41147127986) passed for `ab0ac9514bb6b1ee992d48c2cca7d120a1e04d24`: 121 unit, 23 local integration, 17 Node, 13 Python and 11 deployed cases; five read-only checks against test; 91.51% authored application line coverage. Both Release builds had zero warnings/errors. The six first-review threads were answered and resolved after this evidence. The agent continued watching and received four further findings on that head.

| Review thread / plan item | Change | Acceptance evidence / remaining dependency |
| --- | --- | --- |
| 4429543060 / B04 | Preserve pending production runs with workflow-level `queue: max` and cancellation disabled. | Each queued merge retains its deployment/read-only run within GitHub's 100-pending limit. No production run is claimed before merge. |
| 4429543070 / B04 | Preserve pending test runs under the same workflow-wide queue, covering candidate and trusted integration. | Different feature branches cannot replace one another's pending runs or interleave deployment with either suite. |
| 4429543077 / delivery | Compare the merge commit's Contracts tree with its first parent using local Git, without the REST file-list cap. | Real temporary-repository tests cover over 3,000 changed files, add/delete/rename boundaries, unrelated files and missing-history failure. |
| 4429546450 / B04, B07 | Add a fresh trusted test runner that checks out immutable main, including all harness/project/build/helper inputs, and never feature code/artifacts. | Policy cases reject missing/skipped trusted tests, candidate-only success and a different main SHA. Initial main lacks the harness; that step is explicitly skipped and cannot certify itself. Reviewed administrative activation remains required. |

Local policy validation: `node --test scripts/delivery-policy.test.cjs` passes all 20 cases and `python -m unittest discover -s scripts -p 'test_*.py'` passes all 13 cases. The two workflows using `queue: max` each report actionlint 1.7.12's unsupported-property error; their other syntax is checked with temporary projections omitting only that property. The latest push must receive its own complete feature CI result; the previous green run above does not validate these changes. No package or framework upgrade is included. Package publication and trusted-main deployed execution cannot be claimed from local tests or the initial-installation candidate run.

### PR #6 completion

The final feature run [41149938107](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/41149938107) passed for `bbce7f103275a24255adae2153aec8426fa8aa3e`: 121 unit, 23 local integration, 20 Node, 13 Python and 11 candidate deployed cases; five read-only checks against test; 91.51% authored line coverage. Both builds had zero warnings/errors. The trusted-main harness was absent before installation, so its skipped step is not a passing trusted-suite result.

After the owner's explicit temporary protection change, current-head approval and resolution of all eleven review threads, PR #6 was squash-merged as `18323085062ad49abb62f85c6529e0b857037c9d`. The merge and tested feature trees were identical at `21317ba8cab2727d6795f2f07f18238eaaafe002`. Production run [41157164240](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/41157164240) passed deployment and all five read-only checks for that merge. Package run [41157164182](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/41157164182) succeeded with publication correctly skipped because Contracts did not change. The owner restored the normal `Test deployment verified` protection; required test deployment, one approval, stale-review dismissal, resolved threads, current main, squash-only merges and no bypass actors were verified. Signed commits remain intentionally disabled by owner decision. PR #6 monitoring is complete.

## Author-created PRs (B04 / Q25, 2026-10-31, Pacific/Auckland)

Baseline: latest `origin/main` at task start, `18323085062ad49abb62f85c6529e0b857037c9d`. Branch: `codex/author-managed-prs`, in the existing Niobium.Invoicing cloud workspace. The worktree was clean before this follow-up. Q25 supersedes historical workflow-created PR instructions above; it does not change deployment, review or production verification requirements.

| Acceptance item | Implementation and evidence | Remaining evidence |
| --- | --- | --- |
| Workflows must not create PRs | Remove `openPullRequest` and its caller from the trusted controller; remove its `pull-requests: write` permission. Regression cases require successful exact-SHA readiness with zero PR creation, including when no PR API is available. | Activate the reviewed change on main through the normal PR process. |
| Authors create PRs after verification | `AGENTS.md`, delivery/integration guides and Q25 assign creation to the human or Codex branch author after exact-head build, test deployment and both deployed suites succeed. | New feature CI and an author-created PR. |
| Request review and finish delivery | Project memory requires requesting `niobium-nz` (owner-authorized `5he11` for a `niobium-nz` author), verifying the request, handling automatic Codex and human comments, current-head approval, expected-head merge and terminal production verification. | Authorized author identity, recorded reviewer request, review and resulting production evidence. |
| Preserve protection and package compatibility | Pipeline authentication, candidate/trusted integration, coverage thresholds, approval logic and production read-only boundaries are unchanged. Niobium 4.0.60/4.0.61 and Notification.Contracts 2.0.7 remain unchanged. | Verify live checks and rules again before merge. |

Local checks from the repository root, with the existing SDK environment loaded:

| Exact command | Result |
| --- | --- |
| `node --test scripts/delivery-policy.test.cjs` | 20 passed. |
| `python -m unittest discover -s scripts -p 'test_*.py'` | 13 passed. |
| `dotnet build Niobium.Invoicing.slnx --configuration Release --no-restore --nologo` | Passed, zero warnings/errors. |
| `dotnet build src/Niobium.Invoicing.Deployment.Tests/Niobium.Invoicing.Deployment.Tests.csproj --configuration Release --no-restore --nologo --disable-build-servers -m:1` | Passed, zero warnings/errors. |
| `dotnet test --project src/Niobium.Invoicing.Core.Tests/Niobium.Invoicing.Core.Tests.csproj --configuration Release --no-build --no-restore` | 121 passed, zero failed/skipped. |
| `actionlint .github/workflows/pr-readiness.yml` | Passed with actionlint 1.7.12. |
| `git -c core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol diff --check` | Passed. |

An initial overlapping build of the two projects failed in the shared WorkerExtensions output; the next default build reported prematurely exited MSBuild child nodes. A sequential deployed-project build with build-server reuse disabled and one node passed. These local execution failures did not require source changes. Local build/test logs are under `/workspace/scratch/author-managed-*`; they are not live CI evidence.

For the transition, the owner confirmed that Actions PR creation is disabled and authorized `5he11` as reviewer for a PR authored by `niobium-nz`. The old controller on main still attempts PR creation until this change merges. After successful feature deployment/tests, the author creates the PR; rerunning the old controller then lets it reuse the existing PR and finish successfully. At resumption the tool still authenticates as `niobium-nz` and exposes only that installation, so acting as `5he11` is not yet verified. No ruleset weakening or synthetic status is part of this transition. New CI/CD, reviewer assignment and deployment evidence will be recorded in the author-created PR; local checks alone do not establish them.

[PR #7](https://github.com/niobium-nz/Niobium.Invoicing/pull/7) was created by `niobium-nz` after feature head `ce150a4a251c2cd427cd58b480a9b6636827c2a2` passed [run 41168696031](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/41168696031): both builds without warnings/errors, 121 unit, 23 local integration, 20 Node and 13 Python cases, and 91.51% authored application line coverage. The candidate and trusted-main suites each passed eleven deployed cases and five read-only checks against that same feature SHA. Both artifact digests and reports were verified. Unlike PR #6's initial installation, the trusted harness at `1832308` executed successfully. The old readiness controller first published the valid custom status, then failed its obsolete creation call because Actions PR creation was disabled. It passed on [attempt 2](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/41169231601/attempts/2) after the author-created PR existed; the PR-event readiness run also passed.

The owner then added `5he11` as a collaborator, and its review request and separate account connection were verified. The owner clarified that implementation and review activity must stay in separate sessions, preferably using the native background reviewer with findings on GitHub. Code Review and Security Review were explicitly requested as `5he11`; both completed without findings for `ce150a4`. That demonstrates the manual background path, not future automatic triggering or a formal approval. This documentation follow-up records the role boundary and requires its own complete feature CI/CD result. Native automatic-review settings remain outside the available connector's inspection/edit capabilities; the exact configured scope and trigger were requested from the owner. No duplicate polling task or review conversation was created.

### Native automatic review configuration

Head `25c53bf66b096517ab7146358bd9822a45f34622` passed [run 41170488210](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/41170488210): both Release builds had zero warnings/errors; 121 unit, 23 local integration, 20 Node and 13 Python cases passed; authored line coverage was 91.51%. Candidate and trusted-main suites each passed eleven deployed cases and five read-only checks against that feature SHA. Required readiness certification succeeded. Native code and security reviews explicitly requested for that head completed without findings, but no formal approval was submitted.

Owner-supplied screenshots established that review scope and trigger previously inherited personal preferences. The owner then confirmed saving code review for **All PRs / On every push** and security review for **All PRs / Whenever code review runs**, with both enabled. The delivery guide and project memory now record those exact settings, the separate reviewer assignment and the distinction between native findings and formal approval. This documentation push will exercise the automatic trigger on the existing PR; its current-head review receipt and complete CI/CD results must be recorded in PR #7 before merge. The earlier manual review and green run do not validate the new head. No duplicate PR, watcher, review conversation or infrastructure was created.

### PR #7 completion

The owner approved current head `03c778a30641d95ac7b1db5f2db9a9bc3949bd90` as `5he11` and merged PR #7 as `9983ebebcc47a6c91c58478fa0ef4f736b35ad76`. Both commits have tree `0953f43559db75f36aa6c3293b9d7c17d047c03d`. Feature run [41172691131](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/41172691131) passed both builds without warnings/errors, 121 unit, 23 local integration, 20 Node and 13 Python tests, 91.51% authored coverage, and eleven deployed cases plus five read-only checks in each candidate/trusted suite. Native automatic code/security reviews completed for that head without findings; the separate formal approval was verified before recording completion.

Production run [41175420836](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/41175420836) passed deployment and all five read-only checks against merge `9983ebe`. The downloaded report's digest and exact merge SHA were verified. Package run [41175420783](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/41175420783) succeeded with publication skipped because Contracts did not change. PR #7 monitoring is complete and its existing watch is disabled.

## Restore manual debug email (B04 / Q26)

Baseline: latest `origin/main`, `9983ebebcc47a6c91c58478fa0ef4f736b35ad76`, in the Niobium.Invoicing cloud environment. The clean feature branch `codex/restore-debug-email-tests` starts from that commit. History identifies `EmailInvoice.cs` immediately before removal commit `52b5df9`, with Functions key authorization and `GET /email/{issuer}/invoices/{invoice}`. The owner called it SendEmail; no function with that literal name was found. Restore the existing contract rather than inventing another route.

| Acceptance item | Change / evidence | Remaining work |
| --- | --- | --- |
| Preserve protected delivery while changing the expected API | First update the trusted deployed contract to accept absent-route 404 or key-denial 401 for missing/invalid keys, with an existing ready invoice and unchanged ETag. No application endpoint is added in this preparatory step. | Run feature CI, obtain review/approval and merge the test contract before endpoint restoration. |
| Restore the historical manual trigger | Q26 supersedes the old removal decision. Keep the name, route, Function authorization and current EmailFlow. | Restore source after the reviewed harness is installed; test forwarding, failures/cancellation and real host key enforcement/authorized test notification. |
| Preserve framework and production boundaries | No package, Contracts, protected pipeline or infrastructure changes. Production checks remain GET-only health and designated invoice reads; they must never call the side-effecting email route. | Verify each exact feature head and merged production revision. |

Baseline local verification used the existing SDK environment (`source /workspace/.niobium-tools/env.sh`): `dotnet build Niobium.Invoicing.slnx --configuration Release --no-restore --nologo --disable-build-servers -m:1` passed with zero warnings/errors; `dotnet test --project src/Niobium.Invoicing.Core.Tests/Niobium.Invoicing.Core.Tests.csproj --configuration Release --no-build --no-restore` passed all 121 cases without skips. New CI/CD evidence will be recorded on the author-created PR; baseline success alone does not validate the preparatory change.

Preparatory local checks also passed: the same integration-test command with `NIOBIUM_INVOICING_TEST_TABLE_ENDPOINT=http://127.0.0.1:18482/devstoreaccount1` ran all 23 cases without skips against a disposable Table-only Azurite instance; `dotnet build src/Niobium.Invoicing.Deployment.Tests/Niobium.Invoicing.Deployment.Tests.csproj --configuration Release --no-restore --nologo --disable-build-servers -m:1` completed with zero warnings/errors. The deployed cases require the real test deployment and were not run against production or locally substituted infrastructure.

### PR #8 prerequisite completion

`5he11` formally approved `ad478d6f7cdb1213810c44e587b9419251cb3968`; native code/security reviews had completed without findings and no unresolved threads remained. Feature [run 41176255273](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/41176255273) passed both builds without warnings/errors, 121 unit, 23 local integration, 20 Node and 13 Python tests, 91.51% authored coverage, actual test deployment, twelve candidate cases and eleven trusted-main cases. Both suites passed all five read-only checks against the feature SHA; artifact digests/TRX/reports and the required custom status were verified.

PR #8 was squash-merged through the connector with the expected approved head as `7a44348e7778de6374ef14151a2413681e2f21fa`. Its tree matches the tested prerequisite tree `469ab429ffa925bb794e417f3a3ae2a12b7b1fee`. Production [run 41178564831](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/41178564831) passed deployment and all five GET-only checks for that actual merge; the downloaded report's digest and commit were verified. Package [run 41178564856](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/41178564856) succeeded with publication skipped because Contracts did not change. The trusted test contract is now installed through ordinary review, with no protection exception.

### Endpoint restoration implementation

Branch `codex/restore-debug-email` is based on the approved PR #8 squash merge `7a44348e7778de6374ef14151a2413681e2f21fa`. Its tree matches the tested prerequisite head, so the prepared implementation was preserved while moving to that current main baseline. `EmailInvoice.cs` is restored byte for byte from `52b5df9^`, preserving the historical Function-key guard, GET route, current EmailFlow delegation and empty 201 result. No framework/Contracts versions or pipeline definitions change.

Local integration now verifies exactly one key-guarded email binding, waiting for flow completion before returning an empty 201, forwarding biller/invoice/cancellation, and propagating send failures or cancellation. New deployed cases strictly require 401 for missing/invalid keys and bearer-only calls, 201 plus persisted freeze/token/enqueue acknowledgement for a valid test key using the approved mailbox, and unchanged storage for the historical missing-recipient skip. They read the existing test app's default function key with the workflow Azure identity, match its ARM hostname to the tested deployment and never print the key or CLI output. The production read-only client remains unchanged and never invokes this side-effecting route.

Local validation on this implementation:

| Command (after loading the existing SDK environment) | Result |
| --- | --- |
| `dotnet restore Niobium.Invoicing.slnx --nologo` | Passed with the existing package versions. |
| `dotnet build Niobium.Invoicing.slnx --configuration Release --no-restore --nologo --disable-build-servers -m:1` | Passed, zero warnings/errors. |
| `dotnet restore src/Niobium.Invoicing.Deployment.Tests/Niobium.Invoicing.Deployment.Tests.csproj --nologo` | Passed. |
| `dotnet build src/Niobium.Invoicing.Deployment.Tests/Niobium.Invoicing.Deployment.Tests.csproj --configuration Release --no-restore --nologo --disable-build-servers -m:1` | Passed, zero warnings/errors. |
| `dotnet test --project src/Niobium.Invoicing.Core.Tests/Niobium.Invoicing.Core.Tests.csproj --configuration Release --no-build --no-restore --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml --results-directory /workspace/scratch/restore-debug-coverage/unit` | 121 passed, no skips. |
| `NIOBIUM_INVOICING_TEST_TABLE_ENDPOINT=http://127.0.0.1:18482/devstoreaccount1 dotnet test --project src/Niobium.Invoicing.Integration.Tests/Niobium.Invoicing.Integration.Tests.csproj --configuration Release --no-build --no-restore --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml --results-directory /workspace/scratch/restore-debug-coverage/integration` | 26 passed, no skips, against disposable Table-only Azurite; emulator stopped afterward. |
| `python scripts/check-coverage.py /workspace/scratch/restore-debug-coverage/unit/coverage.cobertura.xml /workspace/scratch/restore-debug-coverage/integration/coverage.cobertura.xml` | Passed: Contracts 100.00%, Core 90.72%, Server 86.21%, combined 91.56%. |
| `git -c core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol diff --check` | Passed. |

The policy/tooling commands `node --test scripts/delivery-policy.test.cjs` and `python -m unittest discover -s scripts -p 'test_*.py'` also passed all 20 and 13 cases on the restoration worktree.

The generated Functions metadata contains `EmailInvoice` at `email/{issuer}/invoices/{invoice}`, GET, `authLevel: Function`. This and local tests are not live-host authorization evidence: candidate/trusted deployment tests and automatic review remain required for the pushed restoration head.


## Read-only debug invoice view (Q27)

Baseline: current `origin/main` at `788313e5c51f1d41dd96d54fe5806d37a6142d00`, tree `ebafa2e974cf8334db0ca3a7b6d32942a2abce76`, verified in the Niobium.Invoicing cloud environment. The baseline solution build had zero warnings/errors and all 121 unit tests passed during the preceding token-lifecycle investigation. Work is isolated on `codex/debug-invoice-view`; completed PR #8/#9 worktrees are preserved.

The owner retained the saved-token/freeze rule and requested privileged debug viewing instead of creating tokens during invoice upsert. `DebugInvoice` adds `GET /debug/{issuer}/invoices/{invoice}` with Functions key authorization. An explicit debug flow shares the encoded invoice/line renderer, avoiding optional authentication-bypass flags on the public endpoint. Responses use `Cache-Control: no-store`. No Contracts, package versions, workflow definitions, infrastructure or production read-only client changes are included.

| Acceptance item | Implementation and local evidence | Live delivery requirement |
| --- | --- | --- |
| Function-key authorization | Binding test plus generated Functions metadata require named DebugInvoice, GET and authLevel Function. Six added deployed cases cover missing/invalid keys, bearer-only denial, draft/frozen views and missing invoice. | Real candidate test deployment and authorization checks, plus unchanged trusted-main suite. |
| View saved data without side effects | Shared renderer covers encoded text/lines, no token disclosure, no save/token/freeze/enqueue; actual Table adapter test verifies unchanged invoice/item ETags, no broker calls and subsequent draft edit. | Candidate suite checks unchanged real invoice/item ETags and draft/frozen editing rules. |
| Preserve public access policy and legacy links | Unit/component checks retain saved-token authorization. Candidate cases require missing/wrong-token rejection even when a Functions key is present, and identical HTML for a valid frozen link. | Existing trusted tests and all five read-only checks must pass against the feature SHA. |
| Deliver through normal review | Author creates PR after terminal feature CI, requests 5he11 and handles native/human feedback. | One current-head approval, expected-head squash merge, production GET-only report and package workflow; no independent production gate. |

Local verification after `source /workspace/.niobium-tools/env.sh`:

```sh
dotnet restore Niobium.Invoicing.slnx --nologo
dotnet restore src/Niobium.Invoicing.Deployment.Tests/Niobium.Invoicing.Deployment.Tests.csproj --nologo
dotnet build Niobium.Invoicing.slnx --configuration Release --no-restore --nologo --disable-build-servers -m:1
dotnet build src/Niobium.Invoicing.Deployment.Tests/Niobium.Invoicing.Deployment.Tests.csproj --configuration Release --no-restore --nologo --disable-build-servers -m:1
dotnet test --project src/Niobium.Invoicing.Core.Tests/Niobium.Invoicing.Core.Tests.csproj --configuration Release --no-build --no-restore --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml --results-directory /workspace/scratch/debug-invoice-coverage/unit
NIOBIUM_INVOICING_TEST_TABLE_ENDPOINT=http://127.0.0.1:18482/devstoreaccount1 dotnet test --project src/Niobium.Invoicing.Integration.Tests/Niobium.Invoicing.Integration.Tests.csproj --configuration Release --no-build --no-restore --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml --results-directory /workspace/scratch/debug-invoice-coverage/integration
python scripts/check-coverage.py /workspace/scratch/debug-invoice-coverage/unit/coverage.cobertura.xml /workspace/scratch/debug-invoice-coverage/integration/coverage.cobertura.xml
node --test scripts/delivery-policy.test.cjs
python -m unittest discover -s scripts -p 'test_*.py'
git -c core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol diff --check
```

Both final Release builds passed with zero warnings/errors. All 123 unit, 34 opted-in local integration, 20 Node policy and 13 Python tooling cases passed; no local test was skipped. Disposable Table-only Azurite ran on port 18482 and was stopped afterward. Combined authored line coverage passed at 91.82% (Contracts 100.00%, Core 90.92%, Server 87.88%). An initial test fixture omitted a required configuration initializer and an HTTP assertion expected an added charset; both test-only errors were corrected before these final results. Deployed authorization is not established by local metadata or these tests: terminal candidate/trusted CI, native reviews, formal approval and production evidence will be recorded on the author-created PR for the exact head/merge.
