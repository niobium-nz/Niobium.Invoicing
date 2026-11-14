# Business names and model contract ownership

The owner's rule of 14 November 2026 applies to all Niobium Table models.
[The framework guide](https://github.com/niobium-nz/Niobium/blob/main/docs/model-contracts.md)
explains the mapper, examples, responsibility boundaries and compatibility checks.

Name properties for their actual business meaning and annotate their storage role
with `[EntityKey(EntityKeyKind.PartitionKey)]` or
`[EntityKey(EntityKeyKind.RowKey)]`. The repository handles physical Table column
names. `Invoice.Biller/Created`, `Billee.Biller/ID` and `InvoiceItem.Invoice/ID` illustrate
the annotated pattern; verify each model's actual partition semantics before
copying it. An invoice-item partition is not simply a tenant partition.

Models own their key representation, ID derivation and serialized/path contracts.
Expose a small meaningful factory or lookup API and keep hashing/encoding helpers
private. A domain should consume a model's identity or file reference, not recreate
its formatting. Pure representation methods do not move issuance, billing,
cancellation, payment or validation policy out of rich domains.
See [domain behavior and persistence](domain-data-access.md).

The Table mapper sees public computed properties as columns. JSON attributes do
not exclude a property from that mapping. Avoid old/new public key aliases on one
mapped model; use methods or separate transport contracts where appropriate.
Generic infrastructure types such as `StorageKey` legitimately describe storage.

## Legacy profile boundary

At inspected main `41deb0b`, [Biller](../src/Niobium.Invoicing.Contracts/Biller.cs)
still exposes `PartitionKey` for management-group identity and `RowKey` for
biller/user identity from the external profile service. This is legacy debt,
not a naming example for new models. The owner selected documentation now and
a separate compatibility rollout for existing public contracts.

Before renaming it, trace profile JSON, `IProfile` consumers, published Contracts,
tests and clients. Preserve stored reverse-time keys, existing invoice-link
tokens, monetary values and wire identities. Keep the original research plan
unchanged. No model, package, storage migration or runtime behavior changes with
this documentation. The framework guide inventories other legacy APIs.
