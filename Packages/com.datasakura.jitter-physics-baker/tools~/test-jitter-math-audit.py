#!/usr/bin/env python3
"""Unit tests for the JMP-P00 executable audit prototype."""

from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).with_name("audit-jitter-math.py")
SPEC = importlib.util.spec_from_file_location("jitter_math_audit", SCRIPT)
AUDIT = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
sys.modules[SPEC.name] = AUDIT
SPEC.loader.exec_module(AUDIT)


class MaskTests(unittest.TestCase):
    def test_comments_and_strings_remain_raw_but_not_code(self) -> None:
        source = '''
// double MathF PhysicsVector3
const string Text = "Math.Sqrt and PhysicsQuaternion";
private double Value = Math.Sqrt(4d);
'''
        masked, regions = AUDIT.mask_csharp(source)
        self.assertNotIn("MathF", masked)
        self.assertNotIn("PhysicsQuaternion", masked)
        self.assertIn("double Value", masked)
        comment_offset = source.index("MathF")
        string_offset = source.index("Math.Sqrt")
        self.assertEqual("comment", AUDIT.region_at(regions, comment_offset))
        self.assertEqual("string", AUDIT.region_at(regions, string_offset))

    def test_verbatim_raw_and_char_literals_are_masked(self) -> None:
        source = "const string A = @\"double \"\"MathF\"\"\";\nconst string B = \"\"\"PhysicsVector3\"\"\";\nchar C = 'f';\n"
        masked, _ = AUDIT.mask_csharp(source)
        self.assertNotIn("double", masked)
        self.assertNotIn("PhysicsVector3", masked)
        self.assertNotIn("'f'", masked)

    def test_unterminated_construct_fails(self) -> None:
        with self.assertRaises(ValueError):
            AUDIT.mask_csharp('var value = "unterminated;')


