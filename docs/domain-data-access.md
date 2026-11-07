# Rich domains and repository usage

This investigation follows the review of
[Notification PR #2](https://github.com/niobium-nz/Niobium.Notification/pull/2).
Invoicing source was checked at baseline `74d7a8f877f38b943f106a8f4745c522fdb4d213`.
The package/source mapping remains in [framework provenance](niobium-integration.md).

## Where behavior belongs

[InvoiceDomain](../src/Niobium.Invoicing.Core/Domains/InvoiceDomain.cs) and
[InvoicePreparation](../src/Niobium.Invoicing.Core/Domains/InvoicePreparation.cs)
form a rich `GenericDomain<Invoice>`. They own validation, preparation, frozen
content and conditional transitions. Invoice and line contracts are stored snapshots;
the presence of POCOs does not mean the design is anemic. Keep meaningful mutations
on the domain, and let flows coordinate the biller/customer domains and messaging.

[InvoiceItemDomain](../src/Niobium.Invoicing.Core/Domains/InvoiceItemDomain.cs)
keeps line persistence behind `IRepository`. Application business logic should not
construct Table entities or use `IAzureTableClientFactory`; the framework adapter
owns those details. Check existing repository operations before adding a new store.

`IDomainRepository.GetAsync` returns an initialized domain, not proof a row exists.
The domain loads through `GetEntityAsync`/`TryGetEntityAsync`; `Initialize` and
`BuildAsync` do not save. Caches do not establish distributed ownership.

## Preserve concurrency and compatibility

Default `SaveAsync` chooses create or conditional update from the mapped ETag.
`force: true` uses an upsert and bypasses the update precondition. The installed
4.0.60 Table adapter loses write-response ETags when converting back to models.
`PersistInvoiceAsync` therefore refreshes a missing version and verifies the entire
persisted content before accepting it. This is distinct from blindly reloading and
using another writer's latest ETag. Notification's claim domain adds a unique write
identity to distinguish otherwise identical competing mutations.

The base domain emits change events after persistence; those events are not an
atomic outbox. Invoice/items/Blob/messaging operations do not become transactional
because they are called from one domain. Preserve the checkpoint protocol and
unknown-writer policy in [recovery and compatibility](invoice-recovery.md).
Direct Table/SAS clients can bypass domain invariants; no documentation change
removes that risk or grants authority to migrate clients.

Use enums for finite state decisions and outcomes. Existing issuance checkpoint
strings are a stored compatibility contract; converting them requires an explicit
versioned plan, not an incidental documentation cleanup. The current Table mapper
supports scalar columns, not a general enum codec. JSON ignore attributes do not
exclude properties from Table mapping. Keep I/O and mutation calls out of `if`
conditions; execute the operation and then test a named result.

The [framework guide](https://github.com/niobium-nz/Niobium/blob/1e370fccd38635f7209c1c4b01ddafcbe10ea618/docs/domain-data-access.md)
records registration, mapping, save/event semantics and source-backed limitations.
It also points to Store examples and the Notification domain refactor. No Invoicing
runtime, public contract, package or schema change accompanies this guide.
