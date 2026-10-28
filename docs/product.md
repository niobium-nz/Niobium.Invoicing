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
| Five-year retention and version audit | Planned | No retention schedule, soft-delete or historical-version store |
| Accounting | Out of scope | Q01 |

Evidence entry points: [Contracts](../src/Niobium.Invoicing.Contracts), [Core flows](../src/Niobium.Invoicing.Core/Flows), [Server](../src/Niobium.Invoicing.Server). Tests and their limits are mapped in [testing](testing-and-conventions.md).

## Confirmed business rules and acceptance statements

These are **Owner-confirmed requirements**, not claims that every row is enforced. They provide future acceptance criteria without adding tests or implementation in this documentation change.

| ID | Required outcome | Current evidence / gap |
| --- | --- | --- |
| R01 | Each biller belongs to exactly one group and issues for its own customers. | Q02/Q12; HTTP claim checks exist; upstream membership rules external. |
| R02 | Stage 1 supports New Zealand and Australia, their currencies and tax rules. | Q03; NZD/AUD formatters exist, but country/currency/tax compatibility is not comprehensively validated. No legal-compliance certification implied. |
| R03 | Final displayed grand total includes tax. Tax treatment is per biller; 0% represents exemption in the product. | Q09 clarified at approval; current subtotal + tax meets this arithmetic interpretation. |
| R04 | An invoice has a due date; use the seven-day default when no valid override is supplied. | Q03; `BuildNew` sets seven days, but `UpdateAsync` can overwrite with null. |
| R05 | Manual bank transfers may overpay. Future automated collection charges the exact remaining balance. | Q03; displayed due is clamped at zero; processor path absent. |
| R06 | Content can change before sending and is frozen when notification is queued. | Q04/Q10; persistent transition and concurrency protection incomplete. |
| R07 | Cancellation is permitted only before any partial/full payment. | Q04; not implemented. |
| R08 | Replay recognises the same invoice and preserves its state; the same outgoing message may be published again. | Q11; conditional upsert planned, stable ID already used. |
| R09 | Transport or Notification prevents replay from causing another recipient email. | Q11; downstream message-ID storage planned, broker detection a later option. |
| R10 | Delivered means positive delivery confirmation; queueing means Delivering. | Q04/Q10; current Delivered records enqueue completion instead. Deferred pending downstream work. |
| R11 | Billees can view via a correct-token link without an account. | Q05/Q13; intended expiry/revocation policy deferred. |
| R12 | PRs to main deploy only to test; successful test-environment checks precede production. | Q06/Q14; unit checks exist, integration tests and promotion correction planned. |
| R13 | Retain data for five years and preserve historical versions across updates/soft deletion. | Q08; retention clock, purge and recovery details deferred; not implemented. |

No SLA was committed (Q08). Success metrics, capacity targets, availability/latency targets, support response times and roadmap dates are Unknown. Detailed rounding policy, fractional quantity support, credits/negative amounts and future MCP/payment protocols are accepted deferrals in [decisions](decisions-and-risks.md#accepted-unknowns).
