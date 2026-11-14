# Prior invoice writer compatibility fixture

These seven files reproduce the invoice model and writer from Invoicing main
`bae20a356dc9c03e89df8c690dcf215b8b5a4825`. Only the source comment, namespace relocation and imports for the unchanged
shared formatting helpers in `InvoiceDomain`/`InvoiceExtensions` are changed. They use the same pinned Niobium packages as the application.
The tests exercise the actual old conditional replace path against disposable
loopback Table storage; they do not run against Azure or production.

The unsafe control proves an unfenced new scalar column is erased by the old
writer. The guarded cases require rejection before its ETag or lifecycle evidence
changes. This covers the previous issuance, freeze and enqueue-acknowledgement
paths, not arbitrary direct repository writes or permission to roll back the host.

| Original path | Original bytes SHA-256 |
| --- | --- |
| `src/Niobium.Invoicing.Contracts/Invoice.cs` | `edbebf66db66c7f2f0f79b2dd00f4861cdabb9e93a07caaec758ee7fa45fb265` |
| `src/Niobium.Invoicing.Core/InvoiceValidation.cs` | `bc88655acf3a8fd72b6449c65c5cce193178432d559de8aefb3aa102b4fab8fe` |
| `src/Niobium.Invoicing.Core/Domains/InvoiceDomain.cs` | `303af144518bbab12be6c364b3beeb0d204dd0cf195fdf79d210e0d54b30cd06` |
| `src/Niobium.Invoicing.Core/Domains/InvoicePreparation.cs` | `ee35953306bbf2f362fdb7a567fe42b93338661a14bcba33082c8128e78101b9` |
| `src/Niobium.Invoicing.Core/Domains/InvoiceContent.cs` | `5a01e69c252cd6faae4327cb3189fea3a9dbddc4616ec99e7d12f9e4159345a8` |
| `src/Niobium.Invoicing.Core/Domains/InvoiceExtensions.cs` | `c332e2c85103cab757b3a630576a76f05d583ee692eddbedee295c38cdaac319` |
| `src/Niobium.Invoicing.Contracts/InvoiceExtensions.cs` | `6652557668c5b12d22beff34498077ed0b0e4f66cc703b9f1cb6e8248dc4fe76` |
