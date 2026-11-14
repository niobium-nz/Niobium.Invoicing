# Cancellation and historical payment evidence

Q28 permits cancellation at any delivery stage before any payment has occurred.
Q29 requires reconciliation when legacy payment history is unknown. This increment
implements those rules in `InvoiceDomain`, with authenticated HTTP access through
`InvoiceLifecycleFlow` and `InvoiceLifecycleFunctions`. Provider-confirmed delivery,
durable payment transactions and the complete B08 version-history store remain
separate work. The [delivery ledger](implementation-progress.md) records verification;
source presence alone does not establish a production release.

## Business behavior

A newly created invoice starts with known no-payment history only when Core has
loaded no existing row and successfully performs the conditional create. A legacy
zero settlement remains Unknown. Any observed nonzero settlement or recorded
historical payment permanently prevents cancellation; returning the current amount
to zero does not remove that evidence.

The owning biller may reconcile Unknown to NoPayments or PaymentOccurred, with an
operation UUID, reason and evidence reference. A later discovery may change
NoPayments to PaymentOccurred. Evidence is retained, and a recorded payment cannot
be reconciled away. Newly discovered payment evidence may also be recorded after
cancellation, preserving the cancellation and earlier findings. New reconciliation
defers while a non-cancelled invoice has an active preparation claim; it must not
replace that claim's ETag and strand its owner. This endpoint records an accountable historical finding; it
does not inspect a bank account or create a payment/refund transaction.

Cancellation records its operation UUID, authenticated actor, reason and UTC time
in the same conditional invoice write as the cancelled state. Exact retries return
the current status without a new write. Reusing an operation with different evidence,
a different cancellation, or a stale/unconditional version fails with conflict.
A cancelled invoice rejects subsequent Core edits and sends. Existing saved-token
links keep their token and totals, show Cancelled/Not payable, and remove the
payment instructions. Public token validation still applies.

Delivery progress is separate from cancellation. The current `Delivered` column
remains the first enqueue acknowledgement. The status endpoint exposes it as
QueuedAt; a legacy invoice has Unknown confirmation. A new freeze records
Delivering, never verified Delivered. A late enqueue acknowledgement reloads the stored invoice and conditionally merges
its time while preserving cancellation and payment evidence. It verifies the frozen
token, content fingerprint and endpoint, and makes at most three conditional attempts
when another write wins. Unknown write outcomes and final conflicts propagate;
the acknowledgement loop never sends another message. EmailFlow re-reads cancellation immediately
before enqueue, but a cancellation racing an already-started send cannot recall
mail or create an atomic transaction with Service Bus. A missing recipient keeps
the established no-send behavior.

## HTTP contracts

All routes use the installed bearer-token middleware and require authenticated GUID
Sid and GroupSid claims. Sid must match the route biller; the loaded invoice must
match both the biller and management group. Missing/invalid identity returns 401,
a different biller/group 403, invalid requests 400 and conflicting business or
storage state 409. Missing invoices retain the framework's 404 response.

| Route | Request | Result |
| --- | --- | --- |
| `GET /{issuer}/invoices/{invoice}/status` | No body | Non-cacheable preparation, delivery, payment-history, cancellation and cancellation-eligibility summary |
| `POST /{issuer}/invoices/{invoice}/cancel` | `CancelInvoiceRequest`: Operation UUID and nonempty Reason, at most 500 characters | Current `InvoiceLifecycleStatus` after the conditional transition or exact replay |
| `POST /{issuer}/invoices/{invoice}/payment-history/reconcile` | `ReconcileInvoicePaymentHistoryRequest`: Operation UUID, Finding, Reason and nonempty Evidence reference, at most 1,000 characters | Current status after preserving the finding and its audit record |

Findings are the Contracts enum values NoPayments (1) and PaymentOccurred (2).
Unknown (0) is not an accepted reconciliation result. Reasons/references are trimmed;
changing the normalized contents on an operation replay is a conflict. Use a stable
operation UUID across transport retries. Send references to retained supporting
records rather than bank credentials or statement contents. These direct biller
routes do not grant future MCP tools blanket sending/cancellation/payment authority.

## Representation and compatibility

`Invoice` privately owns the `LifecycleData` JSON codec, version, size/depth bounds
and per-write identity. The Table adapter maps its public scalar column and private
setter. `[JsonIgnore]` keeps it out of legacy invoice JSON and content fingerprints;
it does not exclude the column from Table storage. `InvoiceLifecycleSnapshot`
provides immutable typed values. Domains choose business transitions, the model
encodes them, flows coordinate, and the pinned Niobium repository persists them.
No storage SDK or JSON path construction is introduced into the business domain.

The existing `IssuanceState` becomes `lifecycle-v1` when lifecycle data is first
written. Version 1 stores the preparation enum in that bounded scalar snapshot,
alongside payment evidence, cancellation and delivery observations. The model reads
old null/preparing-v1/retryable-v1/ready-v1/frozen-v1 checkpoints without a write.
Unsupported versions, mismatched columns and malformed audit data fail closed.
Legacy defaults are still captured from the historical invoice when a reconciled
draft is subsequently prepared.

The old framework adapter uses Replace. An old model can therefore erase a newly
added column even while preserving the row's original ETag protocol. Reusing an old
checkpoint for a new-format invoice would be unsafe. The new checkpoint causes
the prior issuance/freeze/enqueue-acknowledgement paths to reject before writing.
An old request holding a pre-transition version also loses its ETag precondition.
The integration fixture pins the actual prior model/writer to Invoicing
`bae20a356dc9c03e89df8c690dcf215b8b5a4825`, proves the unsafe unfenced control, and
then verifies that the guarded paths preserve the new column and ETag.

Deploy the reviewed trusted-harness compatibility increment before this application
change. Existing invoice keys, tokens, monetary values, v1 content fingerprints,
notification identities and frozen parameters retain their representation. No
historical rows are migrated on read. After new-format rows exist, recovery must
use a compatible host; do not roll back to a writer/renderer that cannot interpret
them or downgrade their checkpoint. Old readers during deployment overlap are not
a cancellation-aware view. Arbitrary direct storage writers can still bypass Core
invariants; this protocol does not authorize their use or a production migration.

The installed 4.0.60 adapter can omit the returned write ETag. The existing read-back
check now compares the opaque lifecycle column as well as all prior persisted
content. A model-owned write identity distinguishes otherwise identical writes.
State and its small bounded audit are one row update; the surrounding invoice items,
message publication and future historical snapshots are not a single transaction.

## Evidence and remaining work

Local tests cover all preparation/delivery cancellation stages, legacy uncertainty,
permanent payment evidence, exact/conflicting retries, lost/stale versions, stored
corruption, historical defaults, late acknowledgements, saved links, real Table
mapping and the prior writer. Component tests parse synthetic JWTs using the pinned
framework and execute the real lifecycle flow. Candidate deployed cases use the
existing test host/storage, owned temporary invoices and existing signing settings;
they cover all three route identities, reconciliation/cancellation, stored evidence,
saved links and competing operations. They send no additional notifications.
Production verification retains its five existing GET-only checks.

This snapshot holds at most two reconciliation findings and one cancellation audit.
It is not the full ten-year B08 invoice/line history, legal hold, soft-delete or
recovery implementation. Durable payment records and manual payment reconciliation
remain the approved next payment work. The separate Notification receipt contract
must be published and consumed before Invoicing can project authenticated recipient
mail-server acceptance. No provider-submission or enqueue observation is presented
as that confirmation.
