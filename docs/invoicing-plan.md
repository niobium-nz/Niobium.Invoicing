# Niobium.Invoicing: implementation plan for deferred and planned work

## Current baseline and what should be treated as authoritative

The repository’s own working instructions now state that `main` is the default branch and normal integration target, while `master` is a legacy branch. I therefore treated the latest GitHub `main` view as the current implementation baseline rather than the legacy branch the request called “master.” The same instructions also explicitly say that Niobium framework source is a read-only reference unless a task authorizes framework changes, and that Codex should match the NuGet versions actually referenced by Niobium.Invoicing to the pinned Niobium source snapshots rather than substituting current Niobium `main` or converting packages to project references. citeturn825261view4

Niobium.Invoicing is currently a .NET 10 isolated Azure Functions service split into Contracts, Core, Server, and Core.Tests. Its present responsibilities are invoice calculation/persistence, HTML rendering, HTTP and Service Bus entry points, and submission of `NotifyCommand` messages to a separate Niobium.Notification service. Direct payment collection, direct debit, reconciliation, delivery confirmation, cancellation, MCP tools, five-year version history, and production-grade integration gates remain planned or incomplete rather than implemented. citeturn299742view1turn825261view0turn110814view0

The project has already undergone a substantial documentation/security follow-up. The current test documentation reports 60 passing mocked unit-test cases—4 IssueFlow, 8 UpsertFlow, 19 EmailFlow, and 29 HTMLFlow cases—and notes that the earlier discovery baseline had 26. There are still no repository-tracked integration, end-to-end, deployment smoke, recovery, or load tests. citeturn144659view0

The most important project-specific constraint for a future Codex implementation is that several apparently simple behaviors are inherited from Niobium packages. Relevant resolved versions include a mix of Niobium 4.0.60 and 4.0.61; for example, `Niobium.Abstractions` provides `GenericDomain`, repositories and domain registration, `Niobium.Database.StorageTable` provides Azure Table persistence, `Niobium.Messaging.ServiceBus` supplies the Service Bus broker, and `Niobium.Platform.Identity` supplies access/resource-token middleware. The repository’s provenance document maps these package versions to exact framework source commits, and implementation work should be checked against those snapshots. citeturn902528view1

A key inherited behavior is that the current invoice update path is not a conventional optimistic-concurrency update. `UpsertFlow.RunAsync` constructs a fresh invoice, `InvoiceDomain.UpdateAsync` ultimately force-saves it, and Niobium’s force-save path invokes create-with-replacement semantics; for a single Azure Table entity this ends up using an upsert with Merge behavior, whereas batched entities use `UpsertReplace`. Regular Niobium update/delete paths can use ETag preconditions, but the application’s current force path does not establish replay-safe conditional persistence. citeturn902528view1turn738141view0

The repository itself already defines a proposed backlog: P1/B01 conditional persistence, B02 downstream email idempotency, B03 billing validation, and B04 deployment/debug-route correction; P2/B06 delivery confirmation/cancellation, B07 integration tests, B08 retention/audit/recovery, and B09 request/display reconciliation; B05 invoice-item key isolation is explicitly deferred; B10 MCP/payments/reconciliation is longer-term product work. This ordering is a useful starting point, but the implementation dependencies suggest an even more explicit sequence for Codex. citeturn144659view2

## Recommended delivery sequence

The safest sequencing is **persistence/lifecycle first, validation second, notification idempotency third, integration tests fourth, and deployment gates fifth**. Delivery confirmation and cancellation should follow once those foundations exist. Retention/audit and request/display cleanup can then be introduced without destabilizing the critical issuance path. MCP and payment/reconciliation should remain separate feature epics rather than being mixed into the reliability changes. This sequencing follows from the fact that invoice persistence, item replacement, token generation, notification enqueue, and status persistence are currently independent stages that can partially complete, while the existing unit tests do not exercise real storage concurrency or queue behavior. citeturn738141view0turn144659view0

The proposed implementation order for a Codex handoff is:

1. **B01 — conditional persistence and explicit lifecycle**
2. **B03 — domain validation and invariant enforcement**
3. **B02 — downstream Notification idempotency protocol**
4. **B07 — real test-environment integration suite**
5. **B04 — workflow promotion and debug-route hardening**
6. **B06 — Delivering/Delivered confirmation and cancellation**
7. **B09 — reconcile request, defaults, and rendering/template semantics**
8. **B08 — retention, audit/versioning, backup and restore**
9. **B05 — reconsider invoice-item key schema when owner reopens the deferral**
10. **B10 — MCP, payments/direct debit, settlement and reconciliation**

