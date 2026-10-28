# Delivery and operations

## Workflow eligibility and side effects

These are **Observed source declarations**, not verified GitHub/Azure settings or deployed state. Owner-confirmed: test/prod have separate resource groups/prefixes (Q06), current checks are unit tests, and integration tests are planned before promotion (Q14). External reviewers, approvals, branch rules and actual credential grants remain Unknown.

| Workflow | Trigger / condition | Actions and artifacts |
| --- | --- | --- |
| [Azure Deployment](../.github/workflows/azure-deploy.yml) | Push `branches: ["*"]`; pull_request targeting main; workflow_dispatch | Test job always eligible. Prod if main ref **or pull_request**; no `needs: test`. Both package/provision/deploy. |
| [Package Publish](../.github/workflows/package-publish.yml) | Push `branches: ["*"]`; workflow_dispatch | Non-main prerelease job, main production release job; restore/build, NuGet OIDC login, conditional package push. No PR trigger. |

The literal single-asterisk branch pattern is preserved here: do not rewrite it as `**` or assume it matches every slash-containing branch. There are no workflow-level path filters. Eligibility matrix for events that match those declarations:

| Event | Deployment test | Deployment prod | Package job |
| --- | --- | --- | --- |
| Push main | Yes | Yes, independent of test | Production release |
| Push matching non-main branch | Yes | No | Prerelease |
| PR targeting main | Yes | Yes under current condition | None |
| Manual dispatch on main | Yes | Yes | Production release when that workflow dispatched |
| Manual dispatch on another ref | Yes | No | Prerelease when that workflow dispatched |

Deployment jobs use separate concurrency groups (`test-deploy`, `production-deploy`) with cancel-in-progress false. Package groups are `pre-release-publish` and `prod-release-publish`, with cancellation true. Concurrency groups do not create a test->prod dependency.

**Documentation changes have side effects if submitted:** root Markdown can trigger deployment. Contract comments or `src/Niobium.Invoicing.Contracts/README.md` also change the package path and can qualify for NuGet publication. The publish script skips a project on push only if its directory has no diff from the event's before SHA; a new branch uses an empty-tree comparison. Manual publication bypasses that changed-path check. Restore/build/login still precede the script's skip. Do not push/open a PR/dispatch based on an assumption that documentation is ignored.

Q06/Q14 intend PRs to deploy only test, and successful test-environment checks before production. Correcting the prod condition/dependency and implementing integration checks are **planned work**, not changes made here.

## Build, tests, packages and identity

Deployment: checkout v4, Azure/setup-azd v2, azure/login v3 and azd federation login, followed by `azd package --no-prompt`, `azd provision --no-state --no-prompt`, `azd deploy --no-prompt`. The workflow grants id-token write and contents read. Login uses GitHub secret names AZURE_CLIENT_ID/TENANT_ID/SUBSCRIPTION_ID; no values are documented. Actual federated credentials/permissions are external.

[azure.yaml](../azure.yaml) prepackage runs `dotnet test --solution ../../Niobium.Invoicing.slnx --configuration Release` in the service context, before deployment. Job name `test` denotes a deployment environment, not a separate integration suite. There are no post-deployment tests in source. The Azure workflow does not explicitly install a .NET SDK; runner availability is an external prerequisite.

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
