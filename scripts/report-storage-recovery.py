"""Print only the known metadata fields in the deployed test storage receipt."""
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re


REPORT = Path("artifacts/deployed/test-storage-recovery.json")
STORAGE_SKUS = (
    "Standard_LRS", "Standard_ZRS", "Standard_GRS", "Standard_RAGRS",
    "Standard_GZRS", "Standard_RAGZRS", "Premium_LRS", "Premium_ZRS",
)
STORAGE_KINDS = ("Storage", "StorageV2", "BlobStorage", "BlockBlobStorage", "FileStorage")
VAULT_SCOPE = "data-protection-vaults-in-test-resource-group"
UNAVAILABLE_STAGES = ("test-storage-account", "blob-protection", "data-protection-backup-vaults")


def nonnegative_integer(value):
    return type(value) is int and 0 <= value <= 2**31 - 1


def select_fields(section, rules):
    if section is None:
        return None
    if not isinstance(section, dict):
        raise ValueError("Invalid evidence section")
    selected = {}
    for field, valid in rules.items():
        value = section[field]
        if not valid(value):
            raise ValueError("Invalid evidence field")
        selected[field] = value
    return selected


def verified_receipt(report, commit):
    if (not re.fullmatch(r"[a-f0-9]{40}", commit) or not isinstance(report, dict)
            or type(report.get("schema")) is not int or report["schema"] != 1
            or report.get("environment") != "test" or report.get("commit") != commit):
        raise ValueError("Expected schema 1 from the current test deployment")

    captured = datetime.fromisoformat(report["capturedAt"])
    unavailable = report["unavailable"]
    if captured.utcoffset() is None or not isinstance(unavailable, list) or any(
            stage not in UNAVAILABLE_STAGES for stage in unavailable):
        raise ValueError("Invalid test evidence metadata")

    def optional_flag(value):
        return value is None or type(value) is bool

    def optional_days(value):
        return value is None or nonnegative_integer(value)

    return {
        "schema": 1,
        "environment": "test",
        "commit": commit,
        "capturedAt": captured.astimezone(timezone.utc).isoformat(),
        "storage": select_fields(report["storage"], {
            "sku": lambda value: value is None or value in STORAGE_SKUS,
            "kind": lambda value: value is None or value in STORAGE_KINDS,
        }),
        "blobProtection": select_fields(report["blobProtection"], {
            "versioning": optional_flag,
            "changeFeed": optional_flag,
            "blobSoftDelete": optional_flag,
            "blobSoftDeleteDays": optional_days,
            "containerSoftDelete": optional_flag,
            "containerSoftDeleteDays": optional_days,
            "pointInTimeRestore": optional_flag,
            "pointInTimeRestoreDays": optional_days,
        }),
        "backupVaults": select_fields(report["backupVaults"], {
            "scope": lambda value: value == VAULT_SCOPE,
            "complete": lambda value: type(value) is bool,
            "visibleVaults": nonnegative_integer,
            "vaultsRead": nonnegative_integer,
            "matchingBlobInstances": nonnegative_integer,
            "matchingTableInstances": nonnegative_integer,
            "matchingOtherInstances": nonnegative_integer,
            "protectionConfigured": nonnegative_integer,
        }),
        "unavailable": unavailable,
    }


def main(path=REPORT):
    if os.environ.get("AZURE_ENV_NAME") != "test":
        raise ValueError("Storage recovery reporting is allowed only in the test environment")
    try:
        with path.open("rb") as stream:
            content = stream.read(8193)
        if len(content) > 8192:
            raise ValueError("Oversized storage recovery receipt")
        receipt = verified_receipt(json.loads(content), os.environ.get("GITHUB_SHA", ""))
    except (OSError, ValueError, TypeError, KeyError, OverflowError, RecursionError):
        print("Test storage recovery evidence unavailable or invalid.")
        return
    print("Storage recovery evidence " + json.dumps(receipt, sort_keys=True, separators=(",", ":")))


if __name__ == "__main__":
    main()
