# Testing, conventions and change recipes

## Executable evidence

[Core.Tests.csproj](../src/Niobium.Invoicing.Core.Tests/Niobium.Invoicing.Core.Tests.csproj) uses MSTest.Sdk 4.4.0, Moq 4.20.72, FluentAssertions 8.10.0, AutoFixture/AutoFixture.AutoMoq 4.18.1. [MSTestSettings.cs](../src/Niobium.Invoicing.Core.Tests/MSTestSettings.cs) enables method-level parallelization. `global.json` selects Microsoft.Testing.Platform. Use the verified [commands](configuration-and-development.md#toolchain-and-safe-commands), not guessed VSTest switches.

**Verified current suite:** 60 cases passed, zero failed/skipped; the original discovery baseline had 26. Infrastructure is mocked; some tests exercise actual invoice/item domains and embedded rendering. Fixtures construct synthetic parties/items. No tests consume a real queue, profile service or Azure table. Counts include data-driven cases, not code coverage percentages. See [follow-up validation](discovery/validation.md#security-follow-up-2026-10-30).

| Suite | Count | Assertions represented | Limits |
| --- | --- | --- | --- |
| [IssueFlowTests](../src/Niobium.Invoicing.Core.Tests/Flows/IssueFlowTests.cs) | 4 | Notify false calls only upsert; notify true calls children; upsert failure prevents email call; token forwarded to children | Mocked child flows, no delivery/persistence guarantee |
| [UpsertFlowTests](../src/Niobium.Invoicing.Core.Tests/Flows/UpsertFlowTests.cs) | 8 | New invoice/items persisted; prior items replaced; missing supplied billee fetched; missing biller/customer fails without persistence; cancellation forwarded; business fields applied; new items recalculate totals | Mocked storage/profile; no ETags, real transactions or race tests |
| [EmailFlowTests](../src/Niobium.Invoicing.Core.Tests/Flows/EmailFlowTests.cs) | 19 | Missing email skips; saved legacy/v2 token reuse; unchanged resend serialization; token-before-enqueue order; save/enqueue failures; full-secret/identity derivation; culture stability; business parameters | Broker capture only, no provider receipt, live persistence or downstream idempotency |
| [HTMLFlowTests](../src/Niobium.Invoicing.Core.Tests/Flows/HTMLFlowTests.cs) | 29 | Full rendering; missing/mismatched token rejection; legacy case-insensitive links; no new expiry/recalculation; v2 viewing; text/attribute encoding; logo schemes; literal placeholders; unencoded notification data; localization/items/dates | Invokes flows/domains, not HTTP/Functions middleware or a browser; focused regressions, not a complete security audit |

For example, `Customer_receives_secure_invoice_link_and_invoice_marked_delivered` verifies queued parameters and persistence calls. Its name is not evidence that a customer received email. Rendering tests do not establish the HTTP endpoint's query binding or middleware behavior. The former tokenless-preview test now verifies rejection, as required by Q17.

## Requirements and coverage gaps

| Requirement or invariant | Current test evidence | Missing evidence / Proposed work |
| --- | --- | --- |
| Calculate totals and replace items | Upsert `New_invoice_is_issued_and_items_are_persisted`, `Reissue_with_different_items_updates_totals` | Currency mismatch, rounding boundaries, overflow, zero/negative values, empty/null collections |
| Required due date | Business-field and HTML due-date assertions | Default preservation/null rejection and invalid date ordering |
| Billers restricted to their group/customers | Offline Server grant/SAS assertions described below; no HTTP authorization tests | Live claims/entitlements/SAS enforcement, embedded-customer/item-key checks |
| Cancellation forwarding | Issue/Upsert token assertions | Cancellation during real I/O, partial completion/recovery and framework omissions |
| Conditional replay and edit freeze | Existing-items replacement assertions only | Same/different payload retry, persisted Delivered state, ETags, simultaneous requests |
| Same downstream message and no extra email | Token/parameter capture and identical serialized NotifyCommand on unchanged resends | Full issuance replay/concurrency, changing reconstruction inputs, Notification inbox/provider crash windows |
| Delivered means confirmation | No such implementation/test | Future confirmation protocol and duplicate/out-of-order events |
| Public token view | Saved legacy/v2 matching, missing-token rejection, HTML/URL encoding regressions | HTTP middleware, browser-level execution and future link lifecycle policies |
| Reliable persistence | Token-save/enqueue ordering/failure tests; status save uses the refreshed entity/ETag returned by the mocked repository | Integration/contract tests for mapping, batch boundaries, real ETags, failed item creation and enqueue/status-save splits |
| Promotion safety | Local unit run only | Planned integration suite in isolated test environment before production |

There are no integration, end-to-end, load, recovery or deployment smoke tests in this repository. Q14 plans integration checks before promotion. No test coverage threshold, recorded percentage, mutation test or CI enforcement beyond the inspected commands is claimed.

An additional outside-repository S01 verification used the built Server registrations, matched resource controls/signature issuer, synthetic claims/credentials and mocked invoice lookup. Eight assertions checked the absence of an application InvoiceItem descriptor, owned-partition bounds in the grant and locally generated SAS, rejection of omitted/empty/unowned/biller-key partitions, and preservation of the Invoice personalized grant. The external entitlement store was replaced with an empty synthetic provider. This is recorded evidence, not a tracked CI test or an audit of deployed identities/entitlements.

## Observed conventions and quality settings

- Contracts/Core use `Niobium.Invoicing` root namespace; domains/flows/options add subnamespaces; Server uses `Niobium.Invoicing.Server`. Block namespaces predominate, with some file-scoped namespaces. Primary constructors inject dependencies; private/internal helpers are common.
- Methods conventionally use Async suffix and CancellationToken. Pass tokens where supported, but review inherited behavior rather than claiming automatic cancellation/rollback.
- Flows coordinate, domains transform/render/save, repositories abstract persistence. `IFlow` and framework assembly scanning determine DI discovery. Preserve attributes and inheritance when adding a component.
- Public DTOs are mutable properties, with selected C# required members. Nullable annotations are enabled. There is no automatic validation merely from this style.
- Contracts/Core enable recommended .NET analyzers, code style in build and TreatWarningsAsErrors. Existing exclusions are CA1848, CA2254, CA1305, CA1310, CA1711, CA1852; Core also excludes CS8618. Server does not adopt the same warning policy. Do not generalize one project's settings to all projects or add suppressions to hide regressions.
- [.editorconfig](../.editorconfig) prefers four spaces, CRLF, no final newline for C#, explicit types, and specific qualification/style rules. Existing files vary in BOM/newline/style. Preserve local conventions rather than running a repository-wide formatter.
- Test names describe scenarios with underscores; Moq verifies boundaries. Do not confuse a test's descriptive name with evidence beyond its assertions.
- No persistent XML output setting was enabled by this documentation change. Temporary compiler validation is recorded in [validation](discovery/validation.md).

The test project declares a CodeCoverage package Update of 18.10.0, but **MSBuild evaluation and assets resolve 18.11.0** through MSTest.Sdk. Record declared and effective settings separately; no dependency upgrade was made.

## Change recipes

| Change | Read / representative code | Required checks for authorized implementation work |
| --- | --- | --- |
| Billing calculation or new field | [data](domain-and-data.md), `Invoice.BuildNew`, `InvoiceDomain.UpdateAsync`, both InvoiceExtensions classes | Decide policy, trace request->snapshot->storage->HTML/message, test meaningful boundaries; review compatibility |
| Endpoint/auth change | [API](api-and-messaging.md), [security](security.md), `NewInvoice.Run`, matched middleware | Trace trigger plus application checks and direct-resource access; test actual HTTP status/result execution |
| Persistence/key change | [data](domain-and-data.md), `UpsertFlow`, matched table adapter | Review all key builders/clients and old records; test concurrency/partial failure; agree migration separately |
| Notification change | `EmailFlow`, [Notification boundary](niobium-integration.md#notification-boundary), Q11 | Preserve biller->sender mapping, MessageId and reconstruction inputs; test failures before/after enqueue and downstream behavior |
| Dependency upgrade | csproj files and [provenance](niobium-integration.md) | Compare resolved graph/pinned implementations, rerun relevant tests, inspect registration/mapping/serialization changes |
| Deployment change | [delivery](delivery-and-operations.md), both workflows, azure.yaml hooks | Check exact event/branch/conditions and secret flows; authorization before any dispatch/deploy |
| Documentation/XML only | [coverage](discovery/coverage.md), [validation](discovery/validation.md) | Verify source/decisions, links, XML and unchanged C# non-comment tokens/directives; no runtime fixes |

Compatibility/versioning rules beyond current workflow version strings are Unknown. No supported public API version prefix is declared. Coordinate DTO/event/schema changes with consumers, especially direct table clients; no silent symbol renames or spelling corrections (`Anually`, `Occurried`, `GetEmbededResourceAsStringAsync`).

No separate CONTRIBUTING/CODEOWNERS policy or externally enforced branch/review rules was established. Use scoped review, explicit validation evidence and owner decisions; do not invent mandatory reviewers or a release SLA. [LICENSE.md](../LICENSE.md) contains an MIT license with its existing Microsoft copyright notice. Preserve license notices; the notice is observed text, not proof of provenance for every dependency. Dependency license/distribution obligations need their own review when changing or distributing packages, including the resolved assertion library; no compliance conclusion was reached here.
