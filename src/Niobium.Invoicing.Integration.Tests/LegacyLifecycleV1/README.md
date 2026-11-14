# Prior lifecycle writer compatibility fixture

These nine source files are pinned to production merge `8b0e9faa1a2561486783ab886530f76313d92e5a`.
Only the added source header, namespace relocation and shared formatting imports
change. They execute the actual prior rich-domain and conditional Table writer
against owned loopback storage. The unsafe schema-1 control demonstrates loss
of an unrecognized history reference; schema 2 must reject that writer.
This is not a production rollback or authorization for direct storage writes.

| Original path | Original bytes SHA-256 |
| --- | --- |
| `src/Niobium.Invoicing.Contracts/Invoice.cs` | `c18a9ba3160bbeb0b55b8e45e4268af70f0c1845bcab3e709c01bb6838d180d7` |
| `src/Niobium.Invoicing.Contracts/InvoiceExtensions.cs` | `6652557668c5b12d22beff34498077ed0b0e4f66cc703b9f1cb6e8248dc4fe76` |
| `src/Niobium.Invoicing.Contracts/InvoiceLifecycle.cs` | `d655f2899c8edcf5087244ba963bb484aa13960fb0c5c2036b39623a00b8cb5e` |
| `src/Niobium.Invoicing.Core/InvoiceValidation.cs` | `bc88655acf3a8fd72b6449c65c5cce193178432d559de8aefb3aa102b4fab8fe` |
| `src/Niobium.Invoicing.Core/Domains/InvoiceDomain.cs` | `5225c71513ec2ade07da19e5d309c9471ad69c10465b26ddcedd542905461545` |
| `src/Niobium.Invoicing.Core/Domains/InvoicePreparation.cs` | `c5a77f909fdc9ca08c326e176d4551bf474a2c424abc4d31fff99fe7449ae62d` |
| `src/Niobium.Invoicing.Core/Domains/InvoiceContent.cs` | `14b8d75b55f8936175dc97fa7912ca175e7c85fe6114af3b0b16ba87f8a245a2` |
| `src/Niobium.Invoicing.Core/Domains/InvoiceExtensions.cs` | `c332e2c85103cab757b3a630576a76f05d583ee692eddbedee295c38cdaac319` |
| `src/Niobium.Invoicing.Core/Domains/InvoiceLifecycle.cs` | `26fc99d7e1ec176e99835b58d1303315638672607cc251f1d2bdb6334db296ba` |
