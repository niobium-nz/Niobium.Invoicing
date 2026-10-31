# API and messaging contracts

## Effective HTTP surface

The test-deployment suite now verifies invoice issuance, saved-token HTML, health identity and the manual email function's key boundary through the real Functions host. Token/resource-grant endpoints remain outside that deployed suite; see [integration evidence](integration-tests.md). [host.json](../src/Niobium.Invoicing.Server/host.json) sets `routePrefix` to empty, so there is no `/api` prefix. [Program.cs](../src/Niobium.Invoicing.Server/Program.cs) installs Niobium identity middleware. Anonymous Functions triggers can still have application checks.

| Method / path | Handler and input | Controls | Success / side effects |
| --- | --- | --- | --- |
| `POST /invoices` | [NewInvoice.Run](../src/Niobium.Invoicing.Server/NewInvoice.cs); JSON `IssueInvoiceRequest` | Access-token principal needs GUID Sid/GroupSid matching BillerID/Tenant; validation helper; decoded invoice age check | `OkResult` (200), no response invoice body. Persists invoice/items; forces NotifyBillee=false. |
| `GET /{issuer}/invoices/{invoice}?token=...` | [GetHTMLInvoice.Run](../src/Niobium.Invoicing.Server/GetHtmlInvoice.cs) | Anonymous; rejects blank token, nonpositive ID or empty issuer with 404; domain requires a matching saved link token | 200 `text/html`; reads invoice/items, no write. No billee account required. |
| `GET /email/{issuer}/invoices/{invoice}` | [EmailInvoice.Run](../src/Niobium.Invoicing.Server/EmailInvoice.cs); no body | Functions host requires a valid function key; no application biller/JWT check | Empty 201 after EmailFlow completes, including its missing-recipient skip. May freeze/save the invoice, enqueue notification and persist Delivered. |
| `GET /health` | [DeploymentInfo.Run](../src/Niobium.Invoicing.Server/DeploymentInfo.cs) | Anonymous, no credentials or runtime settings returned | 200 JSON `status` and build `commit`; deployed tests require the exact pushed SHA. Not a dependency health probe. |
| `GET /token` | [Placeholder.Auth](../src/Niobium.Invoicing.Server/Placeholder.cs), intercepted by AccessTokenMiddleware | ID-token verification and Sid/GroupSid; roles/entitlements lookup | 200 with access token in Authorization response header; fallback placeholder returns empty 200. |
| `GET /rsas` | [Placeholder.RSAS](../src/Niobium.Invoicing.Server/Placeholder.cs), intercepted by ResourceTokenMiddleware | Access principal, requested resource entitlement/control | 200 JSON StorageSignature; fallback placeholder returns empty 200. |

Middleware endpoint names are configurable, but placeholder trigger routes are compiled from default constants. Changing options alone does not create a new Functions route. No `/auth`, `/alive`, payment, settlement, cancellation, invoice-list HTTP endpoint or MCP route is declared. Listing historically used direct table access.

`NewInvoice.Run` rejects an issue time more than 30 minutes old; it does not implement a symmetric clock-skew window. It returns 401 for missing claims and explicit 403 for identity/age mismatches. Invalid date encoding returns 400. Local tests parse signed synthetic access tokens through the installed PrincipalParser and execute ASP.NET results. The deployed suite exercises missing/invalid credentials, identity mismatches, successful issuance, validation errors and frozen-update rejection through the real middleware/binding pipeline.

## Requests, serialization and errors

