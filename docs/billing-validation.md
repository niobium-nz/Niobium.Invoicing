# Billing validation and display defaults

B03 is enforced in Core for both HTTP and private Service Bus issuance. Validation
runs before invoice or item writes; invalid data raises Niobium BadRequest (400).
The queue path propagates the error to Functions redelivery/dead-letter handling.
No recipient notification is queued after validation or persistence failure.

- Tenant, biller and customer IDs must be nonempty. Returned profile identities and
  embedded or retrieved customer ownership must agree with the request.
- Stage one accepts NZD/AUD, with matching biller, customer, invoice and line
  currencies. Provided postal country codes must be NZ/AU; addresses remain
  optional. No country-specific tax rate or cross-border tax rule is inferred.
- The item collection and entries must be nonnull, item subjects cannot be null,
  item IDs must be unique, and every parent ID must match the requested invoice.
  Caller-supplied totals are recalculated. Item storage keys are unchanged (B05).
- Invoice IDs must decode to a supported date. InvoiceCycle must be defined and a
  supplied billing-period end cannot precede its start.
- A missing due-date override retains the seven-day default. An override before
  the issue instant fails validation; it is not silently replaced.
- Line multiplication, subtotal/tax/grand-total and outstanding-balance arithmetic
  must fit integer cents. The contract line/grand-total helpers now throw on
  overflow. Core translates arithmetic overflow into BadRequest.
- Q19: zero settlement is currency-neutral, including the contract's omitted
  zero-USD default. Nonzero settlement must match the invoice currency. Manual
  overpayment is allowed. This does not record or collect payments.

Negative amounts, zero/negative integer quantities and midpoint-to-even tax
rounding retain their prior behavior. U01 credit, quantity and rounding policy is
still undecided; validation does not claim to settle those rules.

For B09/Q20, nonnull Particulars, Reference and PaymentInstructions are trimmed and
applied. Null inherits the invoice's initialized defaults; an explicit empty or
whitespace-only string clears the value. Generated Particulars and Reference keep
their existing 12-character default construction. No new truncation rule is applied
to explicit overrides. The stored Template field still does not select an HTML
resource; that feature remains deferred. HTML encoding remains at rendering and
notification parameters remain unencoded for the downstream renderer.

Tests: `InvoiceValidationTests` exercises failure before persistence, both customer
input paths, overflow, currency-neutral zero, unchanged rounding/quantity behavior,
and display values through persisted invoice rendering. `UpsertFlowTests` covers
normal persistence, replacement, lookup failures, cancellation forwarding, totals
and overpayment. See [implementation progress](implementation-progress.md) for
baseline and exact verification commands.