This order deliberately moves B07 ahead of B04’s final production-promotion change: changing workflow gates without first having meaningful post-deployment integration checks would satisfy the workflow shape but not the underlying owner requirement that successful test-environment checks precede production. citeturn110814view1turn144659view2

## Core reliability and lifecycle work

### Conditional persistence and replay-safe issuance

**Goal.** Make retries recognize the already-persisted invoice, preserve its frozen state when appropriate, distinguish an exact replay from a legitimate pre-send edit, and reject conflicting/concurrent edits instead of rebuilding and force-upserting blindly. The owner requirement is specifically that a retry preserves the existing invoice but may publish the same exact downstream notification message again; duplicate recipient emails are to be prevented downstream, not by suppressing Invoicing’s replay publish. citeturn144659view2turn110814view0

The current implementation builds a fresh invoice from current Biller/Billee profile data, force-saves it, then deletes/recreates the supplied item set. `InvoiceDomain.UpdateAsync` checks `Delivered` only on that newly constructed in-memory entity rather than loading the prior stored invoice first. There is no atomic operation spanning invoice write, prior-item deletion, new-item insertion, token creation, enqueue, and status update. citeturn738141view0

**Implementation shape.** Codex should first introduce an explicit “load current aggregate” step in `UpsertFlow.RunAsync`. The lookup key should remain the existing biller/invoice identity; do not change the storage schema as part of B01. If no invoice exists, create through the ordinary domain/repository path. If one exists, retain its stored identity, token, lifecycle fields and immutable/frozen snapshot fields instead of re-running `BuildNew` and accidentally pulling newer profile values into a historical invoice. Existing ETag data should be carried forward and used through the regular Niobium conditional update path rather than `SaveAsync(force: true)` wherever the framework permits. This design is grounded in the existing Niobium `GenericDomain` behavior: without force, entities with an ETag take the update path; entities without one take create. citeturn902528view1turn738141view0

Codex should add an internal comparison model or deterministic “business-content fingerprint” for deciding whether a repeated request is an exact replay or a changed edit. That fingerprint should be based only on invoice fields whose equality matters to the accepted business semantics—biller/billee identity and snapshot, billing period, due date, terms/payment instructions, tax/currency, line content, and settlement state—and must not include storage ETag/timestamp or regenerated transport/token data. This is a design inference rather than an existing contract, so it should be implemented locally in Core and covered heavily by tests rather than exposed as a public Contracts API unless needed by external consumers. The requirement to distinguish same-payload retry, changed input and concurrent edits is explicitly called out in the backlog. citeturn144659view2turn144659view0

Before notification is queued, changed content should be permitted if it passes concurrency checks. After content is frozen, an exact replay should return/reconstruct the same logical invoice and notification payload; a changed request should fail with a domain-level conflict rather than overwrite persisted state. The owner’s desired freeze point is notification enqueue, not current “Delivered” persistence. citeturn110814view0turn144659view2

For item replacement, Codex should avoid treating invoice and item writes as atomic because Azure Table transaction atomicity is partition-scoped and the current model stores invoice and invoice items in different partition schemes. The implementation should instead make partial failure detectable and recoverable. A practical first iteration is: conditionally persist the invoice state/version, replace items with clear error propagation, and only transition the invoice to a notification-ready/frozen state after item persistence succeeds. Do not claim transactional aggregate persistence unless the schema is redesigned. citeturn738141view0

**Tests to add.** Add cases for first issue, exact replay before notification, modified replay before notification, exact replay after freeze, modified replay after freeze, stale ETag update, simultaneous requests simulated through repository precondition failures, failed old-item deletion, failed new-item creation, and retry after partial item failure. Existing tests explicitly lack ETag and race coverage. citeturn144659view0

### Billing and ownership validation

The owner has confirmed that stage-one operation is NZ/AU; an invoice must retain a due date with the seven-day default when no valid override exists; billers issue for their own customers; and automated collection will eventually charge the exact remaining balance. The current request model, however, has no comprehensive validators for currency compatibility, positive quantities, ranges, ownership/item linkage, settlement currency, or due-date validity. A nullable request `DueBy` currently overwrites the default and can make the stored due date null. citeturn110814view0turn738141view1