The complete [field catalog](domain-and-data.md#field-catalog) covers `IssueInvoiceRequest`, `InvoiceItem`, party snapshots and the derived `IssueInvoiceCommand`. HTTP request body binding is provided by Functions; this app installs no custom request serializer. Queue serialization is established by matched Niobium `JsonMarshaller`: camelCase output, case-insensitive input, ordinary System.Text.Json values, with null/default values included. Dates are ISO-8601 strings, GUIDs are strings, IDs/cents are JSON integers, cycles/tax kinds are numeric, and `Amount` is an object such as `{"cents":0,"currency":"NZD"}`. `Occurried`, `Source`, `Target` are ignored on command JSON. Required C# members do not validate currencies, relationships or amount ranges.

The invoice HTTP endpoint invokes DataAnnotations validation, but the request/items have no comprehensive business validators. `IssueInvoiceCommand.Sanitize` copies embedded `Billee.ID` into `BilleeID`. No recursive item ownership/currency validation is established by this helper.

Error paths differ:

- `NewInvoice` validation failure returns 400 `application/json`, a dictionary mapping member names to arrays of messages.
- Framework HTTP error middleware catches Niobium `ApplicationException`, returns numeric `code`, `message`, and `reference` (or a validation payload for a validation reference), using HTTP status equal to its code when valid, otherwise 500.
- Missing biller/billee causes the domain's NotFound error; delivered-domain update causes Conflict; missing or mismatched saved link token causes Forbidden; missing embedded template causes InternalServerError.
- Framework/Azure/JSON/timezone/culture exceptions outside that exception type propagate to the host. Do not promise one universal error schema.
- Queue handlers throw on invalid parse/validation; HTTP middleware does not turn queue failures into HTTP responses.

A synthetic generic framework error shape is `{"code":404,"message":"Biller does not exist.","reference":null}`. Do not log actual error bodies indiscriminately: references and underlying failures can contain identifiers or input data.

## Safe contract construction example

This is an offline construction example, not a live request or proof of valid service configuration. Replace the synthetic identities only in an approved isolated environment; biller profile, customer record and matching principal must exist. Generating IDs at execution time avoids teaching callers to replay an expired timestamp example.

```csharp
using Niobium.Finance;
using Niobium.Invoicing;

long invoiceId = Invoice.ParseID(DateTimeOffset.UtcNow);
InvoiceItem item = InvoiceItem.BuildNew(invoiceId, "NZD");
item.Subject = "Example service";
item.UnitPriceCents = 10000;
item.Quantity = 1;
IssueInvoiceRequest request = new()
{
    InvoiceID = invoiceId,
    Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111"),
    BillerID = Guid.Parse("22222222-2222-2222-2222-222222222222"),
    BilleeID = Guid.Parse("33333333-3333-3333-3333-333333333333"),
    InvoiceItems = [item],
    DueBy = DateTimeOffset.UtcNow.AddDays(7),
    Settled = new Amount(0L, "NZD"),
    NotifyBillee = false,
};
```

HTTP issuance credentials are `Authorization: Bearer <ACCESS_TOKEN>`; never embed real tokens in examples. The manual email route instead requires `x-functions-key: <FUNCTION_KEY>`. Example public link shape: `https://invoice.example.invalid/<BILLER_GUID>/invoices/<INVOICE_ID>?token=<LINK_TOKEN>`.

## Manual debug email

Q26 restores the historical `EmailInvoice` function, which the owner referred to as SendEmail. Call `GET /email/<BILLER_GUID>/invoices/<INVOICE_ID>` on the intended environment, with its existing Functions key in the `x-functions-key` header. There is no `/api` prefix, request body or recipient override. The invoice ID uses the existing reverse-timestamp encoding. Keep the key out of URLs, logs and saved examples. A bearer token alone does not authorize this route.

The handler delegates to the current `EmailFlow`: load the saved invoice/items, skip a blank recipient, preserve or create the saved link token, freeze verified content, enqueue to the existing Notification service, then save the enqueue acknowledgement. It returns an empty 201 only after that flow completes; exceptions and cancellation propagate. A 201 also covers the historical missing-recipient skip and does not prove recipient delivery. Repeat calls may enqueue again under Q11; they do not rotate an existing saved token. A function key is privileged operational access and is not scoped to a biller's JWT claims.

This historical GET has side effects. Invoke it deliberately for authorized debugging; never use it as a health check, preview link or read-only production integration target. The restoration introduces no new key, Azure resource, package version or alternate sending implementation.

## Resource-token and direct-table contract

`GET /rsas` requires integer `type` and nonempty URL-encoded `resource`; `partition` and `id` are optional inputs to controls. Azure Storage Table is resource type `1`. The matched response has `signature` (SAS string), `control` (resource, numeric permission, start/end partition/row keys), `expiry` (Unix seconds) and computed `expires` (DateTimeOffset). It is not an invoice JSON response. Treat the complete response as a credential.

Default Server registrations ([DependencyModule](../src/Niobium.Invoicing.Server/DependencyModule.cs)) are:

| Resource | Default entitlement | Additional scope/control |
| --- | --- | --- |
| Invoice | Query | Personalized to user/biller partition |
| InvoiceItem | No table entitlement descriptor | Custom OwnershipControl grants Query for a specified item partition after checking Invoice existence under Sid |
| Billee | Query, Add, Delete, Update | Personalized user/biller partition |
| Billable | Query, Add, Delete | Personalized user/biller partition |

SignatureService evaluates suitable controls in registration order and accepts the first non-null grant. Default table control and custom ownership control are alternative grant providers, not automatically cumulative checks. Role/Entitlement data can add externally managed grants; this table is the source registration, not an inventory of deployed permissions. Resource SAS defaults to one day in matched SignatureService, while access tokens default to eight hours. Intended link-token lifetime is a separate deferred policy. The direct-table client's query/paging implementation is outside this repository.

Q15 removes the application's general InvoiceItem entitlement. For newly described application permissions, item access uses the ownership check; an omitted partition produces no ownership grant. Existing access tokens/SAS and external entitlements are separate rollout considerations. Q16 defers the item-key uniqueness risk.

## Incoming command

| Contract detail | Observed behavior |
| --- | --- |
| Queue / consumer | `issueinvoicecommand`; `IssueInvoiceCommandConsumer.Run` |
| Producer | Niobium.Store private integration, Owner-confirmed Q05; additional deployed producers Unknown |
| Body | Direct serialized `IssueInvoiceCommand`, not a JSON wrapper with `value`; all request fields plus embedded Billee and event ID |
| Media type | Exactly `application/json`; nonmatching/empty body fails parsing; malformed JSON may throw |
| Identity | Infrastructure-controlled producer trust; no user-principal validation in this handler |
| Validation | `TryValidate` invokes Sanitize/DataAnnotations; errors logged then InvalidOperationException thrown |
| Side effects | IssueFlow upserts invoice/items, optionally queues notification |
| Correlation | Service Bus MessageId logged for parsing failures; InvoiceID for validation failures; command.ID is not used as a replay record |
| Settlement | No manual message actions or application dead-letter/replay handler. Functions host owns completion/redelivery on return/failure; runtime behavior not exercised. |

Declared incoming queue settings: lock 5 minutes, TTL 14 days, max delivery count 10, dead-letter on expiry, no sessions, no duplicate detection. A configured 10-minute duplicate history window has no effect while detection is disabled. Session ordering and exactly-once processing are not promised. Functions concurrency/lock renewal defaults are not explicitly overridden in `host.json`; actual host settings remain unverified.

## Outgoing notification and retries

`EmailFlow.RunAsync` sends to the lower-case type-name queue `notifycommand` using `NotificationQueueOptions`. Body is `NotifyCommand`: `id = invoiceID@biller`, `tenant = issuer/biller`, `channel = "Invoice"`, destination email/name and `parameters`. Parameters contain unencoded invoice display strings plus `INVOICE_URL` and an `INVOICE_ITEM` array of subject/description/unit-price/quantity/line-total dictionaries. Its optional contract `Token` is not assigned by this app; the saved invoice-link token is URI-escaped inside the URL.

The consumed Notification.Contracts 2.0.7 contract has these additional constraints (source-linked in [integration](niobium-integration.md#notification-boundary)):

| NotifyCommand field | Type / default / validation | Invoicing value |
| --- | --- | --- |
| ID (inherited) | string event ID | Full invoice identity; also used as transport MessageId |
| Tenant | required Guid with Required attribute; the attribute alone does not reject Guid.Empty | Biller/issuer, not management group |
| Channel | required string, Required and MaxLength(50) | Invoice |
| Destination | nullable string, MaxLength(50); sanitized by trimming | Captured recipient email |
| DestinationDisplayName | nullable string, MaxLength(50); sanitized by trimming | Captured billee name |
| Parameters | required Dictionary<string, object> with Required attribute | Invoice strings and line dictionaries |
| Token | nullable string, MaxLength(5000) | Not assigned |

Inherited NotifyCommand domain-event metadata (Occurried, Source, Target) is ignored in JSON; its base constructor sets current UTC occurrence time and Internal audiences. Invoicing does not invoke NotifyCommand validation before enqueueing. The inspected downstream consumer validates it; invalid input logs and returns rather than raising an invoice-domain failure. Provider success and handling of omitted templates remain external outcomes. There is no incoming confirmation contract registered by Invoicing.

Both `MessagingEntry.ID` and `NotifyCommand.ID` use the full invoice identity. The matched broker maps the envelope ID to ServiceBus `MessageId`, sends JSON and may include a type marker and available bearer token in application properties. This app sets no explicit correlation ID, session ID, schedule or retry counter. SDK send retries default to five attempts beyond the original request with configured maximum delay; they do not create business idempotency.

The flow skips blank recipient email. Otherwise it reuses the saved link token or persists a new token before enqueueing, and records Delivered after enqueueing. Token-save failure prevents publishing; enqueue failure leaves the saved token available for retry; status-save failure can still follow an accepted message. It does not wait for provider acceptance or recipient receipt. Development messaging may only log the payload. See [Notification source boundary](niobium-integration.md#notification-boundary) and [link compatibility](security.md#invoice-link-compatibility).

**Q11 implementation:** persisted snapshots, item order, formatting culture, saved token and endpoint support stable exact replays. Changed frozen content receives 409. Exact retries republish the same message, including after an uncertain enqueue result. See [preparation/recovery](invoice-recovery.md) for checkpoint and compatibility limits. Downstream inbox/provider recovery remains B02 in Notification; repeated publication is intentional.

No OpenAPI/AsyncAPI generation, route version prefix or message-version field is configured. Contract/package changes need consumer compatibility review and targeted serialization tests. Machine-readable specifications and new tooling were outside the approved documentation scope.
