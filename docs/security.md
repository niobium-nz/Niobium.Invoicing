# Security and privacy boundaries

This page records controls and assumptions, not a security certification. Detailed suspected weaknesses remain in private discovery findings pending disclosure/remediation decisions; they are not reproduced as exploit instructions. Do not infer production readiness from passing unit tests.

## Identity and trust boundaries

Evidence: [Program](../src/Niobium.Invoicing.Server/Program.cs), [NewInvoice.Run](../src/Niobium.Invoicing.Server/NewInvoice.cs), [DependencyModule](../src/Niobium.Invoicing.Server/DependencyModule.cs), and [matched Niobium middleware](niobium-integration.md).

- The framework verifies ID tokens with configured RSA public-key/issuer/audience settings, and access tokens with a configured symmetric secret/issuer/audience. Lifetime validation and a five-minute clock skew are present in `PrincipalParser`. Default access-token validity is eight hours.
- `NewInvoice` requires GUID Sid and GroupSid claims matching the requested biller and management group. An Anonymous trigger does not bypass these handler checks. Final host behavior on its `ForbidResult` branches is not integration-verified.
- `/token` performs role/entitlement lookup; `/rsas` obtains a scoped resource grant. Membership/role administration and additional deployed entitlements are external, not established by this source.
- Incoming Service Bus commands trust authorized infrastructure producers, intentionally without HTTP user-principal checks (Q05). Queue access control is therefore essential; no public producer access is intended. Deployed RBAC and all producer identities were not audited.
- The email trigger uses a Functions key and has no biller-principal ownership check. Its intended use is debugging; removing/restricting it in production is planned (Q05).
- Public HTML viewing does not require a recipient account. It uses an invoice-link token; it is separate from JWT and table SAS authentication. Exact expiry/revocation policy is deferred (Q13), and there is no explicit expiry field today. Do not promise perpetual access.

MCP/API-key authentication is a future design, not an alternate credential already accepted by these handlers.

## Direct resource access

The owner intended short-lived biller-partition SAS for the historical UI. The matched framework's actual resource-signature default is one day. Invoice and Billee personalized grants use biller/user identity; InvoiceItem has a custom ownership control and a different partition construction. The framework accepts the first suitable non-null grant, including default controls; the custom control is not proof that every grant passes through that ownership check. Inspect [key mappings](domain-and-data.md) and [permission table](api-and-messaging.md#resource-token-and-direct-table-contract) together. Do not equate every table partition with a biller partition or assume tenant is included in storage keys.

Server table access uses `DefaultAzureCredential` when an endpoint is configured. Table SAS signing separately needs `StorageTableOptions.Key`. Read/write cloud permissions, direct SAS access and service-principal profile retrieval are distinct trust paths. Infrastructure declarations alone do not prove deployed isolation.

## Inputs and rendered output

`required` members, type conversion and the framework DataAnnotations helper do not provide comprehensive business validation. Currency, item relationships, due-date validity and quantity/amount rules need the planned enforcement work described in [decisions](decisions-and-risks.md). Stored profile/customer data is still external input, even when submitted through a trusted path.

HTML uses an embedded template and string substitution. No general HTML/template sanitizer or context-aware encoding service is registered by this application; callers must not assume arbitrary input is safe markup. The downstream Notification template renderer has its own encoding behavior, which does not establish protection for Invoicing's HTML response. Review rendering and URL/attribute boundaries before accepting new sources of text. Template CSS/font references also create external browser dependencies. Detailed findings and remediation belong in a private security review.

## Sensitive data and logging

Invoices snapshot business/customer names, addresses, email, identifiers, contacts, payment instructions and settlement amounts. Notification messages contain recipient details, invoice facts and access links. Protect table data, queue bodies, telemetry and backups according to their data content; no approved detailed privacy/retention schedule beyond Q08 was established.

Use synthetic names/identifiers and `.invalid` URLs in examples. Do not copy real credentials, private keys, customer records, complete JWT/SAS/link tokens or raw production messages into issues, public docs or logs. Configuration catalogs identify keys and secret classification only. The owner confirmed tracked development values as public/demo examples (Q07); that is not permission to reuse them as real credentials.

Core enables IdentityModel PII logging for the framework's development environment classification. The development broker logs message content. Error middleware logs exception information, and repository errors may include storage identifiers or underlying responses. No application-wide redaction filter or correlation policy is demonstrated. A `dev`/`test` label does not make sensitive logging or live resources safe.

## Retention, recovery and review

Five-year retention, soft deletion and historical-version audit are planned requirements, not implemented controls. No SLA is committed. Azure PaaS reliance was owner-confirmed, but backup inventory, restore capability, retention clock, deletion requests, regional data handling and RPO/RTO remain [accepted unknowns](decisions-and-risks.md#accepted-unknowns). No compliance claim is made.

For a security-related change, trace both HTTP and direct-resource paths, the matched framework version and private integration assumptions. Add meaningful isolated authorization/contract/failure tests when implementation work is authorized. Report suspected exposures privately with file/symbol locations and no copied values; agree disclosure/remediation before publishing sensitive details.
