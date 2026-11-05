# Decisions, discrepancies and planned work

## Owner decisions

These entries preserve owner answers during discovery and approval. They are retrospective where they explain existing choices; no original decision dates are asserted. Approval on 2026-10-29 (Pacific/Auckland) authorized documentation and XML comments. Later implementation and delivery instructions superseded the original deployment prohibition; Q22-Q25 below and the [delivery guide](delivery-and-operations.md) define current authority. Production test-data mutations and production migrations remain prohibited.

| ID | Owner-confirmed decision | Implication |
| --- | --- | --- |
| Q01 | Small-business invoicing with AI agents as first-class consumers; planned MCP and UI retirement. Invoice calculation/issuance, simple tax, optional payment collection and reconciliation in scope; accounting out. | Separate implemented API/queues from planned MCP/payments. |
| Q02 | Tenant is a management group; billers are businesses with their own access/details/customers. Notification tenant is the individual sender/biller. Millisecond invoice IDs assumed unique per biller, not globally. | Preserve the two meanings of tenant and exact reverse-timestamp wire encoding. |
| Q03 | NZ/AU and their currencies/tax rules are stage 1. Validation gaps are acknowledged. Manual overpayment allowed; future automated collection exact balance. Due date required, default seven days, null overwrite is a bug. | Record missing enforcement as planned work, not current safeguards. |
| Q04 | Delivered should require recipient-delivery confirmation. Original overwrite was a simple retry strategy. Allow edits before sending and cancellation before any payment. | Current enqueue timestamp differs from the desired confirmation lifecycle. Conditional persistence is now implemented. |
| Q05 | HTTP biller checks and private Store queue integration intentionally differ. Email trigger is debug-only and should not be in production. Billees need public token links without accounts. Direct table SAS served historical biller UI. | The earlier debug-route removal is superseded by Q26; restore privileged Function-key access. Public stored-token viewing remains. |
| Q06 | Test/prod use separate resource groups and prefixes. PRs to main should deploy only to test; production should depend on successful test checks. | PR production eligibility is removed and local verification gates deployment; the deployed integration gate remains open. |
| Q07 | Tracked development examples contain public/demo values. Sensitive information still requires careful handling. | Do not copy values into documentation; use placeholders. |
| Q08 | Audit versions across updates/soft deletes are planned. The original five-year retention duration is superseded by ten years under Q28. No SLA. Backup/recovery expected from Azure PaaS. | Actual recovery configuration and detailed policy remain unverified; the new duration is a requirement, not an implemented retention control. |
| Q09 | **Final HTML grand total includes tax.** Separate subtotal/tax calculation is valid. Biller-specific tax treatment uses 0% for exemption. | Earlier inference that arithmetic conflicted with inclusive pricing was withdrawn. No requirement to treat unit prices as tax-inclusive was established. |
| Q10 | Freeze content when notification is queued; desired Delivering then confirmed Delivered. | Content freeze is implemented under Q21; delivery confirmation remains blocked on Notification. |
| Q11 | Invoicing retries preserve the existing invoice but publish the same exact downstream message again. Conditional persistence is the planned way to retain the invoice for reconstruction. Transport/Notification prevents another email. | Do not suppress repeat publishing in the proposed Invoicing requirement. Stable ID/content and conditional persistence are implemented. Downstream protection remains planned. |
| Q12 | Each biller belongs to exactly one management group. | Required ownership invariant; not proof of enforcement in upstream profile storage. |
| Q13 | Defer intended link expiry/revocation policy. | Describe no explicit expiry today without guaranteeing indefinite access. |
| Q14 | Current checks are unit tests only. Integration tests must run in the test environment before future promotion. | No present integration gate claimed; external protections remain Unknown. |
| Q15 | Security follow-up: remove the InvoiceItem general entitlement and retain OwnershipControl; no personalized replacement. | Application registration uses the invoice ownership grant for item access. Existing credentials and externally managed grants require separate operational review. |
| Q16 | Defer the invoice-item identifier/key collision risk under current usage assumptions. | Storage keys are unchanged. This is accepted risk, not verified safety; no customer-count threshold or remediation date was established. |
| Q17 | Existing emailed links must remain usable where the invoice has a token saved in Table Storage. Assume no legacy emailed-link recovery is needed for tokenless records. | Reuse saved tokens, preserve case-insensitive comparison and add no expiry to old links. New tokens are saved before enqueueing; tokenless public viewing is rejected. |
| Q18 | Enhance HTML rendering to handle untrusted invoice and line values safely. | Encode text, restrict logo URL schemes, encode contact destinations separately, and leave downstream notification data unencoded. |

