# Committed invoice history

This implementation work addresses snapshots and authenticated history reading
within Q28/B08. Exact-head delivery evidence is recorded in the implementation
ledger and PRs. Soft deletion, a validated retention
deadline and tested backup/restore remain separate work. Ten years is the accepted
retention duration; no automatic expiry, purge, migration or production fixture is
introduced here.

## Authenticated history reading

`GET /{issuer}/invoices/{invoice}/history` requires the authenticated owning biller
and the invoice's management group, like the lifecycle status route. A saved
recipient link or Function key does not grant access. Responses use `no-store`.
An invoice without recorded history returns an empty page; reading never creates
an observed baseline or changes the invoice, lines, history or notification state.

The response contains one `entry` with its revision ID, change attribution and
verified invoice/line snapshot, plus an optional opaque `continuation`. Supply that
value as `?cursor=...` to read the next older change. One snapshot per response
bounds the payload even at the existing 16 MiB snapshot limit. Each manifest and
all its parts are validated before returning evidence. No storage ETag, saved
invoice-link credential or raw Table key is added to the response contract.

The first page follows the current invoice's committed reference. Subsequent pages
follow only the verified predecessor chain. They never discover revisions by
scanning tables or accepting an arbitrary revision ID. A new edit does not shift
an already issued continuation into the newer chain; restart without a cursor to
see the latest revision. Abandoned candidates remain invisible.

`InvoiceHistoryCursor` privately owns its representation and HMAC. It binds the
continuation to biller, management group, invoice and predecessor, and expires it
after thirty minutes. It uses the existing `BillingOptions.InvoiceTokenSecretSalt`
with a distinct cryptographic purpose; no new environment setting is required.
Secret rotation invalidates temporary continuations. It does not alter stored
public invoice links. A cursor is never authentication: every request rechecks the
current invoice owner. Invalid, expired or cross-invoice continuations return 400
and the caller can restart from the latest history. Missing/corrupt committed
evidence fails instead of returning a partial snapshot.

The history response and existing snapshot/change DTOs live in Contracts. Core
forwards the earlier type identities for binary compatibility; namespace, JSON
names, persisted schema and history digest representation stay unchanged. Storage
models retain private representation responsibilities in Core, and the invoice
domain retains ownership/chain rules. The flow and HTTP handler coordinate them.

Local tests cover cursor binding/expiry, ownership, orphan exclusion, concurrent
edits, missing parts and read-only actual Table pagination. Candidate deployed
tests exercise the authenticated route and earlier pages on the real test host.
The production allowlist remains the same five GET-only checks and does not add
history browsing or test fixtures.

## Commit and failure behavior

`InvoiceHistoryDomain` writes immutable `InvoiceRevisionPart` rows, then their
`InvoiceRevision` manifest, using the existing Niobium repositories and conditional
creates. It verifies the manifest and every part through read-back. The invoice
domain then commits the revision reference with the invoice's existing ETag.
The reference and business change share that conditional invoice write.

The committed history is the chain reachable from the invoice's reference. A lost
history response, failed invoice write or competing writer can leave an unreferenced
candidate, but cannot add it to the committed chain. Readers must follow the chain;
enumerating all rows for an invoice would incorrectly include abandoned candidates.
No history operation overwrites or deletes an existing revision. Automatic removal
of orphan candidates is not implemented or authorized by this change.

Before committing another mutation, the domain reads the current item rows and
compares their business content, ignoring storage versions/timestamps. An item-only
change on a stable invoice receives an unattributed observed baseline and the next
snapshot contains the observed lines. Proposed lines during Preparing/Retryable
are not proof that item writes completed: expected partial writes are not labeled
external changes. Failed preparation retains the proposal; cancellation captures
the lines actually observed. A supplied Verified set must still match the fresh
read. These observations do not create an atomic transaction across invoice/items.


Preparation-start records contain the proposed lines and explicitly say Proposed.
Preparation-completed records contain the lines read and verified after persistence
and say Verified. Failed preparation retains its Proposed record. This does not
make the separate invoice/item writes transactional or relax the rule that an
uncertain active writer requires investigation before recovery.

Cancellation, payment-history reconciliation, freeze, access-token creation and
enqueue acknowledgement retain their history alongside the existing lifecycle
rules. Exact replays do not add a material revision. A conditional conflict can
leave an unreferenced candidate, while the winning invoice and history remain
unchanged. Later payment evidence and enqueue acknowledgement preserve cancellation.

