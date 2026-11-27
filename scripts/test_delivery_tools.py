import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).parent / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


coverage = load("coverage_gate", "check-coverage.py")
resources = load("resources", "test-resources.py")
storage_recovery = load("storage_recovery", "report-storage-recovery.py")


class CoverageGateTests(unittest.TestCase):
    def report(self, directory, filename, hits, names=None):
        packages = []
        for name in names or sorted(coverage.PROJECTS):
            lines = "".join(f'<line number="{index + 1}" hits="{count}" />' for index, count in enumerate(hits))
            packages.append(f'<package name="{name}"><classes><class filename="/runner/src/{name}/Flow.cs"><lines>{lines}</lines></class>'
                            f'<class filename="/runner/src/{name}/obj/Generated.cs"><lines><line number="1" hits="0" /></lines></class></classes></package>')
        path = Path(directory) / filename
        path.write_text('<coverage><packages>' + ''.join(packages) + '</packages></coverage>')
        return path

    def test_union_complementary_suites_without_double_counting_or_generated_sources(self):
        with tempfile.TemporaryDirectory() as directory, contextlib.redirect_stdout(io.StringIO()):
            first = self.report(directory, "unit.xml", [1, 0])
            second = self.report(directory, "integration.xml", [0, 1])
            result = coverage.collect([first, second, first])
            self.assertTrue(all(len(lines) == 2 and all(lines.values()) for lines in result.values()))
            self.assertEqual(coverage.main([first, second]), 0)

    def test_missing_module_or_below_threshold_fails(self):
        with tempfile.TemporaryDirectory() as directory, contextlib.redirect_stdout(io.StringIO()):
            low = self.report(directory, "low.xml", [1, 0])
            missing = self.report(directory, "missing.xml", [1], ["Niobium.Invoicing.Core"])
            self.assertEqual(coverage.main([low]), 1)
            self.assertEqual(coverage.main([missing]), 1)
            self.assertEqual(coverage.main([]), 1)


class ResourceBoundaryTests(unittest.TestCase):
    def test_non_test_environments_fail_before_any_cloud_call(self):
        for name in ["prod", "dev", ""]:
            with patch.dict(os.environ, {"AZURE_ENV_NAME": name}), patch.object(resources, "azure") as azure:
                with self.assertRaisesRegex(ValueError, "only in the test environment"):
                    resources.main()
                azure.assert_not_called()

    def test_configuration_preserves_existing_keys_through_generic_settings(self):
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / "env"
            with patch.dict(os.environ, {"AZURE_ENV_NAME": "test", "APP_SHORT_NAME": "niobiuminvoicing",
                                        "AZURE_RESOURCE_GROUP": "synthetic-test", "GITHUB_ENV": str(target)}), \
                    patch("sys.argv", ["test-resources.py", "configure"]), \
                    patch.object(resources, "azure", side_effect=["existing-test-signing-key", "existing-test-storage-key"]) as azure, \
                    contextlib.redirect_stdout(io.StringIO()) as log:
                resources.main()
                values = dict(line.split("=", 1) for line in target.read_text().splitlines())
                self.assertEqual(values, {
                    "APPSETTING_KV_IDENTITYSERVICEOPTIONS__ACCESSTOKENSECRET": "existing-test-signing-key",
                    "APPSETTING_KV_STORAGETABLEOPTIONS__KEY": "existing-test-storage-key",
                })
                self.assertEqual(log.getvalue().splitlines(), [
                    "::add-mask::existing-test-signing-key", "::add-mask::existing-test-storage-key"])
                self.assertEqual(azure.call_count, 2)
                self.assertEqual(azure.call_args_list[0].args[:3], ("keyvault", "secret", "show"))
                self.assertEqual(azure.call_args_list[1].args, (
                    "storage", "account", "keys", "list", "--resource-group", "synthetic-test",
                    "--account-name", "niobiuminvoicingstdtest", "--query", "[0].value"))

    def test_configuration_rejects_missing_runtime_storage_key(self):
        with tempfile.TemporaryDirectory() as directory:
            with patch.dict(os.environ, {"AZURE_ENV_NAME": "test", "APP_SHORT_NAME": "niobiuminvoicing",
                                        "AZURE_RESOURCE_GROUP": "synthetic-test", "GITHUB_ENV": str(Path(directory) / "env")}), \
                    patch("sys.argv", ["test-resources.py", "configure"]), \
                    patch.object(resources, "azure", side_effect=["existing-test-signing-key", ""]), \
                    contextlib.redirect_stdout(io.StringIO()):
                with self.assertRaisesRegex(ValueError, "APPSETTING_KV_STORAGETABLEOPTIONS__KEY"):
                    resources.main()

    def test_exports_reject_missing_values_and_newline_injection(self):
        for value in ["", "a\nb", "a\rb"]:
            with self.assertRaises(ValueError):
                resources.export("SETTING", value)

    def test_credentials_read_existing_test_resources_without_cloud_mutations(self):
        with patch.dict(os.environ, {"AZURE_ENV_NAME": "test", "APP_SHORT_NAME": "niobiuminvoicing", "AZURE_RESOURCE_GROUP": "synthetic-test", "GITHUB_SHA": "a" * 40}), \
                patch("sys.argv", ["test-resources.py", "credentials"]), patch.object(resources, "export") as export, \
                patch.object(resources, "azure", side_effect=["niobiuminvoicing-func-test.azurewebsites.net",
                    "storage-key", "bus-connection", "signing-key"]) as azure:
            resources.main()
            self.assertEqual(azure.call_args_list[0].args[-1], "properties.defaultHostName")
            self.assertEqual(azure.call_count, 4)
            for call in azure.call_args_list:
                self.assertFalse({"create", "set", "delete", "post", "put", "patch"}.intersection(call.args))
            export.assert_any_call("TEST_EXPECTED_COMMIT", "a" * 40)
            export.assert_any_call("TEST_SIGNING_KEY", "signing-key", secret=True)

    def test_unexpected_function_hostname_stops_before_credentials_are_read(self):
        with patch.dict(os.environ, {"AZURE_ENV_NAME": "test", "APP_SHORT_NAME": "niobiuminvoicing", "AZURE_RESOURCE_GROUP": "synthetic-test"}), \
                patch("sys.argv", ["test-resources.py", "credentials"]), patch.object(resources, "azure", return_value="unrelated.azurewebsites.net") as azure:
            with self.assertRaisesRegex(ValueError, "Unexpected test function hostname"):
                resources.main()
            self.assertEqual(azure.call_count, 1)