Q19/Q20 were confirmed during implementation on 2026-10-30 (Pacific/Auckland): zero settlement is currency-neutral; nonzero settlement must match invoice currency. Supplied Particulars/Reference/PaymentInstructions override defaults after trimming, null inherits defaults, and an empty string clears the value. Template selection remains deferred. See [billing validation](billing-validation.md).

Q15-Q18 were confirmed on 2026-10-30 (Pacific/Auckland), separately authorizing these implementation changes after the documentation bootstrap. S02 remains deferred. The later research implementation adds conditional persistence and validation; its item-by-item status is in the [acceptance ledger](implementation-progress.md).

Q21 was confirmed during implementation: persist preparation/freeze checkpoints, keep content frozen after uncertain enqueue outcomes, allow exact retries only, and treat legacy saved-token invoices as frozen even after failed sends. See [recovery](invoice-recovery.md) for the conservative abandoned-writer policy and compatibility limits.

Q22/Q23 replaced the original delivery scope: push feature work, verify terminal build/test deployment and deployed integration, obtain current-head approval, merge the reviewed PR without another permission request, and monitor the resulting production deployment and read-only tests. Production has no independent approval gate. Tests use the real environment resources; production tests perform only allowed reads. Q24 requires one approval from any approver GitHub permits, without an additional repository-policy author exclusion.

Q25 was confirmed on 2026-10-31 (Pacific/Auckland): the branch author, whether human or Codex, creates the PR after successful verification; workflows must not create it. Codex must create/reuse the PR through its own tools when a completed step is ready to sync, request `niobium-nz` as reviewer (the owner subsequently authorized `5he11` for PRs authored by `niobium-nz`), and actively monitor automatic Codex and human review comments through fixes, approval, merge and production verification. The owner further clarified that review runs separately from the implementation conversation, preferably in the native background service with results on GitHub; a separate review conversation is only a fallback when needed. The implementation agent fixes findings and monitors delivery without issuing its own reviewer decision through `5he11`. `AGENTS.md` records this standing project memory. GitHub's native author/reviewer restrictions still apply; an unavailable author identity or reviewer request must be reported rather than silently bypassed.

Q11 was further clarified at approval: broker duplicate detection is disabled for cost reasons. Enabling it as usage grows is an option. The short-term planned downstream solution records message IDs in a table and uses partition/row-key uniqueness and concurrency checks. This is a planned mechanism, not an implemented exactly-once guarantee. The claim/send/failure-recovery protocol still needs design, especially a crash between recording an ID and provider acceptance.

Q26 was confirmed after PR #7 merged on 2026-10-31: restore the historical function-key-guarded manual email endpoint for the owner's debug activity. The historical function is `EmailInvoice`, using `GET /email/{issuer}/invoices/{invoice}` and `AuthorizationLevel.Function`; no `SendEmail` function was found in history. This supersedes Q05's removal decision for that endpoint. Preserve the route and use the current `EmailFlow`, including persisted-token reuse and freeze/retry behavior. A successful enqueue is not confirmed email delivery. Function keys grant privileged notification access; this is not biller JWT authorization. Production verification must still never call this side-effecting GET route.

