# Configuration and local development

## Toolchain and safe commands

[global.json](../global.json) requests SDK 10.0.100 with `latestFeature` roll-forward and selects Microsoft.Testing.Platform. All four projects target net10.0; Server is Functions v4 isolated with ASP.NET integration. Tests use MSTest.Sdk 4.4.0. Verified environment: Debian 13, linux-x64, .NET SDK 10.0.403/runtime 10.0.14. Other operating systems were not tested. Culture/timezone formatting requires appropriate system globalization data.

For an authorized isolated host session, Azure Functions Core Tools v4 and compatible .NET support are additional prerequisites; host execution was not validated here. Azure provisioning requires Azure Developer CLI, Azure CLI, PowerShell for the tracked hooks, and separately authorized Azure access. Optional VS Code C# Dev Kit/Functions extensions support debugging. See the official [Functions local-development prerequisites](https://learn.microsoft.com/azure/azure-functions/functions-run-local) and [azd installation guidance](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd); installing tools does not authorize deployment.

No repository `NuGet.Config`, central package/build files or lockfile is tracked. Package versions are in csproj files; resolved graph/provenance is in [integration](niobium-integration.md). External feed configuration can affect restore. No new feeds/dependencies were installed for documentation.

| Command from repository root | Status and prerequisites |
| --- | --- |
| `dotnet --info` | Verified SDK/runtime above. Ensure matching SDK is on PATH. |
| `dotnet restore Niobium.Invoicing.slnx` | Inspected, not verified from an empty cache. Requires approved feed/network access; do not treat as a proved offline setup. |
| `dotnet build Niobium.Invoicing.slnx --configuration Release --no-restore --nologo` | Verified with existing restored assets. Builds Contracts package locally because GeneratePackageOnBuild is true. Functions SDK still checks generated WorkerExtensions restore. |
| `dotnet test --project src/Niobium.Invoicing.Core.Tests/Niobium.Invoicing.Core.Tests.csproj --configuration Release --no-build --no-restore` | Verified MTP invocation; 60 isolated cases in the security follow-up, 26 at discovery. Optional `--results-directory <outside-repo-path> --report-trx` was used for recorded evidence. |
| `git diff --check` | Verified documentation hygiene check. |
| Functions host startup / live API requests | Not run. Requires explicitly isolated identity/profile/storage/messaging resources and synthetic records. |
| `azd package`, `azd provision`, `azd deploy` / provisioning scripts | Feature pushes now run these in the authorized test workflow. Provision/deploy have cloud/file/secret side effects; other environments still require authorization. See [delivery](delivery-and-operations.md). |

In the discovery workspace, a reviewed `/workspace/.niobium-tools/env.sh` supplied PATH/cache/telemetry settings. It is environment-specific and is not a repository prerequisite. See [validation](discovery/validation.md) for exact recorded commands.

## Configuration sources and precedence

Application startup uses `FunctionsApplication.CreateBuilder`; there is no custom JSON loader or provider-order override. Options bind from `IConfiguration` sections in `AddInvoicing`, `AddCore` and matched framework modules. Standard .NET environment keys use `__` for `:`. A complete effective provider list and deployed overrides were not captured, so do not infer a live value from a checked-in example.

Known precedence within inspected code:

- Niobium environment classification reads `AZURE_FUNCTIONS_ENVIRONMENT`, then `ASPNETCORE_ENVIRONMENT`; development/dev/local enable its development behavior, test/staging are preproduction rather than development. Profile HTTP resilience setup separately uses `builder.Environment.IsDevelopment()`.
- Storage client endpoint uses `StorageTableOptions:FullyQualifiedDomainName` or `AzureWebJobsStorage:tableServiceUri`. Direct server operations use identity credentials; the table SAS issuer additionally needs the storage key. Resource-entitlement hostname resolution is a distinct framework path with unverified edge behavior; do not assume client fallback proves `/rsas` works.
- Notification broker binds its own `NotificationQueueOptions`; the generic messaging path binds `ServiceBusOptions`. Their namespace fallback is `AzureWebJobsServiceBus:fullyQualifiedNamespace`. Managed-identity selection in the matched broker reads `AzureWebJobsStorage:clientId`.
- Deployment scripts collect `APPSETTING_` variables, then Bicep strips that prefix before creating settings. `KV_` becomes a Key Vault reference. See [delivery](delivery-and-operations.md) before invoking these scripts.

