#!/usr/bin/env python3
"""Read-only Jitter math/precision source inventory and policy checker."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


RULES = (
    ("JMP001", re.compile(r"\bPhysicsVector3\b"), "custom vector DTO"),
    ("JMP002", re.compile(r"\bPhysicsQuaternion\b"), "custom quaternion DTO"),
    ("JMP003", re.compile(r"\b(?:Vector3|Quaternion|Bounds|Matrix4x4)\b"), "Unity math type"),
    ("JMP004", re.compile(r"\bMathf\b"), "Unity Mathf"),
    ("JMP005", re.compile(r"\bMathF\b"), "platform MathF"),
    (
        "JMP006",
        re.compile(r"\b(?:System\.)?Math\.(?:Sin|Cos|Sqrt|Round|Abs|Min|Max|Clamp|Atan2|Asin|Acos)\b"),
        "System.Math operation",
    ),
    ("JMP007", re.compile(r"\b(?:float|System\.Single)\b"), "explicit f32 scalar type"),
    ("JMP008", re.compile(r"\b(?:double|System\.Double)\b"), "explicit f64 scalar type"),
    (
        "JMP009",
        re.compile(r"\b(?:global\s+)?using\s+(?!Real\b)[A-Za-z_]\w*\s*=\s*(?:System\.)?(?:Single|Double)\s*;"),
        "non-canonical scalar alias",
    ),
    ("JMP010", re.compile(r"\b(?:class|struct)\s+StableMath\b"), "local StableMath declaration"),
    (
        "JMP011",
        re.compile(r"\busing\s+(?:static\s+System\.Math|[A-Za-z_]\w*\s*=\s*(?:System\.Math|UnityEngine\.Mathf))\s*;"),
        "math alias or static import",
    ),
    ("JMP012", re.compile(r"\bJMP_AUDIT_(?:IGNORE|ALLOW|SUPPRESS)\b"), "inline audit suppression"),
    (
        "JMP013",
        re.compile(r"(?<![A-Za-z0-9_])(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?[fFdD]\b"),
        "explicit precision numeric literal",
    ),
)

RAW_PATTERN = re.compile(
    r"\b(?:PhysicsVector3|PhysicsQuaternion|Vector3|Quaternion|Bounds|Matrix4x4|"
    r"float|double|Mathf|MathF|StableMath|USE_DOUBLE_PRECISION)\b|"
    r"\bSystem\.(?:Single|Double|Math)\b|\bMath\."
)

MEMBER_RE = re.compile(
    r"\b(?:class|struct|interface|enum)\s+([A-Za-z_]\w*)|"
    r"\b([A-Za-z_]\w*)\s*\([^;{}]*\)\s*(?:=>|\{|$)|"
    r"\b(?:public|private|internal|protected)\s+(?:static\s+)?(?:readonly\s+)?"
    r"[A-Za-z_][\w<>,.\[\]? ]*\s+([A-Za-z_]\w*)\s*(?:[={;(]|$)"
)


@dataclass(frozen=True)
class Region:
    kind: str
    start: int
    end: int


def sha256_text(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()


def position(text: str, offset: int) -> tuple[int, int]:
    line = text.count("\n", 0, offset) + 1
    previous = text.rfind("\n", 0, offset)
    return line, offset - previous


def mask_csharp(text: str) -> tuple[str, list[Region]]:
    """Mask comments and literals while preserving byte-for-byte character positions."""
    chars = list(text)
    regions: list[Region] = []
    index = 0
    length = len(text)

    def blank(start: int, end: int, kind: str) -> None:
        for cursor in range(start, end):
            if chars[cursor] not in "\r\n":
                chars[cursor] = " "
        regions.append(Region(kind, start, end))

    while index < length:
        if text.startswith("//", index):
            end = text.find("\n", index + 2)
            if end < 0:
                end = length
            blank(index, end, "comment")
            index = end
            continue
        if text.startswith("/*", index):
            end_marker = text.find("*/", index + 2)
            if end_marker < 0:
                raise ValueError(f"unterminated block comment at offset {index}")
            end = end_marker + 2
            blank(index, end, "comment")
            index = end
            continue

        prefix_end = index
        while prefix_end < length and text[prefix_end] in "$@":
            prefix_end += 1
        if prefix_end < length and text[prefix_end] == '"':
            quote_count = 0
            while prefix_end + quote_count < length and text[prefix_end + quote_count] == '"':
                quote_count += 1
            start = index
            interpolated = "$" in text[index:prefix_end]
            if quote_count >= 3:
                delimiter = '"' * quote_count
                end_marker = text.find(delimiter, prefix_end + quote_count)
                if end_marker < 0:
                    raise ValueError(f"unterminated raw string at offset {start}")
                end = end_marker + quote_count
                blank(start, end, "interpolated-string" if interpolated else "string")
                index = end
                continue

            verbatim = "@" in text[index:prefix_end]
            cursor = prefix_end + 1
            while cursor < length:
                if verbatim and text.startswith('""', cursor):
                    cursor += 2
                    continue
                if not verbatim and text[cursor] == "\\":
                    cursor += 2
                    continue
                if text[cursor] == '"':
                    cursor += 1
                    break
                cursor += 1
            else:
                raise ValueError(f"unterminated string at offset {start}")
            blank(start, cursor, "interpolated-string" if interpolated else "string")
            index = cursor
            continue

        if text[index] == "'":
            cursor = index + 1
            while cursor < length:
                if text[cursor] == "\\":
                    cursor += 2
                    continue
                if text[cursor] == "'":
                    cursor += 1
                    break
                cursor += 1
            else:
                raise ValueError(f"unterminated character literal at offset {index}")
            blank(index, cursor, "char")
            index = cursor
            continue
        index += 1

    return "".join(chars), regions


def region_at(regions: Iterable[Region], offset: int) -> str:
    for region in regions:
        if region.start <= offset < region.end:
            return region.kind
    return "code"


def nearest_symbol(masked: str, offset: int) -> str | None:
    lines = masked[:offset].splitlines()
    for line in reversed(lines[-80:]):
        match = MEMBER_RE.search(line)
        if not match:
            continue
        value = next((group for group in match.groups() if group), None)
        if value:
            return value
    return None


def classify(path: str, rule_id: str, symbol: str | None, source_line: str) -> dict[str, str | None]:
    normalized = path.replace("\\", "/")
    owner = "Jitter Physics Baker"
    planned = "JMP-E04"
    reason = "Owned simulation or artifact math must migrate to the canonical Jitter contract."
    action = "Replace with Jitter math types, Real, or StableMath in the owning epic."

    if "/Server~/" in normalized or "/Tests/" in normalized or "/tools~/fixtures/" in normalized:
        category, impact, disposition = "test_fixture", "non_affecting", "legacy_fixture"
        reason = "Compiled test or delivery fixture; update with the public contract or retain only as an exact legacy fixture."
        action = "Review together with the production API migration."
        planned = "JMP-E08"
    elif "/Samples~/" in normalized:
        category, impact = "unity_boundary", "runtime_affecting"
        disposition = "must_migrate" if rule_id in {"JMP001", "JMP002", "JMP005", "JMP006", "JMP008", "JMP009", "JMP010", "JMP011", "JMP012"} else "allowed"
        reason = "Sample glue is consumer-facing Unity code; Unity presentation types are allowed but legacy contract usages must migrate."
        action = "Keep Unity presentation at the boundary and update sample contract calls."
        planned = "JMP-E07"
    elif "/JitterIntegration~/" in normalized:
        telemetry = rule_id == "JMP008" and (
            "ElapsedMilliseconds" in source_line or "elapsedMilliseconds" in source_line
        )
        if telemetry:
            category, impact, disposition = "telemetry", "non_affecting", "allowed"
            reason = "Stopwatch duration is diagnostics-only and must not flow into artifact, simulation, topology, or network state."
            action = "Retain as telemetry and keep a data-flow regression assertion."
            planned = None
        else:
            category, impact, disposition = "simulation", "runtime_affecting", "must_migrate"
            reason = "Shared world construction affects the Jitter runtime world."
            action = "Use the canonical Jitter types and Real profile directly."
            planned = "JMP-E07"
    elif "/Runtime/ArtifactCodec/" in normalized:
        category, impact, disposition = "serialization", "deterministic", "must_migrate"
        if rule_id == "JMP006" and (
            normalized.endswith("EmbeddedArtifactSourceGenerator.cs") or normalized.endswith("JitterPhysicsIdUtility.cs")
        ):
            impact, disposition = "non_affecting", "allowed"
            reason = "Integer/string buffer sizing does not operate on simulation values or artifact scalar layout."
            action = "Retain with exact source review."
            planned = None
        else:
            reason = "Artifact codec usage can change schema bytes, validation, or compatibility."
            action = "Migrate with golden-byte and schema evidence."
            planned = "JMP-E04"
    elif "/Runtime/Contracts/" in normalized:
        category, impact, disposition = "simulation", "deterministic", "must_migrate"
        if rule_id == "JMP006" and normalized.endswith("JitterPhysicsIdUtility.cs"):
            category, impact, disposition = "serialization", "non_affecting", "allowed"
            reason = "String identifier truncation uses integer lengths only."
            action = "Retain with exact source review."
            planned = None
    elif "/Runtime/UnityArtifact/" in normalized:
        category, impact, disposition = "serialization", "bake_affecting", "must_migrate"
        reason = "Unity artifact bridge must not redefine authoritative scalar or vector semantics."
        action = "Keep Unity object wrapping separate from canonical records."
        planned = "JMP-E05"
    elif "/Authoring/" in normalized:
        category, impact = "unity_boundary", "bake_affecting"
        disposition = "allowed" if rule_id in {"JMP003", "JMP004", "JMP007", "JMP013"} else "must_migrate"
        reason = "Serialized Unity authoring is an allowed input boundary; authoritative records below it must use Jitter math."
        action = "Retain only at authoring input and convert once in the explicit adapter."
        planned = "JMP-E05"
    elif "/Editor/Baking/" in normalized:
        category, impact = "unity_boundary", "bake_affecting"
        disposition = "allowed" if rule_id == "JMP003" else "must_migrate"
        reason = "Unity collider/transform access is a boundary, while bake-affecting calculations require the canonical math contract."
        action = "Convert once, then use Jitter math and StableMath."
        planned = "JMP-E05"
    elif "/Editor/Diagnostics/" in normalized:
        category = "unity_boundary"
        presentation = "Overlay" in normalized and symbol not in {"ToPhysics", "ToVector", "ToQuaternion"}
        impact = "non_affecting" if presentation else "bake_affecting"
        disposition = "allowed" if presentation and rule_id in {"JMP003", "JMP004", "JMP007", "JMP013"} else "must_migrate"
        reason = "Scene View presentation math is allowed; comparer/conversion math remains migration-owned."
        action = "Keep presentation-only Unity math and migrate authoritative comparison/conversion."
        planned = "JMP-E05"
    elif "/Editor/" in normalized:
        category, impact = "unity_boundary", "non_affecting"
        disposition = "allowed" if rule_id in {"JMP003", "JMP004", "JMP007", "JMP008", "JMP013"} else "must_migrate"
        reason = "Editor UI/diagnostics boundary is non-simulation unless its value feeds bake output."
        action = "Retain UI-only usage; migrate contract references."
        planned = "JMP-E05"
    else:
        category, impact, disposition = "ambiguous", "unknown", "investigate"
        reason = "The owned path is not covered by a reviewed classification rule."
        action = "Classify manually before accepting the inventory."
        planned = "JMP-E00"

    return {
        "category": category,
        "impact": impact,
        "disposition": disposition,
        "owner": owner,
        "reason": reason,
        "targetAction": action,
        "plannedEpic": planned,
    }


def context_hash(masked: str, start: int, end: int) -> str:
    line_start = masked.rfind("\n", 0, start) + 1
    line_end = masked.find("\n", end)
    if line_end < 0:
        line_end = len(masked)
    normalized = " ".join(masked[line_start:line_end].split())
    return sha256_text(normalized)[:16]


def finding_id(
    rule_id: str,
    path: str,
    symbol: str | None,
    matched: str,
    context: str,
    occurrence: int,
) -> str:
    identity = "\n".join(
        (rule_id, path, symbol or "<unknown>", matched, context, str(occurrence), "code")
    )
    return f"{rule_id}:{sha256_text(identity)[:20]}"


def scan_file(repo_root: Path, file_path: Path) -> tuple[list[dict], list[dict]]:
    raw_bytes = file_path.read_bytes()
    try:
        text = raw_bytes.decode("utf-8-sig")
    except UnicodeDecodeError as error:
        raise ValueError(f"{file_path}: invalid UTF-8: {error}") from error
    masked, regions = mask_csharp(text)
    relative = file_path.relative_to(repo_root).as_posix()
    findings: list[dict] = []
    identity_occurrences: dict[tuple[str, str | None, str, str], int] = {}

    for rule_id, pattern, title in RULES:
        for match in pattern.finditer(masked):
            line, column = position(text, match.start())
            source_line = text.splitlines()[line - 1] if text.splitlines() else ""
            symbol = nearest_symbol(masked, match.start())
            context = context_hash(masked, match.start(), match.end())
            occurrence_key = (rule_id, symbol, match.group(0), context)
            occurrence = identity_occurrences.get(occurrence_key, 0) + 1
            identity_occurrences[occurrence_key] = occurrence
            classified = classify(relative, rule_id, symbol, source_line)
            finding = {
                "id": finding_id(rule_id, relative, symbol, match.group(0), context, occurrence),
                "ruleId": rule_id,
                "ruleTitle": title,
                "path": relative,
                "offset": match.start(),
                "line": line,
                "column": column,
                "symbol": symbol,
                "matchedText": match.group(0),
                "lexicalRegion": "code",
                "contextHash": context,
                "contextOccurrence": occurrence,
                "classificationSource": "reviewed-prototype-classifier-v1",
            }
            finding.update(classified)
            findings.append(finding)

    raw_candidates: list[dict] = []
    for match in RAW_PATTERN.finditer(text):
        line, column = position(text, match.start())
        raw_candidates.append(
            {
                "path": relative,
                "offset": match.start(),
                "line": line,
                "column": column,
                "matchedText": match.group(0),
                "lexicalRegion": region_at(regions, match.start()),
            }
        )
    return findings, raw_candidates


def validate_policy(policy: dict) -> None:
    required = {
        "schemaVersion", "repositoryRootMarker", "ownedRoots", "vendorRoots",
        "excludedRoots", "baselineFindingsHash", "allowlist",
    }
    missing = sorted(required - set(policy))
    if missing:
        raise ValueError("policy is missing fields: " + ", ".join(missing))
    unknown = sorted(set(policy) - required)
    if unknown:
        raise ValueError("policy has unknown fields: " + ", ".join(unknown))
    if policy["schemaVersion"] != 2:
        raise ValueError("unsupported policy schemaVersion")
    for key in ("ownedRoots", "vendorRoots", "excludedRoots"):
        if not isinstance(policy[key], list) or not all(isinstance(item, str) for item in policy[key]):
            raise ValueError(f"policy field '{key}' must be a string array")
        for item in policy[key]:
            candidate = Path(item)
            if candidate.is_absolute() or ".." in candidate.parts or "\\" in item:
                raise ValueError(f"policy path must be repository-relative POSIX: {item}")
    baseline = policy["baselineFindingsHash"]
    if not isinstance(baseline, str) or (baseline and not re.fullmatch(r"sha256:[0-9a-f]{64}", baseline)):
        raise ValueError("baselineFindingsHash must be empty or sha256:<64 lowercase hex>")
    if not isinstance(policy["allowlist"], list):
        raise ValueError("policy field 'allowlist' must be an array")
    known_rules = {rule_id for rule_id, _, _ in RULES}
    entry_ids: set[str] = set()
    for entry in policy["allowlist"]:
        required_entry = {"id", "path", "recursive", "ruleIds", "owner", "reason"}
        if not isinstance(entry, dict) or set(entry) != required_entry:
            raise ValueError("each allowlist entry must contain exactly: " + ", ".join(sorted(required_entry)))
        if not all(isinstance(entry[key], str) and entry[key].strip() for key in ("id", "path", "owner", "reason")):
            raise ValueError("allowlist id, path, owner and reason must be non-empty strings")
        if entry["id"] in entry_ids:
            raise ValueError(f"duplicate allowlist id: {entry['id']}")
        entry_ids.add(entry["id"])
        candidate = Path(entry["path"])
        if candidate.is_absolute() or ".." in candidate.parts or "\\" in entry["path"]:
            raise ValueError(f"allowlist path must be repository-relative POSIX: {entry['path']}")
        if not isinstance(entry["recursive"], bool):
            raise ValueError(f"allowlist recursive must be boolean: {entry['id']}")
        if not isinstance(entry["ruleIds"], list) or not entry["ruleIds"]:
            raise ValueError(f"allowlist ruleIds must be a non-empty array: {entry['id']}")
        unknown_rules = sorted(set(entry["ruleIds"]) - known_rules)
        if unknown_rules:
            raise ValueError(f"allowlist entry {entry['id']} has unknown rules: {', '.join(unknown_rules)}")


def allowlist_match(path: str, rule_id: str, entries: list[dict]) -> dict | None:
    for entry in entries:
        configured = entry["path"].rstrip("/")
        path_matches = path == configured or (
            entry["recursive"] and path.startswith(configured + "/")
        )
        if path_matches and rule_id in entry["ruleIds"]:
            return entry
    return None


def enumerate_files(repo_root: Path, policy: dict) -> list[Path]:
    excluded = tuple(item.rstrip("/") + "/" for item in policy["excludedRoots"])
    files: list[Path] = []
    for configured in policy["ownedRoots"]:
        root = (repo_root / configured).resolve()
        try:
            root.relative_to(repo_root.resolve())
        except ValueError as error:
            raise ValueError(f"scan root escapes repository: {configured}") from error
        if not root.exists():
            raise ValueError(f"scan root does not exist: {configured}")
        for candidate in root.rglob("*.cs"):
            if candidate.is_symlink():
                continue
            relative = candidate.relative_to(repo_root).as_posix()
            if any(relative.startswith(prefix) or f"/{prefix}" in relative for prefix in excluded):
                continue
            files.append(candidate)
    normalized = [path.relative_to(repo_root).as_posix() for path in files]
    if len(normalized) != len(set(normalized)):
        raise ValueError("duplicate normalized source path")
    return [repo_root / relative for relative in sorted(normalized)]


def summarize(findings: list[dict]) -> dict:
    dimensions: dict[str, dict[str, int]] = {}
    for key in ("ruleId", "category", "impact", "disposition", "plannedEpic"):
        counts: dict[str, int] = {}
        for finding in findings:
            value = str(finding.get(key) or "none")
            counts[value] = counts.get(value, 0) + 1
        dimensions[key] = dict(sorted(counts.items()))
    return dimensions


def findings_hash(findings: list[dict]) -> str:
    reviewed_fields = (
        "id",
        "category",
        "impact",
        "disposition",
        "owner",
        "reason",
        "targetAction",
        "plannedEpic",
    )
    reviewed = [
        {key: finding.get(key) for key in reviewed_fields}
        for finding in sorted(findings, key=lambda item: item["id"])
    ]
    canonical = json.dumps(reviewed, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    return "sha256:" + sha256_text(canonical + "\n")


def raw_inventory_hash(raw_candidates: list[dict]) -> str:
    identity_fields = ("path", "offset", "matchedText", "lexicalRegion")
    identities = [
        {key: candidate.get(key) for key in identity_fields}
        for candidate in raw_candidates
    ]
    canonical = json.dumps(identities, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    return "sha256:" + sha256_text(canonical + "\n")


def policy_hash(policy: dict) -> str:
    canonical = json.dumps(policy, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    return "sha256:" + sha256_text(canonical)


def build_report(repo_root: Path, policy: dict, mode: str) -> dict:
    repo_root = repo_root.resolve()
    files = enumerate_files(repo_root, policy)
    findings: list[dict] = []
    raw_candidates: list[dict] = []
    for file_path in files:
        file_findings, file_raw = scan_file(repo_root, file_path)
        findings.extend(file_findings)
        raw_candidates.extend(file_raw)
    findings.sort(key=lambda item: (item["path"], item["line"], item["column"], item["ruleId"], item["id"]))
    raw_candidates.sort(key=lambda item: (item["path"], item["line"], item["column"], item["matchedText"]))
    ids = [finding["id"] for finding in findings]
    if len(ids) != len(set(ids)):
        duplicates = sorted({item for item in ids if ids.count(item) > 1})
        raise RuntimeError("duplicate finding ids: " + ", ".join(duplicates[:5]))

    allowlist_hits = {entry["id"]: 0 for entry in policy["allowlist"]}
    violations: list[dict] = []
    for finding in findings:
        entry = allowlist_match(finding["path"], finding["ruleId"], policy["allowlist"])
        finding["allowlistEntryId"] = entry["id"] if entry else None
        if entry:
            allowlist_hits[entry["id"]] += 1
        else:
            violations.append(finding)
    stale_allowlist = sorted(entry_id for entry_id, count in allowlist_hits.items() if count == 0)
    ambiguous = sum(finding["category"] == "ambiguous" for finding in findings)
    debt = len(violations)
    allowed = len(findings) - debt
    legacy = sum(finding["disposition"] == "legacy_fixture" for finding in findings)
    current_hash = findings_hash(findings)
    reviewed = bool(policy["baselineFindingsHash"]) and current_hash == policy["baselineFindingsHash"]
    non_code = sum(candidate["lexicalRegion"] != "code" for candidate in raw_candidates)

    return {
        "schemaVersion": 2,
        "toolVersion": "0.2.0",
        "mode": mode,
        "repositoryRevision": "working-tree",
        "policyHash": policy_hash(policy),
        "scanRoots": policy["ownedRoots"],
        "filesScanned": len(files),
        "rawCandidates": len(raw_candidates),
        "rawInventoryHash": raw_inventory_hash(raw_candidates),
        "nonCodeCandidates": non_code,
        "codeFindings": len(findings),
        "findingsHash": current_hash,
        "baselineReviewed": reviewed,
        "migrationDebtCount": debt,
        "allowedCount": allowed,
        "legacyFixtureCount": legacy,
        "ambiguousCount": ambiguous,
        "unclassifiedCount": debt if reviewed else len(findings),
        "stalePolicyCount": (0 if reviewed or not policy["baselineFindingsHash"] else 1)
        + len(stale_allowlist),
        "allowlistHits": allowlist_hits,
        "staleAllowlistEntries": stale_allowlist,
        "violations": violations,
        "summary": summarize(findings),
        "findings": findings,
        "rawOccurrences": raw_candidates,
    }


def write_json(path: str | None, report: dict) -> None:
    if not path:
        return
    destination = Path(path)
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(report, indent=2, sort_keys=True, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")


def write_markdown(path: str | None, report: dict) -> None:
    if not path:
        return
    destination = Path(path)
    destination.parent.mkdir(parents=True, exist_ok=True)
    lines = [
        "# JMP-P00 source audit report",
        "",
        f"- Mode: `{report['mode']}`",
        f"- Files scanned: `{report['filesScanned']}`",
        f"- Raw candidates: `{report['rawCandidates']}`",
        f"- Code findings: `{report['codeFindings']}`",
        f"- Migration debt: `{report['migrationDebtCount']}`",
        f"- Allowed: `{report['allowedCount']}`",
        f"- Legacy fixtures: `{report['legacyFixtureCount']}`",
        f"- Ambiguous: `{report['ambiguousCount']}`",
        f"- Findings hash: `{report['findingsHash']}`",
        f"- Baseline reviewed: `{str(report['baselineReviewed']).lower()}`",
        "",
        "## Counts by rule",
        "",
        "| Rule | Count |",
        "|---|---:|",
    ]
    lines.extend(f"| `{key}` | {value} |" for key, value in report["summary"]["ruleId"].items())
    lines.append("")
    destination.write_text("\n".join(lines), encoding="utf-8", newline="\n")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("snapshot", "inventory", "check", "validate-policy"))
    parser.add_argument("--policy", required=True)
    parser.add_argument("--repository-root")
    parser.add_argument("--json-report")
    parser.add_argument("--markdown-report")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    policy_path = Path(args.policy).resolve()
    try:
        policy = json.loads(policy_path.read_text(encoding="utf-8"))
        validate_policy(policy)
        repo_root = Path(args.repository_root).resolve() if args.repository_root else Path(__file__).resolve().parents[3]
        marker = repo_root / policy["repositoryRootMarker"]
        if not marker.is_file():
            raise ValueError(f"repository root marker is missing: {marker}")
        report = build_report(repo_root, policy, args.mode)
        write_json(args.json_report, report)
        write_markdown(args.markdown_report, report)
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(f"JMP_P00_AUDIT_FAILED exit=4 error={error}", file=sys.stderr)
        return 4
    except RuntimeError as error:
        print(f"JMP_P00_AUDIT_FAILED exit=5 error={error}", file=sys.stderr)
        return 5

    if args.mode == "snapshot":
        print(report["findingsHash"])
        return 0 if report["ambiguousCount"] == 0 else 2
    if args.mode == "validate-policy":
        if (policy["baselineFindingsHash"] and not report["baselineReviewed"]) or report["staleAllowlistEntries"]:
            print(
                "JMP_P00_AUDIT_FAILED exit=3 "
                f"baseline_stale={int(not report['baselineReviewed'])} "
                f"unused_allowlist={len(report['staleAllowlistEntries'])}",
                file=sys.stderr,
            )
            return 3
        print("JMP_P00_POLICY_OK")
        return 0
    if report["ambiguousCount"] or not report["baselineReviewed"] or report["staleAllowlistEntries"]:
        print(
            "JMP_P00_AUDIT_FAILED exit=2 "
            f"unclassified={report['unclassifiedCount']} ambiguous={report['ambiguousCount']} "
            f"stale={report['stalePolicyCount']}",
            file=sys.stderr,
        )
        return 2
    if args.mode == "check" and report["migrationDebtCount"]:
        for finding in report["violations"][:50]:
            print(
                f"{finding['path']}:{finding['line']}:{finding['column']}: "
                f"{finding['ruleId']} {finding['category']}: {finding['ruleTitle']}; "
                f"remediation: {finding['targetAction']}",
                file=sys.stderr,
            )
        print(
            f"JMP_P00_AUDIT_FAILED exit=2 migration_debt={report['migrationDebtCount']}",
            file=sys.stderr,
        )
        return 2
    print(
        f"JMP_P00_AUDIT_OK mode={args.mode} files={report['filesScanned']} "
        f"findings={report['codeFindings']} debt={report['migrationDebtCount']} "
        f"allowed={report['allowedCount']}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