Restoration uses two reviewed steps because the original immutable-main suite required that route to return 404. PR #8 installed a test contract accepting route absence (404) or Functions key denial (401), using missing/invalid keys against an existing invoice and verifying no invoice change. The route remained absent in that preparatory change. The restoration then adds the function and strict candidate tests for its binding, key enforcement and authorized sending through the real test services. Both steps use ordinary feature CI, review and approval, with no protection exception or fabricated status.

Q27 was confirmed on 2026-10-31 (Pacific/Auckland): keep public viewing dependent on a saved matching token and preserve token-based freeze semantics. Instead of moving token generation to create/update, the owner requested a separate Function-key-guarded endpoint to debug invoice viewing. The implementation is `DebugInvoice`, `GET /debug/{issuer}/invoices/{invoice}`, rendering saved invoice/items without writes, token generation, freeze or notification. Its key grants operational access across billers, like the manual email function. Public tokenless access remains forbidden. No package/schema changes or production test allowlist expansion are required.

### Q28: business-decision follow-up

Recorded on **2026-11-06 (Pacific/Auckland)**. The owner accepted all recommendations in the preceding numbered business-decision discussion, with the following additions and deferrals. These discussion numbers are not backlog B IDs or existing Q IDs.

| Discussion item | Owner instruction | Traceability and next work |
| --- | --- | --- |
| #1 | Retry multiple times with exponential backoff and jitter within the 24-hour window. | B02 / Q11: carry this requirement into the downstream Notification inbox/provider recovery work. Preserve the same outgoing identity/content on exact replay. No business retry scheduler is implemented in Invoicing by this decision. |
| #6 | Use a ten-year retention period instead of the previously proposed duration, to support AU/NZ recordkeeping requirements. | B08 / Q08 / R13: ten years supersedes five years in the current requirements. Historical versions and soft-delete audit remain required. Storage, recovery and deletion controls still need implementation and verification. |
| #8 | Defer for now; revisit after MCP is built. | Keep the original item number and the MCP dependency until its topic is recovered from the numbered discussion. Do not substitute an unrelated backlog item. |
| #10 | Plan further investigation after MCP is released; findings may require changes for AU/NZ markets or the stated IRS/IRD requirements. | Preserve this investigation milestone. Its topic and applicable jurisdictions must be reconciled with the original recommendation; this statement does not establish US product scope or a tax-compliance claim. |
| #11 | Plan implementation after MCP is released. | Preserve this implementation milestone separately from #10's investigation. Map its topic from the original recommendation before scheduling code changes. |
| #2-#5, #7, #9 | Accept the recommendations as previously presented. | Approval is recorded. The exact recommendation text and topic-to-ID mapping still need to be recovered; do not invent their contents or request the same approval again. |

The available project records do not contain the complete numbered recommendation list. This is a source-reconciliation dependency, not a rejection of the owner's approval. The explicit #1/#6 requirements can be recorded now; affected implementation waits only where the accepted terms cannot yet be reconstructed. Existing unknowns below are not automatically new approval requests, nor are their details silently resolved by guessing which numbered recommendation covered them.

For B02, recover the accepted provider-outcome and after-window handling before changing sending behavior. A queue transport retry is not a durable provider retry schedule or duplicate-email protection. The implementation must demonstrate the accepted retry window, exponential delays with jitter, multiple attempts and stable message identity under concurrent delivery/restart; the exact schedule and exhaustion behavior must come from the recovered terms or an explicit subsequent decision.

For B08, ten years is the owner's product retention requirement. It does not establish the retention clock, exceptions, purge/privacy handling, legacy compatibility or recovery guarantees by itself. Reconcile those accepted recommendations before building retention or deletion automation. AU/NZ legal and tax applicability needs separate verification; the duration alone is not evidence of compliance. No production migration or deletion is authorized by this record.

## Current discrepancies and proposed backlog

Priority below is **Proposed review order**, not a committed delivery schedule. Evidence is linked to implementation; security-sensitive details are kept in private findings, not reproduced here.

