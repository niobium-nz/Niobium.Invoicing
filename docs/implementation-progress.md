# Research plan implementation

## Authenticated history reader implementation: 2026-11-14

The next local increment exposes committed invoice/line history to the owning
biller through an authenticated GET route. The domain follows verified predecessor
references; bounded, expiring continuations preserve a page position across new
edits and reject cross-invoice reuse. Reads never write, send, scan abandoned
candidates or create legacy history. Wire DTOs move to Contracts with Core type
forwarding and unchanged stored JSON. [The history guide](invoice-history.md)
records the API, identity, cursor and compatibility boundaries.

Initial reader head `3ed3a784e899990f92e2f43cf5c9bf957346f770` passed the build job
and all 40 candidate deployed cases in [run 43629043886](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/43629043886).
The independent harness from writer main `ae65fb4d2bdf42353c84ee6344640bb999e97977`
then passed 38 of 39 cases: the oversized-history request returned HTTP 500 instead
of 400. The overall run failed. Both downloaded ZIP digests were verified; a
successful candidate did not substitute for trusted verification. No PR was opened
and that failed deployment was not blindly retried.

Local reproduction identified excessive allocation before size rejection, without
establishing the live HTTP 500's cause. The correction adds a model-owned lower
bound for JSON-encoded text and line structure before copies/fingerprints, retaining
the exact 16 MiB envelope check. Scalar line copies preserve the prior business
JSON and exclude storage metadata. Regression coverage includes escaped/Unicode
text, aggregate text, 100,000 short lines, unchanged existing/new/anchored state on
rejection, excluded metadata and an accepted exact-limit snapshot. Independent
probes confirmed 540 line/date/numeric/null/encoding combinations, byte-identical
copies and the exact accepted boundary. They measured full-domain allocation for
the short-line case falling from 123.70 MiB to 18.10 MiB; this is not a universal
request-memory guarantee or evidence of the live host's failure mechanism.

Both corrected Release builds pass with zero warnings/errors. All 245 unit and
116 actual Table/component cases pass without skips, with 94.59% combined authored
application coverage. The earlier binary-consumer probe verified the forwarded
types. The candidate suite now includes 43 cases, with 39 in the immutable-main
harness; fresh feature verification is required before PR creation. Native review,
independent current-head approval and terminal production/package verification
remain required. Soft deletion, retention/holds and tested restore remain unfinished
B08 work.

Corrected head `7b8d255649166e817fdac234dd0efbaa9232de49` passed the local CI job,
but [run 43632811255](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/43632811255)
failed candidate verification. Attempt 1 passed 35/43 cases with eight 40-second
client timeouts. One independently assessed diagnostic retry of the unchanged head
passed 33/43, with five timeouts followed by five HTTP 502 responses. Both attempts
first failed on the 100,000-line request; all three oversized-text cases passed.
The retry later passed the resource-token cases. Trusted verification skipped,
and both overall attempts failed. The five read-only checks occurred before the
failures and do not establish recovery. Both downloaded evidence ZIP digests were
verified; no PR or further unchanged-head retry is justified by these results.

An isolated Functions host using the pinned packages and actual handler/middleware
successfully bound the same shapes and reached a marked mock flow, but its host
and worker together peaked near 915 MiB RSS. This excludes the client and does not
establish a live memory kill or a production configuration. The next candidate
adds bounded read-only test-host aggregate diagnostics, including actual instance
memory and unversioned host events, after owned cleanup. It retains all 43 cases,
40-second request limits, immutable-main verification and production restrictions.
The diagnostic parser has nine local regression cases for output/injection bounds.

Diagnostic head `c6e003d2989118d63592d2f71b6e245316b4332d` also failed: run
[43637210729](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/43637210729)
passed 36/43 candidate cases, with six timeouts and one 502 after the many-line
case. The verified ZIP includes an ARM observation of 512 MB. Aggregate telemetry
failed independently at its query stage, so no live OOM/exit cause is established.
The KQL correction normalizes numeric types across its union branches.