class ScannerTests(unittest.TestCase):
    def create_repo(self, source: str) -> tuple[Path, dict]:
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        root = Path(temp.name)
        marker = root / "Packages/com.datasakura.jitter-physics-baker/package.json"
        marker.parent.mkdir(parents=True)
        marker.write_text("{}\n", encoding="utf-8")
        source_path = root / "Packages/com.datasakura.jitter-physics-baker/Runtime/Contracts/Test.cs"
        source_path.parent.mkdir(parents=True)
        source_path.write_text(source, encoding="utf-8")
        policy = {
            "schemaVersion": 2,
            "repositoryRootMarker": "Packages/com.datasakura.jitter-physics-baker/package.json",
            "ownedRoots": ["Packages/com.datasakura.jitter-physics-baker/Runtime/Contracts"],
            "vendorRoots": ["Packages/com.datasakura.jitter-physics-baker/Jitter2~"],
            "excludedRoots": ["bin", "obj"],
            "baselineFindingsHash": "",
            "allowlist": [],
        }
        return root, policy

    def test_forbidden_contract_math_is_classified_as_debt(self) -> None:
        root, policy = self.create_repo(
            "namespace Demo { public static class Test { public static double Run(float value) => Math.Sqrt(value); } }\n"
        )
        report = AUDIT.build_report(root, policy, "snapshot")
        rules = {finding["ruleId"] for finding in report["findings"]}
        self.assertTrue({"JMP006", "JMP007", "JMP008"}.issubset(rules))
        self.assertEqual(0, report["ambiguousCount"])
        self.assertGreater(report["migrationDebtCount"], 0)

    def test_new_finding_changes_reviewed_hash(self) -> None:
        root, policy = self.create_repo("namespace Demo { public static class Test { } }\n")
        baseline = AUDIT.build_report(root, policy, "snapshot")
        policy["baselineFindingsHash"] = baseline["findingsHash"]
        reviewed = AUDIT.build_report(root, policy, "inventory")
        self.assertTrue(reviewed["baselineReviewed"])
        source = root / policy["ownedRoots"][0] / "Test.cs"
        source.write_text("namespace Demo { public static class Test { private double Added; } }\n", encoding="utf-8")
        changed = AUDIT.build_report(root, policy, "inventory")
        self.assertFalse(changed["baselineReviewed"])
        self.assertGreater(changed["unclassifiedCount"], 0)

    def test_reports_are_deterministic(self) -> None:
        root, policy = self.create_repo("namespace Demo { public struct Test { public float Value; } }\n")
        first = AUDIT.build_report(root, policy, "snapshot")
        second = AUDIT.build_report(root, policy, "snapshot")
        self.assertEqual(first, second)

    def test_review_hash_changes_when_classification_changes(self) -> None:
        root, policy = self.create_repo(
            "namespace Demo { public static class Test { public static float Value; } }\n"
        )
        report = AUDIT.build_report(root, policy, "snapshot")
        changed = [dict(finding) for finding in report["findings"]]
        changed[0]["reason"] = "different reviewed reason"
        self.assertNotEqual(
            AUDIT.findings_hash(report["findings"]),
            AUDIT.findings_hash(changed),
        )

    def test_policy_rejects_absolute_or_parent_paths(self) -> None:
        _, policy = self.create_repo("namespace Demo { }\n")
        policy["ownedRoots"] = ["../escape"]
        with self.assertRaises(ValueError):
            AUDIT.validate_policy(policy)

    def test_policy_rejects_unknown_fields(self) -> None:
        _, policy = self.create_repo("namespace Demo { }\n")
        policy["silentExpansion"] = True
        with self.assertRaisesRegex(ValueError, "unknown fields"):
            AUDIT.validate_policy(policy)

    def test_allowlist_requires_owner_reason_and_known_rule(self) -> None:
        _, policy = self.create_repo("namespace Demo { }\n")
        policy["allowlist"] = [{
            "id": "bad",
            "path": policy["ownedRoots"][0],
            "recursive": True,
            "ruleIds": ["JMP999"],
            "owner": "",
            "reason": "",
        }]
        with self.assertRaises(ValueError):
            AUDIT.validate_policy(policy)

    def test_allowed_finding_records_entry_and_unused_entry_is_stale(self) -> None:
        root, policy = self.create_repo(
            "namespace Demo { public struct Test { public float Value; } }\n"
        )
        policy["allowlist"] = [{
            "id": "contract-f32",
            "path": policy["ownedRoots"][0],
            "recursive": True,
            "ruleIds": ["JMP007"],
            "owner": "test",
            "reason": "explicit fixture",
        }]
        report = AUDIT.build_report(root, policy, "snapshot")
        self.assertEqual(0, report["migrationDebtCount"])
        self.assertEqual([], report["staleAllowlistEntries"])
        self.assertEqual("contract-f32", report["findings"][0]["allowlistEntryId"])

        policy["allowlist"][0]["ruleIds"] = ["JMP008"]
        stale = AUDIT.build_report(root, policy, "snapshot")
        self.assertEqual(["contract-f32"], stale["staleAllowlistEntries"])
        self.assertEqual(1, stale["migrationDebtCount"])

    def test_check_prints_path_category_and_remediation_for_forbidden_use(self) -> None:
        root, policy = self.create_repo(
            "namespace Demo { public static class Test { public static double Run() => Math.Sqrt(4d); } }\n"
        )
        policy["baselineFindingsHash"] = AUDIT.build_report(root, policy, "snapshot")["findingsHash"]
        policy_path = root / "policy.json"
        policy_path.write_text(json.dumps(policy), encoding="utf-8")

        completed = subprocess.run(
            [
                sys.executable,
                str(SCRIPT),
                "check",
                "--policy",
                str(policy_path),
                "--repository-root",
                str(root),
            ],
            capture_output=True,
            text=True,
            check=False,
        )

        self.assertEqual(2, completed.returncode)
        self.assertIn("Test.cs:1:", completed.stderr)
        self.assertIn("simulation", completed.stderr)
        self.assertIn("remediation:", completed.stderr)


if __name__ == "__main__":
    unittest.main(verbosity=2)