Codex should introduce a **single application-level validation layer** in Core rather than scattering additional HTTP-only checks. It should be invoked from both HTTP and private Service Bus issuance, because the private Niobium.Store producer intentionally bypasses HTTP principal checks but should not bypass invoice-domain invariants. Niobium’s `ValidationHelper.TryValidate` performs sanitation and DataAnnotations validation, but C# `required` does not validate monetary ranges, cross-field ownership, currency matching, or nested item semantics. citeturn902528view1turn825261view2

The initial validation set should enforce: non-empty biller/tenant/billee IDs where applicable; valid supported currency for the biller/billee/invoice; invoice item parent linkage to the requested invoice; matching unit/line currency and invoice currency; non-null item collection; no duplicate item identifiers within an invoice; due date present after default resolution; due date not before the invoice issue instant unless the owner explicitly later permits it; supported `InvoiceCycle`; and safe integer/money overflow checks. Do **not** invent policy for negative amounts, zero quantities, fractional quantities, credits or midpoint-rounding changes, because the project explicitly records those as unresolved U01 decisions. citeturn738141view1turn144659view2

A particularly important fix is to normalize each incoming `InvoiceItem.Invoice` parent key to the request invoice identity or reject mismatches. Today `UpsertFlow` does not rewrite item parent keys, so a malformed or inconsistent request can persist lines under a partition unrelated to the invoice being issued. The implementation should choose one rule and make it explicit; rejecting mismatches is safer because it exposes caller bugs rather than silently changing supplied identifiers. citeturn738141view0

Keep settlement validation conservative. `Settled` presently copies cents without currency checking, and manual bank transfer overpayment is explicitly allowed. Therefore enforce currency consistency but **do not** reject a settled amount greater than the grand total. citeturn738141view1turn110814view0

### Notification idempotency

The outgoing notification already has a stable full invoice identity: `NotifyCommand.ID` and `MessagingEntry.ID` are both `invoiceID@biller`, and the broker maps the envelope ID to Service Bus `MessageId`. Replaying the same invoice therefore already produces a usable idempotency key. Service Bus duplicate detection is intentionally disabled for cost reasons; the owner-approved short-term plan is a downstream table keyed by message ID plus uniqueness/concurrency checks. citeturn738141view2turn144659view2

Because this responsibility is downstream, B02 should primarily be implemented in the Niobium.Notification repository, not by changing Invoicing so that it suppresses duplicate publishes. Invoicing’s responsibility should be to keep the `NotifyCommand` stable for an exact replay: same `ID`, sender tenant, destination snapshot, invoice URL/token, and invoice content. The existing EmailFlow tests already check unchanged resend serialization and saved-token reuse; extend those once B01 freezes the source invoice snapshot. citeturn144659view0turn738141view2

The Notification-side inbox record needs at least the stable message ID, processing state, attempt timestamps, and enough provider-result metadata to distinguish “never submitted,” “provider accepted,” “definitive failure,” and “unknown outcome.” A simple “insert ID then send” algorithm is insufficient because a process crash between row creation and provider acceptance can cause a permanently suppressed email, while “send then insert” can cause a duplicate after a crash. The repository already calls out this crash window as an unresolved protocol-design problem. citeturn144659view2

A reasonable first protocol is a lease/claim state machine with optimistic concurrency: create `Pending` if absent; atomically claim to `Sending` with lease metadata; call the provider; persist provider acceptance/reference and transition to `Sent`; allow expired `Sending` claims to enter a recovery path rather than automatically resend. When provider semantics support provider-side idempotency keys, use the full invoice/message ID there too. This is a proposed design inferred from the project’s stated requirements; it should be reviewed against the actual Notification provider implementation before Codex commits it. citeturn144659view2turn738141view2

## Test, deployment, and operational hardening

### Integration test environment

The repository currently has 60 passing unit tests, but infrastructure is mocked. There are no live queue, profile-service, Azure Table, HTTP-host, browser, deployment smoke, recovery or load tests. The owner has explicitly required integration tests in the test environment before future production promotion. citeturn144659view0turn144659view2

Codex should create a dedicated integration-test project rather than folding live Azure tests into `Core.Tests`. It should be opt-in locally and executed automatically only against an isolated test deployment. The test identity/resource prefix must be generated per run or otherwise guarantee cleanup/isolation because test and production are intentionally separate resource groups/prefixes. citeturn110814view1