| Priority / ID | Work | Evidence and completion consideration |
| --- | --- | --- |
| P1 / B01 | Conditional persistence and lifecycle enforcement | `UpsertFlow.RunAsync`, `InvoiceDomain.UpdateAsync`; load/recognise existing state, distinguish replay from changed input and concurrent edits. Preserve replay message identity/content (Q11). |
| P1 / B02 | Downstream email idempotency | [Notification evidence](niobium-integration.md#notification-boundary); planned message-ID table, recovery protocol and tests; Q28 adds multiple retries with exponential backoff and jitter within 24 hours. Broker option later. |
| P1 / B03 | Due-date and billing validation | `Invoice.BuildNew`, `InvoiceDomain.UpdateAsync`; prevent null due date, validate currency/ownership/item linkage and appropriate ranges with decided policy. |
| P1 / B04 | Correct production promotion and debug-route exposure | [Protected delivery](delivery-and-operations.md) verifies feature deployment before reviewed promotion. Q26 restores the manual email route with Function-key protection and explicit deployed authorization tests; production verification must never call that side-effecting route. |
| Deferred / B05 | Review invoice-item identifier/key assumptions | [data model](domain-and-data.md); per-biller ID uniqueness is not global uniqueness. Owner accepted deferral under Q16. Resource registration correction is implemented under Q15; key isolation is still unresolved. |
| P2 / B06 | Delivering/Delivered confirmation and cancellation | Q04/Q10; downstream callback/event and state transitions absent; deferred. |
| P2 / B07 | Integration tests before promotion | Q14; isolate resources/data and cover API/storage/messaging failures before adding execution gates. |
| P2 / B08 | Retention, audit and recoverability | Q08/Q28; ten-year requirement, version history/soft-delete, backup inventory and tested restore procedure need implementation/verification. Reconcile the accepted detailed recommendations before automation. |
| P2 / B09 | Reconcile request/display behavior | Q20 precedence is implemented and tested. Stored Template still does not select HTML; selector and compatibility policy remain deferred. |
| P3 / B10 | MCP, payments and reconciliation | Product direction confirmed; contracts, credentials, scheduling and acceptance details deferred. |

There is **no tax-pricing defect recorded solely because current helpers add tax to subtotal**. Current arithmetic and the clarified HTML-grand-total requirement agree. Detailed policy questions below remain open.

## Accepted unknowns

| ID | Deferred knowledge / decision | Documentation consequence |
| --- | --- | --- |
| U01 | Intended rounding boundaries, fractional quantities, negative/credit amounts | Describe current integer quantities and midpoint-to-even rounding; do not turn them into approved policy. |
| U02 | Future link expiry and revocation policy | Q13 remains deferred, subject to Q17's requirement to preserve existing emailed links with saved tokens. No expiry is introduced by the follow-up. |
| U03 | Actual deployed revisions, GitHub/Azure approvals and branch restrictions | Source declares eligibility; no claim about external protections or production state. |
| U04 | Backup inventory, restores, RPO/RTO, retention start/purge/privacy details | Azure reliance is owner-stated; no verified recovery/compliance guarantee. |
| U05 | MCP tool/auth contracts, payment/reconciliation and confirmation protocols | Planned capabilities, no fabricated API/tool designs. |
| U06 | Success metrics, support/release commitments and capacity targets | Unknown; no SLA per Q08. |
| U07 | Complete isolated application setup and clean-cache restore | Existing-cache diagnostic build, unit tests and local Table/HTTP component integration verified. Complete isolated deployed setup and clean-cache restore remain open. |
| U08 | Exact runtime behavior of untested framework/host edges | Local tests establish adapter read/write ETag behavior, stale rejection and ASP.NET result execution. Functions middleware/binding and live Azure remain open. |

The owner accepted these as explicit unknowns with the original documentation scope. Q28 records later approval of the business recommendations; reconcile their exact terms with these IDs before treating an older unknown as either resolved or a fresh approval request. Resolve a decision by updating its entry, affected requirements, implementation references and tests; never silently replace Unknown with an assumption.
