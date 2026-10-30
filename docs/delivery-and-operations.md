# Delivery and operations

## Required delivery sequence

The owner confirmed this policy on 2026-10-30. It replaces earlier skip-CI submission and deployment restrictions for the **test environment only**.

1. Implement on a feature branch, run applicable local checks, and push directly. Do not create a PR automatically or use skip-CI markers. Slash-containing branches and documentation-only changes also run the deployment workflow.
2. `Azure Deployment` restores/builds the solution and the separate deployed-test project, runs delivery-policy tests, and runs unit/local Azurite integration tests with coverage. Authored Contracts/Core/Server source must reach **90% combined line coverage and 80% per module**. Coverage is a union of executed source lines across suites; generated `obj`/`bin` files are excluded, authored application files are not.
3. A feature push then deploys the test environment and runs the [deployed integration suite](integration-tests.md). Deployment and testing share one concurrency lock across all feature branches. A failed or skipped suite never qualifies as deployment verification. The deployed binary must report the exact pushed commit at `GET /health`.
4. End every change with the latest commit's CI/CD run URL and terminal outcome. Wait for deployment and integration completion. Resolve failures or report their concrete blocker; do not equate local tests or a green older commit with success.
5. Only after success, and when requested, create a PR to `main`. Review must approve the current head. Update the feature branch from current main and repeat deployment/tests when needed so the final merged tree is exactly the tested tree.
6. A main push runs local verification and an independent production gate. It must correspond to one merged same-repository PR, with an approval from someone other than its author and no outstanding change request. The exact PR head must have passed the feature deployment before merge; its Git tree must equal the main commit's tree. Only then can production deployment proceed.

| Event | Verification / test deployment | Production / packages |
| --- | --- | --- |
| Feature push, including docs-only and branches containing `/` | Local checks, coverage, deploy test, deployed integration | Neither |
| Main push | Local checks plus reviewed/tested-content gate; no test redeployment | Production after both gates; package publication has the same reviewed/tested-content gate |
| PR targeting main | API-only `Test deployment verified` status checks existing feature-run evidence | Neither |
| Manual dispatch / PR event in deployment workflow | No trigger | Neither |

The package workflow no longer publishes feature prereleases. Its existing changed-project/version logic is retained for reviewed main pushes. It filters docs-only changes; Azure Deployment does not. There is no manual-dispatch bypass in either workflow. No change here merges a PR, deploys production or publishes a package.

## Enforcement and repository settings

[`scripts/delivery-policy.cjs`](../scripts/delivery-policy.cjs) checks GitHub run, job, deployed-test-step, review and Git-tree evidence. Both required feature jobs must succeed, as must the explicitly named deployed-test step. Production rejects direct pushes, stale reviews, untested heads, test completion after merge, and extra content introduced by a newer base branch. Local policy tests exercise these rejection paths.

[`pr-readiness.yml`](../.github/workflows/pr-readiness.yml) handles PR changes and deployment completion, checks out only the default branch's trusted policy, and writes the `Test deployment verified` commit status. It never runs PR code with `pull_request_target` credentials. This listener becomes active once the workflow reaches the default branch; the initial policy feature branch still obtains evidence from its push-triggered deployment workflow.

**Repository administration still required:** protect `main` with PR-only changes, at least one independent approval, dismissal of stale approvals, up-to-date branches, the required `Test deployment verified` status, and no bypass/direct pushes. Keep workflow and environment administration restricted to trusted maintainers. The connected GitHub identity could not read branch protection (403); the rulesets API returned no rulesets. This task cannot claim these external settings are configured. The production gate blocks deployment without its evidence even when merge protection is absent.

GitHub Actions cannot prevent creation of a PR object. Opening a PR only after green deployed tests is the owner/agent process rule in [AGENTS.md](../AGENTS.md). Required status/review protection prevents an ineligible PR from merging; the workflow additionally blocks unqualified production promotion. Neither environment approval settings nor review rules should be inferred merely from YAML.

## Build, tests, packages and identity

The workflow explicitly installs .NET 10 in build and deployment jobs. Test deployment uses GitHub OIDC through `azure/login` and `azd auth login`, then the existing `azd package`, `azd provision --no-state` and `azd deploy` sequence. Provision hooks still perform cloud writes. Local runners receive no Azure identity from this workflow.

The test job creates a `notifycommand` sink in Invoicing's own test Service Bus namespace and configures the test function to emit there, rather than the Notification email service. A fresh masked access-token signing key is generated for each test deployment and written using the existing Key Vault hook; the selected test function then explicitly refreshes its versionless Key Vault references. Deployed tests obtain test-only keys in memory/environment from the authenticated workflow; no credential is committed in fixtures or artifacts. Production notification routing and secrets retain their existing configuration.

