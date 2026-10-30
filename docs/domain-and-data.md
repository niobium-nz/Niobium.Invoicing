# Domain and data model

## Money, tax and settlement

Observed arithmetic is in [Contracts.InvoiceExtensions](../src/Niobium.Invoicing.Contracts/InvoiceExtensions.cs) and [InvoiceDomain.UpdateAsync](../src/Niobium.Invoicing.Core/Domains/InvoicePreparation.cs):

```text
line cents = UnitPriceCents * Quantity
subtotal cents = sum(line cents)
tax cents = Round(subtotal cents * TaxRatePercentile / 10000, 0)
grand total cents = subtotal cents + tax cents
displayed due cents = max(grand total cents - SettledCents, 0)
```

`long` amounts represent cents; `int` quantities are whole units. Tax rate `1500` means 15%, not 1500% or 0.15%. `Math.Round` uses midpoint-to-even unless specified otherwise. Decimal display helpers round to two decimals; stored totals are recalculated on update. The helpers may enumerate input more than once, so callers need repeatable item sequences. Core now validates ownership, currency consistency, date ordering and checked arithmetic before persistence; see [billing validation](billing-validation.md). Quantity signs and credit policy remain unchanged. A stored line total is overwritten during update; rendering uses stored values.

**Q09 clarification:** tax-inclusive means the final HTML grand total includes tax. It does not mean tax must be backed out of `UnitPriceCents`. For a synthetic line of 10,000 cents, quantity 1 and rate 1500, current subtotal is 10,000, tax 1,500 and final total 11,500 cents. This is consistent with that clarification. Biller-level 0% represents exemption in this product, not a universal statement about legal tax classifications.