No `local.settings.json` is tracked. The project copies one to output when supplied and excludes it from publish. A UserSecretsId is declared, but no application-specific secret-loading code or local secret contents were inspected. The tracked `.azure/dev/.env` contains owner-confirmed examples and mixed assignment styles; it is not a validated runnable setup recipe.

## Runtime configuration catalog

Names below use `:`; environment equivalents use `__`. “Required” means needed by the indicated path, not necessarily validated at startup. Defaults are source defaults, not confirmed deployed values. Secret values are never shown.

| Key / section members | Consumer and purpose | Required / default | Secret? |
| --- | --- | --- | --- |
| `BillingOptions:InvoiceTokenSecretSalt` | InvoiceDomain new v2 link-token derivation, called by EmailFlow | Nonblank when creating a new token; use a high-entropy secret. No default or startup validation. Saved-token validation/reuse does not depend on this value. | Yes |
| `BillingOptions:GetInvoiceEndpoint` | InvoiceDomain notification URL base | Required for useful links; no default | No |
| `IdentityServiceOptions:IDTokenPublicKey` | PrincipalParser RSA ID-token verification | Required in this verifier configuration | Public key; redact examples |
| `IdentityServiceOptions:AccessTokenSecret` | Access token signing/verification | Required for this server path; no default | Yes |
| `IdentityServiceOptions:IDTokenAudience`, `IDTokenIssuer` | ID-token acceptance | Defaults `niobium.client`, `niobium.platform`; workflows override audience | No |
| `IdentityServiceOptions:AccessTokenAudience`, `AccessTokenIssuer` | Access-token scope/issuer | Same framework defaults; environment-specific workflow overrides | No |
| `IdentityServiceOptions:TokenValidity` | Access-token lifetime | 8 hours | No |
| `IdentityServiceOptions:DefaultRole` | Base entitlement role | `User` | No |
| `IdentityServiceOptions:AccessTokenEndpoint`, `ResourceTokenEndpoint` | Middleware path matching | `token`, `rsas`; placeholder routes remain compiled defaults | No |
| `IdentityServiceOptions:IDTokenEndpoint` | Shared identity option | `auth`; no ID-token issuing route registered here | No |
| `IdentityServiceOptions:IDTokenPrivateKey`, `IDTokenPrivateKeyPasscode` | Shared issuer-side options | Optional; no requirement to provision a private signing key for this app's ID-token verification | Yes |
| `StorageTableOptions:FullyQualifiedDomainName` | Table endpoint / entitlement configuration | Client fallback to AzureWebJobsStorage table URI; verify resource path separately | No |
| `StorageTableOptions:Key` | Table SAS signing | Required for resource SAS, not used by direct identity-based table I/O | Yes |
| `StorageTableOptions:MaxRetries`, `MaxDelay` | Azure table retry options | 5; 5 seconds maximum delay | No |
| `StorageTableOptions:ConnectionIdleTimeout` | Assigned to table client Retry.NetworkTimeout | Null leaves SDK default; name is not the applied SDK property | No |
| `StorageTableOptions:EnableInteractiveIdentity` | DefaultAzureCredential interactive option | False; platform post-configures true in development | No |
| `ProfileOptions:ProfileServiceHost` | External profile HTTP base URL | Required, normalized by Validate | No |
| `ProfileOptions:ProfileServiceEndpoint` | Profile route prefix | `profile`, normalized with leading slash | No |
| `ProfileOptions:ProfileAppID` | Entra scope `api://<APP_ID>/.default` | Required for selected service-principal implementation | Identifier, not secret |
| `ProfileOptions:TestMode` | Profile credential interactive allowance | False; distinct from host development flag | No |
| `NotificationQueueOptions:FullyQualifiedNamespace` | Destination Notification namespace | Needed for external notification routing; source fallback exists | No |
| `ServiceBusOptions:FullyQualifiedNamespace` | Generic framework queue namespace | Fallback to AzureWebJobsServiceBus namespace | No |
| Both queue sections: `EnableInteractiveIdentity`, `UseWebSocket` | Credential allowance and transport choice | False; generic section post-configures interactive mode in development | No |
| Both queue sections: `MaxRetries`, `MaxDelay`, `ConnectionIdleTimeout` | SDK send/receive retry options | 5; 5 seconds; nullable timeout | No |
| Both queue sections: `Keys` | Shared option for Service Bus resource-signature keys | Not consumed by the selected outgoing broker; Service Bus resource-token support is not registered here | Yes |
| `AzureWebJobsServiceBus:fullyQualifiedNamespace`, `credential`, `clientId` | Functions incoming queue connection | Injected by Bicep; managed identity | Namespace/identity IDs not secrets |
| `AzureWebJobsStorage:blobServiceUri`, `tableServiceUri`, `credential`, `clientId` | Host storage and framework table/identity fallbacks | Injected by Bicep | Endpoints/identity IDs not secrets |
| `AZURE_FUNCTIONS_ENVIRONMENT`, `ASPNETCORE_ENVIRONMENT` | Environment selection | First takes precedence in Niobium helper; Bicep injects environment name | No |
| `SERVICE_NAME`, `SERVICE_VERSION` | OpenTelemetry resource identity | `unknown-service`, `1.0.0-prerelease` | No |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Optional OTLP exporter | Unset disables that exporter path | Endpoint may be sensitive operational metadata |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Optional Azure Monitor exporter | Unset skips corresponding framework branch | Treat as protected configuration |
| `APPLICATIONINSIGHTS_AUTHENTICATION_STRING` | App Insights AAD identity | Bicep builds from managed identity | Identifier/configuration; no key shown |
| `AzureFunctionsJobHost:logging:logLevel:default` | Host logging | Bicep sets Information | No |

