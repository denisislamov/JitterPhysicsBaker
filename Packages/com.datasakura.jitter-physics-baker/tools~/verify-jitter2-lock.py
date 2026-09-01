#!/usr/bin/env python3
"""Verify canonical Jitter sources, compile profile, patches, and staged binaries."""

from __future__ import annotations

import argparse
import re
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any

from jitter2_lock_common import (
    DEFAULT_LOCK_PATH,
    DEFAULT_SOURCE_ROOT,
    canonical_compile_profile_text,
    collect_inputs,
    compile_profile_id,
    compute_build_input_hash,
    compute_source_content_hash,
    load_lock,
    sha256_file,
    verify_declared_artifacts,
)

REQUIRED_PROFILE: dict[str, Any] = {
    "allowUnsafe": True,
    "continuousIntegrationBuild": True,
    "deterministic": True,
    "intrinsicsProfile": "scalar-shim",
    "languageVersion": "latest",
    "polyfillProfile": "netstandard21",
    "precision": "f32",
    "targetFramework": "netstandard2.1",
    "unityDefine": "",
}

PROJECT_PROPERTIES = {
    "TargetFramework": "targetFramework",
    "LangVersion": "languageVersion",
    "AllowUnsafeBlocks": "allowUnsafe",
    "Deterministic": "deterministic",
    "ContinuousIntegrationBuild": "continuousIntegrationBuild",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--package-root",
        type=Path,
        default=Path(__file__).resolve().parent.parent,
        help="Root folder of com.datasakura.jitter-physics-baker package.",
    )
    parser.add_argument(
        "--lock-file",
        default=DEFAULT_LOCK_PATH,
        help="Lock file path relative to --package-root.",
    )
    parser.add_argument(
        "--source-root",
        default=DEFAULT_SOURCE_ROOT,
        help="Source root relative to --package-root.",
    )
    return parser.parse_args()


def scalar_project_value(value: str) -> Any:
    lowered = value.strip().lower()
    if lowered == "true":
        return True
    if lowered == "false":
        return False
    return value.strip()


