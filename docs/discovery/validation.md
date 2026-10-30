# Validation and handoff

## Scope and environment

Baseline: commit `f403f50aa6358e9c56dd5561d9f6073d7a73a1eb`, branch `work`, `/workspace/Niobium.Invoicing`, initially clean. See the [central baseline](../README.md#baseline-and-evidence-language) and [coverage ledger](coverage.md). Work was approved on 2026-10-29 (Pacific/Auckland) for Markdown/XML comments only.

The result contains **16 Markdown files** (two existing READMEs updated, fourteen new files) and XML comments in **30 application C# files**. [AGENTS.md](../../AGENTS.md) is 81 lines. No project/build settings, dependencies, tests, templates, infrastructure, workflow files or framework source were changed. Validation was completed before submission. No branch switch, reset, clean, push, PR, publication or deployment was performed during that validation pass.

Validation environment: Debian 13 linux-x64; .NET SDK 10.0.403, runtime 10.0.14. Existing restored NuGet assets were used. A reviewed workspace-only environment script configured SDK PATH/cache locations and telemetry opt-out. That script is not a repository setup requirement.

## Commands and outcomes

Commands ran from the repository root unless an outside-repository path is shown. Temporary inventory/example projects and sanitized logs were kept under `/workspace/scratch/`; no tool dependencies or persistent build configuration were added.

```sh
dotnet build Niobium.Invoicing.slnx --configuration Release --no-restore --nologo

dotnet test --project src/Niobium.Invoicing.Core.Tests/Niobium.Invoicing.Core.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --results-directory /workspace/scratch/invoicing-post-tests --report-trx

dotnet build src/Niobium.Invoicing.Server/Niobium.Invoicing.Server.csproj \
  --configuration Release --no-restore --nologo -p:GenerateDocumentationFile=true

git diff --check
```

| Check | Before edits | After edits / result |
| --- | --- | --- |
| Release solution build | Passed: 0 warnings, 0 errors | Passed: 0 warnings, 0 errors |
| MTP unit tests | 26 passed, 0 failed, 0 skipped | 26 passed, 0 failed, 0 skipped |
| Compiler XML validation | No handwritten XML comments | Server build and its Contracts/Core references passed with temporary GenerateDocumentationFile=true: 0 warnings/errors, including final wording/cref corrections |
| XML structure / references | Not applicable | Compiler XML for all three app projects parsed; source param/typeparam inventories checked; no unresolved cref markers |
| C# implementation preservation | Captured before edits | All 36 handwritten C# files have identical Roslyn non-comment tokens/directives; removing inserted XML lines from 30 changed files reproduces original bytes, including BOM/newlines |
| Declaration inventory | 242 included application sites lacked XML | 242 documented; 0 inherited; 0 unresolved included sites; 48 test sites excluded as approved |
| Scope / diff hygiene | Clean | Full tracked diff and new-file inventory reviewed; only approved Markdown/XML changes; git diff --check passed |
| Markdown navigation | Template documentation obsolete | Relative file/anchor links checked; pinned framework file links resolved against matching snapshots; six referenced Notification files fetched at the pinned commit |
| API construction example | Not present | Compiled against built Contracts and matching framework assemblies in an outside-repo project, without running it or contacting services |
| Architecture diagrams | Not present | Mermaid source/participants/sequence reviewed against code; not rendered with a Mermaid engine |
| Sensitive content | Example values not copied | Authored examples use synthetic IDs, placeholders and .invalid URLs; private detailed findings retained outside repository |

The post-edit unit suite ran after the main comment batch. Final comment wording/reference corrections were compiler-validated and rechecked against the unchanged implementation token/byte baseline; no new runtime behavior needed additional test cases. The smaller Contracts README construction example is a subset of the compiled API example. No application host or live HTTP/queue integration was executed.

The Functions SDK checked the generated WorkerExtensions restore despite `--no-restore`; that is recorded rather than advertised as a fully offline build. Contracts builds create local nupkg/snupkg artifacts by existing project settings. A rebuild printed the informational notice that the package lacks an included README; the source README exists, but PackageReadmeFile is not configured. Build summary remained zero warnings/errors. Persistent package README/XML-output settings were outside scope and left unchanged.

Session evidence: `invoicing-baseline-build.log`, `invoicing-baseline-tests.log`, `invoicing-post-build.log`, `invoicing-post-tests.log`, `invoicing-docxml-final.log`, `invoicing-examples-build.log` and TRX output under `/workspace/scratch/`. These temporary files are not durable repository dependencies; the commands/results above are the maintainable record.

## Reproduction of structural checks

[Coverage](coverage.md#reproduce-the-inventory) describes the SDK Roslyn inventory. Parsing used DocumentationMode.Diagnose and compared declaration identities, all token RawKind/Text values, and directives. The stronger byte comparison removed only added `///` lines and compared against `git show <baseline>:<path>`. Existing comments/formatting/signatures/attributes/directives remained unchanged. Compiler output checked XML syntax, parameter names, type parameters and cref resolution without new suppressions.

Markdown checking resolved relative paths and heading fragments from each page; framework `/blob/<sha>/<path>` links were checked against the matched source snapshots. Notification checks used read-only GitHub file retrieval, not current main assumptions. This was not a crawl of every external documentation URL. Configuration/API/data tables were reviewed against the referenced source and Q decisions; no deployed-state values were inferred.

## Fresh-reader walkthrough

Performed as a separate reading pass through the published navigation, then checked against source; no second agent or independent human review is claimed.

| Reader task | Navigation and verified answer |
| --- | --- |
| Locate a billing change | AGENTS -> domain/data -> Contracts.InvoiceExtensions and InvoiceDomain.UpdateAsync -> UpsertFlowTests. Units, rate representation and tax-inclusive final total are explicit. |
| Explain issuance | Architecture -> IssueFlow/UpsertFlow/EmailFlow. Invoice save, item replacement, enqueue and status save are separate stages; HTTP disables notification. |
| Understand tenant boundaries | Product -> API/security -> Server.DependencyModule and matched resource controls. Management group, biller, customer and Notification sender meanings differ; controls are not assumed cumulative. |
| Identify inherited behavior | Niobium integration -> exact package/version/source row -> GenericDomain/table adapter/middleware. Force save, default lifetimes and queue serialization are sourced. |
| Find appropriate checks | Testing -> development -> MTP project command. Twenty-six mocked unit tests are distinguished from missing HTTP/storage/queue integration evidence. |
| Know which actions need authorization | AGENTS -> delivery/operations. Push/PR can have deployment side effects; Contracts changes can publish. Cloud actions, messages and workflow execution need separate authorization. |
| Resolve a requirement ambiguity | Product R IDs -> decisions Q IDs/U IDs. Q09 means final grand total includes tax; Q11 preserves replay publishing with downstream idempotency planned. |

## Limits and next review

No clean-cache restore, complete offline/isolated host setup, live Azure state, external GitHub protections, production Notification revision, real ETag round-trip, recipient delivery or recovery procedure was verified. Tests do not prove security, production correctness, availability or compliance. Detailed private security findings were not published as exploit descriptions.

The owner accepted [U01-U08](../decisions-and-risks.md#accepted-unknowns) as explicit deferrals. Planned lifecycle, conditional persistence/downstream idempotency, promotion/integration checks, retention/audit, MCP and payment work remain outside this change. Review [product](../product.md), [decisions](../decisions-and-risks.md), then data/API/framework pages before selecting the next implementation task. Review private security findings with the owner before broader access or disclosure. Reinspect workflow triggers before any authorized submission; this handoff does not authorize publication or deployment.

## Security follow-up 2026-10-30

Separate authorization under Q15-Q18 permitted implementation and regression tests for resource grants, stored-token compatibility and HTML encoding. Starting checkout: `/workspace/Niobium.Invoicing`, branch `work`, commit `910fea08ffd2d608e9d9534c4607a2aeaa9c9822`, clean. This follow-up intentionally changes behavior; the comment-only preservation checks above apply to the original documentation bootstrap. Framework/package versions, infrastructure, workflows and build settings remain unchanged. S02 key changes remain deferred.

Environment remains .NET SDK 10.0.403 on Debian 13 linux-x64, using existing restored dependencies. Before changes, Release build passed with zero warnings/errors and all 26 baseline tests passed. After changes:

| Check | Result / limits |
| --- | --- |
| Release solution build, same command as above | Passed, zero warnings/errors |
| Core.Tests MTP run, same command with security-specific output directory | 60 cases passed, zero failed/skipped: Issue 4, Upsert 8, Email 19, HTML 29 |
| Temporary compiler XML validation, same GenerateDocumentationFile command | Passed, zero warnings/errors |
| Saved-link compatibility | Legacy stored tokens accepted case-insensitively without regeneration or new expiry; resends preserve tokens even after secret/content changes; tokenless or mismatched access rejected |
| New token and enqueue ordering | Full-secret/full-identity derivation, stable culture-independent output, token saved before publishing, persistence failure prevents enqueueing, enqueue failure preserves token for retry |
| Rendering regressions | Encoded text/attributes, safe logo schemes, escaped contact URI values, literal placeholders, unchanged raw notification values |
| Additional local S01 verification | Eight assertions using actual Server registrations and matched controls/SAS issuer; synthetic principal/key, mocked invoice lookup and empty external entitlement provider. No HTTP host or Azure calls. Verified owned grant/SAS bounds, denied missing/empty/unowned/biller-key partitions and preserved Invoice grant. |
| Persistence sequencing | Status save uses the new entity/ETag returned from the token save; verified with the real domain and mocked repository, not live Azure storage |
| Scope, XML and documentation | Complete changed-file review; git diff --check passed. Compiler XML parsed without unresolved cref markers; 437 local Markdown links/anchors and 29 pinned framework file links checked. Changes comprise 14 Markdown files, 10 C# files and the embedded HTML template. |

Sanitized session logs are `invoicing-security-baseline-build.log`, `invoicing-security-baseline-tests.log`, `invoicing-security-build.log`, `invoicing-security-tests.log`, `invoicing-security-xml.log` and `invoicing-security-registration.log` under `/workspace/scratch/`. The extra grant verification project is also outside the repository and is not a permanent CI dependency. Build-generated package files remain local; no publication was performed.

No live credentials, queues, cloud data, browser execution or deployed security posture were tested. Existing access tokens/SAS are not revoked by the code change, and external grants remain outside verified scope. Legacy saved tokens retain their original properties for compatibility. Historical token recovery, expiry/revocation policy, ETag integration behavior and concurrency across issuance remain separate concerns. No application source or framework changes beyond the approved follow-up were made.
