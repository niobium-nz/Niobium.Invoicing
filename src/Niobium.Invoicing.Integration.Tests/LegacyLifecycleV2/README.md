# Prior lifecycle schema-2 writer fixture

These source files are pinned to production merge `f9fa136771bef08b837a72379a8a1d7d77d7ec0f`.
Only the added source header, namespace relocation and shared rendering/formatting imports
change. They exercise the actual prior domain, history writer, model codec and
conditional Table adapter against owned loopback fixtures. An unsafe schema-2
control demonstrates loss of an unknown payment position; schema 3 must reject
that writer before any persistence. This is a test fixture, not a production
rollback, migration or authorization to write production data.

| Original path | Original bytes SHA-256 |
| --- | --- |
| `src/Niobium.Invoicing.Contracts/Invoice.cs` | `c18a9ba3160bbeb0b55b8e45e4268af70f0c1845bcab3e709c01bb6838d180d7` |
| `src/Niobium.Invoicing.Contracts/InvoiceExtensions.cs` | `6652557668c5b12d22beff34498077ed0b0e4f66cc703b9f1cb6e8248dc4fe76` |
| `src/Niobium.Invoicing.Contracts/InvoiceLifecycle.cs` | `0a6d0afcd1879f6af8c446ae8954fd5e611a620364d0d98108a72996398ae4f4` |
| `src/Niobium.Invoicing.Contracts/InvoiceHistory.cs` | `404d39e155798d75d5a1d10da6de4d8349fda36aa09d8babfb2b6320f0a345c8` |
| `src/Niobium.Invoicing.Core/InvoiceValidation.cs` | `dd35018ac1800c2978ae456f1299336b40d9e8209845bdc594c0e9f0a9398ae8` |
| `src/Niobium.Invoicing.Core/Domains/InvoiceDomain.cs` | `f81040599bbdfa31bc584228abcc2bddca62db450d3e3d69c104858c2c747153` |
| `src/Niobium.Invoicing.Core/Domains/InvoicePreparation.cs` | `3bdf5a1bfe4337c42affb49770a78aff5b3df0a35753c1ec5bfbf189cc2e2ddb` |
| `src/Niobium.Invoicing.Core/Domains/InvoiceContent.cs` | `14b8d75b55f8936175dc97fa7912ca175e7c85fe6114af3b0b16ba87f8a245a2` |
| `src/Niobium.Invoicing.Core/Domains/InvoiceExtensions.cs` | `c332e2c85103cab757b3a630576a76f05d583ee692eddbedee295c38cdaac319` |
| `src/Niobium.Invoicing.Core/Domains/InvoiceLifecycle.cs` | `205be974db09102595bacfa3554744d6fbfe3f288a13576adb4948f5d8e4af11` |
| `src/Niobium.Invoicing.Core/Domains/InvoiceHistory.cs` | `791e50bc6ab96cded1fd7074b411405b4abbe65bf80fd23e797eeba5e691a4c0` |
| `src/Niobium.Invoicing.Core/Domains/InvoiceHistoryReader.cs` | `6689cdf67e2b93601fc0840da54d5bee3775f6e59e38ae7d8ff807798b4c531f` |
| `src/Niobium.Invoicing.Core/History/InvoiceRevision.cs` | `0747a71cfd52824ebc150c3eee32f82f31a21aa1992854fbb6b36bb640626dcc` |
| `src/Niobium.Invoicing.Core/History/InvoiceRevisionPart.cs` | `9456c6cba29a9967015779013fe146f387b17388200cd6d54618d0f8006f2ffd` |
| `src/Niobium.Invoicing.Core/History/InvoiceHistoryDomain.cs` | `2802fe7d23aec712e1327d6209f42a4c2b54b775b1560e32fe7dff975229fc29` |
| `src/Niobium.Invoicing.Core/History/InvoiceHistoryCursor.cs` | `5e980ffdffc22287643351ce3df55783290b16aa4a1a18632b68a0306abbacb9` |
