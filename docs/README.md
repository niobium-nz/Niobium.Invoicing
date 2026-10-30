# Documentation index

These documents describe implementation and owner-confirmed product direction. Planned requirements are not implementation guarantees.

## Baseline and evidence language

Analyzed baseline: `niobium-nz/Niobium.Invoicing`, commit **`f403f50aa6358e9c56dd5561d9f6073d7a73a1eb`**, branch `work`, working directory `/workspace/Niobium.Invoicing`. The working tree was clean before documentation work. Scope was approved on **2026-10-29, Pacific/Auckland**. This records approval, not original architectural-decision dates.

Framework sources are pinned by consumed package version in [Niobium integration](niobium-integration.md). The [delivery follow-up](implementation-progress.md#feature-delivery-policy-follow-up-2026-10-30) adds live test deployment verification. GitHub environment protection and external service revisions remain unverified; consult the exact CI run for deployed-test results.

The authorized security follow-up on **2026-10-30, Pacific/Auckland** starts from `910fea08ffd2d608e9d9534c4607a2aeaa9c9822` and updates resource grants, stored-link compatibility and HTML rendering. Current behavior pages include those changes; the original discovery ledger/validation remain historical records. See [Q15-Q18](decisions-and-risks.md#owner-decisions) and the [follow-up validation](discovery/validation.md#security-follow-up-2026-10-30).

| Status | Meaning |
| --- | --- |
| Observed | Supported by inspected source/configuration. Default for implementation descriptions. |
| Verified | A named safe command/test ran; its result and limits are recorded. |
| Owner-confirmed | Intent/context established by an answer linked to a Q decision ID. |
| Inferred | Plausible interpretation, not a verified fact or confirmed requirement. |
| Proposed | Recommendation/future work, not existing behavior. |
| Unknown | Evidence/decision missing; accepted deferrals remain unknown. |

Source links are relative and name symbols where useful. They follow the checkout; the central commit identifies the analyzed version. Framework links are commit-pinned. Tests support only what they actually assert.

The research implementation starts from main `e2f7269676359d85cc7e4308d4babfd1f6f0b39c`. Its [acceptance ledger](implementation-progress.md), [billing rules](billing-validation.md), [recovery protocol](invoice-recovery.md) and [local integration guide](integration-tests.md) record current changes and limits. Discovery records above remain historical evidence.

## Reading map and maintenance

| Page | Use it for | Update when |
| --- | --- | --- |
| [Product](product.md) | Actors, journeys, scope, capabilities, requirements | Owner decisions, markets or scope change |
| [Architecture](architecture.md) | Boundaries, DI, flows, partial failures | Projects, registrations or dependencies change |
| [Niobium integration](niobium-integration.md) | Package provenance and inherited contracts | Relevant package/resolved graph changes |
| [Domain and data](domain-and-data.md) | Fields, money, IDs, keys, persistence | Models, calculation, mapping or retention changes |
| [API and messaging](api-and-messaging.md) | Routes, authorization, wire contracts, retries | Triggers, middleware, DTOs or messages change |
| [Security](security.md) | Controls, assumptions, data handling | Identity, permissions or rendering changes |
| [Configuration and development](configuration-and-development.md) | Settings and safe commands | SDK, options, setup or tooling changes |
| [Testing and conventions](testing-and-conventions.md) | Evidence and change recipes | Tests, warnings or compatibility policy change |
| [Delivery and operations](delivery-and-operations.md) | Workflows, Azure declarations, operations | Hooks/resources/operating practices change |
| [Decisions and risks](decisions-and-risks.md) | Confirmed decisions and planned work | Requirements clarified or backlog implemented |
| [Discovery coverage](discovery/coverage.md) | File/component and symbol ledger | Source scope or exclusions change |
| [Validation](discovery/validation.md) | Commands, outcomes, limits, walkthrough | Documentation/behavior changes are validated |

Start with [AGENTS.md](../AGENTS.md) for repository work. Consult linked pages explicitly; an agent may not automatically load them. Keep one authoritative description per topic. Never duplicate secrets or private security findings in this public set.
