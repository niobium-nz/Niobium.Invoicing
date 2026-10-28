# Niobium.Invoicing

Niobium.Invoicing calculates and issues invoices, renders them as HTML, and queues email notifications through the separate Niobium.Notification service. It is a .NET 10 Azure Functions application with shared C# contracts.

The product direction is invoicing for small businesses using AI agents as first-class consumers. An MCP server layer and retirement of the historical UI are planned; this repository currently exposes HTTP and Service Bus entry points. Payment collection, direct debit, reconciliation, and delivery confirmation are planned. Accounting is outside the service boundary. See the [product and capability map](docs/product.md).

An invoicing **tenant** is a management group containing biller businesses. Each biller belongs to exactly one group and invoices its own customers, called **billees**. In Notification, the sender tenant corresponds to the invoicing biller, not the management group.

## Repository map

| Project or directory | Responsibility |
| --- | --- |
| [Contracts](src/Niobium.Invoicing.Contracts) | Models, requests/commands, identifiers and arithmetic; published NuGet package. |
| [Core](src/Niobium.Invoicing.Core) | Issue/upsert/email/HTML flows, domains, embedded template and configuration. |
| [Server](src/Niobium.Invoicing.Server) | Functions triggers, middleware and service registration. |
| [Core.Tests](src/Niobium.Invoicing.Core.Tests) | Unit tests using mocked infrastructure and synthetic data. |
| [infra](infra), [workflows](.github/workflows), [azure.yaml](azure.yaml) | Declared resources, provisioning hooks, deployment and publication. |
| [docs](docs/README.md) | Product, architecture, contracts, operations, decisions and validation evidence. |

## Start safely

Read [AGENTS.md](AGENTS.md) for working rules and [configuration and development](docs/configuration-and-development.md) for prerequisites and command status. `global.json` requests .NET SDK 10.0.100 with `latestFeature` roll-forward and Microsoft.Testing.Platform. Discovery verified SDK 10.0.403 on Linux, a Release build, and 26 unit tests against existing restored dependencies:

```sh
dotnet build Niobium.Invoicing.slnx --configuration Release --no-restore --nologo
dotnet test --project src/Niobium.Invoicing.Core.Tests/Niobium.Invoicing.Core.Tests.csproj --configuration Release --no-build --no-restore
```

These commands require restored packages. A clean-cache restore and a complete isolated application setup have not been demonstrated. Running the Functions host needs identity, profile, storage and messaging configuration; a development environment name alone does not isolate those dependencies. Do not start against existing resources or use credentials copied from tracked examples.

**Review [delivery behavior](docs/delivery-and-operations.md) before submitting changes.** The deployment workflow has no documentation path exclusion. Changes under Contracts, including comments or its README, can also qualify for package publication. Deployment and publication are separate actions requiring authorization.

## Read next

- [Documentation index and analyzed baseline](docs/README.md)
- [Architecture and invoice flows](docs/architecture.md)
- [Domain, money and persistence](docs/domain-and-data.md)
- [API and messaging contracts](docs/api-and-messaging.md)
- [Niobium package provenance](docs/niobium-integration.md)
- [Confirmed decisions and planned improvements](docs/decisions-and-risks.md)
- [Validation results and limitations](docs/discovery/validation.md)

Source inspection describes implementation, not production correctness or readiness. Documents distinguish Observed, Verified, Owner-confirmed, Inferred, Proposed and Unknown claims. This repository uses [MIT license text](LICENSE.md).
