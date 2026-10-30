# Delivery and operations

## Required delivery sequence

The owner clarified this process on **2026-10-31 (Pacific/Auckland)**. This replaces the earlier request-before-PR rule, independent-reviewer restriction, independent production gate and isolated notification sink.

1. Implement on a feature branch, run applicable local checks, and push directly. Do not push to `main`, create an untested PR, or use skip-CI markers. Slash-containing branches and documentation-only changes also run verification.
2. `Azure Deployment` restores/builds the solution and deployed-test project, runs policy/tooling tests, and runs unit/local integration tests with coverage. Authored Contracts/Core/Server source must reach **90% combined line coverage and 80% per module**. Generated `obj`/`bin` code is excluded; authored application files are not.
3. Deploy the feature commit to **the existing test environment**, then run deployed integration tests against its actual Functions host, Table storage, Service Bus and Notification service. Use test data in those resources, without extra Azure test stacks, substitute queues or notification sinks. Deployment and integration share a concurrency lock across feature branches. The host must report the exact feature SHA at `/health`.
4. Verify the current commit's terminal CI/CD result, including test deployment and integration. Fix failures; do not substitute a local pass, an older green commit, a skip or a running job for success. Coverage/TRX/read-only results are uploaded as run artifacts.
5. **After success, automatically create a ready PR to `main` without asking.** The trusted main controller authenticates the feature run, then creates or reuses the PR. The agent keeps actively watching review submissions/threads/comments, fixes actionable issues raised by any reviewer, and repeats testing after changes. A ready PR with no comments yet is not a stopping point. Exactly **one valid approval of the current head** is required, from any approver GitHub permits; there is no repository-policy exclusion for the PR owner. Update the feature from current main and repeat deployment/tests as needed so the final merged tree is the tested tree. Once review issues are addressed, the current head is approved and checks pass, the agent **merges the PR without another permission request**.
6. **A PR merge into main immediately triggers production deployment, followed by read-only deployed integration tests against production.** There is no independent production approval or verification gate at this stage; one may be added later. Production tests use the configured existing test invoice and real production service, with no data writes, provisioning, fixture creation, queue operations, sending, or cleanup. The merging agent must monitor deployment and production test results until they finish. A failure requires immediate investigation and a fix through the same feature/test/review/merge process; merging does not complete the task.

| Event | Result |
| --- | --- |
| Feature push, including docs-only and branch names containing `/` | Build, coverage, deploy test, real deployed integration, read-only client verification, then automatic PR creation/reuse |
| PR review/comment/update | Agent handles feedback; changed heads repeat feature deployment/tests and require current-head approval |
| PR closed **with `merged: true`** into main | Deploy the merge commit to production, then run read-only production integration; publish changed Contracts packages using the existing release rules |
| PR closed without merging | No production deployment or package publication |
| Direct push to main | Prohibited. It is not a deployment trigger; branch protection must prevent it. |

The production job has no dependency on a second review/verification gate. Its trigger is the merged-PR event and it checks out that event's merge SHA. Production deployment and its tests share the existing production concurrency lock. Feature pushes never select production. Manual dispatch is not configured. The package workflow checks both `filename` and `previous_filename`, so moving files out of Contracts still publishes the changed package. Its release group uses `cancel-in-progress: false` and `queue: max`, preserving running and pending publications when unrelated PRs merge. GitHub supports up to 100 pending entries; overflow is a platform cancellation that must be reported, not treated as a successful publication. Feature prerelease publication remains removed.

## Review and repository enforcement

[`scripts/delivery-policy.cjs`](../scripts/delivery-policy.cjs) implements exact-feature-run evidence, pre-merge eligibility and automatic PR creation. Its pre-merge check verifies approval of the current head, successful test deployment and a main ancestor without running as an independent production gate. The agent must additionally handle review comments, including actionable conversation comments that cannot be classified automatically. It must not submit an approval merely to end its own wait for review.

[`pr-readiness.yml`](../.github/workflows/pr-readiness.yml) owns all status/PR writes. Its checkout is pinned to `github.workflow_sha`, the immutable main commit defining that controller. It never executes feature code or downloaded artifacts. Feature deployment jobs have no status/PR write permission. The controller checks the run's repository, SHA, push event and workflow path, then compares the Git blob identities of `azure-deploy.yml`, `azure.yaml`, `check-coverage.py` and `test-resources.py` with its trusted main commit. Only then does it accept successful build/deployment jobs and the integration step. A renamed or no-op feature workflow cannot obtain certification just by producing green job names. A failed triggering run cannot borrow success from an older run of the same SHA.

The listener becomes active only when installed on the default branch. The initial installation and changes to the protected pipeline definitions therefore require an explicit reviewed activation decision; they cannot certify themselves using branch code. The source deliberately fails closed instead of introducing an undocumented bootstrap exception. Before the controller exists on main, actual feature CI can still pass, but the required custom status is unavailable. An administrator must decide how to install the reviewed controller while retaining the test evidence and required approval. Do not silently weaken rules, bypass review or post a synthetic success status to resolve that dependency. This concerns pre-merge verification, not an independent production gate.

