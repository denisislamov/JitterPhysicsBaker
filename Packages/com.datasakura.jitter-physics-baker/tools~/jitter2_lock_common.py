#!/usr/bin/env python3
"""Shared helpers for jitter2 lock hash tooling."""

from __future__ import annotations

import hashlib
import json
import re
from pathlib import Path
from typing import Any, Iterable


DEFAULT_LOCK_PATH = "jitter2.lock.json"
DEFAULT_SOURCE_ROOT = "Jitter2~/Runtime"


def load_lock(lock_path: Path) -> dict[str, Any]:
    with lock_path.open("r", encoding="utf-8") as handle:
        return json.load(handle)


def sha256_file(path: Path) -> str:
    """Returns the lock-format SHA-256 of one file without loading it all at once."""
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return "sha256:" + digest.hexdigest()


def compile_profile_id(lock_data: dict[str, Any]) -> str:
    """Returns the lowercase hash used by the runtime compatibility contract."""
    profile = canonical_compile_profile_text(lock_data).encode("utf-8")
    return hashlib.sha256(profile).hexdigest()


def verify_declared_artifacts(package_root: Path, lock_data: dict[str, Any]) -> list[str]:
    """Checks the exact staged Jitter artifacts declared by the lock."""
    errors: list[str] = []
    assembly = lock_data.get("unityAssembly")
    if not isinstance(assembly, dict):
        return ["unityAssembly must be an object"]

    output = assembly.get("output")
    files = assembly.get("files")
    artifacts = assembly.get("artifacts")
    if not isinstance(output, str) or not output:
        errors.append("unityAssembly.output must be a non-empty string")
        return errors
    if not isinstance(files, list) or not all(isinstance(item, str) for item in files):
        errors.append("unityAssembly.files must be a string array")
        return errors
    if not isinstance(artifacts, dict):
        errors.append("unityAssembly.artifacts must be an object")
        return errors

    if sorted(files) != sorted(artifacts):
        errors.append("unityAssembly.files and unityAssembly.artifacts must name the same files")

    output_root = package_root / output
    for relative in sorted(artifacts):
        expected = artifacts[relative]
        if not isinstance(expected, str) or not re.fullmatch(r"sha256:[0-9a-f]{64}", expected):
            errors.append(f"invalid artifact hash for {relative!r}: {expected!r}")
            continue

        path = output_root / relative
        if not path.is_file():
            errors.append(f"declared artifact is missing: {output}/{relative}")
            continue

        actual = sha256_file(path)
        if actual != expected:
            errors.append(
                f"artifact hash mismatch for {output}/{relative}: expected {expected}, actual {actual}"
            )

    return errors


def compute_build_input_hash(package_root: Path, lock_data: dict[str, Any]) -> str:
    """Hashes every package-owned input that can affect the canonical Jitter DLL."""
    jitter_root = package_root / "Jitter2~"
    inputs = collect_inputs(
        jitter_root,
        [
            "Runtime/**/*.cs",
            "Runtime/**/csc.rsp",
            "Compat/**/*.cs",
            "StandaloneUnity/Jitter2.Core.csproj",
        ],
        ["**/bin/**", "**/obj/**"],
    )
    return compute_source_content_hash(inputs, canonical_compile_profile_text(lock_data))


def canonical_compile_profile_text(lock_data: dict[str, Any]) -> str:
    profile = lock_data.get("compileProfile", {})
    return json.dumps(profile, sort_keys=True, ensure_ascii=True, separators=(",", ":"))


def canonical_relative_path(path: Path, root: Path) -> str:
    return str(path.relative_to(root)).replace("\\", "/")


def normalize_content(path: Path) -> bytes:
    data = path.read_bytes()
    if is_text_file(path):
        text = data.decode("utf-8")
        text = text.replace("\r\n", "\n").replace("\r", "\n")
        return text.encode("utf-8")
    return data


def is_text_file(path: Path) -> bool:
    return path.suffix.lower() in {".cs", ".rsp", ".json", ".txt", ".md", ".asmdef"}


def matches_any_pattern(path: str, patterns: Iterable[str]) -> bool:
    return any(glob_matches(path, pattern) for pattern in patterns)


def glob_matches(path: str, pattern: str) -> bool:
    """Deterministic glob matching, defined here rather than taken from pathlib.

    `pathlib.PurePosixPath.match` changed `**` semantics between Python releases and
    never matched a top-level file against `**/*.cs`. The lock hash has to be identical
    in this script and in the C# editor implementation, so the rules are spelled out:

    * `**/` matches zero or more leading directories,
    * `**`  matches anything, including `/`,
    * `*`   matches anything except `/`,
    * `?`   matches a single character except `/`.
    """
    return _compile_glob(pattern).match(path) is not None


def _compile_glob(pattern: str) -> re.Pattern[str]:
    cached = _GLOB_CACHE.get(pattern)
    if cached is not None:
        return cached

    regex: list[str] = ["^"]
    index = 0
    length = len(pattern)
    while index < length:
        character = pattern[index]
        if pattern.startswith("**/", index):
            regex.append("(?:[^/]+/)*")
            index += 3
        elif pattern.startswith("**", index):
            regex.append(".*")
            index += 2
        elif character == "*":
            regex.append("[^/]*")
            index += 1
        elif character == "?":
            regex.append("[^/]")
            index += 1
        else:
            regex.append(re.escape(character))
            index += 1

    regex.append("$")
    compiled = re.compile("".join(regex))
    _GLOB_CACHE[pattern] = compiled
    return compiled


_GLOB_CACHE: dict[str, re.Pattern[str]] = {}


def collect_inputs(root: Path, include_patterns: list[str], exclude_patterns: list[str]) -> list[tuple[str, bytes]]:
    if not root.exists():
        return []

    selected: list[str] = []
    for path in root.rglob("*"):
        if not path.is_file():
            continue
        relative = canonical_relative_path(path, root)
        if include_patterns and not matches_any_pattern(relative, include_patterns):
            continue
        if exclude_patterns and matches_any_pattern(relative, exclude_patterns):
            continue
        selected.append(relative)

    # Ordinal sort on the canonical relative path, so that the digest order does not
    # depend on the file system enumeration order or on the absolute location of the
    # package. The C# editor implementation sorts the same way.
    selected.sort()

    return [(relative, normalize_content(root / relative)) for relative in selected]


def compute_source_content_hash(inputs: list[tuple[str, bytes]], compile_profile_text: str) -> str:
    digest = hashlib.sha256()

    profile_bytes = compile_profile_text.encode("utf-8")
    digest.update(b"compileProfile\n")
    digest.update(str(len(profile_bytes)).encode("ascii"))
    digest.update(b"\n")
    digest.update(profile_bytes)
    digest.update(b"\n")

    for relative_path, content in inputs:
        path_bytes = relative_path.encode("utf-8")
        digest.update(path_bytes)
        digest.update(b"\n")
        digest.update(str(len(content)).encode("ascii"))
        digest.update(b"\n")
        digest.update(content)
        digest.update(b"\n")

    return "sha256:" + digest.hexdigest()


