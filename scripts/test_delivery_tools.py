import contextlib
import importlib.util
import io
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

    def test_configuration_uses_own_sink_and_a_masked_ephemeral_signing_key(self):
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / "env"
            with patch.dict(os.environ, {"AZURE_ENV_NAME": "test", "APP_SHORT_NAME": "niobiuminvoicing", "GITHUB_ENV": str(target)}), \
                    patch("sys.argv", ["test-resources.py", "configure"]), patch.object(resources, "azure") as azure, contextlib.redirect_stdout(io.StringIO()) as log:
                resources.main()
                values = dict(line.split("=", 1) for line in target.read_text().splitlines())
                self.assertEqual(values["APPSETTING_NOTIFICATIONQUEUEOPTIONS__FULLYQUALIFIEDNAMESPACE"], "niobiuminvoicing-sb-test.servicebus.windows.net")
                key = values["APPSETTING_KV_IDENTITYSERVICEOPTIONS__ACCESSTOKENSECRET"]
                self.assertGreaterEqual(len(key), 64)
                self.assertEqual(log.getvalue().strip(), "::add-mask::" + key)
                azure.assert_not_called()

    def test_exports_reject_missing_values_and_newline_injection(self):
        for value in ["", "a\nb", "a\rb"]:
            with self.assertRaises(ValueError):
                resources.export("SETTING", value)


if __name__ == "__main__":
    unittest.main()
