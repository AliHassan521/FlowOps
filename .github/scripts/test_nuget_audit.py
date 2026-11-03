import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


class AuditGateTests(unittest.TestCase):
    def run_gate(self, report):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "report.json"
            path.write_text(json.dumps(report), encoding="utf-8")
            return subprocess.run([sys.executable, str(Path(__file__).with_name("check-nuget-audit.py")), str(path)], capture_output=True).returncode

    def report(self, **framework):
        return {"version": 1, "sources": ["https://data.nuget.org/v3/index.json"], "projects": [{"frameworks": [{"framework": "net10.0", **framework}]}]}

    def test_clean_report_passes(self):
        self.assertEqual(self.run_gate(self.report(topLevelPackages=[])), 0)

    def test_direct_and_transitive_findings_fail(self):
        for field in ["topLevelPackages", "transitivePackages"]:
            with self.subTest(field=field):
                self.assertNotEqual(self.run_gate(self.report(**{field: [{"id": "UnsafeFixture", "vulnerabilities": [{"severity": "High"}]}]})), 0)

    def test_source_warning_fails_instead_of_looking_clean(self):
        report = self.report()
        report["problems"] = [{"level": "Warning", "text": "Audit feed unavailable"}]
        self.assertNotEqual(self.run_gate(report), 0)

    def test_missing_and_incomplete_reports_fail(self):
        for report in [{}, {"version": 1, "projects": []}, {"version": 1, "sources": ["feed"], "projects": [{}]}]:
            with self.subTest(report=report):
                self.assertNotEqual(self.run_gate(report), 0)


if __name__ == "__main__":
    unittest.main()
