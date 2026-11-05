# Product and requirements

## Purpose, people and scope

**Owner-confirmed, [Q01/Q02](decisions-and-risks.md#owner-decisions):** the product is an invoicing service for small businesses, designed to become accessible through their AI agents. It owns invoice calculation, issuance, simple tax calculation and notification, with optional payment collection/direct debit and invoice/payment reconciliation planned. It is not an accounting system.

Historical usage was one owner issuing invoices through a separate UI, plus private automatic issuance from Niobium.Store. Those consumers explain current behavior; they do not define the future audience. The UI is planned for decommissioning. An MCP layer and agent authentication design are planned, not present in this checkout.

| Actor or term | Meaning and responsibility |
| --- | --- |
| Management group / invoicing tenant | Groups separate biller businesses; intended shared defaults include tax, language, region and timezone. No management-policy engine is implemented here. |
| Biller | Business issuing invoices to its own customers, with its own access, contact and payment details. Exactly one management group per biller (Q12). |
| Billee | Customer receiving an invoice. Need not register or possess a service user principal to follow an invoice link. |
| Agent / historical UI | Consumer acting for a biller. MCP and API-key design remain planned; current HTTP identity uses Niobium bearer claims. |
| Niobium.Store | Owner-confirmed private producer of `IssueInvoiceCommand` messages. Its implementation was outside discovery scope. |
| Notification sender tenant | The invoicing biller, with sender branding/address managed by the separate Notification service. |
| Service owner/operator | Product owner confirmed these requirements. Future support, release ownership and escalation commitments remain Unknown. |

For example, an invoicing management group can oversee a restaurant and a playground. Each business issues invoices independently. A restaurant invoice notification identifies the restaurant as sender, not the management group.

## Journeys and capability status

| Capability | Current status | Evidence / intended behavior |
| --- | --- | --- |
| Calculate and persist an invoice | Implemented with validation/concurrency gaps | `Invoice.BuildNew`, `InvoiceDomain.UpdateAsync`, `UpsertFlow.RunAsync`; [data model](domain-and-data.md) |
| Issue from HTTP | Implemented | `NewInvoice.Run` checks biller/group claims, saves, and explicitly disables notification |
| Issue from private integration | Implemented | `IssueInvoiceCommandConsumer.Run` invokes issue flow, optionally notifying |
| Render HTML invoice | Implemented | `GetHTMLInvoice.Run`, `HTMLFlow.RunAsync`; anonymous link with token |
| Queue email notification | Implemented | `EmailFlow.RunAsync`; recipient delivery is not confirmed |
| Biller direct table access | Implemented framework path | Entitlements/resource SAS in Server registration; historical UI use (Q05) |
| Tax-inclusive HTML grand total | Implemented | Subtotal plus tax produces final grand total; Q09 does not require tax-inclusive unit prices |
| Comprehensive billing validation | Partial | Owner acknowledges gaps from trusted single-user history (Q03) |
| Replay-safe issuance and downstream email idempotency | Partial / planned improvement | Stable outgoing message ID exists; conditional persistence and downstream protection planned (Q11) |
| Edit before sending, freeze at enqueue | Intended; incompletely enforced | Q04/Q10; current upsert builds a fresh domain |
| Cancel only before any payment | Planned | No cancellation endpoint/state/flow exists |
| Delivery confirmation | Planned and deferred | Requires Notification changes; desired Delivering then Delivered |
| Manual bank-transfer settlement | Business use confirmed; recording partial | Payment instructions and request `Settled` exist; no reconciliation/ledger/settlement endpoint |
| Processor collection/direct debit/reconciliation | Planned | No processor integrations or reconciliation workflows |
| MCP tools | Planned | No MCP server or tool contracts in repository |
| Ten-year retention and version audit | Planned | Q28 supersedes the original five-year duration; no retention schedule, soft-delete or historical-version store |
| Accounting | Out of scope | Q01 |

Evidence entry points: [Contracts](../src/Niobium.Invoicing.Contracts), [Core flows](../src/Niobium.Invoicing.Core/Flows), [Server](../src/Niobium.Invoicing.Server). Tests and their limits are mapped in [testing](testing-and-conventions.md).

## Confirmed business rules and acceptance statements

These are **Owner-confirmed requirements**, not claims that every row is enforced. They provide future acceptance criteria without adding tests or implementation in this documentation change.

| ID | Required outcome | Current evidence / gap |
| --- | --- | --- |
| R01 | Each biller belongs to exactly one group and issues for its own customers. | Q02/Q12; HTTP claim checks exist; upstream membership rules external. |
| R02 | Stage 1 supports New Zealand and Australia, their currencies and tax rules. | Q03; NZD/AUD formatters exist, but country/currency/tax compatibility is not comprehensively validated. No legal-compliance certification implied. |
| R03 | Final displayed grand total includes tax. Tax treatment is per biller; 0% represents exemption in the product. | Q09 clarified at approval; current subtotal + tax meets this arithmetic interpretation. |
| R04 | An invoice has a due date; use the seven-day default when no valid override is supplied. | Q03; shared validation and default preservation are implemented under B03. |
| R05 | Manual bank transfers may overpay. Future automated collection charges the exact remaining balance. | Q03; displayed due is clamped at zero; processor path absent. |
| R06 | Content can change before sending and is frozen when notification is queued. | Q04/Q10/Q21; cooperative Core checkpoints and conservative uncertain-send freeze are implemented. Direct writers remain outside that guarantee. |
| R07 | Allow cancellation at any delivery stage before any payment has occurred. Preserve history and show Cancelled on existing links; a late delivery event cannot undo it. | Q28 #3; not implemented. Zero current settlement, refunds or reversals cannot prove that no payment ever occurred. In-flight email cannot be recalled reliably. |
| R08 | Replay recognises the same invoice and preserves its state; the same outgoing message may be published again. | Q11; conditional preparation, stable identity and frozen replay content implemented. |
| R09 | Prevent replay from causing another email. Retry uncertain submissions multiple times with exponential backoff/jitter within 24 hours using the same key/payload; hold unknown outcomes afterward for operator resolution or explicit resend. | Q28 #1; downstream durable inbox/provider recovery not yet implemented. Confirmed-send suppression must outlive the provider window; deliberate extra copies have separate operation identities. |
| R10 | Delivered requires verified recipient mail-server acceptance; queue/provider acceptance remains Delivering. Track failures/delays separately from invoice state. | Q28 #2/#4; current Delivered still records enqueue completion. Preserve legacy timestamps as enqueue evidence and report historical confirmation unknown. Authenticated events remain to be implemented. |
| R11 | Billees can view via a saved matching-token link without an account. | Q05/Q17; implemented. Q28 #11 schedules versioned new-link expiry/revocation and replacement links after MCP while preserving existing links. |
| R12 | Feature deployment and real test integration succeed before a reviewed PR merge triggers production and read-only verification. | Q22-Q25; implemented in Invoicing. Q28 #5 requires equivalent Notification safeguards before downstream changes. |
| R13 | Retain data for ten years with committed material-change snapshots, actor, reason and timestamps; soft deletion retains history. | Q28 #6 supersedes Q08's five years. A common invoice/history deadline and candidate financial-year clock need AU/NZ validation before purging. Controls, holds/privacy handling and restores still require implementation/verification. |
| R14 | Previously emailed links continue to work where the invoice has a saved token; resends preserve that token. | Q17; saved-token validation/reuse is implemented. No new expiry or legacy recovery for tokenless records is introduced. |

No advertised SLA is committed. Q28 #7 sets engineering targets to cost and test: one hour maximum data loss and four hours restoration time. Q28 #13 starts measurement with queue age, uncertain sends, delivery failures and processing latency; capacity and support thresholds follow actual usage.

The next milestone is Notification delivery protection plus B02, then B06. Keep the current template default during reliability work; future approved versioned templates must handle legacy selectors and pin the version at freeze (Q28 #9). Begin MCP with scoped read/draft capabilities and payments with durable records/manual reconciliation before selecting a provider (Q28 #12). After MCP, revisit biller-aware item keys, investigate calculation/credit rules, and implement versioned new-link expiry/revocation (Q28 #8/#10/#11). The [decision register](decisions-and-risks.md#q28-business-decision-follow-up) preserves detailed acceptance and remaining implementation prerequisites.
