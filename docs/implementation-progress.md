# Research plan implementation

## Baseline and source

The supplied `invoicing-plan.md` is readable in full: 175 lines, 34,257 bytes,
SHA-256 `1321b7712c4c9e10b477d03b230e12fce5485fbde3375d94c374fc96290313eb`.
Its recommendations are task input; the user's instructions and recorded owner
decisions govern implementation. The report's citation markers are not new evidence.

The selected cloud workspace is `/workspace/Niobium.Invoicing`, with origin
`https://github.com/niobium-nz/Niobium.Invoicing.git`. The initial checkout was clean.
The user corrected the requested baseline from master to main on 2026-10-30
(Pacific/Auckland). The implementation baseline is therefore
`e2f7269676359d85cc7e4308d4babfd1f6f0b39c`, on feature branch
`codex/invoicing-reliability-main`.

The superseded master baseline was `275beb0b326c3fa5daf43533ec0a6778409828ac`.
Its restore/build and 26 tests passed. Those results do not describe main.
[PR #3](https://github.com/niobium-nz/Niobium.Invoicing/pull/3) separately consolidates
master ancestry into main, with no file changes. It must use a merge commit to
preserve that ancestry. Neither branch has been merged or deleted by this task.

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
| B01 | Load persisted aggregate; preserve identity/token/snapshots; conditional create/update; distinguish exact replay from edits; freeze at enqueue; reject stale/concurrent edits; propagate item failures and recover on retry. | Fresh invoice plus force-upsert remains. ETag return-value behavior is now reproduced locally. Invoice/item writes and enqueue cannot form one transaction with the existing keys. In progress; aggregate operation ownership and ambiguous enqueue recovery must not be represented as solved by a single invoice ETag. |
| B03 | Shared Core validation for HTTP/queue; nonempty identities; ownership/item linkage; supported currencies/cycles; unique nonnull lines; due-date default and ordering; checked arithmetic; settlement currency while allowing overpayment. | Missing at baseline. In progress. U01 negative/zero/credit/rounding policy remains unchanged. Q19 resolves the default zero-USD ambiguity: zero is currency-neutral; nonzero settlement must match. |
| B02 | Stable outgoing identity/content on exact replay; downstream concurrency-safe inbox/claim/provider outcome and duplicate/crash recovery tests. | Stable message ID and saved-token reuse already implemented with tests. Downstream inbox belongs to Niobium.Notification, outside the target repository; provider recovery protocol remains unresolved. Invoicing must continue republishing replays. |
| B07 | Separate opt-in integration project; isolated data/cleanup; real DI, ETag/stale writes, partial failure, HTTP auth/public token view, queue ingestion/emission and replay coverage. | No tracked integration project. Local storage/DI checks can proceed. Live deployment, profile/identity resources and controlled Notification sink are unavailable and deployment is forbidden by the task. |
| B04 | Build/unit -> test deploy -> integration -> production dependency; PR never deploys production; docs-only changes do not deploy; debug email route absent/disabled in production. | Both deployment jobs currently qualify for PRs and are independent. Debug route is unconditional. Safety corrections can proceed; final promotion awaits meaningful B07 deployment checks and operational configuration. |
| B06 | Explicit Delivering/Delivered lifecycle; idempotent confirmations; cancellation with zero settlement; concurrency tests and legacy-record interpretation. | Blocked by U05: confirmation event/authentication and provider outcomes are undefined. Cancellation during Delivering and legacy migration semantics need decisions. No new public contract or migration will be invented. |
| B09 | Decide Particulars/Reference precedence, null-versus-empty semantics and Template support; test request -> persistence -> rendering. | Still ignored/null-overwritten at baseline. Q20 resolves precedence: nonnull trimmed values override defaults, null inherits and empty clears. Implemented with persistence/rendering tests; Template selection remains deferred. |
| B08 | Append-only historical versions/actor/reason across material mutations and soft delete; five-year retention; tested backup/restore and RPO/RTO. | Blocked by version-store/legacy compatibility and U04 retention clock, purge/privacy, backup and recovery decisions. No deletion or production migration. |
| B05 | Reopen key-isolation decision; compatible schema, migration/dual reads/backfill/rollback and ownership tests. | Explicitly deferred under Q16. Ownership entitlement correction already completed under Q15; do not change invoice-item keys. |
| B10 | MCP over Core flows with approved tool/auth contracts; payment attempts/idempotency, mandates, exact remaining-balance charge and reconciliation. | Separate product epics blocked by U05 contracts/provider/credentials/scheduling decisions. No accounting scope. |

## Delivery boundaries

No deployment, package publication, workflow dispatch, real queue consumption,
production data access or production migration is authorized. Submission commits
use `[skip ci]` while the existing workflow can deploy on PR events. Local checks
and unexecuted workflow changes are reported separately from deployed evidence.

Implementation changes, final commands/results and remaining decisions are appended
below as work completes.

## B03 and B09 changes

`InvoiceValidation` runs in the shared UpsertFlow and validates again at the domain mutation boundary. Checked contract arithmetic prevents silent line/grand-total wraparound. Command sanitation only fills a missing customer ID, allowing inconsistent supplied IDs to be rejected. Domain mapping preserves the due-date default and implements Q19/Q20. The eight original upsert scenarios now use valid synthetic NZD parties and a reusable repository fixture.

Verification: `dotnet test --project src/Niobium.Invoicing.Core.Tests/Niobium.Invoicing.Core.Tests.csproj --configuration Release --no-restore -p:EnforceCodeStyleInBuild=false` passed 97 cases (zero skipped). The normal-build style failure remains pre-existing. `InvoiceValidationTests` covers invalid nested inputs, identity/ownership, currencies, dates, checked arithmetic, zero/negative quantity compatibility, command sanitation, and request-to-persisted-to-rendered display values.
