# Delivery and operations

## Workflow eligibility and side effects

These are source declarations, not verified GitHub/Azure settings or deployed state. Test/prod resource separation is owner-confirmed (Q06). Environment reviewers, protections and actual credential grants remain unknown.

Azure Deployment now runs restore/build, unit tests and local Azurite/HTTP component integration checks in `verify`, then permits the test deployment through `needs: verify`. Production depends on test deployment and is eligible only on main push or main manual dispatch. PRs cannot select production. A meaningful post-deployment integration gate is still missing (B07); B04 is partial, not a completed production-promotion design.

| Matching event | Test deployment | Production deployment | Package workflow |
| --- | --- | --- | --- |
| Push main | After verification | After successful test deployment | Production release |
| Push matching non-main branch | After verification | No | Prerelease |
| PR targeting main | After verification | No | No PR trigger |
| Manual dispatch on main | After verification | After successful test deployment | Production release if that workflow is dispatched |
| Manual dispatch on another ref | After verification | No | Prerelease if that workflow is dispatched |
| Markdown/docs-only push or PR | Filtered out | Filtered out | Push filtered out |

Both workflows exclude `**/*.md` and `docs/**` on automatic triggers. C# comments still count as code-path changes. The literal push branch pattern `["*"]` is unchanged; do not assume it matches slash-containing branches. Manual dispatch bypasses path filtering. Package publication still has its original version/changed-project rules and does not inherit the deployment verification job.

Deployment concurrency groups serialize each environment, with cancellation disabled; they do not themselves provide verification. This task submitted commits with `[skip ci]`, ran local checks, and did not dispatch, deploy, provision, publish or change external environment protections. The main formatting update resolves the earlier style failures; the normal Release build now passes. See the [acceptance ledger](implementation-progress.md) for verification and remaining deployment gaps.

## Build, tests, packages and identity

Deployment: checkout v4, Azure/setup-azd v2, azure/login v3 and azd federation login, followed by `azd package --no-prompt`, `azd provision --no-state --no-prompt`, `azd deploy --no-prompt`. The workflow grants id-token write and contents read. Login uses GitHub secret names AZURE_CLIENT_ID/TENANT_ID/SUBSCRIPTION_ID; no values are documented. Actual federated credentials/permissions are external.

[azure.yaml](../azure.yaml) prepackage runs `dotnet test --solution ../../Niobium.Invoicing.slnx --configuration Release` in the service context, before deployment. Job name `test` denotes a deployment environment, not a separate integration suite. There are no post-deployment tests in source. The verification job explicitly installs .NET 10. Deployment jobs still depend on their runner/azd tooling. The debug EmailInvoice function has been removed, with local compiled-route evidence; no deployed route check was run.

Package publication: checkout v7, setup-dotnet v6 (`10.x.x`), NuGet/login v1. Version is `MAJOR_VERSION.MINOR_VERSION.github.run_number`, plus `-prerelease` off main, passed to build as BUILD_BUILDNUMBER. Contracts has GeneratePackageOnBuild/IsPackable true, repository metadata, embedded untracked sources and snupkg symbols. Only Contracts is selected for push to the public NuGet feed; no explicit symbol-push command is present. Without BUILD_BUILDNUMBER, application project version properties default to 1.0.0.0. Deployment injects SERVICE_VERSION from its workflow buildVersion; that is not proof that assembly/package versions received the same property during azd packaging.

Publishing uses a short-lived NuGet credential obtained through OIDC; `NUGET_USER` identifies the publishing account. No release-tag, manual package approval or rollback workflow is declared. Dependency versions are fixed in csproj files, with no lockfile.

## Provisioning and settings flow

Evidence: [main.bicep](../infra/main.bicep), [function-app.bicep](../infra/function-app.bicep), [service-bus.bicep](../infra/service-bus.bicep), [rbac.bicep](../infra/rbac.bicep), [main.bicepparam](../infra/main.bicepparam), and [scripts](../infra/scripts).

