# GitHub environment configuration

This is the fixture-setting reference for agents, DevOps and developer onboarding. The owner reconfirmed the existing test biller and approved recipient on 2026-11-06 (Pacific/Auckland). Reuse these configured identities; do not request a new biller or mailbox because a dependent workflow is missing its configuration binding. Values, recipient addresses and tokens must remain outside source, logs and review comments.

## Existing Invoicing settings

These names belong to GitHub environments in **`niobium-nz/Niobium.Invoicing`**. The same names in another repository or environment are separate settings. The inventory records the configuration contract and owner confirmation, not a disclosure or independent retrieval of secret values.

| Environment | Stored GitHub name | Kind | Purpose and consumer |
| --- | --- | --- | --- |
| `test` | `TEST_INVOICE_BILLER` | Variable | Existing valid test biller GUID, authorized to issue invoices and trigger test notifications. Candidate and trusted deployed suites use it to select the reference invoice's biller. |
| `test` | `TEST_INVOICE_ID` | Variable | Existing retained reference invoice ID belonging to that biller. The deployed suite reads the pair before creating its own temporary case records. |
| `test` | `TEST_NOTIFICATION_RECIPIENT` | Secret | Existing owner-approved mailbox for real test notifications. Both deployed suites receive it directly as a runner input; never substitute a template's fallback recipient. |
| `prod` | `TEST_INVOICE_BILLER` | Variable | Biller of the designated existing production test invoice. This is production's own fixture, not the test environment's biller. |
| `prod` | `TEST_INVOICE_ID` | Variable | ID of that existing production test invoice. Production tests never create it. |
| `prod` | `TEST_INVOICE_TOKEN` | Secret | Saved public invoice-link token for that exact production biller/invoice pair. Required for the positive invoice HTML read in production verification. |

The [deployment workflow](../.github/workflows/azure-deploy.yml) explicitly projects these settings into its test or production steps. A stored GitHub setting does not automatically become a runner environment variable or a Function App setting.

The existing [deployed test fixture](../src/Niobium.Invoicing.Deployment.Tests/DeployedInvoiceTests.cs), `EnsureReferenceInvoice`, consumes `TEST_INVOICE_BILLER` and `TEST_INVOICE_ID` together. Partial configuration fails. Its previously authorized fallback can retain a reference invoice when both are absent, but that is not a reason to replace the owner's already configured fixture. Temporary case records remain subject to ownership-checked cleanup.

## Invoice terminology and dependency mapping

Use **biller** for the invoice issuer, including configuration and tests that interact with Notification. For new invoice-related harness inputs, the name is `TEST_BILLER_ID`. Preserve the existing stored GitHub variable `TEST_INVOICE_BILLER`; map it explicitly rather than requiring a second variable or renaming the existing configuration:

```yaml
# Job: environment: test
# Test-step environment inputs:
env:
  TEST_BILLER_ID: ${{ vars.TEST_INVOICE_BILLER }}
  TEST_NOTIFICATION_RECIPIENT: ${{ secrets.TEST_NOTIFICATION_RECIPIENT }}
```

This is the Notification harness input mapping. Invoicing's existing deployed suite still consumes `TEST_INVOICE_BILLER`; no runtime or wire contract is renamed by this naming rule. Notification's established `Tenant` field and source `Invoice` template partition represent the Invoicing biller at that boundary. Invoicing's management-group tenant is a different identity. A uniquely owned Notification fixture may use its own partition; it must retain an explicit mapping to the source biller rather than changing shared template records.

GitHub environment settings are **repository scoped**. A job using `environment: test` in `Niobium.Notification` reads Notification's test settings, not Invoicing's. To use the same approved biller and recipient there, bind the same approved values under `TEST_INVOICE_BILLER` and secret `TEST_NOTIFICATION_RECIPIENT` in that repository's `test` environment. This is configuration reuse, not authorization for a new recipient or new infrastructure. Do not assume that an identically named environment or a cross-repository reusable workflow automatically shares environment secrets. The current connector cannot inspect or manage these environment settings; a missing binding may require an administrator, but it does not reopen the choice of biller or mailbox.

