# Decisions, discrepancies and planned work

## Owner decisions

These entries preserve owner answers during discovery and approval. They are retrospective where they explain existing choices; no original decision dates are asserted. Approval on 2026-10-29 (Pacific/Auckland) authorized documentation and XML comments, including the exclusions and unknowns below. It did not authorize feature, workflow or infrastructure changes.

| ID | Owner-confirmed decision | Implication |
| --- | --- | --- |
| Q01 | Small-business invoicing with AI agents as first-class consumers; planned MCP and UI retirement. Invoice calculation/issuance, simple tax, optional payment collection and reconciliation in scope; accounting out. | Separate implemented API/queues from planned MCP/payments. |
| Q02 | Tenant is a management group; billers are businesses with their own access/details/customers. Notification tenant is the individual sender/biller. Millisecond invoice IDs assumed unique per biller, not globally. | Preserve the two meanings of tenant and exact reverse-timestamp wire encoding. |
| Q03 | NZ/AU and their currencies/tax rules are stage 1. Validation gaps are acknowledged. Manual overpayment allowed; future automated collection exact balance. Due date required, default seven days, null overwrite is a bug. | Record missing enforcement as planned work, not current safeguards. |
| Q04 | Delivered should require recipient-delivery confirmation. Original overwrite was a simple retry strategy. Allow edits before sending and cancellation before any payment. | Current enqueue timestamp/force save differ from desired lifecycle. |
| Q05 | HTTP biller checks and private Store queue integration intentionally differ. Email trigger is debug-only and should not be in production. Billees need public token links without accounts. Direct table SAS served historical biller UI. | Document actual entry-specific controls and planned production restriction. |
| Q06 | Test/prod use separate resource groups and prefixes. PRs to main should deploy only to test; production should depend on successful test checks. | Correct promotion behavior is planned; workflow currently differs. |
| Q07 | Tracked development examples contain public/demo values. Sensitive information still requires careful handling. | Do not copy values into documentation; use placeholders. |
| Q08 | Five-year retention and audit versions across updates/soft deletes are planned. No SLA. Backup/recovery expected from Azure PaaS. | Actual recovery configuration and detailed policy remain unverified/deferred. |
| Q09 | **Final HTML grand total includes tax.** Separate subtotal/tax calculation is valid. Biller-specific tax treatment uses 0% for exemption. | Earlier inference that arithmetic conflicted with inclusive pricing was withdrawn. No requirement to treat unit prices as tax-inclusive was established. |
| Q10 | Freeze content when notification is queued; desired Delivering then confirmed Delivered. | Deferred pending downstream Notification changes. |
| Q11 | Invoicing retries preserve the existing invoice but publish the same exact downstream message again. Conditional persistence is the planned way to retain the invoice for reconstruction. Transport/Notification prevents another email. | Do not suppress repeat publishing in the proposed Invoicing requirement. Stable ID exists; conditional persistence and downstream protection remain planned. |
| Q12 | Each biller belongs to exactly one management group. | Required ownership invariant; not proof of enforcement in upstream profile storage. |
| Q13 | Defer intended link expiry/revocation policy. | Describe no explicit expiry today without guaranteeing indefinite access. |
| Q14 | Current checks are unit tests only. Integration tests must run in the test environment before future promotion. | No present integration gate claimed; external protections remain Unknown. |
| Q15 | Security follow-up: remove the InvoiceItem general entitlement and retain OwnershipControl; no personalized replacement. | Application registration uses the invoice ownership grant for item access. Existing credentials and externally managed grants require separate operational review. |
| Q16 | Defer the invoice-item identifier/key collision risk under current usage assumptions. | Storage keys are unchanged. This is accepted risk, not verified safety; no customer-count threshold or remediation date was established. |
| Q17 | Existing emailed links must remain usable where the invoice has a token saved in Table Storage. Assume no legacy emailed-link recovery is needed for tokenless records. | Reuse saved tokens, preserve case-insensitive comparison and add no expiry to old links. New tokens are saved before enqueueing; tokenless public viewing is rejected. |
| Q18 | Enhance HTML rendering to handle untrusted invoice and line values safely. | Encode text, restrict logo URL schemes, encode contact destinations separately, and leave downstream notification data unencoded. |

Q15-Q18 were confirmed on 2026-10-30 (Pacific/Auckland), separately authorizing these implementation changes after the documentation bootstrap. S02 remains deferred; notification confirmation, conditional invoice upsert, downstream idempotency, deployment and other planned work are unchanged.

Q11 was further clarified at approval: broker duplicate detection is disabled for cost reasons. Enabling it as usage grows is an option. The short-term planned downstream solution records message IDs in a table and uses partition/row-key uniqueness and concurrency checks. This is a planned mechanism, not an implemented exactly-once guarantee. The claim/send/failure-recovery protocol still needs design, especially a crash between recording an ID and provider acceptance.

## Current discrepancies and proposed backlog

Priority below is **Proposed review order**, not a committed delivery schedule. Evidence is linked to implementation; security-sensitive details are kept in private findings, not reproduced here.

| Priority / ID | Work | Evidence and completion consideration |
| --- | --- | --- |
| P1 / B01 | Conditional persistence and lifecycle enforcement | `UpsertFlow.RunAsync`, `InvoiceDomain.UpdateAsync`; load/recognise existing state, distinguish replay from changed input and concurrent edits. Preserve replay message identity/content (Q11). |
| P1 / B02 | Downstream email idempotency | [Notification evidence](niobium-integration.md#notification-boundary); planned message-ID table, recovery protocol and tests; broker option later. |
| P1 / B03 | Due-date and billing validation | `Invoice.BuildNew`, `InvoiceDomain.UpdateAsync`; prevent null due date, validate currency/ownership/item linkage and appropriate ranges with decided policy. |
| P1 / B04 | Correct production promotion and debug-route exposure | [workflow](../.github/workflows/azure-deploy.yml), `EmailInvoice.Run`; PRs test-only, production dependent on checks, debug route not production. |
| Deferred / B05 | Review invoice-item identifier/key assumptions | [data model](domain-and-data.md); per-biller ID uniqueness is not global uniqueness. Owner accepted deferral under Q16. Resource registration correction is implemented under Q15; key isolation is still unresolved. |
| P2 / B06 | Delivering/Delivered confirmation and cancellation | Q04/Q10; downstream callback/event and state transitions absent; deferred. |
| P2 / B07 | Integration tests before promotion | Q14; isolate resources/data and cover API/storage/messaging failures before adding execution gates. |
| P2 / B08 | Retention, audit and recoverability | Q08; five-year requirement, version history/soft-delete, backup inventory and tested restore procedure need implementation/confirmation. |
| P2 / B09 | Reconcile request/display behavior | `InvoiceDomain.UpdateAsync` ignores request Particulars/Reference; null request payment instructions overwrite profile value; stored Template does not select HTML. Decide intended behavior before changes. |
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
| U07 | Complete isolated application setup and clean-cache restore | Only existing-cache build and isolated unit tests verified. |
| U08 | Exact runtime behavior of untested framework/host edges | ETag round-trip and final HTTP result execution need safe integration evidence; static inspection does not settle them. |

The owner accepted these as explicit unknowns with the documentation scope. Resolve a decision by updating its entry, affected requirements, implementation references and tests; never silently replace Unknown with an assumption.