def verify_compile_profile(package_root: Path, lock_data: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    profile = lock_data.get("compileProfile")
    if profile != REQUIRED_PROFILE:
        errors.append(
            "compileProfile must be the canonical f32/netstandard2.1 profile: "
            + canonical_compile_profile_text({"compileProfile": REQUIRED_PROFILE})
        )

    assembly = lock_data.get("unityAssembly", {})
    project_relative = assembly.get("project") if isinstance(assembly, dict) else None
    if not isinstance(project_relative, str):
        return errors + ["unityAssembly.project must be a string"]

    project_path = package_root / project_relative
    if not project_path.is_file():
        return errors + [f"compile project is missing: {project_relative}"]

    root = ET.parse(project_path).getroot()
    values = {child.tag: (child.text or "") for group in root.findall("PropertyGroup") for child in group}
    for project_name, profile_name in PROJECT_PROPERTIES.items():
        actual = scalar_project_value(values.get(project_name, ""))
        expected = REQUIRED_PROFILE[profile_name]
        if actual != expected:
            errors.append(
                f"{project_relative} {project_name}={actual!r}, compileProfile {profile_name}={expected!r}"
            )

    if assembly.get("targetFramework") != REQUIRED_PROFILE["targetFramework"]:
        errors.append("unityAssembly.targetFramework differs from compileProfile.targetFramework")
    if assembly.get("reproducibilityPolicy") != "byte-identical":
        errors.append("unityAssembly.reproducibilityPolicy must be 'byte-identical'")
    if assembly.get("unsafePackageVersion") != "6.0.0":
        errors.append("unityAssembly.unsafePackageVersion must pin 6.0.0")
    return errors


def verify_consumer_patches(source_root: Path, lock_data: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    patches = lock_data.get("consumerPatches")
    if not isinstance(patches, list) or not patches:
        return ["consumerPatches must list every consumer-only source deviation"]

    seen: set[str] = set()
    for patch in patches:
        if not isinstance(patch, dict):
            errors.append("consumerPatches entries must be objects")
            continue
        relative = patch.get("path")
        expected = patch.get("sha256")
        reason = patch.get("reason")
        if not isinstance(relative, str) or not relative or relative in seen:
            errors.append(f"invalid or duplicate consumer patch path: {relative!r}")
            continue
        seen.add(relative)
        if not isinstance(reason, str) or not reason.strip():
            errors.append(f"consumer patch {relative!r} has no reason")
        if not isinstance(expected, str) or not re.fullmatch(r"sha256:[0-9a-f]{64}", expected):
            errors.append(f"consumer patch {relative!r} has an invalid hash")
            continue
        path = source_root / relative
        if not path.is_file():
            errors.append(f"consumer patch is missing: {relative}")
            continue
        actual = sha256_file(path)
        if actual != expected:
            errors.append(
                f"consumer patch hash mismatch for {relative}: expected {expected}, actual {actual}"
            )
    return errors


def verify_source_tree(source_root: Path) -> list[str]:
    errors: list[str] = []
    forbidden_directories = {"bin", "obj", ".git", "packages", "_package"}
    allowed_suffixes = {".cs", ".rsp"}
    for path in source_root.rglob("*"):
        relative = path.relative_to(source_root)
        if any(part in forbidden_directories for part in relative.parts):
            errors.append(f"generated/vendor path is forbidden in Runtime: {relative}")
        elif path.is_file() and path.suffix.lower() not in allowed_suffixes:
            errors.append(f"unexpected source snapshot file: {relative}")
    return errors


def main() -> int:
    args = parse_args()
    package_root = args.package_root.resolve()
    lock_path = (package_root / args.lock_file).resolve()
    source_root = (package_root / args.source_root).resolve()

    lock_data = load_lock(lock_path)
    errors: list[str] = []
    if lock_data.get("schemaVersion") != 2:
        errors.append("schemaVersion must be 2")
    if lock_data.get("patchSetId") != "unity-netstandard21-stablemath-v2":
        errors.append("patchSetId must identify the StableMath v2 patch set")

    errors.extend(verify_compile_profile(package_root, lock_data))
    errors.extend(verify_consumer_patches(source_root, lock_data))
    errors.extend(verify_source_tree(source_root))

    include_patterns = lock_data.get("includedFiles", ["**/*.cs", "**/*.rsp"])
    exclude_patterns = lock_data.get("excludedFiles", [])
    compile_profile_text = canonical_compile_profile_text(lock_data)
    inputs = collect_inputs(source_root, include_patterns, exclude_patterns)
    actual = compute_source_content_hash(inputs, compile_profile_text)
    expected = lock_data.get("sourceContentHash", "")
    if expected != actual:
        errors.append(f"source hash mismatch: expected {expected}, actual {actual}")

    errors.extend(verify_declared_artifacts(package_root, lock_data))
    expected_build_input = lock_data.get("unityAssembly", {}).get("buildInputHash", "")
    actual_build_input = compute_build_input_hash(package_root, lock_data)
    if expected_build_input != actual_build_input:
        errors.append(
            f"build input hash mismatch: expected {expected_build_input}, actual {actual_build_input}"
        )

    if errors:
        print("ERROR: Jitter lock verification failed")
        for error in errors:
            print(f"- {error}")
        print(f"included files: {len(inputs)}")
        return 1

    print(f"OK: {actual}")
    print(f"compileProfileId: {compile_profile_id(lock_data)}")
    print(f"included files: {len(inputs)}")
    print("consumer patches: " + str(len(lock_data["consumerPatches"])))
    print("binary artifacts: " + str(len(lock_data["unityAssembly"]["artifacts"])))
    print("buildInputHash: " + lock_data["unityAssembly"]["buildInputHash"])
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