The minimum integration suite should cover: host startup with real Niobium DI assembly scanning; bearer/access-token middleware behavior for `POST /invoices`; Azure Table create/read/update with real ETag round-trip; stale ETag rejection; invoice/item persistence and partial-failure handling; public token HTML retrieval; Service Bus ingestion of `IssueInvoiceCommand`; emission of a `NotifyCommand`; and retry of the same invoice with stable message identity. It should also verify the ambiguous framework-host edges called out as U08, including ETag mapping and final HTTP result behavior. citeturn902528view1turn144659view2

Do not use the actual external email provider in the primary deployment gate. Either point the test Notification service at a test provider/sink or assert queue/consumer state up to a controlled boundary. Recipient delivery confirmation belongs to B06 and should get its own contract tests once the protocol exists. citeturn738141view2turn110814view0

### CI/CD and production promotion

The current Azure Deployment workflow is unsafe relative to the stated policy: test is always eligible, but production can also run for a pull request targeting `main`, and production does not depend on the test job. A push to `main` also makes both test and production eligible independently. citeturn110814view1

The target workflow should have a clear dependency graph: build/unit tests → deploy test → test-environment integration suite → production approval/promotion. The production job should use `needs` to depend on successful test deployment and integration checks, and its condition should restrict production to an approved event on `main`, not pull-request execution. Whether the final production release is automatic after main merge or requires an environment approval is an operational decision not established in source, so Codex should not invent organization-level approval policy; it should preserve compatibility with GitHub Environment protection if configured externally. citeturn110814view1turn144659view2

The current workflow also means documentation-only changes can deploy, and changes under Contracts can trigger package publication. Any implementation PR should therefore be treated as potentially side-effecting. Path exclusions can be added, but publication and deployment filters need to be designed separately: for example, a docs-only change should not deploy application resources, while a Contracts README or comment change should not accidentally publish a new NuGet unless publication semantics explicitly require it. citeturn110814view1

### Remove the production debug email route

`GET /email/{issuer}/invoices/{invoice}` is owner-designated debug-only and should not exist in production. It currently has Functions `Function` authorization but lacks an application-level biller-principal check, can enqueue notification, and returns 201 even in some paths where no recipient email is available. citeturn825261view2turn144659view2

Prefer compile/runtime registration control rather than relying only on secrecy of a Function key. The route can be guarded by a configuration option that defaults disabled and is enabled only in development/test, or removed entirely in favor of a test/admin mechanism. If retained for test, it should still validate authorization appropriate to the environment. Add a deployment integration assertion that the route is absent or returns an intentionally disabled response in production configuration. citeturn144659view2turn110814view1

## Delivery confirmation, cancellation, and data lifecycle

### Delivering and Delivered states

The present `Delivered` value represents enqueue-related completion, not actual recipient/provider delivery. The owner requirement is that queueing transitions the invoice to **Delivering**, and **Delivered** is set only after positive downstream confirmation. This work is explicitly deferred until Notification can send a callback/event. citeturn110814view0turn144659view2

Codex should avoid simply adding another timestamp and continue using implicit null/non-null semantics. Introduce an explicit lifecycle enum or equivalent persisted state, for example Draft/Issued/Delivering/Delivered/Cancelled, while retaining existing fields needed for backward compatibility. Existing records need a documented interpretation/migration strategy because Azure Table has no built-in schema migration/backfill mechanism in this project. citeturn738141view0

The confirmation protocol should carry the full invoice identity plus a stable notification/message ID, outcome, provider timestamp/reference and event ID. It must tolerate duplicate and out-of-order callbacks. The handler should use ETag-aware updates so an older confirmation cannot overwrite newer lifecycle state. This protocol is currently unspecified under U05, so the exact public contract must be owner-approved before implementation. citeturn144659view2

### Cancellation

The confirmed rule is simple: invoices may be cancelled only before any partial or full payment. No cancellation endpoint, command or state exists today. citeturn110814view0

Once explicit lifecycle state exists, add a `CancelInvoiceFlow` in Core and one carefully chosen external entry point—HTTP initially, MCP later—rather than mutating invoice state directly inside a Server function. The flow should load the current invoice, require `SettledCents == 0`, reject Delivered/Cancelled states, and conditionally persist the cancellation with ETag protection. Whether cancellation is allowed during Delivering is not explicitly decided; given the freeze-at-enqueue requirement, the safer implementation is to reject it until Notification cancellation semantics are explicitly designed. That last restriction is an inference and should be marked for owner confirmation. citeturn110814view0turn144659view2

### Retention, audit history, and recovery

The owner requires five-year retention and historical versions across updates and soft deletes, but there is no retention schedule, version-history store, purge mechanism, verified backup inventory, restore procedure or RPO/RTO. Azure PaaS backup/recovery is an expectation rather than verified evidence. citeturn110814view0turn110814view1

