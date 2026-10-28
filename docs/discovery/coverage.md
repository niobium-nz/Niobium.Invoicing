# Discovery coverage and comment inventory

## Scope and method

The [central baseline](../README.md#baseline-and-evidence-language) identifies the analyzed commit. All **68 tracked files** were inventoried; every handwritten first-party implementation and test file was read. Configuration values were deliberately redacted where appropriate. No first-party source file remains unread or blocked. This is source-review coverage, not a claim of production validation.

Bounded passes covered build/entry points, contracts/domain/flows, tests/templates, framework provenance/transitive behavior, infrastructure/workflows/configuration, and targeted history. No tracked generated, vendored or binary implementation was found. Initial ignored artifacts comprised 604 files (493 bin, 111 obj): generated code/binaries were excluded from narrative reading, while relevant assets, imported build targets, runtime configs and Functions metadata were inspected. No unrelated local files or credentials were absorbed into public evidence.

Local history was non-shallow. Flow implementation appeared in migration commit `f0cf846bd65d3631e24c75f6701628952f44b43e`; it did not establish earlier business rationale. Targeted issue/PR searches for ambiguities in the application/framework returned no matching discussions. This is not an exhaustive audit of every historical discussion. Live resources, external GitHub protections, Store/UI implementation and production Notification revision were outside verified scope.

Framework work followed consumed behavior only. NuGet metadata/PDB SourceLink matched two Niobium commits and a separate Notification.Contracts commit; see [provenance](../niobium-integration.md). Unrelated framework modules were not reviewed or documented.

## File ledger

Each path is both the inventory identity and source evidence. C# responsibilities refer to the declared type and its handwritten members; symbol reconciliation follows below. Questions are decision IDs in [the register](../decisions-and-risks.md); resolved decisions are retained so readers can trace intent. No secret values or raw logs are included.

Dependency groups: **D1** Niobium abstractions/helper/finance/profile contracts; **D2** D1 plus table/domain/profile/messaging/options; **D3** Functions/ASP.NET plus Niobium identity/resource/messaging adapters and Core; **D4** MSTest/Moq/fixture/assertion packages plus Core; **D5** .NET SDK and evaluated package/build graph; **D6** GitHub Actions/azd/Bicep/PowerShell/Azure; **D7** host/editor/environment conventions; **D8** owner product decisions/license text; **D9** embedded HTML, model parameters and external browser styles/fonts.

| File / evidence | Responsibility | Review | Dependencies | Decisions / unknowns | Documentation |
| --- | --- | --- | --- | --- | --- |
| [.azure/config.json](../../.azure/config.json) | azd selected-environment metadata | Structure read; values omitted | D7 | Q07, U07 | [configuration-and-development](../configuration-and-development.md), [testing-and-conventions](../testing-and-conventions.md), [security](../security.md) |
| [.azure/dev/.env](../../.azure/dev/.env) | Owner-confirmed example deployment/configuration input; values omitted | Structure read; values omitted | D7 | Q07, U07 | [configuration-and-development](../configuration-and-development.md), [testing-and-conventions](../testing-and-conventions.md), [security](../security.md) |
| [.azure/dev/config.json](../../.azure/dev/config.json) | Local azd environment configuration | Structure read; values omitted | D7 | Q07, U07 | [configuration-and-development](../configuration-and-development.md), [testing-and-conventions](../testing-and-conventions.md), [security](../security.md) |
| [.editorconfig](../../.editorconfig) | C# style and newline preferences | Read | D7 | Q07, U07 | [configuration-and-development](../configuration-and-development.md), [testing-and-conventions](../testing-and-conventions.md), [security](../security.md) |
| [.github/workflows/azure-deploy.yml](../../.github/workflows/azure-deploy.yml) | Event/branch eligibility and test/prod provisioning/deployment | Read | D6 | Q06/Q14, U03/U04 | [delivery-and-operations](../delivery-and-operations.md), [configuration-and-development](../configuration-and-development.md) |
| [.github/workflows/package-publish.yml](../../.github/workflows/package-publish.yml) | Version, build, OIDC and conditional Contracts publication | Read | D6 | Q06/Q14, U03/U04 | [delivery-and-operations](../delivery-and-operations.md), [configuration-and-development](../configuration-and-development.md) |
| [.gitignore](../../.gitignore) | Build/local artifact exclusions | Read | D7 | Q07, U07 | [configuration-and-development](../configuration-and-development.md), [testing-and-conventions](../testing-and-conventions.md), [security](../security.md) |
| [.vscode/extensions.json](../../.vscode/extensions.json) | Suggested editor extensions | Read | D7 | Q07, U07 | [configuration-and-development](../configuration-and-development.md), [testing-and-conventions](../testing-and-conventions.md), [security](../security.md) |
| [LICENSE.md](../../LICENSE.md) | Existing MIT license text and attribution | Read | D8 | Q01/Q07 | [README](../README.md), [testing-and-conventions](../testing-and-conventions.md) |
| [Niobium.Invoicing.slnx](../../Niobium.Invoicing.slnx) | Four-project solution and infrastructure/workflow navigation | Read | D5 | U07 | [configuration-and-development](../configuration-and-development.md), [testing-and-conventions](../testing-and-conventions.md), [niobium-integration](../niobium-integration.md) |
| [README.md](../../README.md) | Product/repository entry point, previously unrelated template content | Read | D8 | Q01/Q07 | [README](../README.md), [testing-and-conventions](../testing-and-conventions.md) |
| [azure.yaml](../../azure.yaml) | azd service, provisioning hooks and prepackage tests | Read | D6 | Q06/Q14, U03/U04 | [delivery-and-operations](../delivery-and-operations.md), [configuration-and-development](../configuration-and-development.md) |
| [global.json](../../global.json) | SDK roll-forward and Microsoft.Testing.Platform selection | Read | D5 | U07 | [configuration-and-development](../configuration-and-development.md), [testing-and-conventions](../testing-and-conventions.md), [niobium-integration](../niobium-integration.md) |
| [infra/abbreviations.json](../../infra/abbreviations.json) | Resource-name abbreviation input | Read | D6 | Q06/Q14, U03/U04 | [delivery-and-operations](../delivery-and-operations.md), [configuration-and-development](../configuration-and-development.md) |
| [infra/function-app.bicep](../../infra/function-app.bicep) | Functions hosting, telemetry, package storage, Key Vault and bindings | Read | D6 | Q06/Q14, U03/U04 | [delivery-and-operations](../delivery-and-operations.md), [configuration-and-development](../configuration-and-development.md) |
| [infra/main.bicep](../../infra/main.bicep) | Resource-group composition, data storage and identities | Read | D6 | Q06/Q14, U03/U04 | [delivery-and-operations](../delivery-and-operations.md), [configuration-and-development](../configuration-and-development.md) |
| [infra/main.bicepparam](../../infra/main.bicepparam) | Environment-derived deployment parameter defaults | Read | D6 | Q06/Q14, U03/U04 | [delivery-and-operations](../delivery-and-operations.md), [configuration-and-development](../configuration-and-development.md) |
| [infra/rbac.bicep](../../infra/rbac.bicep) | Declared resource-level role assignments | Read | D6 | Q06/Q14, U03/U04 | [delivery-and-operations](../delivery-and-operations.md), [configuration-and-development](../configuration-and-development.md) |
| [infra/scripts/ClearAppSettings.ps1](../../infra/scripts/ClearAppSettings.ps1) | Restore parameter backup after provisioning | Read | D6 | Q06/Q14, U03/U04 | [delivery-and-operations](../delivery-and-operations.md), [configuration-and-development](../configuration-and-development.md) |
| [infra/scripts/SetAppSettings.ps1](../../infra/scripts/SetAppSettings.ps1) | Generate app-setting parameter references with local backup | Read | D6 | Q06/Q14, U03/U04 | [delivery-and-operations](../delivery-and-operations.md), [configuration-and-development](../configuration-and-development.md) |
| [infra/scripts/SetKeyVaultSecrets.ps1](../../infra/scripts/SetKeyVaultSecrets.ps1) | Write configured secret values to Key Vault | Read | D6 | Q06/Q14, U03/U04 | [delivery-and-operations](../delivery-and-operations.md), [configuration-and-development](../configuration-and-development.md) |
| [infra/service-bus.bicep](../../infra/service-bus.bicep) | Namespace/queue defaults and authorization-rule declarations | Read | D6 | Q06/Q14, U03/U04 | [delivery-and-operations](../delivery-and-operations.md), [configuration-and-development](../configuration-and-development.md) |
| [src/Niobium.Invoicing.Contracts/Billable.cs](../../src/Niobium.Invoicing.Contracts/Billable.cs) | Reusable biller catalog data; current invoice flows do not perform a catalog lookup. | Read | D1 | Q02/Q03/Q09/Q12, U01 | [domain-and-data](../domain-and-data.md), [api-and-messaging](../api-and-messaging.md), [product](../product.md) |
| [src/Niobium.Invoicing.Contracts/Billee.cs](../../src/Niobium.Invoicing.Contracts/Billee.cs) | Customer data scoped to a biller and copied into invoice snapshots. | Read | D1 | Q02/Q03/Q09/Q12, U01 | [domain-and-data](../domain-and-data.md), [api-and-messaging](../api-and-messaging.md), [product](../product.md) |
| [src/Niobium.Invoicing.Contracts/Biller.cs](../../src/Niobium.Invoicing.Contracts/Biller.cs) | External biller profile containing issuer identity, contact, tax and presentation defaults. | Read | D1 | Q02/Q03/Q09/Q12, U01 | [domain-and-data](../domain-and-data.md), [api-and-messaging](../api-and-messaging.md), [product](../product.md) |
| [src/Niobium.Invoicing.Contracts/Invoice.cs](../../src/Niobium.Invoicing.Contracts/Invoice.cs) | Persisted invoice snapshot with biller/customer details, amounts and enqueue-related delivery metadata. | Read | D1 | Q02/Q03/Q09/Q12, U01 | [domain-and-data](../domain-and-data.md), [api-and-messaging](../api-and-messaging.md), [product](../product.md) |
| [src/Niobium.Invoicing.Contracts/InvoiceCycle.cs](../../src/Niobium.Invoicing.Contracts/InvoiceCycle.cs) | Selects billing-period presentation; these values do not schedule recurring issuance. | Read | D1 | Q02/Q03/Q09/Q12, U01 | [domain-and-data](../domain-and-data.md), [api-and-messaging](../api-and-messaging.md), [product](../product.md) |
| [src/Niobium.Invoicing.Contracts/InvoiceExtensions.cs](../../src/Niobium.Invoicing.Contracts/InvoiceExtensions.cs) | Calculates line, subtotal, invoice tax and final tax-inclusive totals from supplied items. | Read | D1 | Q02/Q03/Q09/Q12, U01 | [domain-and-data](../domain-and-data.md), [api-and-messaging](../api-and-messaging.md), [product](../product.md) |
| [src/Niobium.Invoicing.Contracts/InvoiceItem.cs](../../src/Niobium.Invoicing.Contracts/InvoiceItem.cs) | Persisted invoice line keyed by the parent issue timestamp and a line identifier. | Read | D1 | Q02/Q03/Q09/Q12, U01 | [domain-and-data](../domain-and-data.md), [api-and-messaging](../api-and-messaging.md), [product](../product.md) |
| [src/Niobium.Invoicing.Contracts/IssueInvoiceCommand.cs](../../src/Niobium.Invoicing.Contracts/IssueInvoiceCommand.cs) | Trusted integration request with an embedded customer and domain-event metadata. | Read | D1 | Q02/Q03/Q09/Q12, U01 | [domain-and-data](../domain-and-data.md), [api-and-messaging](../api-and-messaging.md), [product](../product.md) |
| [src/Niobium.Invoicing.Contracts/IssueInvoiceRequest.cs](../../src/Niobium.Invoicing.Contracts/IssueInvoiceRequest.cs) | Input for issuing or reissuing an invoice; transport-specific checks are applied by its caller. | Read | D1 | Q02/Q03/Q09/Q12, U01 | [domain-and-data](../domain-and-data.md), [api-and-messaging](../api-and-messaging.md), [product](../product.md) |
| [src/Niobium.Invoicing.Contracts/Niobium.Invoicing.Contracts.csproj](../../src/Niobium.Invoicing.Contracts/Niobium.Invoicing.Contracts.csproj) | Project target, package references, warnings and packaging settings | Read | D5 | U07 | [configuration-and-development](../configuration-and-development.md), [testing-and-conventions](../testing-and-conventions.md), [niobium-integration](../niobium-integration.md) |
| [src/Niobium.Invoicing.Contracts/README.md](../../src/Niobium.Invoicing.Contracts/README.md) | Contract-package usage; previously obsolete interface/project references | Read | D8 | Q01/Q07 | [README](../README.md), [testing-and-conventions](../testing-and-conventions.md) |
| [src/Niobium.Invoicing.Core.Tests/Flows/EmailFlowTests.cs](../../src/Niobium.Invoicing.Core.Tests/Flows/EmailFlowTests.cs) | EmailFlowTests scenario assertions and fixtures | Read | D4 | Q14 | [testing-and-conventions](../testing-and-conventions.md), [validation](validation.md) |
| [src/Niobium.Invoicing.Core.Tests/Flows/HTMLFlowTests.cs](../../src/Niobium.Invoicing.Core.Tests/Flows/HTMLFlowTests.cs) | HTMLFlowTests scenario assertions and fixtures | Read | D4 | Q14 | [testing-and-conventions](../testing-and-conventions.md), [validation](validation.md) |
| [src/Niobium.Invoicing.Core.Tests/Flows/IssueFlowTests.cs](../../src/Niobium.Invoicing.Core.Tests/Flows/IssueFlowTests.cs) | IssueFlowTests scenario assertions and fixtures | Read | D4 | Q14 | [testing-and-conventions](../testing-and-conventions.md), [validation](validation.md) |
| [src/Niobium.Invoicing.Core.Tests/Flows/UpsertFlowTests.cs](../../src/Niobium.Invoicing.Core.Tests/Flows/UpsertFlowTests.cs) | UpsertFlowTests scenario assertions and fixtures | Read | D4 | Q14 | [testing-and-conventions](../testing-and-conventions.md), [validation](validation.md) |
| [src/Niobium.Invoicing.Core.Tests/MSTestSettings.cs](../../src/Niobium.Invoicing.Core.Tests/MSTestSettings.cs) | Assembly-level method-parallel test execution | Read | D4 | Q14 | [testing-and-conventions](../testing-and-conventions.md), [validation](validation.md) |
| [src/Niobium.Invoicing.Core.Tests/Niobium.Invoicing.Core.Tests.csproj](../../src/Niobium.Invoicing.Core.Tests/Niobium.Invoicing.Core.Tests.csproj) | Project target, package references, warnings and packaging settings | Read | D5 | U07 | [configuration-and-development](../configuration-and-development.md), [testing-and-conventions](../testing-and-conventions.md), [niobium-integration](../niobium-integration.md) |
| [src/Niobium.Invoicing.Core/Constants.cs](../../src/Niobium.Invoicing.Core/Constants.cs) | Embedded-resource names used by invoice rendering. | Read | D2 | Q04/Q10/Q11, U08 | [architecture](../architecture.md), [domain-and-data](../domain-and-data.md), [api-and-messaging](../api-and-messaging.md) |
| [src/Niobium.Invoicing.Core/DependencyModule.cs](../../src/Niobium.Invoicing.Core/DependencyModule.cs) | Registers invoice domains, flows, repositories, profile access and billing options. | Read | D2 | Q04/Q10/Q11, U08 | [architecture](../architecture.md), [domain-and-data](../domain-and-data.md), [api-and-messaging](../api-and-messaging.md) |
| [src/Niobium.Invoicing.Core/Domains/InvoiceDomain.cs](../../src/Niobium.Invoicing.Core/Domains/InvoiceDomain.cs) | Updates and renders an invoice through the Niobium domain/repository abstraction. | Read | D2 | Q04/Q10/Q11, U08 | [architecture](../architecture.md), [domain-and-data](../domain-and-data.md), [api-and-messaging](../api-and-messaging.md) |
| [src/Niobium.Invoicing.Core/Domains/InvoiceExtensions.cs](../../src/Niobium.Invoicing.Core/Domains/InvoiceExtensions.cs) | Builds invoice-link tokens and invoice template parameters from a snapshot. | Read | D2 | Q04/Q10/Q11, U08 | [architecture](../architecture.md), [domain-and-data](../domain-and-data.md), [api-and-messaging](../api-and-messaging.md) |
| [src/Niobium.Invoicing.Core/Domains/InvoiceItemDomain.cs](../../src/Niobium.Invoicing.Core/Domains/InvoiceItemDomain.cs) | Renders one invoice line from the marked block in the embedded template. | Read | D2 | Q04/Q10/Q11, U08 | [architecture](../architecture.md), [domain-and-data](../domain-and-data.md), [api-and-messaging](../api-and-messaging.md) |
| [src/Niobium.Invoicing.Core/Domains/InvoiceItemExtensions.cs](../../src/Niobium.Invoicing.Core/Domains/InvoiceItemExtensions.cs) | Formats stored line values for HTML and notification templates. | Read | D2 | Q04/Q10/Q11, U08 | [architecture](../architecture.md), [domain-and-data](../domain-and-data.md), [api-and-messaging](../api-and-messaging.md) |
| [src/Niobium.Invoicing.Core/Domains/StringExtensions.cs](../../src/Niobium.Invoicing.Core/Domains/StringExtensions.cs) | Converts model member names into the uppercase template-key convention. | Read | D2 | Q04/Q10/Q11, U08 | [architecture](../architecture.md), [domain-and-data](../domain-and-data.md), [api-and-messaging](../api-and-messaging.md) |
| [src/Niobium.Invoicing.Core/Flows/EmailFlow.cs](../../src/Niobium.Invoicing.Core/Flows/EmailFlow.cs) | Queues an invoice notification, then records its link token and enqueue-related timestamp. | Read | D2 | Q04/Q10/Q11, U08 | [architecture](../architecture.md), [domain-and-data](../domain-and-data.md), [api-and-messaging](../api-and-messaging.md) |
| [src/Niobium.Invoicing.Core/Flows/HTMLFlow.cs](../../src/Niobium.Invoicing.Core/Flows/HTMLFlow.cs) | Assembles invoice HTML and all persisted line fragments without writing storage. | Read | D2 | Q04/Q10/Q11, U08 | [architecture](../architecture.md), [domain-and-data](../domain-and-data.md), [api-and-messaging](../api-and-messaging.md) |
| [src/Niobium.Invoicing.Core/Flows/IssueFlow.cs](../../src/Niobium.Invoicing.Core/Flows/IssueFlow.cs) | Coordinates invoice persistence followed by optional notification enqueueing. | Read | D2 | Q04/Q10/Q11, U08 | [architecture](../architecture.md), [domain-and-data](../domain-and-data.md), [api-and-messaging](../api-and-messaging.md) |
| [src/Niobium.Invoicing.Core/Flows/UpsertFlow.cs](../../src/Niobium.Invoicing.Core/Flows/UpsertFlow.cs) | Rebuilds an invoice snapshot and replaces its line set using separate repository operations. | Read | D2 | Q04/Q10/Q11, U08 | [architecture](../architecture.md), [domain-and-data](../domain-and-data.md), [api-and-messaging](../api-and-messaging.md) |
| [src/Niobium.Invoicing.Core/InvoiceTemplate.html](../../src/Niobium.Invoicing.Core/InvoiceTemplate.html) | Embedded invoice markup and item-block markers | Read | D9 | Q09/Q13 | [domain-and-data](../domain-and-data.md), [security](../security.md) |
| [src/Niobium.Invoicing.Core/Niobium.Invoicing.Core.csproj](../../src/Niobium.Invoicing.Core/Niobium.Invoicing.Core.csproj) | Project target, package references, warnings and packaging settings | Read | D5 | U07 | [configuration-and-development](../configuration-and-development.md), [testing-and-conventions](../testing-and-conventions.md), [niobium-integration](../niobium-integration.md) |
| [src/Niobium.Invoicing.Core/Options/BillingOptions.cs](../../src/Niobium.Invoicing.Core/Options/BillingOptions.cs) | Configuration for public invoice links and their token derivation. | Read | D2 | Q04/Q10/Q11, U08 | [architecture](../architecture.md), [domain-and-data](../domain-and-data.md), [api-and-messaging](../api-and-messaging.md) |
| [src/Niobium.Invoicing.Core/R.cs](../../src/Niobium.Invoicing.Core/R.cs) | Reads embedded text resources from the Core assembly. | Read | D2 | Q04/Q10/Q11, U08 | [architecture](../architecture.md), [domain-and-data](../domain-and-data.md), [api-and-messaging](../api-and-messaging.md) |
| [src/Niobium.Invoicing.Core/TimeZoneInfoHelper.cs](../../src/Niobium.Invoicing.Core/TimeZoneInfoHelper.cs) | Resolves IANA timezone names through system timezone conversion data. | Read | D2 | Q04/Q10/Q11, U08 | [architecture](../architecture.md), [domain-and-data](../domain-and-data.md), [api-and-messaging](../api-and-messaging.md) |
| [src/Niobium.Invoicing.Server/DependencyModule.cs](../../src/Niobium.Invoicing.Server/DependencyModule.cs) | Composes Functions infrastructure, identity, table entitlements and notification transport. | Read | D3 | Q05/Q13, U08 | [architecture](../architecture.md), [api-and-messaging](../api-and-messaging.md), [security](../security.md), [configuration-and-development](../configuration-and-development.md) |
| [src/Niobium.Invoicing.Server/EmailInvoice.cs](../../src/Niobium.Invoicing.Server/EmailInvoice.cs) | Function-key-protected manual notification endpoint intended for debugging. | Read | D3 | Q05/Q13, U08 | [architecture](../architecture.md), [api-and-messaging](../api-and-messaging.md), [security](../security.md), [configuration-and-development](../configuration-and-development.md) |
| [src/Niobium.Invoicing.Server/GetHtmlInvoice.cs](../../src/Niobium.Invoicing.Server/GetHtmlInvoice.cs) | Public invoice HTML endpoint accepting an invoice-link token without a recipient account. | Read | D3 | Q05/Q13, U08 | [architecture](../architecture.md), [api-and-messaging](../api-and-messaging.md), [security](../security.md), [configuration-and-development](../configuration-and-development.md) |
| [src/Niobium.Invoicing.Server/IssueInvoiceCommandConsumer.cs](../../src/Niobium.Invoicing.Server/IssueInvoiceCommandConsumer.cs) | Consumes trusted private invoice commands from the issueinvoicecommand queue. | Read | D3 | Q05/Q13, U08 | [architecture](../architecture.md), [api-and-messaging](../api-and-messaging.md), [security](../security.md), [configuration-and-development](../configuration-and-development.md) |
| [src/Niobium.Invoicing.Server/NewInvoice.cs](../../src/Niobium.Invoicing.Server/NewInvoice.cs) | HTTP issuance endpoint that checks biller/group claims and disables notification for this path. | Read | D3 | Q05/Q13, U08 | [architecture](../architecture.md), [api-and-messaging](../api-and-messaging.md), [security](../security.md), [configuration-and-development](../configuration-and-development.md) |
| [src/Niobium.Invoicing.Server/Niobium.Invoicing.Server.csproj](../../src/Niobium.Invoicing.Server/Niobium.Invoicing.Server.csproj) | Project target, package references, warnings and packaging settings | Read | D5 | U07 | [configuration-and-development](../configuration-and-development.md), [testing-and-conventions](../testing-and-conventions.md), [niobium-integration](../niobium-integration.md) |
| [src/Niobium.Invoicing.Server/NotificationQueueOptions.cs](../../src/Niobium.Invoicing.Server/NotificationQueueOptions.cs) | Named configuration section for the downstream NotifyCommand broker; inherits Service Bus options. | Read | D3 | Q05/Q13, U08 | [architecture](../architecture.md), [api-and-messaging](../api-and-messaging.md), [security](../security.md), [configuration-and-development](../configuration-and-development.md) |
| [src/Niobium.Invoicing.Server/Placeholder.cs](../../src/Niobium.Invoicing.Server/Placeholder.cs) | Declares Functions routes intercepted by Niobium access-token and resource-token middleware. | Read | D3 | Q05/Q13, U08 | [architecture](../architecture.md), [api-and-messaging](../api-and-messaging.md), [security](../security.md), [configuration-and-development](../configuration-and-development.md) |
| [src/Niobium.Invoicing.Server/Program.cs](../../src/Niobium.Invoicing.Server/Program.cs) | Top-level Functions creation, registration, middleware and host startup | Read | D3 | Q05/Q13, U08 | [architecture](../architecture.md), [api-and-messaging](../api-and-messaging.md), [security](../security.md), [configuration-and-development](../configuration-and-development.md) |
| [src/Niobium.Invoicing.Server/Properties/launchSettings.json](../../src/Niobium.Invoicing.Server/Properties/launchSettings.json) | launchSettings.json host/tool configuration | Read | D7 | Q07, U07 | [configuration-and-development](../configuration-and-development.md), [testing-and-conventions](../testing-and-conventions.md), [security](../security.md) |
| [src/Niobium.Invoicing.Server/Properties/serviceDependencies.json](../../src/Niobium.Invoicing.Server/Properties/serviceDependencies.json) | serviceDependencies.json host/tool configuration | Read | D7 | Q07, U07 | [configuration-and-development](../configuration-and-development.md), [testing-and-conventions](../testing-and-conventions.md), [security](../security.md) |
| [src/Niobium.Invoicing.Server/Properties/serviceDependencies.local.json](../../src/Niobium.Invoicing.Server/Properties/serviceDependencies.local.json) | serviceDependencies.local.json host/tool configuration | Read | D7 | Q07, U07 | [configuration-and-development](../configuration-and-development.md), [testing-and-conventions](../testing-and-conventions.md), [security](../security.md) |
| [src/Niobium.Invoicing.Server/host.json](../../src/Niobium.Invoicing.Server/host.json) | host.json host/tool configuration | Read | D7 | Q07, U07 | [configuration-and-development](../configuration-and-development.md), [testing-and-conventions](../testing-and-conventions.md), [security](../security.md) |

## XML inventory and approved exclusions

The approved scope includes all handwritten application types and explicit methods/constructors, including private/internal helpers, plus all exposed properties/enum values. Useful private state/constant fields were also documented. There are no handwritten events/operators/indexers in the application inventory. No inheritdoc was used because an applicable documented inherited contract was not established.

| Declaration category | Documented sites | Inherited | Unresolved included sites |
| --- | --- | --- | --- |
| Classes and enum type | 30 | 0 | 0 |
| Methods, including private generated-regex declaration | 58 | 0 | 0 |
| Explicit ordinary constructor | 1 | 0 | 0 |
| Properties | 137 | 0 | 0 |
| Fields/constants | 11 | 0 | 0 |
| Enum values | 5 | 0 | 0 |
| **Total** | **242** | **0** | **0** |

These are **comment-bearing declaration sites**, not a compiler-generated symbol coverage percentage. Ten class declarations also carry primary-constructor parameters; their comments document both the type and primary constructor, without counting the same comment twice. All 242 sites are in 30 application files. The remaining application file is Program.cs, whose top-level statements have no XML declaration site and are explained in architecture.

Approved exclusions: all five test C# files (48 declaration sites) from XML editing; Program.cs top-level statements; implicit constructors/accessors, generated regex implementation, compiler/Functions output, vendored/binary output and all framework/Notification source. Tests remain fully covered by Markdown and were not changed. Generated members are not enumerated as handwritten omissions. Private detailed security findings and unverified runtime behavior are retained as limitations, not fabricated contracts.

### Reproduce the inventory

Use the installed SDK Roslyn assemblies in a temporary directory outside the repository; no NuGet tool/package is needed. A temporary net10.0 console project can reference `$(MSBuildToolsPath)/Roslyn/bincore/Microsoft.CodeAnalysis.dll` and `Microsoft.CodeAnalysis.CSharp.dll` through `<Reference>`/`<HintPath>`, with ImplicitUsings enabled and a NuGet.Config containing `<packageSources><clear /></packageSources>`. Build/run that project against the repository path. The following is the counting core used by the verification approach:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var counts = new Dictionary<string, int>();
int documented = 0, excluded = 0, primaryConstructors = 0;
foreach (string path in Directory.GetFiles(args[0], "*.cs", SearchOption.AllDirectories))
{
    if (path.Split(Path.DirectorySeparatorChar).Any(p => p is "bin" or "obj")) continue;
    var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(path),
        new CSharpParseOptions(documentationMode: DocumentationMode.Diagnose));
    var nodes = tree.GetRoot().DescendantNodes().Where(n => n is
        BaseTypeDeclarationSyntax or MethodDeclarationSyntax or ConstructorDeclarationSyntax
        or PropertyDeclarationSyntax or EnumMemberDeclarationSyntax or FieldDeclarationSyntax);
    foreach (var node in nodes)
    {
        if (path.Contains("Niobium.Invoicing.Core.Tests")) { excluded++; continue; }
        string kind = node.Kind().ToString();
        counts[kind] = counts.GetValueOrDefault(kind) + 1;
        if (node is ClassDeclarationSyntax c && c.ParameterList != null) primaryConstructors++;
        if (node.GetLeadingTrivia().Any(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
            || t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))) documented++;
    }
}
foreach (var pair in counts.OrderBy(p => p.Key)) Console.WriteLine($"{pair.Key}: {pair.Value}");
Console.WriteLine($"Documented: {documented}; excluded test sites: {excluded}; primary constructors: {primaryConstructors}");
```

Pass the repository `src` directory. This syntax inventory includes private members and excludes implicit/generated members by construction. Parameter/type-parameter and cref correctness is checked separately by the compiler and XML checks, not inferred from comment presence. Compare before/after declaration identities (path, containing type, kind, name and parameter names) and every Roslyn token's RawKind/Text plus directives to detect changes beyond comments. The actual verification also removed inserted XML lines and compared each original C# byte stream, preserving BOM/newlines and implementation text. See [validation](validation.md).

## Knowledge coverage

| Required area | Destination / accepted limit |
| --- | --- |
| Product, users, requirements, scope, acceptance | [product](../product.md); success/support metrics U06 |
| Architecture, DI, trust and failure flows | [architecture](../architecture.md) and [integration](../niobium-integration.md) |
| Framework ownership/version/provenance | [integration](../niobium-integration.md); deployed revisions U03 |
| Domain, fields, units, keys, concurrency, retention | [data](../domain-and-data.md); rounding U01, recovery/privacy U04, runtime edges U08 |
| API, messaging, examples, retries and compatibility | [API/messaging](../api-and-messaging.md); future protocols U05 |
| Security and privacy | [security](../security.md); sensitive detail withheld from public docs, link policy U02 |
| Configuration, safe setup and commands | [development](../configuration-and-development.md); complete isolated host U07 |
| Tests, quality and conventions | [testing/conventions](../testing-and-conventions.md); absent test categories explicitly proposed |
| Delivery, packaging and workflow side effects | [delivery/operations](../delivery-and-operations.md); external protections U03 |
| Operations, reliability, recovery/capacity | [delivery/operations](../delivery-and-operations.md); U04/U06 |
| Decisions, discrepancies and backlog | [register](../decisions-and-risks.md); Q01-Q14 and U01-U08 |
| Human/agent navigation and maintenance | [root README](../../README.md), [AGENTS](../../AGENTS.md), [index](../README.md) |
| Validation and handoff | [validation](validation.md) |

SQL schemas/migrations, accounting, implemented MCP tools and payment-provider runbooks are not applicable to the current implementation for the reasons recorded in product/data/API pages. They are not hidden omissions. No new OpenAPI/AsyncAPI specifications, tooling, persistent build settings, tests or deployment changes were approved or introduced.