The next correction selects 2048 MB and explicit HTTP concurrency 2 in application
composition for both environments, retaining the generic module boundary and
10-instance cap. It adds a concurrent rejection/post-rejection usability case and
reports actual deployed concurrency with memory. The candidate now has 44 cases;
all 39 trusted cases and 40-second request limits remain. The increased provisioned
memory and verification limits are recorded in [operations](delivery-and-operations.md#invoice-request-capacity).
A proposed host transport-size setting was not retained: local validation returned
a host binding failure before application execution. No transport-limit behavior
is claimed here. Current-head feature verification, native review and formal
approval remain required before merge and production/package verification.

Local verification of the narrowed capacity correction passed both C# Release
builds without warnings/errors and all 125 actual Table/component cases without
skips. A same-host real-binding/domain probe passed two concurrent large requests
with zero writes and an ordinary write afterward; peak combined RSS was about
1.45 GiB, excluding the client/emulator and without reproducing Azure limits.
Local Bicep validation could not restore pinned registry modules because the
workspace proxy returned HTTP 403. Successful CI compilation/provisioning and
actual deployed settings remain required; local template validation is not claimed.

The trusted-verifier prerequisite [PR #24](https://github.com/niobium-nz/Niobium.Invoicing/pull/24)
is delivered as `b702d8743ac1211b5b3389f630eea9768eebaa06`: production run
43621450032 passed all five GET checks on the actual merge, and package run
43621449958 correctly skipped unchanged Contracts. The downloaded report and
[terminal evidence](https://github.com/niobium-nz/Niobium.Invoicing/pull/24#issuecomment-6881551491)
were verified. The separate history-writing application's production verification
is recorded below; it does not certify this new reader increment.

## B08 committed snapshot implementation: 2026-11-14

Invoicing [PR #23](https://github.com/niobium-nz/Niobium.Invoicing/pull/23) is
delivered as `8b0e9faa1a2561486783ab886530f76313d92e5a`. Feature run 43607155583
passed, native review completed, and independent approval 5905234801 covered its
exact head. Production run 43608750098 succeeded with all five GET-only checks on
the actual merge. Its downloaded report ZIP matched SHA-256
`eda86adc7f42486458a95df180ea15a5bd2c746be904e3f0c1c6710efe166393`.
Package run 43608750148 published Contracts 2.0.36; the public package was downloaded
and its repository commit verified. This closes the cancellation increment's
pending delivery language below, not the full roadmap.

The next application increment records immutable invoice and line snapshots with
actor, reason and time. Rich domains own the transitions and commit ordering;
business-named models privately own keys, serialization and digest. The existing
Niobium repositories perform persistence. [The history guide](invoice-history.md)
describes the invoice-reference commit point, failure behavior, proposed versus
verified lines, legacy observed baselines and old-writer fencing. No current key,
legacy link or historical total is migrated.

Local solution and deployed-test builds passed. All 219 unit and 115 opted-in local
integration cases passed with 94.41% combined authored application line coverage.
The 24 Python tooling and 20 Node policy cases also pass. A local Bicep compile
could not restore the unchanged AVM modules because the workspace proxy returned
HTTP 403 for the public registry. Actual provisioning and candidate/trusted testing
subsequently succeeded on the initial PR #25 head; changed heads must repeat it.
An actual-adapter regression also verifies snapshots spanning more than one
100-operation Table batch and an unchanged invoice/history on exact replay.
New deployed coverage checks multipart snapshots, authenticated attribution,
retained previous lines/totals and exact replays through real test HTTP/storage.
Independent source review found that first-time token creation on a legacy invoice
captured history before initializing its target lifecycle state. The domain now
initializes that state before capture; unit and actual Table regressions verify a
matching committed snapshot and no invented external-change baseline on the next
legitimate reconciliation.
The independent trusted verifier was delivered in PR #24 as
`b702d8743ac1211b5b3389f630eea9768eebaa06`, with terminal production and package
verification. Initial PR #25 head `96c5115a` passed feature run 43622719859 with
38 candidate and 37 trusted deployed cases. Native review and independent review
5906266568 then identified three issues: item-only drift, unhandled snapshot size
and a current-model lifecycle downgrade. The correction reads/compares observed
lines while preserving proposed/partial-write semantics, returns domain-level
400/409 size failures before history persistence, and prevents removing an existing
history anchor. Thirteen unit boundary cases, two actual-adapter cases and additional
deployed drift/oversize checks cover these corrections. The first regression run
reproduced nine failures before the implementation fix.
Corrected head `422e5be96a03479f9025a3c27167a424255d258d` then passed feature run
43626303603: 219 unit, 115 Table/component, 39 candidate and 37 trusted deployed
cases, plus all five read-only checks in both deployed reports. Downloaded candidate
and trusted ZIP digests matched their published values. Native code/security review
completed, all three findings were resolved, and independent review 5906488222 by
`5he11` approved that exact head. Readiness passed; its early PR-event attempt was
retried once after feature prerequisites completed.

[PR #25](https://github.com/niobium-nz/Niobium.Invoicing/pull/25) was protected-squash
merged as `ae65fb4d2bdf42353c84ee6344640bb999e97977`, with the tested tree unchanged.
Production run 43628431431 and job 132549432828 completed successfully. The downloaded
report verifies the actual merge and all five approved GET-only checks; its ZIP
SHA-256 is `51dad649c1b4acea9efb50113016550381b8b87438dbfa69bb503c45f3aca002`.
Package run 43628431454 successfully uploaded Contracts 2.0.38 and symbols. The
public NuGet package was subsequently downloaded and its repository commit matched
the actual merge. Its SHA-256 is
`2f1cb9d4275671ee0b986fd119a37523166e9060897bce3102d264b91a4e0a70`.

This is the snapshot portion of B08. A history-reading API, soft deletion, validated
ten-year retention clock/holds, purge policy and tested recovery remain unfinished.
Notification receipts still need the separately requested provider/environment
configuration; Invoicing delivery projection cannot consume an unpublished contract.
The accepted payment, operator, template, MCP and post-MCP work remains active.

## Cancellation and historical eligibility implementation: 2026-11-14

The original research plan remains unchanged. Q29 records the accepted operator,
legacy-history, MCP and payment implementation recommendations. The overall roadmap
remains active, including the work scheduled after MCP.

Notification [PR #9](https://github.com/niobium-nz/Niobium.Notification/pull/9)
completed the live B02 recovery proof. Its production run
[43592105122](https://github.com/niobium-nz/Niobium.Notification/actions/runs/43592105122)
succeeded on actual merge `d34bffbc49b980d6c1c372f90a57c21846eccad4`.
The downloaded GET-only report ZIP matched SHA-256
`885a69f58365bea432853c41a60bbefc69c638820cbacbb279a20e6bc1269e40`.
Package run 43592105110 succeeded and correctly skipped unchanged Contracts.
This supersedes historical optional-cohort/live-proof prerequisites below.

The current Invoicing feature implements conditional cancellation, historical
payment reconciliation, preserved cancelled links and atomic actor/reason/operation
evidence. [The lifecycle guide](invoice-lifecycle.md) describes the code and limits.
Trusted-test compatibility [PR #22](https://github.com/niobium-nz/Niobium.Invoicing/pull/22)
merged as `97fed2ea3257ea01558c9861838d71fab0f90f41` after verified feature run
43601451194, completed native reviews and independent approval 5904727952. Its
active-invoice assertions include cancellation-absence regressions. The application
branch includes that main commit; local solution/deployed builds and all 169 unit /
77 integration cases pass, with 93.40% combined authored application coverage. Candidate deployed tests add ownership,
state/evidence, saved-link and competing-operation cases using existing test
resources. Exact-head CI, native review, independent formal approval and terminal
production/package evidence belong on the implementation PR; local tests alone do
not establish delivered behavior.

PR #23 native review identified three concurrency gaps in the initial head. The
correction permits a later PaymentOccurred finding after cancellation while keeping
both audits, defers new reconciliation during active non-cancelled preparation,
and merges post-enqueue acknowledgement against a freshly loaded conditional version.
Acknowledgement conflicts have at most three attempts; unknown outcomes propagate
and no acknowledgement retry sends again. Eleven unit regressions include the
original sender flow, competing writes, bounded retries and changed frozen identity;
two actual-adapter cases verify cached-domain cancellation/payment races. Deployed
coverage extends cancelled-history reconciliation and adds two active-claim cases.
The new head must repeat full feature/native/formal review before merge.

Notification provider receipts are implemented on its feature branch but still
require webhook configuration and successful deployed/trusted activation. Invoicing
has not yet consumed the published receipt contract. Payment transactions, complete
B08 historical snapshots/retention/recovery, operator recovery/resend controls,
templates, MCP and the remaining original-plan work are not completed by this
cancellation increment.


## B02 / B07 concurrent recovery evidence: 2026-11-14

This continuation starts from Invoicing main
`bbc978acdbc29583115638c270df6df9027fdbf3` and Notification main
`708b9c27774d6895a61e187c20ed75f60f985376`. The complete original
[research report](invoicing-plan.md) is readable and unchanged: 34,257 bytes,
SHA-256 `1321b7712c4c9e10b477d03b230e12fce5485fbde3375d94c374fc96290313eb`.
The parent ledger change is isolated on `codex/invoicing-concurrent-recovery-ledger`.

[Notification PR #5](https://github.com/niobium-nz/Niobium.Notification/pull/5)
adds five real-adapter integration cases to B02/B07. Two durable flows compete for
the same stored ETag; only one may schedule and submit. Four late provider outcomes
(acceptance, rejection, retry and hold) cannot overwrite a recovery worker's saved
acceptance. Each stale write receives HTTP 412, preserving the complete winning
record, provider evidence and first-submission boundary. The attempts retain the
same key and frozen bytes, and accepted replays remain suppressed after two days.

The [Notification ledger](https://github.com/niobium-nz/Niobium.Notification/blob/5485c840974cab0a86907f7defbbee173945bf28/docs/implementation-progress.md)
records the exact commands and limits. All nine local Table/Blob cases and 142
application cases passed, with 98.38% application coverage and the same six
pre-existing NU1603 warnings. The opt-in storage suite uses the actual pinned
Niobium 4.2.66 adapters against disposable loopback Azurite. Its controlled queue,
clock and HTTP replies do not establish live scheduling or provider deduplication.
Runtime code, package references, schema and environment activation are unchanged.

[Feature workflow 43540899694](https://github.com/niobium-nz/Niobium.Notification/actions/runs/43540899694)
passed on `5485c840974cab0a86907f7defbbee173945bf28`: 142 application, 20 Node and
46 Python checks, plus eight candidate and eight trusted deployed checks. The
trusted harness checked out immutable main `708b9c27774d6895a61e187c20ed75f60f985376`.
Both downloaded integration reports identify the feature head and all eight passes;
ZIP digests match GitHub metadata and upload logs. The candidate digest is
`d2c28bf448b65a21a478a2dd67b7e072acf57120f171918f2fcd3176c4ed4fca`; the trusted
digest is `05b3f0b83f514950ba61bb2c29ab46b1afe63b5954f6bc083c814a78a137b368`.
These deployed checks still exercise the existing synchronous send path.

Native code/security review completed on that feature head without reported
findings. The separate reviewer independently built and ran the nine storage cases
and recorded [approval 5900556073](https://github.com/niobium-nz/Niobium.Notification/pull/5#pullrequestreview-5900556073)
as `5he11`. The protected squash merge is
`72f100bfc5a742de42a4aa860e5e5e41585ff06f`, with actual tree
`69c9c6b15dea1c6d61d4dadfae063be0bbcd52c1`, matching the tested tree.

Production [run 43542466096](https://github.com/niobium-nz/Niobium.Notification/actions/runs/43542466096)
and job 132285923081 completed successfully at 01:49 UTC on 14 November. The
downloaded GET-only `/health`
[report 14092343424](https://github.com/niobium-nz/Niobium.Notification/actions/runs/43542466096/artifacts/14092343424)
identifies merge `72f100bfc5a742de42a4aa860e5e5e41585ff06f` and success.
Its ZIP SHA-256 `6c67809da84de4b3eb4e98164f88c48f53b6f837d367c1c1f9c4257feb12dfa7`
matches metadata and upload logs. Package
[run 43542466113](https://github.com/niobium-nz/Niobium.Notification/actions/runs/43542466113)
succeeded and correctly skipped publication because Contracts did not change.

| Plan item | Acceptance gained and remaining work |
| --- | --- |
| B02 / B07 | Actual adapter/domain concurrency and late-result recovery are now tested together. The storage project remains opt-in locally, outside automatic CI. |
| B02 activation / U06 | An approved new-operation cohort or verified historical suppression, quiesced legacy consumers, enabled live queue/provider evidence and an owner for held/dead-lettered outcomes are still required. No answer is inferred for the pending cohort/operator question. |
| B06 | Verified B02 remains a dependency; recipient mail-server acceptance and cancellation before any payment remain the accepted rules. |
| B01, B03, B04, B05, B08, B09, B10 | Existing acceptance, compatibility boundaries and post-MCP deferrals in the reconciliation below are unchanged. No migration, purge or new machine/payment authority is introduced. |

The previous parent-ledger [PR #19](https://github.com/niobium-nz/Niobium.Invoicing/pull/19)
is also verified complete. Production
[run 43173686392](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/43173686392)
and job 131032736462 succeeded at 07:17 UTC on 12 November. Its downloaded
[report 13924015254](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/43173686392/artifacts/13924015254)
identifies actual merge `bbc978acdbc29583115638c270df6df9027fdbf3`, all five
read-only checks and success. ZIP SHA-256
`40e50ff3aee06135093b3c3ce5cb247d5c3be4b8c7700667f759b89a6122b010` matches
metadata and logs. Package run 43173686526 succeeded and correctly skipped
publication. Its PR description now records the terminal result.

This parent-ledger update changes only this document. Local whitespace, relative
file links and the original-report digest/byte count passed. Its own exact-head
CI, review and delivery results are recorded on its author-created PR.

## B02 selective rollout and delivery evidence: 2026-11-12

This continuation uses Invoicing main
`36020f1dfa725dd7505b5fb961add8e5083fb585` and Notification main
`9857b67d1199713017dcc97cf64dc1bee985be21` as its starting baselines. The complete
original [research plan](invoicing-plan.md) remains unchanged: SHA-256
`1321b7712c4c9e10b477d03b230e12fce5485fbde3375d94c374fc96290313eb`.
The parent ledger update is isolated on `codex/invoicing-delivery-ledger`.

[Notification PR #4](https://github.com/niobium-nz/Niobium.Notification/pull/4)
adds optional sender-tenant selection for the existing B02 durable submission
switch. Selected commands reuse the existing rich domain, frozen payload and
operation identity. The entire comma-separated filter is validated before
routing; malformed entries cannot broaden activation. Empty command tenants are
rejected before either send path, including when the switch is disabled. The
false default and absent-filter global behavior for valid commands are preserved.

The verified feature head is `3646b16a7ee2dc3411f4f7688f99124bd4d0eec9`. The
[Notification ledger](https://github.com/niobium-nz/Niobium.Notification/blob/3646b16a7ee2dc3411f4f7688f99124bd4d0eec9/docs/implementation-progress.md)
records the exact commands, 142 passing application tests, 98.38% coverage and the
six pre-existing package warnings. Native and separate review identified the
empty-tenant boundary; four regressions reproduced it before the transport guard.

[Feature run 43168181582](https://github.com/niobium-nz/Niobium.Notification/actions/runs/43168181582)
passed 142 application, 20 Node and 46 Python tests, plus eight candidate and eight
trusted deployed checks. Both downloaded reports identify the exact feature head
and their ZIP digests match GitHub metadata and upload logs. The trusted harness
ran from the immutable Notification baseline above. Current-head native review
completed, the finding was resolved, and the independent reviewer recorded
[approval 5870221642](https://github.com/niobium-nz/Niobium.Notification/pull/4#pullrequestreview-5870221642)
as `5he11`. The early readiness invocation preceded completed feature tests; its
single retry passed once those tests were complete. The protected squash merge is
`708b9c27774d6895a61e187c20ed75f60f985376`, with the same tested tree.

[Production run 43170018582](https://github.com/niobium-nz/Niobium.Notification/actions/runs/43170018582)
and its production job completed successfully at 06:46 UTC on 12 November 2026.
The downloaded GET-only `/health`
[report artifact 13921329087](https://github.com/niobium-nz/Niobium.Notification/actions/runs/43170018582/artifacts/13921329087)
identifies merge `708b9c27774d6895a61e187c20ed75f60f985376` and records success.
Its ZIP SHA-256
`3bfda31fa6c5a33c478dc955764b99c43673b32dfe7bf14a151e8caa1d52393e` matches
GitHub metadata and upload logs. Package
[run 43170018518](https://github.com/niobium-nz/Niobium.Notification/actions/runs/43170018518)
passed and skipped publication because Contracts did not change.

| Plan item | Current acceptance and remaining work |
| --- | --- |
| B02 / Q28 #1 | Selective routing is implemented without enabling an environment or changing wire/storage formats. Enabled real queue recovery, scheduled wakes and provider replay/hold evidence remain separate acceptance work. |
| B02 cutover / U06 | Still needs an approved new-operation cohort or verified historical suppression, quiesced legacy consumers and an operator for held/dead-lettered outcomes. A tenant filter alone does not prove operation history. Keep enrolled tenants on the durable path while their operations can replay. |
| B06 | The required B02 activation/verification dependency remains. Recipient mail-server acceptance, cancellation before any payment and legacy enqueue interpretation remain the accepted rules; no lifecycle or payment-history change is claimed here. |
| B01, B03, B04, B07 | Existing implementations remain as reconciled below. The verified production result for PR #18 is recorded next. |
| B05, B08, B09, B10 | The existing post-MCP deferrals, history/recovery dependencies, template compatibility and concrete machine/payment contracts remain. This routing increment does not resolve them. |

### PR #18 production recovery completed

[PR #18](https://github.com/niobium-nz/Niobium.Invoicing/pull/18) merged approved
feature `10abab669fb13b8cfc99043533f93c2d1f9b7581` as
`36020f1dfa725dd7505b5fb961add8e5083fb585`. Production
[run 43095043731, attempt 2](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/43095043731/attempts/2)
completed successfully at 21:44 UTC on 11 November 2026. The first attempt passed
deployment and all five read-only checks, but artifact creation received HTML
instead of JSON and exhausted five upload requests. After terminal failure and
confirmation that main and production had not advanced, the failed job was
retried once under the standing delivery authorization. No confirmed platform
outage is inferred from that response.

The retry's downloaded report identifies the actual squash merge above and all
five passes: deployed commit, missing invoice, missing token, the configured
existing invoice HTML and wrong-token rejection. Artifact
[13893517846](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/43095043731/artifacts/13893517846)
has ZIP SHA-256
`b89767d7b176ee69ff2d08887c1b5390e5c432c379f0c283370f51aa6a4dd3d0`, matching
GitHub metadata and the upload log. Package
[run 43095043750](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/43095043750)
succeeded and correctly skipped publication because Contracts did not change.
The bounded recovery continuation is disabled. The successful steps from the
first attempt are not represented as a successful overall run.

This parent-ledger change preserves application code, package references,
workflows and the original research report. Local whitespace and relative-file
link checks passed, and `sha256sum docs/invoicing-plan.md` matched the digest above.
Its own exact-head CI, review and deployment evidence is recorded on the
author-created PR under the normal delivery process.

## Formal approval handoff corrected: 2026-11-12

The owner corrected a second premature stop at PR approval. Native code/security
review had completed for Invoicing #18 and Notification #3, but the implementation
agent had only assigned `5he11`; it had not invoked the recorded `pr_approver`
handoff and incorrectly marked the goal blocked. The historical handoff depended
on `/workspace/AGENTS.md`, which was absent in the resumed workspace, while some
repository wording conflated a genuine approval agent with delegated self-approval.

[The handoff procedure](pr-approval-handoff.md) is now versioned with each project
and required by its AGENTS.md. The implementation agent must invoke the separate
reviewer, supply the final head and evidence, handle findings and verify the actual
formal GitHub decision. Pending review does not end the goal. The implementation
agent still cannot approve its own change through `5he11`, bypass protection or
predetermine the reviewer's verdict. Native review remains independently required.
The separate approver session is active; current-head CI/review, merge and production
results belong in the PR, without claiming an approval before it is recorded.

## Delivery authority reaffirmed: 2026-11-12

The owner confirmed that automatic author-created/submitted PRs, native Codex
review and the agreed deployment process were already authorized. The previous
continuation incorrectly treated the original goal's no-deployment wording as a
current restriction and used `[skip ci]`. That was an agent error, not a missing
owner approval or an external deployment blocker.

This follow-up corrects AGENTS.md and the delivery guide so resumed goals retain
the latest owner authorization. It resumes normal feature verification for
[PR #18](https://github.com/niobium-nz/Niobium.Invoicing/pull/18), followed by ready
review, native review monitoring and fixes, current-head approval, then merge and
production read-only verification. No new permission is needed to create/submit
the PR or run its ordinary test deployment. The runtime implementation and the
original research plan remain unchanged. The PR records terminal workflow and
review evidence for the new head; earlier local results do not replace it.

Remaining B02 activation and later business/compatibility dependencies in the
reconciliation below do not block delivery of this independent B07 change.
Production data mutations, migrations and bypassing review are still prohibited.

The normal head `8df2e40e5796468d143291415be7740b66e6503a` passed
[Azure Deployment 43013720334](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/43013720334):
123 unit, 47 local integration, 20 Node and 24 Python cases, 92.14% application
coverage, and 27 candidate plus 27 trusted deployed cases. The immutable-main
readiness controller then published success. Marking PR #18 ready automatically
triggered Codex review of that head; its actionable P1 requested deployed exporter
verification. The follow-up adds a mandatory test-only Application Insights
assertion for all three queue instruments, scoped to the current workflow service
version and test window, using existing resources and identity. The candidate
suite is now 28 cases; the unchanged trusted baseline remains 27 until merge.
The new head must repeat the complete pipeline and automatic review before merge;
the earlier green run does not verify this added assertion.

The deployed assertion correctly failed in run
[43017086652](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/43017086652)
at head `90388deb6368d72e75ed0068d813e43785b85b11`: 27 cases passed, while the
current-version metrics assertion timed out. The installed Functions 1.2.0
resource detector runs after Platform's explicit service identity and replaces
its name/version with site/assembly defaults. A local regression reproduced the
override. Application registration now reapplies nonblank `SERVICE_NAME` and
`SERVICE_VERSION` after the detector, preserving its other metadata and its
fallback when a setting is absent. Four regression cases cover metric/trace
identity, bringing local integration to 51. The live assertion remains strict;
the next head must repeat deployment and automatic review. The shared resource
identity change and dashboard grouping implications are documented in the
[measurement guide](queue-observability.md#framework-integration).

After that fix, normal Release solution and deployed-harness builds passed with
zero warnings/errors. All 123 unit and 51 local integration cases passed, zero
skips, using disposable loopback Table storage. Authored application coverage is
92.25%: Contracts 100.00%, Core 90.92%, Server 91.61%. These local results do not
replace the next head's required deployed export assertion.

## Queue measurements continuation: 2026-11-11

The complete original report is available and unchanged: 34,257 bytes, SHA-256
`1321b7712c4c9e10b477d03b230e12fce5485fbde3375d94c374fc96290313eb`.
The selected managed workspace contains `/workspace/Niobium.Invoicing` with the
expected `niobium-nz/Niobium.Invoicing` origin. Freshly fetched main baseline:
`1d7bd30b848fa0592656e7ca4a9671603252b918`. The prior branch was clean; this work
uses `codex/invoicing-queue-observability` created from that exact main commit.
The normal Release baseline build and all 123 unit tests passed before edits.

The next unblocked implementation subset is **B07 / Q28 #13**: queue age,
processing latency and bounded processing outcomes. `InvoiceQueueTelemetry`
observes the actual Service Bus consumer, including parsing failures and awaited
issuance, through the existing Niobium OpenTelemetry pipeline. It preserves thrown
exceptions and cancellation, uses monotonic elapsed time, handles missing/future
enqueue times, and counts redeliveries as attempts. It emits no invoice, recipient,
message-ID or payload attributes. [The instrument guide](queue-observability.md)
records exact names, units and limitations. No public DTO, Table schema, package
version, framework source, workflow or deployment setting changes are included.

The restored graph still uses Niobium 4.0.60/4.0.61 and Notification.Contracts
2.0.7. Package metadata and corresponding framework source were rechecked:
Platform 4.0.61 at `c2462746614ee6c768d725c99e3c54df2e3848c8` owns metrics/exporters;
Functions 4.0.60 at `1fa044968f504ef575021720c728a17dc72bb067` composes that pipeline.
No latest-main framework behavior is substituted for these packages.

Current reconciliation retains all original IDs and acceptance criteria:

| ID | Current source/evidence and remaining dependency |
| --- | --- |
| B01 | `UpsertFlow`, `InvoicePreparation` and replay/storage tests retain conditional preparation, freeze and conservative uncertain-write recovery. Direct writers and cross-table atomicity remain outside those guarantees. |
| B02 | Notification PR #2 is merged at `adb9e59dd4428e1c8bf496fc40a42699bd003ba0`. Its durable inbox/retry implementation remains disabled by default. Activation needs quiesced legacy consumers, an approved new-operation cohort or verified historical suppression records, an operator for held outcomes, and enabled real queue/provider verification. The old draft-PR status below is historical. |
| B03 | Shared `InvoiceValidation` and validation/calculation tests already implement the accepted rules, including default dates, item linkage, checked arithmetic and allowed overpayment. Existing calculation policy stays compatible. |
| B04 | Reviewed feature/test and trusted-main readiness workflows are present. Q26/Q27 supersede the original debug-route-removal recommendation. Additional production workflow changes require reviewed activation of the trusted pipeline; this continuation does not change or certify them. |
| B05 | Explicitly deferred until after MCP under Q28 #8. Existing key and direct-SAS compatibility are preserved. |
| B06 | B02 activation/verification remains a dependency. The signed provider-event/internal-confirmation contract and treatment of legacy records without trustworthy payment-ever evidence still require concrete implementation review. Accepted delivery and cancellation business rules are unchanged. |
| B07 | Nine local measurement and four Functions identity cases bring component/storage integration to 51. The 28-case candidate deployed suite now requires actual export of all three queue instruments to the current test Application Insights deployment. Current-head live evidence is recorded on PR #18. Provider receipts, external Identity/profile integration and recovery remain separate evidence. |
| B08 | Ten-year history/soft deletion and one-hour RPO/four-hour RTO remain requirements. Committed aggregate/history consistency, direct Table/SAS writers, retention-clock/hold/privacy validation, recovery ownership and actual restore evidence are unresolved. No purge or migration is performed. |
| B09 | Q20 display precedence is implemented. Versioned template customization remains deferrable; compatibility for formerly ignored selectors must precede enforcement. |
| B10 | Read/draft MCP and durable payment records/manual reconciliation are the accepted starting scope. Concrete machine authority, payment identity/history and provider/recurrence contracts remain; no new authority is inferred. |

Verification used .NET SDK 10.0.403 and the existing package cache. Commands ran
from the repository root after sourcing the reviewed workspace tool environment:

```sh
dotnet build Niobium.Invoicing.slnx --configuration Release --no-restore --nologo --disable-build-servers -m:1
dotnet test --project src/Niobium.Invoicing.Core.Tests/Niobium.Invoicing.Core.Tests.csproj --configuration Release --no-build --no-restore --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml --report-trx --results-directory /workspace/scratch/invoicing-observability-unit
NIOBIUM_INVOICING_TEST_TABLE_ENDPOINT=http://127.0.0.1:18486/devstoreaccount1 dotnet test --project src/Niobium.Invoicing.Integration.Tests/Niobium.Invoicing.Integration.Tests.csproj --configuration Release --no-build --no-restore --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml --report-trx --results-directory /workspace/scratch/invoicing-observability-integration
python scripts/check-coverage.py /workspace/scratch/invoicing-observability-unit/coverage.cobertura.xml /workspace/scratch/invoicing-observability-integration/coverage.cobertura.xml
git -c core.whitespace=cr-at-eol diff --check
```

The build passed with zero warnings/errors. All **123 unit tests** and **47 local
integration tests** passed, zero skipped. Authored line coverage is **92.14%**:
Contracts 100.00%, Core 90.92%, Server 90.84%; all existing thresholds pass.
The existing Table fixture used an owned in-memory Azurite process on loopback
port 18486, cleaned its records/tables and was stopped afterward. The explicit
Git whitespace option respects the existing CRLF test-file convention.

Historical submission: the agent incorrectly reapplied the original goal's
deployment restriction and submitted a draft with `[skip ci]`. PR readiness then
failed because the feature commit had no authenticated deployment run. Manual
code/security review completed without findings, but could not replace that run.
The delivery-authority correction above resumes the already authorized normal
pipeline. This historical checkpoint does not authorize skipping current checks.

## Approved delivery follow-up: 2026-11-07

The owner requested merging the approved documentation PRs while keeping
Notification PR #2 open for its remaining review. This supersedes the earlier
no-merge/no-deployment boundary for Invoicing PR #17 and resumes the normal
[test deployment, review and production verification process](delivery-and-operations.md#required-delivery-sequence).
The original uploaded plan remains unchanged; runtime and package changes are
not part of this PR.

The automated review on PR #17 identified a mutable framework-guide link. It now
pins the exact Niobium documentation commit
`1e370fccd38635f7209c1c4b01ddafcbe10ea618`, which is included in merged Niobium PR #1.
The referenced file matches the merged guide. Relative link and whitespace checks
passed. This correction uses a normal feature push so the required deployment
checks can run. A valid approval for the resulting head and successful delivery
checks remain prerequisites; PR evidence records their outcome.

Niobium PR #1 and Store PR #2 were already merged by the owner. Their
[framework workflow](https://github.com/niobium-nz/Niobium/actions/runs/42337718517),
[Store deployment](https://github.com/niobium-nz/Niobium.Store/actions/runs/42337681464)
and [Store package workflow](https://github.com/niobium-nz/Niobium.Store/actions/runs/42337681547)
were observed completed successfully. A successful package workflow does not by
itself mean a package was published.

## Historical Notification domain review checkpoint: 2026-11-07

The owner asked for a DDD/data-access investigation and documentation in the three
application repositories and Niobium. The new [domain/data-access guide](domain-data-access.md),
linked from AGENTS.md and the documentation index, explains InvoiceDomain and
InvoicePreparation, repository mapping, conditional save, verified read-back,
storage compatibility and the limits of direct writers. Invoicing runtime code and
package versions remain unchanged.

The corresponding Notification refactor is commit
`eea196e71adfb24ad8d4cadc435f81e9506f7bfb` in
[Notification PR #2](https://github.com/niobium-nz/Niobium.Notification/pull/2).
It addresses all five owner review comments using enums, the existing options
class, explicit actions before conditions, and a rich submission domain backed by
Niobium's repository. The SDK-specific application store is removed. The generic
repository requires a provisioned table, so Notification declares that table in
its existing data account. The internal draft schema is version 2 and the new
option remains disabled; no historical sends are migrated or replayed.

Notification's Release build passed with zero warnings/errors, 96 application
tests and four actual Table/Blob adapter scenarios passed, and authored line
coverage is 98.15%. Full Bicep compilation is blocked by proxy 403 responses when
restoring existing AVM modules; syntax validation passed. Its
[ledger](https://github.com/niobium-nz/Niobium.Notification/blob/eea196e71adfb24ad8d4cadc435f81e9506f7bfb/docs/implementation-progress.md)
records commands, results and limits. Shared guidance is in
[Niobium PR #1](https://github.com/niobium-nz/Niobium/pull/1) and
[Store PR #2](https://github.com/niobium-nz/Niobium.Store/pull/2).

At this earlier checkpoint, relative file links and whitespace passed and the
original uploaded plan matched byte for byte with the checksum below. Invoicing
application tests were not rerun. Those draft commits used `[skip ci]` under the
then-current no-deployment/no-merge/no-publication restriction. The approved
delivery follow-up above supersedes that restriction for Invoicing PR #17; normal
verification and merge requirements now apply. This paragraph records historical
evidence, not current delivery instructions.

## Prior follow-through: 2026-11-06

This continuation starts from clean `origin/main` at
`74d7a8f877f38b943f106a8f4745c522fdb4d213`, on
`codex/invoicing-plan-follow-through` in `/workspace/Niobium.Invoicing`.
The original upload was absent from that baseline. It is now preserved byte for
byte in [invoicing-plan.md](invoicing-plan.md), indexed in the documentation and
included in [draft PR #17](https://github.com/niobium-nz/Niobium.Invoicing/pull/17).
Its checksum below matches the upload; instructions in the report do not override
the user's request or the recorded owner decisions.

The user explicitly included Niobium.Notification. Its clean main baseline was
`4a93385be1b09a9d97d0c2384e1085f254c7d84c`; B02 implementation is on
`codex/notification-durable-retries`, commit
`5ea944a7bec36978d9d8cc21d3cea94c3b23b754`, in
[Notification draft PR #2](https://github.com/niobium-nz/Niobium.Notification/pull/2).
The main ruleset was observed active with one approval, resolved review threads,
test deployment and the trusted `Test deployment verified` status required.
This read-only inspection is not a new deployed verification result.

| Plan ID | Current reconciliation |
| --- | --- |
| B01 | Existing conditional aggregate preparation/freeze, replay and conservative uncertain-write handling remain implemented. The cooperative-writer and nontransactional limits still apply. |
| B02 | Invoicing already republishes stable exact replays. Notification draft PR #2 adds conditional inbox claims, frozen provider payload/key, persisted 24-hour retry state, recovery wakes, accepted-send suppression and held outcomes. It is disabled by default until legacy-operation cutover and controlled live verification are complete. |
| B03 | Shared validation, identity/ownership checks, defaults and checked arithmetic already exist. Current quantity, credit and rounding compatibility remains unchanged. |
| B04 | Current protected delivery supersedes the early workflow notes below. Q26 restored the Function-key EmailInvoice route; Q27 added the read-only debug view. The original report's route-removal recommendation is not applied again. |
| B05 | Key migration remains deferred until after MCP under Q28; existing collision and compatibility risks remain recorded. |
| B06 | Follows B02. Signed provider events, authenticated internal confirmation and payment-ever evidence still need implementation; current Delivered remains enqueue evidence. No confirmation or cancellation behavior changes here. |
| B07 | Existing Invoicing local/deployed harnesses remain. Notification adds restart, lost-response, scheduling, concurrency and cutoff tests plus real local Table/Blob adapter coverage. Enabled cloud identity, Service Bus and provider integration remain unverified. |
| B08 | Ten-year history/soft deletion and one-hour RPO/four-hour RTO are accepted requirements, not completed controls. Aggregate/history commit consistency, direct-writer compatibility, retention-clock/hold/privacy validation and actual restore evidence remain. |
| B09 | Q20 display precedence is implemented. Versioned template selection remains deferred, including compatibility for legacy ignored selectors before enforcement. |
| B10 | Read/draft MCP and durable payment records/manual reconciliation remain the accepted starting scope. Concrete authorization, payment and recurrence contracts remain outstanding. |

Notification's Release builds passed with zero warnings/errors; 88 application
tests and four separately invoked local storage scenarios passed with no
failures or skips. The storage follow-through uses the pinned Niobium Blob adapter
and real Table client to verify frozen payloads and recovery after a lost outcome
write, whether that write committed or not. Queue/provider replies remain simulated.
Authored application coverage was 98.17%, with all module thresholds met; application
source and that test result are unchanged by the additional storage tests.
Its [ledger](https://github.com/niobium-nz/Niobium.Notification/blob/5ea944a7bec36978d9d8cc21d3cea94c3b23b754/docs/implementation-progress.md)
records exact commands and local evidence; the
[protocol](https://github.com/niobium-nz/Niobium.Notification/blob/5ea944a7bec36978d9d8cc21d3cea94c3b23b754/docs/durable-submission.md)
records activation prerequisites and operational limits. Invoicing changes are
documentation only: byte comparison, SHA-256 and `git diff --check` passed; its
application suites were not rerun for this continuation.

At the 2026-11-06 checkpoint, the task prohibited deployment, merging, package
publication, production data access and production migrations. Those draft commits
used `[skip ci]` under that earlier restriction and had no live readiness result.
The approved delivery follow-up above supersedes the Invoicing merge/deployment
restriction; production data mutations and migrations remain outside scope. B02 activation and the later milestones remain open;
the historical evidence below describes earlier changes, not this draft's CI.
Notification's [PR readiness run](https://github.com/niobium-nz/Niobium.Notification/actions/runs/42200884092)
failed because this head has no successful authenticated build/test deployment/
deployed integration run. No deployment or package run existed for that head at
inspection. This required delivery gate remains unresolved under the task boundary.

The remaining compatibility decisions are specific: B02 needs an identified
new-operation cohort or verified suppression evidence for historical sends; B06
needs the versioned confirmation contract and a treatment for legacy records with
no trustworthy payment-ever evidence; B08 must reconcile required history with
direct Table/SAS writers that can bypass Core or erase additive columns. The
accepted delivery, cancellation and ten-year retention business rules are not
being reopened. Live verification/restore exercises and production changes cannot
be used to resolve these gaps within this task's delivery boundary.

## Baseline and source

The supplied [original research plan](invoicing-plan.md) is preserved unchanged in
`docs/invoicing-plan.md` and is readable in full: 175 lines, 34,257 bytes,
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

The report's IDs are retained. Partial work is not completion of the entire epic. Later owner decisions supersede the original report where explicitly recorded: Q28 maps all thirteen accepted recommendations, changes B08 retention from five to ten years and settles B02/B06 business policy. Historical unresolved-decision rows below are qualified by that later acceptance.

| ID | Acceptance criteria retained from the report | Baseline reconciliation and work status |
| --- | --- | --- |
| B01 | Load persisted aggregate; preserve identity/token/snapshots; conditional create/update; distinguish exact replay from edits; freeze at enqueue; reject stale/concurrent edits; propagate item failures and recover on retry. | Implemented for cooperative Core writers under Q21: load/snapshot preservation, conditional preparation/freeze, exact replay, ETag conflicts, verified item read-back, definitive-failure recovery and conservative blocking of uncertain storage writes. Unit and real-adapter local tests pass. No transaction, direct-writer protection or automatic abandoned-writer takeover is claimed. |
| B03 | Shared Core validation for HTTP/queue; nonempty identities; ownership/item linkage; supported currencies/cycles; unique nonnull lines; due-date default and ordering; checked arithmetic; settlement currency while allowing overpayment. | Implemented and tested in shared Core. U01 negative/zero/credit/rounding policy remains unchanged. Q19 resolves the default zero-USD ambiguity: zero is currency-neutral; nonzero settlement must match. |
| B02 | Stable outgoing identity/content on exact replay; downstream concurrency-safe inbox/claim/provider outcome and duplicate/crash recovery tests. Q28 adds multiple exponential retries with jitter within 24 hours, then a hold/investigation for uncertain outcomes. | Invoicing replay responsibility implemented. Q28 #1/#5 authorizes the downstream Notification work after its delivery safeguards. Persist provider/window state, suppress confirmed sends beyond 24 hours and distinguish deliberate extra-copy operations. Invoicing keeps republishing exact replays. |
| B07 | Separate opt-in integration project; isolated data/cleanup; real DI, ETag/stale writes, partial failure, HTTP auth/public token view, queue ingestion/emission and replay coverage. | Partially implemented: separate opt-in project, isolated/cleaned local tables, real registration and adapters, ETag conflicts, partial checkpoint repair, public token HTML and HTTP component checks. Deployed Functions/middleware, Service Bus and controlled Azure sink remain blocked by unavailable setup and the no-deployment boundary. |
| B04 | Build/unit -> test deploy -> integration -> production dependency; PR never deploys production; docs-only changes do not deploy; debug email route absent/disabled in production. | Partially implemented: PR production eligibility removed; verify -> test deployment -> production dependency added; docs-only automatic events filtered; debug function removed and compiled-route tested. Local integration runs before deployment. Full post-deployment promotion gate remains blocked on B07. |
| B06 | Explicit Delivering/Delivered lifecycle; idempotent confirmations; cancellation and concurrency tests; legacy-record interpretation. Q28 strengthens the original zero-settlement condition to no payment ever having occurred. | Q28 #2-#4 resolves mail-server acceptance, authenticated persisted provider events/internal messaging, independent cancellation and legacy enqueue evidence with unknown confirmation. Implement after B02; additive wire/storage schemas and payment-history evidence still need design/test. |
| B09 | Decide Particulars/Reference precedence, null-versus-empty semantics and Template support; test request -> persistence -> rendering. | Q20 display precedence is implemented/tested. Q28 #9 defines future approved versioned templates, omitted-selector default, invalid-selector rejection and freeze pinning. Compatibility for legacy ignored selectors precedes enforcement; customization stays deferrable. |
| B08 | Append-only historical versions/actor/reason across material mutations and soft delete; ten-year retention (Q28 supersedes the report's five years); tested backup/restore and RPO/RTO. | Q28 #6/#7 accepts material-change invoice/line snapshots, soft deletion and one-hour RPO/four-hour RTO engineering targets. Validate the candidate financial-year retention clock, holds/privacy and recovery capability before purging or claiming controls. No production migration/deletion. |
| B05 | Reopen key-isolation decision; compatible schema, migration/dual reads/backfill/rollback and ownership tests. | Q28 #8 defers until after MCP, retaining Q16's collision risk. Ownership correction is complete under Q15; plan migration compatibility and rollback before wider multi-biller rollout. |
| B10 | MCP over Core flows with approved tool/auth contracts; payment attempts/idempotency, mandates, exact remaining-balance charge and reconciliation. | Q28 #12 starts MCP with read/draft and payments with durable records/manual reconciliation before a provider. Concrete privileged tool authority, provider/merchant/mandate/webhook/recurrence details remain. No accounting scope. |

## Historical delivery boundaries for the initial research

These initial-task restrictions were superseded by the delivery-policy follow-ups
and the approved delivery section at the top of this ledger. They do not authorize
skipping current verification.

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

## Historical blockers and limits at PR #4

This table preserves the original handoff. Q28 below supersedes its business-decision and downstream-scope blockers; it is not a request to approve those choices again. The current next milestone is Notification delivery protection plus B02, then B06.

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

## B07 invoice-item resource-token coverage

The owner requested work to resolve the remaining research blockers on 2026-11-04 (Pacific/Auckland). Baseline is current `main` at `31b11ebbd73117f7f8bb08c207ca4de0c5dae74f`, tree `209d38634af1cf974a73f1791e60aa851fa0c7bd`. Work is isolated on `codex/resource-token-integration`; completed worktrees and deliveries remain intact. The complete report is still readable and its SHA-256 remains `1321b7712c4c9e10b477d03b230e12fce5485fbde3375d94c374fc96290313eb`.

This B07 subset reuses the existing real test storage, bearer signing configuration and temporary invoice fixtures. Inspection matched OwnershipControl and AzureTableSignatureIssuer to Niobium 4.0.60 source `1fa0449`, and ResourceTokenMiddleware/SignatureService to 4.0.61 source `c246274`. The deployed positive case exposed missing test-host SAS signing configuration, which this change supplies through the existing test Key Vault. Application code, key schema, framework packages, workflows and the production verification allowlist are unchanged.

| Acceptance item | Change and evidence | Limit |
| --- | --- | --- |
| Real resource-control registration and storage ownership | Four local cases load the actual Server controls and real adapter; owned partitions receive only Query, while another biller, no partition and missing invoice receive no grant from any applicable control. | Synthetic principals have no external entitlement overrides. |
| Deployed authentication and scope | Five deployed rejection cases call the actual `/rsas` middleware with missing/invalid bearer credentials, another biller, no partition and a missing invoice. | Current-head deployment results are required; compilation alone is not live evidence. |
| Usable restricted SAS | One deployed case obtains a credential, reads its existing synthetic line through real test Table storage, rejects a second synthetic partition and an insert, and checks the original ETag. | SAS/credential bodies are never logged. Q16's shared invoice-ID collision risk and externally managed grants remain separate. |
| Test signing configuration | Bicep reads the existing test data-account key, passes it as a secure module parameter and stores it in the existing vault before applying the host's Key Vault reference. | No account/key creation or rotation; production retains its existing externally supplied setting. |
| Delivery and review | Preserve normal candidate/trusted test gates; author creates the PR after success, then rereads `/workspace/AGENTS.md` and delegates independent approval to `pr_approver`. | Merge only with current-head approval and repository checks, then verify production and package completion. |

Local commands ran after `source /workspace/.niobium-tools/env.sh` from the feature worktree:

```sh
dotnet restore Niobium.Invoicing.slnx --nologo
dotnet restore src/Niobium.Invoicing.Deployment.Tests/Niobium.Invoicing.Deployment.Tests.csproj --nologo
dotnet build Niobium.Invoicing.slnx --configuration Release --no-restore --nologo --disable-build-servers -m:1
dotnet build src/Niobium.Invoicing.Deployment.Tests/Niobium.Invoicing.Deployment.Tests.csproj --configuration Release --no-restore --nologo --disable-build-servers -m:1
dotnet test --project src/Niobium.Invoicing.Core.Tests/Niobium.Invoicing.Core.Tests.csproj --configuration Release --no-build --no-restore
NIOBIUM_INVOICING_TEST_TABLE_ENDPOINT=http://127.0.0.1:18482/devstoreaccount1 dotnet test --project src/Niobium.Invoicing.Integration.Tests/Niobium.Invoicing.Integration.Tests.csproj --configuration Release --no-build --no-restore
```

The baseline build and 123 unit cases passed. Both Release builds passed with zero warnings/errors; all 38 local integration cases passed without skips. Initial feature run [41808753569](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/41808753569) passed local checks but failed deployed integration: 26/27 cases passed and the owning biller's resource-token request returned 503. The pinned issuer requires `StorageTableOptions:Key`; the test workflow did not supply it, although ordinary managed-identity Table access succeeded. The trusted job skipped after that failure and is not pass evidence. The configuration correction requires a fresh successful deployment. Exact feature SHA, CI artifacts, review and production results will be recorded on the author-created PR after verification. B02/B06 business contracts, B08 compatibility/retention, B09 Template, B05 key migration and B10 product contracts remain separate decisions; this change does not silently resolve them.

For the configuration correction, `bicep build infra/main.bicep --outfile /workspace/scratch/resource-token-infra.json` (CLI 0.48.1) could not complete locally: the managed network proxy rejected registry-module downloads with HTTP 403. This is not a successful template validation. The normal feature workflow must compile/provision the template and pass both deployed suites before PR creation. `git diff --check` passed.

Follow-up run [41810676341](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/41810676341) compiled/provisioned the signing correction and the issued SAS successfully read its own item. It failed on an incorrect test expectation: Azure filtered the out-of-range point read to 404, rather than the expected 403. The corrected assertion follows the documented Table SAS range filtering and additionally verifies the other item still exists unchanged through the fixture credential. The write-permission assertion remains 403. Both failed runs are retained as diagnostic evidence; neither validates the final head.

## Infrastructure composition correction after PR #12

The owner requested reverting PR #11's test-specific SAS infrastructure after documenting the environment-agnostic composition contract. Baseline is main `023a4d78ade44159d4a508656a4ecf25c929e931`, tree `8fd15cf72a1414b390994114bbf157b72fd20086`. Work is isolated on `codex/revert-test-sas-plumbing`. The managed Niobium.Invoicing executor is unavailable, so source work uses the authorized GitHub Git-object API and verification runs in GitHub Actions; no local command results or worktree inspection are claimed.

- Restore both Bicep modules to their pre-PR #11 contents, removing the test-only key parameter, secret resource, conditional app setting and dependency. Existing host/application resource ownership and package/module versions are unchanged.
- The existing test configuration helper reads the same current data-account key with the authorized workflow identity, masks it and exports `APPSETTING_KV_STORAGETABLEOPTIONS__KEY`. The generic hooks own its app setting and existing vault secret. No account, key or resource is created or rotated.
- Generic parameter generation passes an empty placeholder for every `KV_` setting. Bicep uses its name to build the reference; the postprovision hook reads the real value directly from the runner. Ordinary settings keep their environment expressions. PowerShell-backed tooling cases cover case-insensitive reference-only generation, repeat generation and restoration; helper cases cover the selected existing account, masking and missing-key failure. Review also identified cached Key Vault references after postprovision writes. The generic secret hook now refreshes references after all writes succeed, with fake-CLI cases covering real runner-value forwarding, ordering, no-setting behavior and metadata/lookup/write/refresh failures. Actual account-key rotation is not performed for verification.
- Keep all B07 SAS boundary cases, existing candidate/trusted deployed suites and production GET-only allowlist. Production keeps its existing environment-secret source. No application behavior, Contracts, framework package or entitlement changes are included.

Required verification is `node --test scripts/delivery-policy.test.cjs`, `python -m unittest discover -s scripts -p "test_*.py"`, both Release builds, unit/component tests with coverage, actual test provisioning/deployment, candidate and immutable-main deployed tests, and both read-only test reports. Exact-head run URLs/results will be recorded on the author-created PR after execution.

The helper `scripts/test-resources.py` is a protected pipeline definition. Its reviewed change cannot certify itself against the unchanged main controller: `Test deployment verified` must remain blocked until an explicit administrative activation decision. Actual successful candidate/trusted tests and current-head review are still required. No rule exception, synthetic status, direct-main push or inherited PR #6 installation authorization is assumed.

## Business-decision follow-up (Q28)

Recorded 2026-11-06 (Pacific/Auckland), from current main `5cc8c4542bd1e6c3561296fed3ddcdd2b10e318a`, tree `61dddd6080a571e5b401c32e2374df18514e1d20`. The Niobium.Invoicing cloud workspace is available again. Work is isolated on `codex/record-business-decisions`; the existing worktrees remain untouched.

PR #13's infrastructure correction is complete: its actual merge has the tested tree. Production run [42024621841](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/42024621841) passed all five GET-only checks against that merge. The downloaded report ZIP matched SHA-256 `bdd2efab045bcf0678891d9c2f4690d3c329143ab79682eaaa47500bcc547d13`. Package run [42024621932](https://github.com/niobium-nz/Niobium.Invoicing/actions/runs/42024621932) succeeded with publication skipped because Contracts did not change. Ruleset 26202562 again requires `Test deployment verified`, test deployment, one approval, resolved threads and up-to-date main with no bypass actors. The preceding activation blocker is closed; no duplicate delivery watch is needed.

| Owner discussion item / plan mapping | Change and acceptance status | Remaining work |
| --- | --- | --- |
| #1 / B02 / Q11 | Exponential retries with jitter and identical key/payload within 24 hours; hold unknown outcomes afterward for investigation/explicit resend; durable confirmed-send suppression. | Implement/test downstream concurrency, provider attempts, persisted scheduling, restart/lost-response/window boundaries and deliberate extra-copy identity after Notification safeguards. |
| #2-#4 / B06 | Recipient mail-server acceptance defines Delivered; queued/submitted stays Delivering. Cancellation before any payment at any delivery stage; authenticated persisted provider events/internal confirmations; legacy confirmation unknown. | Add compatible lifecycle/event schemas and payment-history evidence. Test duplicates/out-of-order events, cancellation/send races and old records without fabricating delivery or resending. |
| #5 / B02/B06/B07 | Notification delivery safeguards are authorized prerequisites, before B02 then B06. | Recheck external protection and reviewer permission; prepare concrete feature/test/review/production changes before administrative handoff. Live main `989b7c4b36154c5da2cdd3391ec297e1f453ea14` remains unprotected with no rulesets at this check. |
| #6-#7 / B08 / Q08/R13/U04/U06 | Ten-year material-change audit/history and soft deletion; one-hour RPO/four-hour RTO engineering targets to cost/test. | Validate retention clock/privacy/holds for AU/NZ, assign operational ownership, inventory backups and test restores using existing test resources. No compliance guarantee, production migration or deletion. |
| #8 / B05/Q16; #10 / U01; #11 / U02/Q13 | After MCP: revisit biller-aware item-key isolation, investigate calculation/fractional/credit policy, implement versioned new-link expiry/revocation with controlled replacement. | Retain current keys/accepted collision risk, calculations/historical totals and existing emailed-link compatibility until that work. Investigate applicable AU/NZ authority requirements without assuming new US scope. |
| #9 / B09 | Current default template remains; future approved versions default absent selectors, reject invalid explicit ones and pin at freeze. | Handle legacy ignored selectors before enforcement; not a notification-reliability blocker. |
| #12 / B10/U05; #13 / B07/U06-U08 | Read/draft MCP first; payment records/manual reconciliation before provider. Real-service test principals/fixtures and initial operational measurements accepted. | Design scoped privileged actions, payment/recurrence contracts and fixture access. Measure queue age, uncertain sends, delivery failures and latency before further capacity/support targets. |

The owner supplied the complete thirteen-item recommendation text after the first documentation push, closing the source-reconciliation blocker. [Q28](decisions-and-risks.md#q28-business-decision-follow-up) now preserves every item and owner override, with essential agent memory in `AGENTS.md` and downstream semantics in [recovery](invoice-recovery.md). This change records accepted requirements; it does not claim their runtime implementation. Application code, packages, Contracts, infrastructure and protected pipeline definitions are unchanged. The updated head requires fresh feature deployment, candidate/trusted integration, review and production verification; the first head's run cannot validate it. Local documentation checks and live results are recorded on the author-created PR.

## Existing fixture configuration inventory (2026-11-06)

Baseline: main `50df240f8212af33a152a13bcce11c848d6b51a8`. The owner reconfirmed the existing test biller variable `TEST_INVOICE_BILLER` and approved mailbox secret `TEST_NOTIFICATION_RECIPIENT`. The [configuration inventory](environment-configuration.md) records repository/environment scope, stored versus runner names, dependency mapping, production fixture requirements and the separation from runtime Bicep settings. Repository and workspace agent memory preserve reuse of these identities. Production `TEST_INVOICE_TOKEN` remains consumed as `READONLY_INVOICE_TOKEN` by the required existing-invoice GET check. No values, runtime behavior, workflows, infrastructure or packages change in this documentation follow-up.

Local validation: `git diff --check` passed, and every relative Markdown target in the changed guides and new inventory resolved. Source mappings were checked against `azure-deploy.yml`, `EnsureReferenceInvoice` and `read-only-deployment.py`. Exact-head feature deployment, candidate/trusted suites, approval and merge/production results belong in the delivery checkpoint and PR; documentation-only work still follows the normal pipeline. The separate Notification workflow correction reuses the stored names, but its repository-scoped binding and first-installation protections remain independent delivery prerequisites.

## Independent Notification service boundary (2026-11-06)

Baseline: main `72071c83ab0b04edae6ad97455a3423bc29db874` after PR #15. The owner clarified that Notification is an independent shared service. The prior instruction to copy Invoicing's fixture settings into Notification and discover its test sender through an `Invoice` template introduced avoidable test coupling. That request and documentation guidance are withdrawn.

The configuration inventory and agent memory now keep invoice-specific end-to-end tests and existing invoice fixtures in Invoicing. Notification owns generic sender/template/recipient tests and retains its established `Tenant`/`Channel` contract; Invoicing translates biller identity only at its caller boundary. The approved Invoicing fixture inventory and active production `TEST_INVOICE_TOKEN` consumer are unchanged. This is a documentation correction with no runtime, workflow, infrastructure or package change in Invoicing. The separate Notification feature branch removes the coupled lookup and tests generic fixtures; its deployment/admin prerequisites remain separately tracked.


## Invoice history verifier preparation (2026-11-14)

Based on main `8b0e9faa1a2561486783ab886530f76313d92e5a` after PR #23. This
prerequisite updates the independent test harness before the separately implemented
B08 history writer changes lifecycle JSON to schema 2. It does not activate history,
change application code or relax any deployment gate.

The raw checkpoint verifier accepts only reviewed versions 1 and 2, requires the
schema-2 revision reference, and reads lifecycle evidence without an older codec
rewriting a newer fixture. Unknown schemas remain rejected. A schema-2 row cannot
be synthetically frozen: that material transition must use its history writer.
Saved-link/debug fixtures are instead fully seeded legacy frozen snapshots, with
owned lines, totals and token, before any HTTP mutation. Real reconciliation and
cancellation then use the deployed application; existing email/queue cases still
exercise the actual current freeze path.

Cleanup validates every root representation and all selected child identities
before invoking an awaited callback that conditionally deletes the owned invoice.
A schema-2 reference must identify a selected revision, and every part must refer
to a selected revision. Unmatched or foreign references stop cleanup and remain
for investigation; incomplete pre-manifest candidates are not guessed safe to
remove. The callback uses the
observed parent ETag, and failed or uncertain parent deletion preserves all history.
Only after success are child rows deleted with their observed ETags. Queries are
scoped to the biller and invoice; missing tables are allowed only for unanchored
fixtures, and permission failures propagate. Actual Table tests cover a missing
advertised revision, a stale parent, foreign/unmatched part references and retention of another invoice under the same
biller. No production fixture mutation or client entitlement is introduced.

Revised local verification passed both Release builds with zero warnings/errors,
169 unit and 105 opted-in actual Table/component cases with no skips, and 93.40%
application coverage. The 24 Python and 20 Node tooling checks remain unchanged.

PR #24 native review and the separate approver identified missing-reference,
unsupported-root and inconsistent-freeze gaps in initial head `00ee1e8`. These are
corrected above and have dedicated regressions. Its earlier successful deployed
run does not validate the revised head. Local and new exact-head feature evidence,
native re-review, separate formal decision and terminal production/package results
will be recorded on the PR before delivery is claimed. The application history
increment and remaining B08 work are not complete here.