1. Preprovision `SetAppSettings.ps1` reads APPSETTING_ variable names, backs up the parameter file and appends a generated appSettings block with environment references. It modifies the working tree temporarily.
2. Bicep creates settings; names prefixed KV_ become Key Vault references with underscores converted to dashes in secret names. Runtime environment/logging/identity settings are added by the template.
3. Postprovision `SetKeyVaultSecrets.ps1` resolves output `keyVaultName` and calls `az keyvault secret set` for APPSETTING_KV_ variables. This is a cloud write, not a harmless configuration check.
4. `ClearAppSettings.ps1` restores the backed-up parameter file. No rollback/finally guarantee across failed hooks is established; inspect leftover generated content/backup after a failed authorized provision.

Do not run these scripts to validate documentation. Settings and precedence are catalogued in [development](configuration-and-development.md).

## Declared runtime topology

| Component | Declaration / defaults | Limit of evidence |
| --- | --- | --- |
| Functions | Linux .NET 10 isolated, FC1 Flex Consumption, 512 MB instances, max 10, alwaysOn false | Not a measured capacity or actual scale record |
| Storage | Two Standard_LRS StorageV2 accounts: data/host dependencies and deployment package blob container | LRS is not evidence of recoverability from overwrite/delete; no application backup policy declared |
| Service Bus | Basic default, incoming queue `issueinvoicecommand`; 5-minute lock, 14-day TTL, max deliveries 10, expiry dead-lettering, sessions/duplicate detection/partitioning off | No observed live queue contents/configuration; Notification queue is external |
| Identity | User-assigned shared identity plus function system identity | Actual identity permissions outside declarations not verified |
| RBAC | Table Data Contributor, Blob Data Owner, Service Bus Data Owner, telemetry publishing and Key Vault read roles as declared; optional user/deployer assignments | External profile and Notification permissions require separate configuration |
| Observability | Application Insights linked to Log Analytics; host OpenTelemetry mode | Alerts, dashboards, retention and ingestion/cost behavior unverified |
| Key Vault | App-setting references; purge protection false in declared module inputs | Do not infer production policy or successful secret rotation |
| Network / browser | Public access and wildcard CORS declared for relevant resources; optional custom domain/managed certificate | No deployed firewall/edge/security audit performed |

Azure Verified Modules and API versions are pinned in Bicep. Some comments describe a container app, but the declared app is a Function App. No SQL schema/migration, scheduler, payment processor or local Notification provider is provisioned here. Table creation is not declared by the app or these templates. The solution includes `infra/RBAC.bicep` with different casing from the tracked file; this is a navigation inconsistency, not changed by documentation.

## Telemetry, failure recovery and operating knowledge

Matched Niobium service defaults configure OpenTelemetry logs (formatted messages/scopes), ASP.NET/HTTP client/runtime metrics and tracing, service name/version/environment attributes, optional OTLP and Azure Monitor exporters. App code does not establish a durable correlation ID connecting request, invoice, queue and downstream provider. Message ID/full invoice identity help investigation, but do not constitute a complete tracing policy.

Health checks are registered, but `/health` and `/alive` are not mapped by this Functions startup. No application alert rules, on-call procedure, queue replay tool, backup export, restore automation or disaster-recovery test is tracked. Platform capabilities and configured operations must be distinguished.

**Proposed investigation procedure, not a verified recovery runbook:** identify the affected stage and full invoice identity; inspect authorized sanitized telemetry; compare invoice/items and enqueue/status outcomes; establish whether Notification/provider may already have accepted a send; then agree replay or repair with the owner. Do not automatically replay a dead-letter message or mark Delivered based on queue acceptance. Each may repeat side effects. Inspect message age, attempt history and failure cause before an authorized replay.

Backups/restores, recovery objectives, incident/support ownership and detailed retention/privacy policy remain U04/U06. The owner expects Azure PaaS support and has committed no SLA. No evidence here proves automatic five-year history, point-in-time table recovery or cross-region disaster recovery. Costs/capacity have not been measured; scale and queue limits above are declarations only. Downstream duplicate detection was deferred for cost reasons, with message-ID storage planned (Q11).

Rollback and environment promotion are not implemented as dedicated runbooks. A proposed redeploy of a prior artifact still needs known artifact/version, compatibility review and authorization; package publication and storage schema changes cannot be treated as automatically reversible. No cloud resources, queues or workflows were invoked during this documentation work.
