# Security and privacy boundaries

This page records controls and assumptions, not a security certification. Detailed suspected weaknesses remain in private discovery findings pending disclosure/remediation decisions; they are not reproduced as exploit instructions. Do not infer production readiness from passing unit tests.

## Identity and trust boundaries

Evidence: [Program](../src/Niobium.Invoicing.Server/Program.cs), [NewInvoice.Run](../src/Niobium.Invoicing.Server/NewInvoice.cs), [DependencyModule](../src/Niobium.Invoicing.Server/DependencyModule.cs), and [matched Niobium middleware](niobium-integration.md).

- The framework verifies ID tokens with configured RSA public-key/issuer/audience settings, and access tokens with a configured symmetric secret/issuer/audience. Lifetime validation and a five-minute clock skew are present in `PrincipalParser`. Default access-token validity is eight hours.
- `NewInvoice` requires GUID Sid and GroupSid claims matching the requested biller and management group. An Anonymous trigger does not bypass these handler checks. It returns explicit 403 status results for mismatches. Local integration tests execute ASP.NET results with principals parsed by the installed framework; deployed test cases verify missing/invalid authentication and mismatched biller/group claims through the real Functions middleware. This does not audit direct-resource grants or all external identities.
- `/token` performs role/entitlement lookup; `/rsas` obtains a scoped resource grant. Membership/role administration and additional deployed entitlements are external, not established by this source.
- Incoming Service Bus commands trust authorized infrastructure producers, intentionally without HTTP user-principal checks (Q05). Queue access control is therefore essential; no public producer access is intended. Deployed RBAC and all producer identities were not audited.
- The debug email HTTP function has been removed (Q05). A compiled-assembly integration test checks that its function and route are absent.
- Public HTML viewing does not require a recipient account. It requires a nonblank saved invoice-link token matching the supplied value; missing or mismatched tokens are rejected. This token is separate from JWT and table SAS authentication. Future expiry/revocation policy remains deferred (Q13), with compatibility constrained by Q17.

MCP/API-key authentication is a future design, not an alternate credential already accepted by these handlers.

## Direct resource access

The owner intended short-lived biller-partition SAS for the historical UI. The matched framework's actual resource-signature default is one day. Invoice and Billee personalized grants use biller/user identity; InvoiceItem uses a custom ownership control and a different partition construction. Application registration has no general or personalized InvoiceItem entitlement (Q15). The ownership control requires an invoice partition and checks the corresponding Invoice under authenticated Sid before granting that partition. The framework still accepts the first suitable non-null grant; externally managed entitlements or previously issued tokens can add other grants. Inspect [key mappings](domain-and-data.md) and [permission table](api-and-messaging.md#resource-token-and-direct-table-contract) together. The item-key collision risk is explicitly deferred under Q16.

Changing registration does not revoke existing access tokens or already-issued SAS credentials. Before an authorized rollout, review outstanding credential lifetimes and external entitlements. No revocation, credential rotation or deployed permission audit was performed here.

Server table access uses `DefaultAzureCredential` when an endpoint is configured. Table SAS signing separately needs `StorageTableOptions.Key`. Read/write cloud permissions, direct SAS access and service-principal profile retrieval are distinct trust paths. Infrastructure declarations alone do not prove deployed isolation.

## Inputs and rendered output

`required` members, type conversion and the framework DataAnnotations helper do not provide comprehensive business validation. Core enforces the decided currency, ownership, item, date and overflow invariants described in [billing validation](billing-validation.md). Negative/credit/rounding policy remains undecided under U01. Stored profile/customer data is still external input, even when submitted through a trusted path.

HTML uses an embedded template with single-pass, HTML-encoded substitution through `HtmlTemplate.Render`. Names, addresses, terms, payment instructions and line text are treated as text, including previously stored markup. Inserted values are not processed as further placeholders. Logo URLs must be absolute HTTP/HTTPS URLs without control characters; unsupported URLs render empty. Contact destinations are URI-escaped beneath fixed mailto/tel schemes and HTML-encoded separately from their displayed text. The template sets a no-referrer policy for browser requests. External logo/CSS/font resources still create browser dependencies; no server-side fetching or general rich-HTML sanitizer is introduced.

Notification parameters remain unencoded display data; the downstream renderer owns encoding for its own output. Do not pre-encode shared parameter dictionaries or assume this HTML change modifies Notification.

## Invoice-link compatibility

Q17 requires existing emailed links to remain usable when their token is present in Table Storage. `InvoiceDomain.BuildHTMLAsync` compares against that saved value using case normalization and fixed-time byte comparison. Viewing does not recalculate a token, consult the current token-generation secret, or impose a new expiry. There is no fallback for records without a saved token, by owner confirmation.

`FreezeAsync` reuses a saved token unchanged. Otherwise it derives a `v2.` token with a full SHA-256 HMAC using the entire configured secret and the management-group/biller/invoice identity, then saves it before notification enqueueing. The same identity/secret gives the same token; changing lines or culture does not rotate a saved link. Use a high-entropy secret: a full-length HMAC does not make a weak configuration secret strong. Secret changes affect new tokens, not validation of saved links.

Token persistence failure prevents enqueueing; enqueue failure leaves the saved token available for retry. Delivered is still recorded only after enqueueing and is not recipient confirmation. Conditional preparation and freeze now protect cooperative Core issuance flows; see [recovery](invoice-recovery.md). This is not an outbox or a mechanism to reconstruct deleted historical tokens. Direct table writers and downstream duplicate handling remain separate risks.

## Sensitive data and logging

Invoices snapshot business/customer names, addresses, email, identifiers, contacts, payment instructions and settlement amounts. Notification messages contain recipient details, invoice facts and access links. Protect table data, queue bodies, telemetry and backups according to their data content; no approved detailed privacy/retention schedule beyond Q08 was established.

Use synthetic names/identifiers and `.invalid` URLs in examples. Do not copy real credentials, private keys, customer records, complete JWT/SAS/link tokens or raw production messages into issues, public docs or logs. Configuration catalogs identify keys and secret classification only. The owner confirmed tracked development values as public/demo examples (Q07); that is not permission to reuse them as real credentials.

Core enables IdentityModel PII logging for the framework's development environment classification. The development broker logs message content. Error middleware logs exception information, and repository errors may include storage identifiers or underlying responses. No application-wide redaction filter or correlation policy is demonstrated. A `dev`/`test` label does not make sensitive logging or live resources safe.

## Retention, recovery and review

Five-year retention, soft deletion and historical-version audit are planned requirements, not implemented controls. No SLA is committed. Azure PaaS reliance was owner-confirmed, but backup inventory, restore capability, retention clock, deletion requests, regional data handling and RPO/RTO remain [accepted unknowns](decisions-and-risks.md#accepted-unknowns). No compliance claim is made.

For a security-related change, trace both HTTP and direct-resource paths, the matched framework version and private integration assumptions. Add meaningful isolated authorization/contract/failure tests when implementation work is authorized. Report suspected exposures privately with file/symbol locations and no copied values; agree disclosure/remediation before publishing sensitive details.
