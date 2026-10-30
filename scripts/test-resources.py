"""Prepare credentials for this repository's test deployment without printing their values."""
import json
import os
from pathlib import Path
import subprocess
import sys
import secrets


def export(name, value, secret=False):
    if not value or "\n" in value or "\r" in value:
        raise ValueError(f"Missing or invalid {name}")
    if secret:
        print(f"::add-mask::{value}", flush=True)
    with open(os.environ["GITHUB_ENV"], "a", encoding="utf-8") as stream:
        stream.write(f"{name}={value}\n")


def azure(*args):
    return subprocess.check_output(["az", *args, "--only-show-errors", "--output", "tsv"], text=True).strip()


def main():
    if os.environ.get("AZURE_ENV_NAME") != "test":
        raise ValueError("Integration fixtures are allowed only in the test environment")
    prefix = os.environ["APP_SHORT_NAME"]
    abbreviations = json.loads(Path("infra/abbreviations.json").read_text())
    function = f"{prefix}-{abbreviations['webSitesFunctions']}test"
    storage = f"{prefix}-{abbreviations['storageStorageAccounts']}dtest".replace("-", "")
    bus = f"{prefix}-{abbreviations['serviceBusNamespaces']}test"
    vault = f"{prefix}-{abbreviations['keyVaultVaults']}test"
    if sys.argv[1] == "configure":
        # The test host emits to an isolated sink in its own namespace. No Notification provider consumes it.
        export("APPSETTING_NOTIFICATIONQUEUEOPTIONS__FULLYQUALIFIEDNAMESPACE", bus + ".servicebus.windows.net")
        export("APPSETTING_KV_IDENTITYSERVICEOPTIONS__ACCESSTOKENSECRET", secrets.token_urlsafe(48), secret=True)
        return
    if sys.argv[1] != "credentials":
        raise ValueError("Expected configure or credentials")
    group = os.environ["AZURE_RESOURCE_GROUP"]
    # Read the ARM properties explicitly; az functionapp's model projection can omit defaultHostName.
    resource = ("resource", "show", "--resource-group", group, "--name", function,
                "--resource-type", "Microsoft.Web/sites", "--api-version", "2024-11-01")
    hostname = azure(*resource, "--query", "properties.defaultHostName")
    if not hostname.endswith(".azurewebsites.net") or not hostname.startswith(function + "."):
        raise ValueError(f"Unexpected test function hostname: {hostname!r}")
    resource_id = azure(*resource, "--query", "id")
    if not resource_id.lower().endswith("/providers/microsoft.web/sites/" + function.lower()):
        raise ValueError("Unexpected test function resource ID")
    # Provision writes the new key after settings are applied. Refresh the versionless reference
    # explicitly so authentication checks do not observe the previous deployment's cached key.
    azure("rest", "--method", "post", "--url",
          f"https://management.azure.com{resource_id}/config/configreferences/appsettings/refresh?api-version=2022-03-01")
    export("TEST_ENVIRONMENT", "test")
    export("TEST_API_URL", "https://" + hostname)
    export("TEST_STORAGE_ACCOUNT", storage)
    export("TEST_STORAGE_KEY", azure("storage", "account", "keys", "list", "--resource-group", group, "--account-name", storage, "--query", "[0].value"), secret=True)
    export("TEST_SERVICEBUS_CONNECTION", azure("servicebus", "namespace", "authorization-rule", "keys", "list", "--resource-group", group, "--namespace-name", bus, "--name", "RootManageSharedAccessKey", "--query", "primaryConnectionString"), secret=True)
    export("TEST_SIGNING_KEY", azure("keyvault", "secret", "show", "--vault-name", vault, "--name", "IDENTITYSERVICEOPTIONS--ACCESSTOKENSECRET", "--query", "value"), secret=True)
    export("TEST_EXPECTED_COMMIT", os.environ["GITHUB_SHA"])


if __name__ == "__main__":
    main()
