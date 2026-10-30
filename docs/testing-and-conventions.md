# Testing, conventions and change recipes

## Executable evidence

[Core.Tests.csproj](../src/Niobium.Invoicing.Core.Tests/Niobium.Invoicing.Core.Tests.csproj) uses MSTest.Sdk 4.4.0, Moq 4.20.72, FluentAssertions 8.10.0, AutoFixture/AutoFixture.AutoMoq 4.18.1. [MSTestSettings.cs](../src/Niobium.Invoicing.Core.Tests/MSTestSettings.cs) enables method-level parallelization. `global.json` selects Microsoft.Testing.Platform. Use the verified [commands](configuration-and-development.md#toolchain-and-safe-commands), not guessed VSTest switches.

**Verified current suite:** 114 unit cases and 16 local integration cases passed, zero skipped in the opted-in run. The main baseline had 60 unit cases; original discovery had 26. Counts include data rows, not coverage percentages. Exact commands, diagnostic-build qualification and remaining blockers are in the [implementation ledger](implementation-progress.md).

| Suite | Count | Evidence | Limits |
| --- | --- | --- | --- |
| IssueFlowTests | 4 | Notification selection, child-flow ordering and cancellation forwarding | Mocked child flows |
| UpsertFlowTests | 8 | New invoice, pre-freeze replacement, party lookup, cancellation, totals and overpayment | Synthetic repository fixture |
| EmailFlowTests | 19 | Token preservation, freeze-before-enqueue, failure ordering, stable notification parameters | Controlled broker; no provider receipt |
| HTMLFlowTests | 29 | Saved-token authorization, HTML/URL encoding, localization and line rendering | No browser or Functions HTTP binding |
| InvoiceValidationTests | 37 | Shared identity/ownership/currency/date/item/overflow checks, null/empty defaults, unchanged quantity/rounding policy | U01 business decisions remain open |
| InvoiceReplayTests | 17 | Exact/frozen replay, stale/concurrent writes, known partial recovery, unknown-outcome blocking, legacy tokens, stable defaults and JSON compatibility | Mocked failure injection; no automatic abandoned-writer recovery |
| StorageFlowTests | 4 | Real registration/adapters and local Table HTTP calls, ETags/stale rejection, competing claims, partial checkpoint repair, replay and public HTML | Azurite; synthetic profile and controlled outgoing broker |
| HttpBoundaryTests | 12 | Installed token parser, ASP.NET status result execution, notify suppression and removed debug route | Does not run Functions middleware or a deployed host |

The [integration guide](integration-tests.md) documents opt-in isolation, cleanup and commands. Storage tests skip without the emulator endpoint; skips are not passing storage evidence. No tests consume a real Service Bus queue, profile service or email provider.

Still missing: deployed Functions authentication/binding, direct-resource authorization in Azure, real Service Bus ingestion/emission, Notification inbox/provider crash recovery, delivery confirmations, backup/restore and post-deployment promotion checks. No load-test result, coverage percentage, recovery SLA or live deployment guarantee is claimed.

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
