# Manual payment rollout and verification

Q28/Q29 authorize durable manual payment records and reconciliation before a
processor. Manual overpayment remains allowed. A reversal records evidence of an
external event; it does not execute a refund, move money or restore cancellation
eligibility. Historical opening settlement is an observed amount, not an invented
list of earlier transactions. See [lifecycle](invoice-lifecycle.md).

## Baseline readiness

The separately reviewed [PR #27](https://github.com/niobium-nz/Niobium.Invoicing/pull/27)
prepares the independent test harness and provisions three empty
application tables: `InvoicePayment`, `InvoicePaymentPosting` and
`InvoicePaymentIndex`. That baseline retained lifecycle schemas 1/2 and history
snapshot version 1. The application candidate below adds payment endpoints and the
schema-3 writer on top of the actual readiness merge.

The default `STORAGE_TABLE_NAMES` list in `infra/main.bicepparam` includes the new
tables. Deployments that explicitly override that input must include the complete
required list. The deployed readiness test queries only its synthetic biller
partition to prove all three tables exist. No new credential or production
fixture is required, and the production five-GET allowlist is unchanged.

`test-support/InvoiceCheckpoint.cs` validates the reviewed raw schema independently
of the candidate model. Schema 3 requires a real history reference, permanent
payment evidence, an operation, a positive entry count, matching currency/opening
settlement and both committed posting/index references. Schemas 1/2 cannot carry a
payment position. Missing, inconsistent or future representations fail closed.
Synthetic viewing setup cannot rewrite an anchored invoice.

The schema-2 compatibility fixture pins the actual domain, model, history writer
and cursor source to production merge
`f9fa136771bef08b837a72379a8a1d7d77d7ec0f`. Its
[source inventory](../src/Niobium.Invoicing.Integration.Tests/LegacyLifecycleV2/README.md)
records original hashes and namespace-only relocation/shared formatting imports.
Against the real loopback Table adapter, the unsafe schema-2 control demonstrates
that an old writer discards an unknown payment position. Schema 3 then rejects
that writer's reads and mutations before the stored version changes. Never
downgrade a production row to enable an old writer.

## Reviewed representation for the application stage

All new Table models use business key names `Biller` and `ID` with `EntityKey`
attributes. Models privately own ID derivation, canonical serialization, index
routing and references. Rich invoice domains own amounts, ownership, reversal
eligibility and transitions. Flows coordinate and framework adapters persist.
Follow [model contracts](model-contracts.md) and [domain/data access](domain-data-access.md).

An immutable receipt reserves the caller's operation and evidence. A posting
applies it to a particular predecessor. An immutable operation index provides
committed membership and cumulative reversal evidence. The conditional invoice
write commits the posting, index root and verified history together. Candidate
rows alone never prove a payment was applied; stale or unknown outcomes require
an exact retry against the committed root.

The index uses at most 32 routing levels plus a leaf. Nodes are immutable and
content-addressed; updates create only the changed path. Business callers do not
reproduce routing or hashes. Bounded online reads verify selected membership
paths, the current posting and its immediate predecessor. This is distinct from
re-auditing every older receipt, node and history snapshot. The application stage
must preserve that distinction in its API documentation and operational evidence.

History snapshot version 2 adds payment summaries while retaining the version-1
shape for invoices without payment records. Existing settlement fields, frozen
notification parameters and saved invoice-link tokens remain compatible; the
application stage must test those invariants before promotion.

## Owned fixture cleanup

`InvoicePaymentFixture` and `InvoicePaymentIndexFixture` read raw rows without
using candidate payment codecs. Queries are restricted to the observed biller
partition and exact invoice creation instant. The verifier checks tenant,
versions, conditional ETags, hashes, receipt/index references, committed sequence,
balances and reversal counters before allowing the parent-delete callback.
History validation runs inside that callback before the conditional parent delete.
Only then can owned history, posting, index and receipt rows be removed using
their observed ETags. Missing required evidence, unknown versions, stale or
uncertain parent deletion stop cleanup. There is no wildcard or partition purge.

Selection is bounded to 10,000 rows per child table. An exceeded bound or partial
cleanup failure fails the test run and requires investigation; it does not grant
permission to widen the deletion scope. These helpers operate only in owned test
fixtures. They are not production retention, reconciliation or recovery tools.

## Delivery sequence

First verify and merge this baseline readiness change through candidate/trusted
tests, native review, independent approval and protected merge. Check terminal
production read-only and package results. Then base the payment application
change on that actual merge and repeat the complete delivery process, including
deployed payment/reversal/retry/concurrency cases. No production payment creation,
data migration, notification or processor action is authorized by these tests.

## Application candidate: manual receipt API

The application candidate adds authenticated owner routes below. Delivery is not
complete until its feature suites, native review, independent approval, protected
merge and terminal production/package checks succeed. It is based on readiness
merge `b2154c9bfea52d9be567d3008738415dc11056a6`. The prerequisite's production
verification must finish before this candidate is published for activation.

All paths begin `/{issuer}/invoices/{invoice}`. The biller's access-token Sid must
match `issuer`, and GroupSid must match the stored management group. Existing
invoice-link tokens and Functions keys do not grant payment mutation authority.
No payment-table resource entitlements or blanket MCP privileges are added.

| Method and suffix | Request/result |
| --- | --- |
| `POST /payments` | `RecordInvoicePaymentRequest`; returns current committed `InvoicePaymentBalance` |
| `POST /payments/reversals` | `ReverseInvoicePaymentRequest`, including the original receipt operation; returns current committed balance |
| `GET /payments/balance` | Current opening amount, committed settlement, outstanding/overpaid cents and entry count |
| `GET /payments/{operation}` | One committed receipt/reversal with its evidence and actor; uncommitted candidates return 404 |

Each mutation supplies a nonempty operation UUID, positive integer cents,
matching NZD/AUD currency, an occurrence time no later than the server clock,
a nonblank reason (at most 500 characters) and evidence reference (at most 1,000).
The first immutable receipt retains its server recording time. Exact retries
preserve one committed effect; reuse with different business evidence returns
409. A conflict after a competing write or an unknown transport outcome requires
a fresh request with exactly the same operation and payload. Do not allocate a
new operation merely to retry. Balance and payment responses are non-cacheable.

A reversal must reference a committed received payment on the same invoice,
cannot predate that receipt, and cannot exceed its remaining unreversed amount.
Partial and complete reversals are supported. They record external evidence;
this service neither initiates refunds nor contacts a payment processor. They
never reset permanent payment history or permit cancellation after a payment.
Late real payment evidence can be recorded after cancellation while retaining
the cancelled state. Active noncancelled preparation must finish before payment
recording. Existing opening settlement cannot be silently rewritten once a ledger
exists; reconciliation does not manufacture missing historical transactions.

New payments leave the original `Invoice.SettledCents`, saved link token,
issuance fingerprint and frozen notification payload unchanged. Live HTML derives
its amount due from the committed payment position. Authenticated history adds a
payment summary and operation ID, from which the owner can retrieve the detailed
record. Recording or reversing payment evidence does not enqueue notifications.

The bounded index tests cover 256 operations and the maximum shared UUID prefix.
Their limits count payment repository reads, in addition to loading the invoice:
at most 36 for balance, 70 for a selected operation and 71 for an exact retry.
These are structural read bounds, not latency or throughput guarantees. Selected
paths and current posting evidence are verified online; older snapshots and
unvisited nodes are verified when read or by a separate complete audit. No
production full-audit service, bank reconciliation import or processor integration
is claimed by this increment.