`SourceRevisionId` stamps the commit into Server assembly metadata. The anonymous `/health` response exposes only `status` and `commit`; it proves which host binary responds, not that all dependencies are healthy. The deployed tests exercise HTTP authentication/validation, Table persistence, saved-token HTML, replay, real incoming Service Bus processing and the controlled notification sink, and the removed debug route.

[azure.yaml](../azure.yaml)'s prepackage solution tests remain an additional check; their unconfigured local storage skips are not deployed integration evidence. The required verification job explicitly opts into Azurite, and the separately invoked deployed suite fails on missing configuration instead of skipping. Test and coverage artifacts are uploaded even on failures where reports exist.

Package version remains `MAJOR_VERSION.MINOR_VERSION.github.run_number`, passed as BUILD_BUILDNUMBER; only changed Contracts packages are selected. NuGet uses its existing OIDC login. No package version or Niobium dependency was upgraded for this delivery change. Production rollback and artifact promotion runbooks remain separate work; rebuilding an identical source tree does not guarantee identical dependency restore or byte-for-byte binaries.

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
| Service Bus | Basic default, incoming queue `issueinvoicecommand` plus the controlled `notifycommand` sink in test; 5-minute lock, 14-day TTL, max deliveries 10, expiry dead-lettering, sessions/duplicate detection/partitioning off | Production Notification is external; test fixtures use only the local namespace sink |
| Identity | User-assigned shared identity plus function system identity | Actual identity permissions outside declarations not verified |
| RBAC | Table Data Contributor, Blob Data Owner, Service Bus Data Owner, telemetry publishing and Key Vault read roles as declared; optional user/deployer assignments | External profile and Notification permissions require separate configuration |
| Observability | Application Insights linked to Log Analytics; host OpenTelemetry mode | Alerts, dashboards, retention and ingestion/cost behavior unverified |
| Key Vault | App-setting references; purge protection false in declared module inputs | Do not infer production policy or successful secret rotation |
| Network / browser | Public access and wildcard CORS declared for relevant resources; optional custom domain/managed certificate | No deployed firewall/edge/security audit performed |

Azure Verified Modules and API versions are pinned in Bicep. Some comments describe a container app, but the declared app is a Function App. No SQL schema/migration, scheduler, payment processor or local Notification provider is provisioned here. Table creation is not declared by the app or these templates. The solution includes `infra/RBAC.bicep` with different casing from the tracked file; this is a navigation inconsistency, not changed by documentation.

## Telemetry, failure recovery and operating knowledge

Matched Niobium service defaults configure OpenTelemetry logs (formatted messages/scopes), ASP.NET/HTTP client/runtime metrics and tracing, service name/version/environment attributes, optional OTLP and Azure Monitor exporters. App code does not establish a durable correlation ID connecting request, invoice, queue and downstream provider. Message ID/full invoice identity help investigation, but do not constitute a complete tracing policy.

`GET /health` identifies the responding binary; `/alive` is not mapped. Framework health-check registration does not by itself test dependencies through this endpoint. No application alert rules, on-call procedure, queue replay tool, backup export, restore automation or disaster-recovery test is tracked. Platform capabilities and configured operations must be distinguished.

**Proposed investigation procedure, not a verified recovery runbook:** identify the affected stage and full invoice identity; inspect authorized sanitized telemetry; compare invoice/items and enqueue/status outcomes; establish whether Notification/provider may already have accepted a send; then agree replay or repair with the owner. Do not automatically replay a dead-letter message or mark Delivered based on queue acceptance. Each may repeat side effects. Inspect message age, attempt history and failure cause before an authorized replay.

Backups/restores, recovery objectives, incident/support ownership and detailed retention/privacy policy remain U04/U06. The owner expects Azure PaaS support and has committed no SLA. No evidence here proves automatic five-year history, point-in-time table recovery or cross-region disaster recovery. Costs/capacity have not been measured; scale and queue limits above are declarations only. Downstream duplicate detection was deferred for cost reasons, with message-ID storage planned (Q11).

Rollback and environment promotion are not implemented as dedicated runbooks. A proposed redeploy of a prior artifact still needs known artifact/version, compatibility review and authorization; package publication and storage schema changes cannot be treated as automatically reversible. Feature changes now invoke the authorized test workflow; production operations still require reviewed promotion.
