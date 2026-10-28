# Niobium.Invoicing.Contracts

Shared .NET 10 invoice models, requests, commands, key helpers and arithmetic in the `Niobium.Invoicing` namespace. Core and Server consume this project; it is the package selected by the repository's NuGet publication workflow. It does not implement a client UI, MCP server or payment processor.

## Contents and use

- `Biller`, `Billee`, `Billable`: issuer profile, customer and catalog contracts.
- `Invoice`, `InvoiceItem`, `InvoiceCycle`: snapshots, line items and billing-period presentation.
- `IssueInvoiceRequest`, `IssueInvoiceCommand`: HTTP flow input and private queue input with embedded customer data.
- `InvoiceExtensions`: subtotal, tax and tax-inclusive final-total calculations.

Consumers should select a compatible published version of `Niobium.Invoicing.Contracts` explicitly. Its declared framework dependencies are Niobium.Abstractions/Helper 4.0.61 and Niobium.Profile/Finance 4.0.60 at the [analyzed baseline](../../docs/README.md). Package availability and consumer compatibility were not tested by this documentation task.

```csharp
using Niobium.Invoicing;

long invoiceId = Invoice.ParseID(DateTimeOffset.UtcNow);
InvoiceItem item = InvoiceItem.BuildNew(invoiceId, "NZD");
item.Subject = "Example service";
item.UnitPriceCents = 10000;
item.Quantity = 1;
```

This constructs synthetic data; it does not save or send it. Invoice IDs use the matched framework's **reverse** Unix millisecond encoding. Cents are integer minor units, and a tax rate of 1500 means 15%. The final grand total includes tax. Required C# members do not establish business validation. Read [domain/data](../../docs/domain-and-data.md) before changing identifiers, amounts or fields, and [API/messaging](../../docs/api-and-messaging.md) for wire behavior and an issuance example.

## Compatibility and contribution

The public types and their storage/JSON representations are consumed across service boundaries. Preserve existing names, including legacy spelling, unless a separately reviewed compatibility change is intended. Follow [AGENTS.md](../../AGENTS.md) and [testing/conventions](../../docs/testing-and-conventions.md). Review [workflow side effects](../../docs/delivery-and-operations.md) before submitting: changes anywhere under this project can qualify for package publication. XML comments were added without enabling persistent XML-output/package settings.

The repository's existing license text is in [LICENSE.md](../../LICENSE.md).