Implement audit/version history as append-only records rather than trying to reconstruct history from Azure Table service timestamps. Before each material mutation—edit, freeze/send, settlement adjustment, cancellation, and eventual deletion—persist a version snapshot keyed by stable invoice identity plus monotonically sortable version/timestamp. Store the actor/source and reason/event type separately from the invoice snapshot. This recommendation satisfies the stated “all versions across updates/soft deletes” requirement without depending on storage-provider recovery features. citeturn144659view2

Do not implement automated deletion until the owner decides when the five-year clock begins, what privacy/legal exceptions apply, and how purge interacts with audit history; those details are explicitly unresolved under U04. Infrastructure work should separately inventory Azure Storage protection settings, test a restore in the test environment, document recovery steps, and establish actual RPO/RTO before claiming recoverability. citeturn144659view2turn110814view1

## Deferred schema and behavior decisions

### Invoice-item key isolation

The current `InvoiceItem` partition key is derived from the invoice issue instant and its row key is the line ID; biller or management-group identity is not part of that key. Invoice IDs are only assumed unique per biller, not globally. The owner explicitly accepted this collision/isolation risk as deferred under Q16, and the repository warns against inventing a relational key redesign without a migration plan. citeturn738141view1turn144659view2

Codex should therefore **not** change InvoiceItem keys as incidental work in B01 or B03. When B05 is reopened, treat it as a schema-migration epic: define the target key shape, assess direct-table historical clients, support dual-read or migration, backfill existing rows, update ownership controls/SAS behavior, test rollback, and only then remove the old layout. A likely target is a partition containing biller plus invoice identity, but the exact representation needs compatibility analysis with Niobium’s key mapping and direct resource-token access. citeturn825261view4turn902528view1

### Particulars, Reference, payment instructions, and templates

`IssueInvoiceRequest.Particulars` and `Reference` are accepted but ignored during `InvoiceDomain.UpdateAsync`. `PaymentInstructions = null` currently overwrites the biller-provided default, and the stored `Template` field does not choose the HTML template. The repository deliberately places this under B09 because intended behavior must be decided before changing it. citeturn144659view2turn738141view0

The implementation decision should define three things before Codex edits code: whether request `Particulars`/`Reference` override biller defaults; whether `null` means “inherit default” while an explicit empty string means “clear”; and whether `Template` becomes a supported template selector or is removed/deprecated. Once decided, normalize this in one Core mapping method and add request-to-persisted-to-rendered tests rather than reproducing precedence rules in HTML and issuance separately. citeturn738141view1turn144659view2

### Public invoice-link expiry

Current saved invoice tokens are intentionally reused so previously emailed links remain usable, and tokenless public invoice viewing is rejected. There is no explicit expiry today; future expiry/revocation policy is deferred. citeturn144659view2turn110814view0

Do not add expiry opportunistically during authentication or lifecycle work. When the decision is reopened, preserve legacy saved tokens according to Q17, then add versioned token metadata so newly issued links can carry expiry/revocation semantics without invalidating historical links unexpectedly. citeturn144659view2

## MCP, payments, and reconciliation roadmap

MCP is clearly part of the product direction: AI agents are intended to become first-class consumers and the historical UI is planned for retirement. There is currently no MCP server, MCP route, tool contract or agent-authentication design, and the accepted unknowns explicitly say not to fabricate those contracts. citeturn110814view0turn144659view2

When reliability work is complete, MCP should initially be a thin adapter over Core flows rather than a second business-logic implementation. Candidate tools can eventually map to capabilities such as create/issue invoice, retrieve/render invoice, cancel invoice, list/query invoices, inspect balance/status, and later collect/reconcile payment. The exact names, JSON schemas and authentication model should be a separate design artifact because current HTTP identity relies on Niobium bearer/access/resource-token concepts designed around users/billers, while agent/API-key semantics remain undecided. citeturn825261view4turn902528view1

The existing Niobium identity stack issues access tokens using Sid/GroupSid, roles and entitlements, and resource tokens through registered controls. An MCP implementation should therefore either deliberately reuse that model with a service/agent principal or introduce a separately approved machine credential model; it should not silently expose the existing user token endpoint to autonomous agents as if that were the agreed architecture. citeturn902528view1