class StorageRecoveryEvidenceTests(unittest.TestCase):
    commit = "7" * 40

    def report(self):
        return {
            "schema": 1, "environment": "test", "commit": self.commit,
            "capturedAt": "2026-11-27T12:30:00.1234567+00:00",
            "storage": {"sku": "Standard_LRS", "kind": "StorageV2", "accountId": "private-account"},
            "blobProtection": {
                "versioning": False, "changeFeed": None, "blobSoftDelete": True,
                "blobSoftDeleteDays": 7, "containerSoftDelete": False, "containerSoftDeleteDays": None,
                "pointInTimeRestore": False, "pointInTimeRestoreDays": None,
            },
            "backupVaults": {
                "scope": storage_recovery.VAULT_SCOPE, "complete": False, "visibleVaults": 1,
                "vaultsRead": 0, "matchingBlobInstances": 0, "matchingTableInstances": 0,
                "matchingOtherInstances": 0, "protectionConfigured": 0, "vaultId": "private-vault",
            },
            "unavailable": [],
            "opaqueData": "private-detail",
        }

    def test_prints_only_allowlisted_metadata_from_the_current_test_deployment(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "test-storage-recovery.json"
            path.write_text(json.dumps(self.report()), encoding="utf-8")
            with patch.dict(os.environ, {"AZURE_ENV_NAME": "test", "GITHUB_SHA": self.commit}), \
                    contextlib.redirect_stdout(io.StringIO()) as log:
                storage_recovery.main(path)
        line = log.getvalue().strip()
        self.assertTrue(line.startswith("Storage recovery evidence "))
        self.assertNotIn("private-", line)
        result = json.loads(line.removeprefix("Storage recovery evidence "))
        self.assertEqual(result["storage"], {"sku": "Standard_LRS", "kind": "StorageV2"})
        self.assertEqual(result["blobProtection"]["blobSoftDeleteDays"], 7)
        self.assertFalse(result["backupVaults"]["complete"])

    def test_retains_unavailable_sections_as_unknown(self):
        report = self.report()
        report.update(storage=None, blobProtection=None, backupVaults=None,
                      unavailable=["test-storage-account"])
        result = storage_recovery.verified_receipt(report, self.commit)
        self.assertIsNone(result["storage"])
        self.assertEqual(result["unavailable"], ["test-storage-account"])

    def test_mismatched_and_malformed_reports_emit_only_a_fixed_status(self):
        wrong_commit = self.report()
        wrong_commit["commit"] = "8" * 40
        untrusted_value = self.report()
        untrusted_value["storage"]["sku"] = "::notice::do not echo this value"
        invalid_count = self.report()
        invalid_count["backupVaults"]["matchingTableInstances"] = True
        with tempfile.TemporaryDirectory() as directory, \
                patch.dict(os.environ, {"AZURE_ENV_NAME": "test", "GITHUB_SHA": self.commit}):
            path = Path(directory) / "test-storage-recovery.json"
            for report in (wrong_commit, untrusted_value, invalid_count):
                with self.subTest(report=report), contextlib.redirect_stdout(io.StringIO()) as log:
                    path.write_text(json.dumps(report), encoding="utf-8")
                    storage_recovery.main(path)
                    self.assertEqual(log.getvalue(), "Test storage recovery evidence unavailable or invalid.\n")

    def test_rejects_non_test_runs_before_opening_the_report(self):
        with patch.dict(os.environ, {"AZURE_ENV_NAME": "prod"}), patch.object(Path, "open") as opening:
            with self.assertRaisesRegex(ValueError, "only in the test environment"):
                storage_recovery.main()
            opening.assert_not_called()


if __name__ == "__main__":
    unittest.main()
