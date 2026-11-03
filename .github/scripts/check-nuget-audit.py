"""Fail CI on vulnerability findings or incomplete NuGet audit output."""
import json
import sys

with open(sys.argv[1], encoding="utf-8-sig") as source:
    report = json.load(source)
if report.get("version") != 1 or not isinstance(report.get("projects"), list) or not report["projects"]:
    sys.exit("NuGet audit did not return a complete project report.")
if report.get("problems") or not report.get("sources") or any(not project.get("frameworks") for project in report["projects"]):
    sys.exit("NuGet audit source reported a problem or an incomplete framework report.")
findings = []
for project in report["projects"]:
    for framework in project.get("frameworks", []):
        for package in framework.get("topLevelPackages", []) + framework.get("transitivePackages", []):
            if package.get("vulnerabilities"):
                findings.append(package["id"])
if findings:
    sys.exit("Vulnerable packages: " + ", ".join(sorted(set(findings))))
print("No known direct or transitive NuGet vulnerabilities reported.")