`Settled` is not a payment transaction: nonzero amounts must match the invoice currency, and zero is currency-neutral. Its cents are copied without payment reconciliation. Manual bank-transfer overpayment is allowed by intended policy (Q03); due display clamps at zero. Automated collection for the exact outstanding balance is planned. Detailed rounding, fractional quantities and negative/credit policy remain [U01](decisions-and-risks.md#accepted-unknowns).

## Identifiers, tables and mapping

Evidence: [Invoice](../src/Niobium.Invoicing.Contracts/Invoice.cs), [InvoiceItem](../src/Niobium.Invoicing.Contracts/InvoiceItem.cs), [Billee](../src/Niobium.Invoicing.Contracts/Billee.cs), and matched `EntityMappingHelper` / `DBEntityHelper` / `CloudTableRepository` in [Niobium integration](niobium-integration.md).

| Type / default table | Partition key | Row key | Access patterns |
| --- | --- | --- | --- |
| Invoice | `Invoice.BuildPartitionKey(biller)`: standard GUID string | `Created` mapped to reverse Unix milliseconds, padded to at least 12 characters; `BuildRowKey(id)` | Retrieve one biller+invoice; direct biller-partition query |
| InvoiceItem | `Invoice` issue instant mapped to the same reverse invoice timestamp | `ID` converted to decimal text | Query all lines by invoice ID; replace supplied set; no biller/group column in key |
| Billee | `Biller.ToKey()` | `ID.ToKey()` | Retrieve customer within biller; direct personalized CRUD |
| Billable | No key attributes or PartitionKey property declared | No key attribute or RowKey property declared | Direct resource entitlement exists; no app catalog flow or complete mapped schema established |
| Biller | External IProfile PartitionKey = group | External IProfile RowKey = biller/user | Retrieved via profile HTTP service, not a local Biller repository |
| Role / Entitlement | Framework-managed schema | Framework-managed schema | Access-token role and entitlement lookup; lifecycle/administration external |

Default keys use hyphenated GUID strings. Framework legacy GUID formatting can differ, but this app does not enable it. Reverse invoice IDs use `anchorUnixMilliseconds - createdUnixMilliseconds`, with anchor **2100-01-01 UTC** in the matched default; a framework legacy option uses 2050. The app does not enable that option. `Invoice.ParseID` encodes/decodes; `GetFullID` is `invoiceID@biller`. Owner shorthand “timestamp ID” must not be substituted for this wire encoding. `InvoiceItem.BuildNew` uses ordinary Unix milliseconds for the line ID; its public `BuildRowKey` takes `int` even though ID is `long`.

Q02 establishes that global invoice-ID uniqueness is not guaranteed. Exactly one group per biller (Q12) does not add biller identity to item keys. Q16 explicitly defers this risk under current usage assumptions; it is not a safety guarantee. Ownership and uniqueness assumptions need review before schema changes; do not invent relational foreign keys or transactions. Item inputs are not rewritten to the request invoice key by `UpsertFlow`.

Mapping uses public properties, with `EntityKey` renaming the partition/row/timestamp/ETag properties. Keys are strings in storage; non-key scalar properties retain Azure-supported scalar types (such as GUID, integer, string and DateTimeOffset). Null properties are not stored as ordinary null-valued table columns. Timestamp is service metadata and is skipped on writes. ETag is separate SDK metadata. Ordinary JSON ignores table-key attributes: it uses model property names rather than renaming `Biller` to `partitionKey`.

No table creation or schema migration is declared here. The repository obtains clients for existing table names. Adding a nullable property is still a compatibility change for serializers, existing entities and direct table clients; there is no automatic backfill/version migration mechanism.

## Persistence and concurrency

`UpsertFlow.RunAsync` loads the existing aggregate and preserves its snapshots. A conditional preparation claim precedes item replacement; read-back verification precedes readiness; content and token are frozen before enqueue. Exact replays avoid business writes and may republish the same notification. See [preparation and recovery](invoice-recovery.md) for states, failure handling, compatibility and limitations.

The installed adapter retains ETags on reads but loses them on write results. The domain reloads and verifies persisted content before continuing. Local Azurite tests establish stale-ETag rejection and real adapter behavior. These checks do not establish live Azure deployment behavior or atomicity across tables and messaging.

The table adapter groups transactions by partition and at most 100 operations, executing batches sequentially. Separate groups can partially complete. No outbox/inbox, key migration, automatic abandoned-writer takeover or downstream delivery guarantee is introduced.

## Lifecycle, dates and presentation

`Invoice.BuildNew` snapshots biller contact/address/tax data and billee identity/address/email/currency/culture/timezone. It creates particulars/reference and sets `DueBy = UtcNow + 7 days`. Update preserves the default due date when no override is supplied and rejects dates before issue. Terms, billing period, totals and settlement are applied. Particulars, Reference and PaymentInstructions accept trimmed overrides; null inherits defaults and an empty string clears the value. Replays preserve the stored snapshots and original defaults (Q11/Q20).

`Delivered` stores enqueue-related completion time. Internal preparation/freeze columns are distinct from a delivery lifecycle. There is no public Delivering/Cancelled state, payment ledger or audit-version collection. Intended editing/freeze/cancellation rules are in [product requirements](product.md). Five-year retention, soft deletion and all-version audit history are planned (Q08); no expiry/migration/backup mechanism is inferred from table storage.

`InvoiceCycle` values are `Once=0`, `Daily=1`, `Monthly=2`, **`Anually=3`** (existing spelling), `Range=4`. They only choose date presentation: none, start day, start month, start year or start/end range. They do not schedule recurring invoices. Range needs both endpoints; missing inputs omit the billing-period label. DueBy null omits its label.

`TimeZoneInfoHelper` converts IANA IDs to Windows IDs then resolves system timezone data. Missing/invalid zones fail; culture/country/currency parsing can also fail during rendering. The template has English labels and external CSS/font dependencies, so localized dates/currencies do not imply a fully translated invoice. InvariantGlobalization is declared in class libraries but was not enabled in the inspected Server/test runtime configs.

[InvoiceTemplate.html](../src/Niobium.Invoicing.Core/InvoiceTemplate.html) is the embedded resource actually selected; saved `Template` is not used. `BuildTemplateParameters` creates upper snake-case strings, blank strings for optional text and special keys including INVOICE_ID, BILL_DATE, BILLING_PERIOD, SUBTOTAL, TAX_AMOUNT, TAX_RATE, TAX_KIND, GRAND_TOTAL, SETTLED, DUE and DUE_BY. Notification adds INVOICE_URL and INVOICE_ITEM arrays. The renderer extracts the marked invoice-item block, renders each line and replaces that block. No general-purpose localization or template-selection engine is present.

`HtmlTemplate.Render` encodes values once at HTML output and does not expand placeholders contained in supplied text. The HTML path prepares logo/contact URLs separately; shared notification parameter values remain unencoded. Text fields containing stored markup are displayed as literal text. Public rendering requires a matching saved token; saved legacy tokens remain authoritative and are not regenerated from totals or line data. See [security](security.md#invoice-link-compatibility).

## Field catalog

The tables below cover every declared application contract property. `required` records the C# modifier, not runtime business validation. `?` means nullable. Unless an initializer/factory is described, numbers default to zero, booleans to false, GUIDs to empty, nullable/reference fields to null and non-nullable DateTimeOffset to its default in an uninitialized object. Required members must be supplied by callers/compatible JSON deserialization; Core validation separately checks the billing invariants described above. Factories and update behavior are described separately above.

JSON uses the queue camelCase conventions described in [API/messaging](api-and-messaging.md); no per-field JSON renaming is declared. Only IssueInvoiceCommand event metadata has explicit JsonIgnore. Configuration types follow the catalog in [development](configuration-and-development.md).

### Biller

Source: [Biller.cs](../src/Niobium.Invoicing.Contracts/Biller.cs).

| Field | C# type / modifier | Meaning and behavior |
| --- | --- | --- |
| `PartitionKey` | `Guid` | Management-group identifier exposed by IProfile.GetTenant; profile storage is external. |
| `RowKey` | `Guid` | Biller/user identifier exposed by IProfile.GetUser; copied to Invoice.Biller. |
| `Timestamp` | `DateTimeOffset?` | Optional upstream profile modification timestamp. |
| `ETag` | `string?` | Optional upstream profile concurrency metadata. |
| `AddressLine1` | `string?` | First postal-address line. |
| `AddressLine2` | `string?` | Optional second postal-address line. |
| `Suburb` | `string?` | Optional postal suburb or locality. |
| `BusinessID` | `string?` | Optional business identifier copied into invoices. |
| `BusinessName` | `string` required | Business display name copied into the issued invoice snapshot. |
| `City` | `string?` | Optional postal city. |
| `State` | `string?` | Optional postal state or region. |
| `Country` | `string?` | Optional country code; invoice rendering parses it to a country name. |
| `Email` | `string?` | Biller contact email copied to the invoice, not the notification destination. |
| `Logo` | `string?` | Optional logo reference copied into the invoice template parameters. |
| `PaymentInstructions` | `string?` | Default payment text copied by BuildNew; UpdateAsync subsequently assigns the request value, including null. |
| `ContactName` | `string?` | Biller contact person copied into the invoice. |
| `Phone` | `string?` | Biller contact telephone copied into the invoice. |
| `TaxID` | `string?` | Tax registration identifier copied into the invoice; no jurisdictional validation is implemented. |
| `TaxRatePercentile` | `int` | Tax rate in hundredths of one percent: 1500 means 15%; copied to invoices. Zero represents exemption in the product. |
| `TaxKind` | `int?` | Optional numeric Niobium.Finance.TaxKind value used as a display label. |
| `Zipcode` | `string?` | Optional postal code. |
| `Template` | `string?` | Optional template reference copied to invoices; current HTML rendering uses the embedded resource instead. |
| `TimeZone` | `string` required | Biller timezone profile value; BuildNew currently selects the billee timezone for the invoice. |
| `Currency` | `string` required | Biller currency profile value; BuildNew currently selects the billee currency for totals. |
| `Culture` | `string` required | Biller culture profile value; BuildNew currently selects the billee culture for the invoice. |

### Billee

Source: [Billee.cs](../src/Niobium.Invoicing.Contracts/Billee.cs).

| Field | C# type / modifier | Meaning and behavior |
| --- | --- | --- |
| `Biller` | `Guid` | Owning biller identifier, mapped to the table partition key. |
| `ID` | `Guid` | Customer identifier within the biller partition, mapped to the row key. |
| `Timestamp` | `DateTimeOffset?` | Storage modification timestamp, distinct from Created. |
| `ETag` | `string?` | Optional storage ETag for precondition-aware repository operations. |
| `Created` | `DateTimeOffset?` | Optional creation time; populated by the framework on create when absent. |
| `AddressLine1` | `string?` | First postal-address line. |
| `AddressLine2` | `string?` | Optional second postal-address line. |
| `Suburb` | `string?` | Optional postal suburb or locality. |
| `BusinessID` | `string?` | Optional business identifier copied into invoices. |
| `Name` | `string` required | Customer display name copied to new invoices and notification destination display name. |
| `City` | `string?` | Optional postal city. |
| `State` | `string?` | Optional postal state or region. |
| `Country` | `string?` | Optional country code; invoice rendering parses it to a country name. |
| `Email` | `string?` | Optional invoice recipient address; blank causes EmailFlow to skip enqueueing. |
| `InstantMessaging` | `string?` | Optional customer messaging contact; unused by current invoice flows. |
| `Phone` | `string?` | Optional customer telephone contact; not copied into invoice biller contact fields. |
| `Zipcode` | `string?` | Optional postal code. |
| `Culture` | `string` required | Culture name used for invoice date formatting, for example en-NZ. |
| `Currency` | `string` required | Currency code copied to all invoice total-currency fields; Core requires matching supported biller/item currencies. |
| `TimeZone` | `string` required | IANA timezone used for invoice dates, for example Pacific/Auckland. |

### Billable

Source: [Billable.cs](../src/Niobium.Invoicing.Contracts/Billable.cs).

| Field | C# type / modifier | Meaning and behavior |
| --- | --- | --- |
| `Biller` | `Guid` | Biller identity associated with this catalog entry; this type has no explicit table-key mapping. |
| `ID` | `Guid` | Catalog-entry identifier; this type has no explicit row-key mapping. |
| `Timestamp` | `DateTimeOffset?` | Optional storage modification time supplied by infrastructure. |
| `Created` | `DateTimeOffset?` | Optional creation time; the framework fills it on create for ITrackable entities when absent. |
| `ETag` | `string?` | Optional storage concurrency metadata; not a business version number. |
| `Subject` | `string?` | Optional reusable line subject; no catalog-to-invoice lookup is implemented here. |
| `Description` | `string?` | Optional reusable line description. |

### Invoice

Source: [Invoice.cs](../src/Niobium.Invoicing.Contracts/Invoice.cs).

| Field | C# type / modifier | Meaning and behavior |
| --- | --- | --- |
| `Biller` | `Guid` required | Issuing business identifier and table partition key. |
| `Created` | `DateTimeOffset` required | Issue instant encoded in the consumer-supplied reverse-millisecond invoice ID; mapped to the row key. |
| `Timestamp` | `DateTimeOffset?` | Storage modification timestamp, distinct from issue time. |
| `ETag` | `string?` | Optional storage ETag; force-save does not use it as an update precondition. |
| `Tenant` | `Guid` required | Invoicing management-group identifier copied from the biller profile; not part of the table key. |
| `BillerLogo` | `string?` | Biller logo reference captured when the invoice is built. |
| `BillerName` | `string` required | Biller business display name captured when the invoice is built. |
| `BillerAddressLine1` | `string?` | Biller first address line snapshot copied when the invoice is built. |
| `BillerAddressLine2` | `string?` | Biller second address line snapshot copied when the invoice is built. |
| `BillerAddressSuburb` | `string?` | Biller suburb/locality snapshot copied when the invoice is built. |
| `BillerAddressCity` | `string?` | Biller city snapshot copied when the invoice is built. |
| `BillerAddressState` | `string?` | Biller state/region snapshot copied when the invoice is built. |
| `BillerAddressCountry` | `string?` | Biller country code snapshot copied when the invoice is built. |
| `BillerAddressZipcode` | `string?` | Biller postal code snapshot copied when the invoice is built. |
| `BillerBusinessID` | `string?` | Optional biller business identifier snapshot. |
| `BillerTaxID` | `string?` | Optional biller tax registration identifier snapshot. |
| `Billee` | `Guid` required | Customer identifier captured from the billee; no storage foreign key enforces this relationship. |
| `BilleeName` | `string` required | Customer display name snapshot used in the invoice and notification. |
| `BilleeAddressLine1` | `string?` | Billee first address line snapshot copied when the invoice is built. |
| `BilleeAddressLine2` | `string?` | Billee second address line snapshot copied when the invoice is built. |
| `BilleeAddressSuburb` | `string?` | Billee suburb/locality snapshot copied when the invoice is built. |
| `BilleeAddressCity` | `string?` | Billee city snapshot copied when the invoice is built. |
| `BilleeAddressState` | `string?` | Billee state/region snapshot copied when the invoice is built. |
| `BilleeAddressCountry` | `string?` | Billee country code snapshot copied when the invoice is built. |
| `BilleeAddressZipcode` | `string?` | Billee postal code snapshot copied when the invoice is built. |
| `BilleeBusinessID` | `string?` | Optional customer business identifier snapshot. |
| `Particulars` | `string?` | Payment particulars generated from the customer name, uppercased with LIMITED/spaces removed and truncated to 12 characters; a nonnull request overrides this default. |
| `Reference` | `string?` | Payment reference derived from the invoice ID, retaining its last 12 characters; a nonnull request overrides this default. |
| `InvoiceCycle` | `int` | Numeric InvoiceCycle used only for billing-period presentation; it does not schedule recurring invoices. |
| `BillingPeriodStartDay` | `DateTimeOffset?` | Optional start instant used for daily, monthly, annual or range presentation. |
| `BillingPeriodEndDay` | `DateTimeOffset?` | Optional end instant used only for custom-range presentation. |
| `SubtotalCurrency` | `string` required | Currency code for displaying the subtotal, initially copied from the billee. |
| `SubtotalCents` | `long` | Stored sum of unit-price cents multiplied by integer quantities, before invoice-level tax. |
| `TaxCurrency` | `string` required | Currency code for displaying tax, initially copied from the billee. |
| `TaxCents` | `long` | Stored invoice-level tax rounded to whole cents using midpoint-to-even. |
| `TaxRatePercentile` | `int` | Biller tax rate in hundredths of one percent; 1500 means 15%. |
| `TaxKind` | `int?` | Optional numeric Niobium.Finance.TaxKind used as a display label. |
| `GrandTotalCurrency` | `string` required | Currency code for the grand total, settlement and due display, initially copied from the billee. |
| `GrandTotalCents` | `long` | Stored subtotal plus tax in cents; the displayed final total includes tax. |
| `DueBy` | `DateTimeOffset?` | Optional due instant in the current contract. BuildNew defaults to UTC now plus seven days; Core preserves it for a null override and rejects dates before issue. |
| `ContactName` | `string?` | Biller contact person snapshot. |
| `ContactPhoneNumber` | `string?` | Biller contact telephone snapshot. |
| `ContactEmailAddress` | `string?` | Biller contact email snapshot, not the notification destination. |
| `Terms` | `string?` | Optional request terms, trimmed during update. |
| `PaymentInstructions` | `string?` | Optional payment text; a nonnull trimmed request overrides the profile default; empty clears it and null inherits it. |
| `TimeZone` | `string` required | Billee IANA timezone captured for invoice date presentation. |
| `Culture` | `string` required | Billee culture name captured for invoice date presentation. |
| `RecipientEmail` | `string?` | Captured customer email address; a blank value skips notification enqueueing. |
| `SettledCents` | `long` | Reported amount already settled in cents; nonzero settlement must use the invoice currency; zero is currency-neutral and overpayment remains allowed. |
| `Delivered` | `DateTimeOffset?` | UTC time recorded after notification enqueueing succeeds and this invoice is saved; not recipient-delivery confirmation. |
| `Token` | `string?` | Saved bearer link token required for public viewing and reused on resend. New v2 tokens are saved before enqueueing; legacy saved values remain valid. No explicit expiry field is present. |
| `Template` | `string?` | Copied profile template reference; current HTML rendering ignores it in favor of the embedded template. |

### InvoiceItem

Source: [InvoiceItem.cs](../src/Niobium.Invoicing.Contracts/InvoiceItem.cs).

| Field | C# type / modifier | Meaning and behavior |
| --- | --- | --- |
| `Invoice` | `DateTimeOffset` required | Parent invoice issue instant, mapped to the reverse-timestamp partition key; it contains no biller or management-group identifier. |
| `ID` | `long` required | Line identifier mapped to the row key; BuildNew uses current ordinary Unix milliseconds. |
| `UpdatedAt` | `DateTimeOffset?` | Storage modification timestamp for the line. |
| `ETag` | `string?` | Optional storage ETag for precondition-aware operations. |
| `Subject` | `string` required | Line subject shown in invoice and notification templates. |
| `Description` | `string?` | Optional line description. |
| `UnitPriceCurrency` | `string` required | Currency code for unit-price display; Core requires a supported currency matching the invoice. |
| `UnitPriceCents` | `long` | Unit price in cents used by line arithmetic before invoice-level tax. |
| `Quantity` | `int` | Integer number of units; BuildNew sets one, with no range validator here. |
| `LineTotalCurrency` | `string` required | Currency code for stored line-total display; BuildNew copies the supplied currency; Core requires it to match the invoice. |
| `LineTotalCents` | `long` | Stored unit-price cents times quantity, recalculated by InvoiceDomain.UpdateAsync. |

### IssueInvoiceRequest

Source: [IssueInvoiceRequest.cs](../src/Niobium.Invoicing.Contracts/IssueInvoiceRequest.cs).

| Field | C# type / modifier | Meaning and behavior |
| --- | --- | --- |
| `InvoiceID` | `long` | Consumer-supplied reverse Unix millisecond identifier; HTTP issuance checks its decoded issue time against a 30-minute age limit. |
| `Tenant` | `Guid` required | Invoicing management group; HTTP issuance requires equality with the GroupSid claim. |
| `BillerID` | `Guid` required | Issuing business; HTTP issuance requires equality with the Sid claim. |
| `BilleeID` | `Guid` required | Customer row identifier used when no embedded billee is supplied. |
| `Particulars` | `string?` | Optional requested payment particulars; nonnull trimmed values override the generated default; empty clears it. |
| `Reference` | `string?` | Optional requested payment reference; nonnull trimmed values override the generated default; empty clears it. |
| `InvoiceCycle` | `int` | Numeric InvoiceCycle used for billing-period display, not scheduling. |
| `BillingPeriodStartDay` | `DateTimeOffset?` | Optional billing-period start instant copied to the invoice. |
| `BillingPeriodEndDay` | `DateTimeOffset?` | Optional custom-range end instant copied to the invoice. |
| `DueBy` | `DateTimeOffset?` | Requested due instant; null preserves the default; a supplied date must not precede issue. |
| `Terms` | `string?` | Optional invoice terms trimmed on update. |
| `PaymentInstructions` | `string?` | Optional payment instructions trimmed on update; null inherits the biller default; empty clears it. |
| `InvoiceItems` | `List<InvoiceItem>` required | Supplied replacement line collection; the flow does not rewrite each item parent key. |
| `Settled` | `Amount` | Reported settlement amount, default Amount.Zero; nonzero amounts must match invoice currency; zero is currency-neutral. |
| `NotifyBillee` | `bool` | Whether IssueFlow should enqueue notification after persistence; the HTTP endpoint always resets this to false. |

### IssueInvoiceCommand

Source: [IssueInvoiceCommand.cs](../src/Niobium.Invoicing.Contracts/IssueInvoiceCommand.cs). Inherits all IssueInvoiceRequest fields; its Billee is an embedded snapshot, not persisted as a customer by UpsertFlow.

| Field | C# type / modifier | Meaning and behavior |
| --- | --- | --- |
| `ID` | `string` | Domain-event identifier, default a new GUID string; separate from InvoiceID and not used as an invoice replay guard. |
| `Occurried` | `DateTimeOffset` | Domain-event occurrence time, excluded from JSON; retains default unless assigned. |
| `Source` | `DomainEventAudience` | Internal source audience set by the constructor and excluded from JSON. |
| `Target` | `DomainEventAudience` | Internal target audience set by the constructor and excluded from JSON. |
| `Billee` | `Billee` required | Embedded customer snapshot used by trusted queue issuance; Sanitize copies its ID to BilleeID. |