Payments/direct debit/reconciliation should likewise be a separate bounded capability. Today `Settled` is only an amount copied into the invoice; it is not a payment transaction or ledger, currency compatibility is not checked, manual bank-transfer overpayment is allowed, and there is no settlement endpoint. Future automated collection is required to charge exactly the remaining balance. citeturn738141view1turn110814view0

Before integrating a processor, add an explicit payment model with immutable payment attempts/transactions rather than continuing to mutate a single `Settled` value. It should distinguish manual settlements from processor charges/refunds, preserve processor IDs/idempotency keys, and calculate invoice settlement from those records. Because the product explicitly excludes accounting, the model should stop at invoice/payment reconciliation rather than grow into journals, ledgers, chart-of-accounts or financial statements. citeturn110814view0

The processor workflow should be idempotent by invoice/payment intent, calculate the current outstanding amount server-side immediately before charge, conditionally claim the payment attempt, and reconcile asynchronous provider events. Direct debit should not be implemented as merely another invoice-status field; it needs mandate/customer/payment-instrument lifecycle and secure credentials. The concrete provider, webhook contract, scheduling rules and credential ownership are still U05 decisions and therefore need owner approval before Codex can write stable public contracts. citeturn144659view2

## Codex handoff specification

For each implementation epic, Codex should begin by recording the branch, commit and clean/dirty state, reading `AGENTS.md` plus the linked task-specific docs, and confirming the exact Niobium NuGet versions in the affected project. Framework behavior should be checked against the pinned source revision documented in `docs/niobium-integration.md`; the framework should not be modified or upgraded unless that is separately authorized. citeturn825261view4turn902528view1

A strong Codex task for **B01** should say, in substance: modify `UpsertFlow`, `InvoiceDomain` and supporting Core code so an existing invoice is loaded before mutation; preserve persisted invoice/token/snapshot state on replay; use ETag-aware conditional updates instead of unconditional force upsert; permit edits only before freeze; reject changed requests after freeze; keep exact replay downstream message identity/content stable; and add race, stale-ETag, replay and partial-failure unit tests. Do not modify InvoiceItem storage keys. citeturn144659view2turn738141view0

A strong task for **B03** should require centralized Core validation shared by HTTP and queue issuance; due-date default preservation; supported-country/currency rules; invoice/item parent and currency consistency; overflow/range checks limited to already-approved policy; settlement currency validation while retaining allowed overpayment; and tests for invalid nested/item data. It should explicitly prohibit inventing credit/fractional-quantity/negative-value policy. citeturn110814view0turn144659view2

A strong task for **B02** should be assigned primarily against Niobium.Notification: implement an inbox/idempotency store keyed by existing `NotifyCommand.ID`/Service Bus `MessageId`; use concurrency-safe claim transitions; persist provider outcome; handle duplicate delivery and crash recovery explicitly; add duplicate/out-of-order/recovery tests; and retain Invoicing’s behavior of republishing the same exact notification on replay. citeturn738141view2turn144659view2

A strong task for **B07/B04** should add a separately runnable integration suite against isolated Azure test resources, then change the workflow so production depends on successful test deployment plus those checks, and ensure pull requests never deploy production. The debug `EmailInvoice` route should be disabled/absent in production configuration and covered by an environment-level test. citeturn110814view1turn825261view2

A strong task for **B06** should not start until the Notification confirmation contract is approved. It should introduce explicit lifecycle state, transition to Delivering at enqueue/freeze, process idempotent delivery-confirmation events into Delivered, reject invalid/out-of-order transitions, implement cancellation only with zero settlement, and add concurrency tests. Backward compatibility for existing records must be documented because no automatic migration system exists. citeturn110814view0turn738141view0

Every Codex PR should keep the existing verified build/test command style, preserve local formatting instead of repository-wide formatting, maintain warnings-as-errors behavior where already configured, and expand tests at the same layer as the behavior being changed. The present project conventions favor flows for orchestration, domains for transformations/save behavior, repositories for persistence, `Async`/`CancellationToken`, mutable public DTOs, and Niobium assembly scanning for `IFlow`/domain registration. citeturn144659view0

The completion bar for the reliability roadmap should be higher than “unit tests pass”: exact retries preserve persisted content, concurrent conflicting edits fail predictably, recipient duplicate email is prevented downstream, due-date and currency/item invariants are enforced across both HTTP and queue issuance, real Azure Table ETags are exercised, test-environment integration checks run before production, the debug email endpoint is unavailable in production, and deployment promotion has an actual test-to-production dependency. Those outcomes collectively close the highest-priority discrepancies the project has already documented. citeturn144659view2turn110814view1turn144659view0