## Why the production token is still needed

The production workflow maps the stored fixture into the read-only runner:

```yaml
READONLY_INVOICE_BILLER_ID: ${{ vars.TEST_INVOICE_BILLER }}
READONLY_INVOICE_ID: ${{ vars.TEST_INVOICE_ID }}
READONLY_INVOICE_TOKEN: ${{ secrets.TEST_INVOICE_TOKEN }}
```

It then runs `python scripts/read-only-deployment.py --require-fixture --report artifacts/production-read-only.json`. The [client](../scripts/read-only-deployment.py) requires all three values. It performs five GET-only checks: deployed merge SHA, a missing invoice, a missing link token, successful HTML for the designated existing invoice, and rejection of a wrong token for that invoice. The two negative missing-invoice checks use a synthetic absent ID; they do not seed a record.

`TEST_INVOICE_TOKEN` authenticates the successful existing-invoice read. It is not a Functions key, bearer token, storage credential or `BillingOptions:InvoiceTokenSecretSalt`. It must match the token already saved on the designated production invoice. Removing it makes required production verification fail; it cannot be replaced with an arbitrary value. The test reports no token or invoice body.

Keep this secret configured. If the fixture needs maintenance, establish the correct existing production biller/invoice/token pair through the authorized administrative process; do not generate or rotate a production token, seed an invoice, send email or invoke a debug endpoint to make a test pass. Production verification has no write or cleanup path. In test, the read-only client receives a temporary case's saved token from the deployed suite; it does not need production's secret.

## Runner inputs versus service configuration

The fixture settings above are test-runner inputs. They are not `APPSETTING_` values and are not passed to Bicep, Key Vault or the Function App by the current workflow. Keep them separate from runtime credentials such as invoice-token derivation, storage SAS signing and provider API settings.

For service configuration, preserve the [infrastructure composition](delivery-and-operations.md#provisioning-and-settings-flow): the workflow explicitly projects GitHub inputs, `infra/scripts` builds the generic `appSettings` parameter, `infra/main.bicep` owns application-specific resources, and `infra/function-app.bicep` owns host-bound resources and unions internal settings with the caller's settings. Ordinary `APPSETTING_` values follow the generic parameter path; `APPSETTING_KV_` values are written directly from the runner into the existing vault with only names/placeholders entering Bicep. Environment suffixes and input values select resources. Do not add test-only secret resources or application-specific parameters to the reusable host module.

See the [runtime configuration catalog](configuration-and-development.md#runtime-configuration-catalog) for service settings. The fixture inventory above is not a complete inventory of Azure identity, deployment or application secrets.

## Onboarding and troubleshooting

1. Identify the repository, GitHub environment, workflow job and exact commit being run. Check its explicit `vars`/`secrets` projection before asking for another setting.
2. Use the stored names and kinds in the inventory. Keep the approved recipient in a secret, not a plain variable, documentation or a fallback template. A secret's value cannot be read back through GitHub's settings API.
3. For a dependent repository, verify its own binding to the same approved biller and recipient. An empty Notification input does not show that Invoicing's configured value is missing or invalid. The Notification preflight checks configuration without resource access; its actual deployed cases must still verify the biller's existing `Invoice` template and real provider path.
4. Keep environment fixtures separate. In production, check that the biller, invoice ID and saved token refer to the same existing invoice; do not copy test fixture values into production.
5. Verify the latest run's actual deployment, candidate/trusted suites and read-only report. A local pass, an old green run, a skipped suite or configuration confirmation is not current deployment evidence. Use names and sanitized error codes in diagnostics, never credential or mailbox values.

Update this inventory and [agent memory](../AGENTS.md) when a setting's owner, consumer or scope changes. Record verification in the normal PR and delivery checkpoint; retain the [delivery process](delivery-and-operations.md) for documentation changes too.