After installation, successful authenticated runs create or reuse ready PRs. Superseded feature runs do not open PRs for old heads. GitHub's [workflow_run documentation](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#workflow_run) explains the default-branch requirement and privileged-code boundary; its [concurrency reference](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#concurrency) documents `queue: max`. These source controls do not replace repository administration or establish that every workflow a write-capable collaborator could add is trusted; maintain native review/protection and audit workflow permission changes.

Required repository settings are PR-only main updates with no direct-push bypass, **one** required approval, stale-approval dismissal, up-to-date branches, and the required `Test deployment verified` status. The Actions setting must permit workflow PR creation. GitHub itself normally disallows formal self-approval; the policy adds no author exclusion, and a workflow-authored PR lets the repository owner review it normally. No extra approval identity or CODEOWNERS requirement is introduced here.

The connected GitHub identity cannot administer repository rules or Actions permissions. After the owner corrected the settings, a 2026-10-31 read-only audit verified the required `test` deployment, `Test deployment verified` from GitHub Actions, one approval, stale-review dismissal, resolved threads and no bypass actors. Run 41054903186 attempt 2 also proved that Actions could create PR #6. Those results describe the earlier feature-side publisher; the review fix removes that publisher and exposes the trusted-listener installation dependency above. Verify external settings separately; source changes do not configure them. GitHub Actions cannot prevent someone opening a PR manually before tests; protected checks and the agent process enforce the intended review sequence.

## Real-environment integration and configuration

The test function uses its normal `niobiumnotify-sb-test.servicebus.windows.net` namespace. Tests never receive or complete messages from the Notification service's queue. They send one invoice notification to the approved `TEST_NOTIFICATION_RECIPIENT` **environment secret**, using a test biller, and wait for Invoicing's persisted enqueue acknowledgement. That verifies the real outgoing transport; `Delivered` still does not prove the email provider accepted or delivered a message. Provider receipts remain outside this repository's observable contract.

The test workflow reads and preserves its existing signing key instead of rotating shared authentication configuration for every run. Credential setup reads actual test resource metadata and keys; it does not create tables, queues or a separate test stack. The prior extra sink queue is no longer declared or used; incremental provisioning does not automatically delete old resources.

For test fixture selection, `TEST_INVOICE_BILLER` and `TEST_INVOICE_ID` may identify an existing invoice in the test environment. At the owner's request, when both are absent the suite creates and retains a reference invoice through the real test host, reusing an existing test biller when available. Its non-secret IDs are recorded in `artifacts/deployed/test-invoice-fixture.json` for the owner to configure. Later unconfigured runs reuse that marked reference. Temporary case records are cleaned up separately. The suite fails on a partial configuration or missing required recipient; it never falls back to a fake outgoing transport.

Production requires its **own existing** test fixture using environment variables `TEST_INVOICE_BILLER`, `TEST_INVOICE_ID` and environment secret `TEST_INVOICE_TOKEN`. The production test command is:

```sh
python scripts/read-only-deployment.py --require-fixture --report artifacts/production-read-only.json
```

[`read-only-deployment.py`](../scripts/read-only-deployment.py) has no Azure storage/queue SDK, authentication mutation or fixture-writing path. Its HTTP client permits only same-origin `GET /health` and `GET /{biller}/invoices/{id}`, rejects other methods/routes and redirects, and omits response bodies and tokens from failure reports. It checks the deployed merge SHA, missing-invoice/token behavior, the designated invoice's HTML and rejection of a wrong token. Missing production fixture configuration fails; no invoice is created to work around it. All five checks from the same client run inside the test HTTP case before its temporary invoice is cleaned up, with the read-only report uploaded alongside the TRX.

`SourceRevisionId` stamps the deployed commit into Server metadata. `/health` exposes only `status` and `commit`; it is a binary-identity probe, not a complete dependency health check. Azure login/provision/deploy uses the existing GitHub OIDC configuration. Production deployment naturally writes application/infrastructure configuration; the **subsequent production test step** is read-only.

No package or Niobium dependency upgrade is part of this change. Contracts publication retains its existing OIDC/version rules and runs only if that project changed in the merged PR. A production failure requires an immediate agent response, not an automatic rollback or a direct push that bypasses review.

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
| Service Bus | Basic default, incoming queue `issueinvoicecommand`; 5-minute lock, 14-day TTL, max deliveries 10, expiry dead-lettering, sessions/duplicate detection/partitioning off | Both environments use their real external Notification service; tests do not intercept that service’s messages |
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

Rollback and environment promotion are not implemented as dedicated runbooks. A proposed redeploy of a prior artifact still needs known artifact/version, compatibility review and authorization; package publication and storage schema changes cannot be treated as automatically reversible. Feature changes invoke the test workflow; reviewed PR merges trigger production deployment and read-only verification.