## Identity, representation and attribution

`InvoiceRevision.Biller/ID` and `InvoiceRevisionPart.Biller/ID` have meaningful
business names and EntityKey annotations. Models privately own revision/part IDs,
JSON, digest and bounded part layout; the domain owns history registration and
commit ordering. Parts also identify their invoice and revision. No application
Table SDK implementation or new framework package is needed.

Snapshots preserve business invoice/line values, preparation state and lifecycle
evidence. They exclude link credentials and storage ETags/timestamps. They are an
audit format, not a backup format for restoring live worker checkpoints. Snapshots
are bounded to 16 MiB of the complete serialized envelope and use bounded scalar
parts within Table limits. The model owns that representation limit; the domain
returns 400 for an oversized requested update and 409 when existing state cannot
be captured. Both the observed baseline and target are checked before any history
write, so rejected oversized input cannot leave a partial baseline. Values are not
truncated. Failure to capture/verify a snapshot prevents the invoice commit.

A model-owned preflight counts encoded text and a conservative minimum for each
line's serialized structure before content copying or fingerprinting. It uses
bounded scratch space and also catches many short lines whose combined structure
cannot fit. A passing preflight is not proof of capacity: the complete serialized
envelope still supplies the authoritative byte count. The representation limit,
stored JSON and issuance fingerprint algorithm remain unchanged. Invoice/item
comparisons and preparation/freeze paths perform the preflight before materializing
large content. This reduces demonstrated allocation amplification; it is not a
general memory-budget guarantee for every request shape or host configuration.

HTTP issuance records the authenticated biller; private queue issuance records the
trusted consumer authority; the operational email endpoint records Function-key
authority. A shared Function key does not identify a human operator. Existing Core
callers without supplied context remain explicitly unattributed. An optional
`IssueInvoiceRequest.ChangeReason` preserves a nonblank reason of at most 500
characters. When it is omitted, the domain records the operation description,
without claiming that description was supplied by a person.

The first mutation of a legacy invoice records its observed baseline, not invented
earlier history. The target lifecycle state is initialized before taking the new
snapshot, including first-time token creation; the original observed baseline still
retains its pre-lifecycle representation. If a direct writer changes invoice state outside this domain, the
next domain mutation records the observed difference as unattributed and retains
the preceding chain. Its time is the observation time; intermediate external
changes cannot be reconstructed. Direct storage privileges can bypass application
invariants and are not removed by this work.

## Compatibility and delivery

The existing `lifecycle-v1` checkpoint retains its physical name. Lifecycle JSON
schema 2 adds the model-owned `HistoryRevision` reference. Schema 1 remains readable
without a history reference. The domain advances to schema 2 only when referencing
a verified revision; a schema-1 row with a history reference is invalid. The current
model also rejects storing an unanchored lifecycle over an anchored invoice before
mutating its serialized value, preserving the chain and old-writer fence. The actual
prior schema-1 model/domain is pinned in the integration suite. Its unsafe control
demonstrates lost references, while schema 2 rejects its replace-mode writes.

The new `InvoiceRevision` and `InvoiceRevisionPart` tables use the existing data
account. Generic Bicep takes `storageTableNames`; names remain defaults in
`infra/main.bicepparam`. Overrides must retain the required existing and new tables.
No new client SAS grant or broader storage entitlement is added.

The immutable-main test harness installed by PR #24 understands schema 2, rejects direct
synthetic freezes of history-linked rows, and arranges legacy saved-link fixtures
before any HTTP mutation. Cleanup validates the root representation and referenced
revision and every part/revision relationship before conditionally deleting its
owned parent, then history with observed ETags. Failed or uncertain parent deletion
preserves history; unmatched or incomplete candidates remain for investigation. That preparatory
change was delivered before the history application in PR #25 satisfied trusted
verification. Its installation did not substitute for the application's own
candidate evidence and production verification.

Local tests cover snapshots spanning the pinned adapter's 100-operation Table
batch boundary, interrupted history writes, rejected invoice commits, prior
snapshots, exact replay, caller attribution, actual Table mapping, old-writer
fencing and owned cleanup. The candidate deployed suite must verify actual history
through authenticated HTTP and the existing test tables. The existing approved
manual-email test also verifies that actual freeze/enqueue transitions retain a
history snapshot matching the live invoice, without adding another send. Production verification
continues to use the same five GET-only checks. Record exact-head feature tests,
native review, separate approval, protected merge and terminal production/package
results before describing this increment as delivered.
