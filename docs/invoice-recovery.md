# Invoice preparation, replay and recovery

B01 uses the existing biller/invoice key and Niobium's ordinary conditional save.
It does not make the invoice row, item partitions and queue transactional. Item
keys remain unchanged under Q16. The same invoice ID under two billers can still
share an item partition; this change does not resolve B05.

## Preparation and freeze

`UpsertFlow` loads the stored invoice and items before mutation. New invoices
snapshot profile/customer data once. Existing invoices preserve the biller and
customer snapshot unless an explicit customer update is supplied before freeze.
Null display overrides use the original stored defaults, not a new live profile.

| Persisted state | Meaning | Allowed next action |
| --- | --- | --- |
| No state and no token/Delivered | Legacy draft or new invoice | Validate and conditionally claim preparation. |
| `preparing-v1` | One conditional writer owns item replacement | Other requests receive 409. No timed takeover. |
| `retryable-v1` | Item operation definitively failed; previous writer has stopped | Only the same content may claim preparation and replace the partial set. |
| `ready-v1` | Read-back items match the prepared fingerprint | Exact replay avoids writes; edits claim a new preparation; notification may freeze. |
| `frozen-v1`, or a legacy saved token/Delivered | Content may have reached the notification system | Exact replay only. Preserve token, content and saved endpoint. |
| Unknown state version | A newer/unrecognized writer may own the row | Reject with 409; do not downgrade or rewrite. |

Preparation writes the candidate invoice and a unique operation ID using its ETag
(or conditional create for a new identity). It deletes old items with ETag checks,
creates the supplied items without replacement, reads them back, verifies the
fingerprint, and conditionally marks the invoice ready. Partial batches remain
possible. Invoice concurrency conflicts happen before this writer changes items.

Niobium.Database.StorageTable 4.0.60 returns ETags on reads but drops them from
write result models. After such writes the domain reloads and verifies both the
business content and checkpoint fields before using the refreshed ETag. An
intervening writer produces a conflict rather than silently lending its version
to this operation. Local Azurite integration tests exercise this actual adapter.

Before enqueue, `EmailFlow` verifies the persisted items and conditionally saves
the frozen state, fingerprint, link token and invoice endpoint. It then constructs
the notification from that snapshot with stable item order and culture formatting.
An exact replay republishes the same message identity and content. `Delivered`
remains the first successfully saved enqueue completion time, not provider or
recipient confirmation. No downstream exactly-once guarantee is introduced (B02).

Q21 explicitly approves freezing after an uncertain enqueue outcome, including
legacy records with saved tokens even when a historical send failed. A blank
recipient email still skips notification. Legacy token values remain usable;
an already-frozen legacy invoice with no due date is not silently migrated.

## Failure handling

Definitive item rejections (400/404/409/412/413/422) permit a conditional transition
to retryable and propagate the original failure. If saving that checkpoint also
fails, both errors are propagated. No notification is attempted before readiness.
After a retryable failure, resubmit the original business content; the flow reads
the partial set and repairs it. Changed recovery payloads receive 409.

Timeouts, cancellation, transport failures and process loss can leave a remote
write outcome unknown. They leave preparation claimed. Automatic lease expiry
would allow an old writer to overwrite new items because the current item schema
has no fencing token. This implementation deliberately has no automatic takeover.

Recovery of an abandoned preparation requires an independently authorized operator
procedure: stop/quiesce the old writer, retain the original request and evidence,
read invoice/items and the current ETag, establish that no old operation can still
complete, and review whether the stored fingerprint matches that original request.
Only then can a conditional recovery transition be considered. No production
recovery command, migration or unsafe force-save endpoint is supplied here. If
the original request or writer outcome is unknown, the invoice remains blocked.

If enqueue or the subsequent Delivered save fails, keep the invoice frozen and
retry the exact request. The queue may already contain the message. Notification
must handle duplicates; never clear the saved token or thaw content to retry.

## Storage and consumer compatibility

Seven nullable string columns are added to Invoice: `IssuanceState`,
`IssuanceFingerprint`, `IssuanceOperation`, `IssuanceDefaultParticulars`,
`IssuanceDefaultReference`, `IssuanceDefaultPaymentInstructions` and
`IssuanceNotificationEndpoint`. They are excluded from ordinary invoice JSON and
messages using JsonIgnore, but the installed framework's table mapper persists
them. Real adapter tests cover this distinction. No key, table or package version
changes, automatic backfill, or production migration are performed.

The fingerprint is a versioned SHA-256 of business invoice fields and ordered
line values, excluding storage metadata, token, enqueue timestamp and coordination
fields. Equivalent instants use UTC. Future business-field or serialization
changes need explicit fingerprint compatibility handling before deployment; adding
a field to this contract is not automatically replay-compatible.

Older servers and direct table writers can bypass this protocol, erase columns
with replace writes or change frozen items. They must be reviewed and quiesced
before rollout or rollback. The direct-resource entitlement model is unchanged;
these safeguards govern cooperative Core issuance flows, not arbitrary authorized
table mutations. No live client inventory or safe mixed-version rollout is claimed.

See [implementation progress](implementation-progress.md) for acceptance evidence,
remaining deployment checks and decisions. B06 confirmation/cancellation and B08
audit/history/retention remain separate work, not meanings of these internal states.