Evidence: [BillingOptions](../src/Niobium.Invoicing.Core/Options/BillingOptions.cs), [NotificationQueueOptions](../src/Niobium.Invoicing.Server/NotificationQueueOptions.cs), and pinned option/client/registration sources in [integration](niobium-integration.md).

## Deployment inputs and environment differences

| Input | Purpose / default |
| --- | --- |
| `AZURE_ENV_NAME` | Bicep environment; default `dev`, workflows use test/prod |
| `APP_SHORT_NAME` | Resource prefix, default `niobiuminvoicing` |
| `CUSTOM_DOMAIN_NAME` | Optional host/certificate binding, default empty |
| `SERVICE_BUS_QUEUE_NAMES` | Comma-separated queues; default `issueinvoicecommand` |
| `IS_INTERACTIVE_DEPLOYER` | RBAC principal type; default true, workflow false |
| `AZURE_LOCATION`, `AZURE_RESOURCE_GROUP`, `AZURE_SUBSCRIPTION_ID`, `AZURE_TENANT_ID`, `AZURE_CLIENT_ID` | azd/Azure login and target selection; protected identifiers, no values reproduced |
| `APPSETTING_*`, `APPSETTING_KV_*`, output `keyVaultName` | App-setting and secret provisioning pipeline; values may be secret |
| GitHub vars `MAJOR_VERSION`, `MINOR_VERSION` | Combined with workflow run number for build/package versions |
| GitHub secret `NUGET_USER` | NuGet OIDC publishing identity; returned API key is secret |

Test/prod vary service identity/version, token audience/issuer, invoice URL, Notification namespace and secret sources. Owner confirms separate resource groups/prefixes; actual deployed values were not queried. A `ResendServiceOptions:GlobalAPIKey` secret is projected by the production workflow, but no Resend consumer is registered in this application's source; do not treat it as a required Invoicing runtime setting.

## Isolated development and troubleshooting

The demonstrated safe starting point is unit tests with synthetic fixtures and mocked repositories/profile/broker. A complete offline host environment is **Unknown**, not an existing feature. Before attempting it, inventory dependencies, disable/redirect real triggers, provision approved isolated identity/profile/table/messaging resources, seed synthetic biller/customer data, and verify destination configuration. Those steps are prerequisites, not a validated runbook or authorization to provision.

Debug pure flows through the unit tests first. Missing profile audience/host can fail retrieval; invalid culture/IANA timezone or unsupported display currency can fail rendering; missing template resource fails domain rendering; absent restore assets fail `--no-restore` builds. Microsoft.Testing.Platform uses `--project`/`--solution`, not arbitrary legacy VSTest switches. An empty recipient intentionally skips notification. A successful development broker call does not prove a message was sent. Record concrete failures before proposing setup or runtime changes.
