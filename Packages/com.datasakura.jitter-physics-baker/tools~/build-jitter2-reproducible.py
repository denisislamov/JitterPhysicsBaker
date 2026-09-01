#!/usr/bin/env python3
"""Build Jitter2 twice, require byte identity, and optionally stage the verified files."""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import tempfile
from pathlib import Path

from jitter2_lock_common import compute_build_input_hash, load_lock, sha256_file

ARTIFACTS = (
    "Jitter2.Core.dll",
    "Jitter2.Core.xml",
    "System.Runtime.CompilerServices.Unsafe.dll",
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--package-root",
        type=Path,
        default=Path(__file__).resolve().parent.parent,
        help="Root folder of com.datasakura.jitter-physics-baker.",
    )
    parser.add_argument(
        "--stage",
        action="store_true",
        help="Replace Jitter2~/Prebuilt and refresh its hashes only after both builds match.",
    )
    return parser.parse_args()


def run(arguments: list[str], cwd: Path | None = None) -> str:
    completed = subprocess.run(
        arguments,
        cwd=str(cwd) if cwd else None,
        check=True,
        capture_output=True,
        text=True,
    )
    return completed.stdout.strip()


def find_unsafe(lock_data: dict) -> Path:
    version = lock_data["unityAssembly"]["unsafePackageVersion"]
    output = run(["dotnet", "nuget", "locals", "global-packages", "--list"])
    _, separator, folder = output.partition(":")
    if not separator or not folder.strip():
        raise SystemExit("error: dotnet did not report the global NuGet package folder")

    path = (
        Path(folder.strip())
        / "system.runtime.compilerservices.unsafe"
        / version
        / "lib"
        / "netstandard2.0"
        / "System.Runtime.CompilerServices.Unsafe.dll"
    )
    if not path.is_file():
        raise SystemExit(f"error: pinned Unsafe dependency is missing: {path}")
    return path


def copy_build_inputs(package_root: Path, destination: Path) -> Path:
    jitter_root = destination / "Jitter2~"
    jitter_root.mkdir(parents=True)
    for name in ("Runtime", "Compat", "StandaloneUnity"):
        shutil.copytree(
            package_root / "Jitter2~" / name,
            jitter_root / name,
            ignore=shutil.ignore_patterns("bin", "obj"),
        )
    return jitter_root / "StandaloneUnity" / "Jitter2.Core.csproj"


def clean_build(package_root: Path, destination: Path, unsafe: Path) -> dict[str, Path]:
    project = copy_build_inputs(package_root, destination)
    run(
        [
            "dotnet",
            "build",
            str(project),
            "-c",
            "Release",
            "--nologo",
            "--disable-build-servers",
            "-v",
            "quiet",
            "-p:UseSharedCompilation=false",
            "-p:RestoreIgnoreFailedSources=true",
        ]
    )
    output = project.parent / "bin" / "Release" / "netstandard2.1"
    files = {
        "Jitter2.Core.dll": output / "Jitter2.Core.dll",
        "Jitter2.Core.xml": output / "Jitter2.Core.xml",
        "System.Runtime.CompilerServices.Unsafe.dll": unsafe,
    }
    for name, path in files.items():
        if not path.is_file():
            raise SystemExit(f"error: clean build produced no {name}: {path}")
    return files


def compare_builds(first: dict[str, Path], second: dict[str, Path]) -> dict[str, str]:
    hashes: dict[str, str] = {}
    mismatches: list[str] = []
    for name in ARTIFACTS:
        first_hash = sha256_file(first[name])
        second_hash = sha256_file(second[name])
        hashes[name] = first_hash
        print(f"{name}: {first_hash}")
        if first_hash != second_hash:
            mismatches.append(f"{name}: first {first_hash}, second {second_hash}")
    if mismatches:
        raise SystemExit("error: clean builds are not byte-identical\n" + "\n".join(mismatches))
    return hashes


def stage_verified(package_root: Path, files: dict[str, Path], hashes: dict[str, str]) -> None:
    output = package_root / "Jitter2~" / "Prebuilt"
    output.mkdir(parents=True, exist_ok=True)

    with tempfile.TemporaryDirectory(prefix="jitter2-stage-", dir=output.parent) as temporary:
        staging = Path(temporary)
        for name in ARTIFACTS:
            shutil.copyfile(files[name], staging / name)
        for name in ARTIFACTS:
            os.replace(staging / name, output / name)

    lock_path = package_root / "jitter2.lock.json"
    lock_data = load_lock(lock_path)
    lock_data["unityAssembly"]["buildInputHash"] = compute_build_input_hash(
        package_root, lock_data
    )
    lock_data["unityAssembly"]["artifacts"] = {name: hashes[name] for name in ARTIFACTS}
    with lock_path.open("w", encoding="utf-8", newline="\n") as handle:
        json.dump(lock_data, handle, ensure_ascii=True, indent=2)
        handle.write("\n")

    run(["python3", str(package_root / "tools~" / "verify-jitter2-lock.py")])
    print(f"staged verified artifacts into {output}")


def main() -> int:
    args = parse_args()
    package_root = args.package_root.resolve()
    lock_data = load_lock(package_root / "jitter2.lock.json")

    run(["python3", str(package_root / "tools~" / "patch-jitter2-netstandard.py"), "--check"])
    unsafe = find_unsafe(lock_data)

    with tempfile.TemporaryDirectory(prefix="jitter2-reproducible-") as temporary:
        workspace = Path(temporary)
        first = clean_build(package_root, workspace / "build-a", unsafe)
        second = clean_build(package_root, workspace / "build-b", unsafe)
        hashes = compare_builds(first, second)
        if args.stage:
            stage_verified(package_root, first, hashes)

    print("OK: two clean builds are byte-identical")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
