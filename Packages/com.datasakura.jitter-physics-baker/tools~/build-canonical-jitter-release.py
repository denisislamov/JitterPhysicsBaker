#!/usr/bin/env python3
"""Build the deterministic, separately installable canonical Jitter RC archive."""

from __future__ import annotations

import argparse
import hashlib
import json
import zipfile
from pathlib import Path

from jitter2_lock_common import (
    canonical_compile_profile_text,
    collect_inputs,
    compute_source_content_hash,
    load_lock,
)

RELEASE_VERSION = "2.8.9-datasakura.1-rc.1"
RELEASE_TAG = f"jitter-v{RELEASE_VERSION}"
ASSET_NAME = f"DataSakura.Jitter2.Core-{RELEASE_VERSION}.zip"
REPOSITORY = "https://github.com/denisislamov/jitter-physics-baker"
STABLE_MATH_API_VERSION = 1
ZIP_TIMESTAMP = (1980, 1, 1, 0, 0, 0)


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def sha256_file(path: Path) -> str:
    return sha256_bytes(path.read_bytes())


def runtime_compatibility_id(source_hash: str, compile_profile_id: str) -> str:
    fields = (
        ("schema", "1"),
        ("jitterSource", source_hash),
        ("precision", "f32"),
        ("compileProfile", compile_profile_id),
        ("colliderConversion", "1"),
        ("shapeConstruction", "1"),
        ("worldBuilder", "1"),
        ("worldDefaults", "1"),
    )
    text = "".join(f"{name}={len(value)}:{value}\n" for name, value in fields)
    return sha256_bytes(text.encode("utf-8"))


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--package-root",
        type=Path,
        default=Path(__file__).resolve().parent.parent,
        help="Root of com.datasakura.jitter-physics-baker.",
    )
    parser.add_argument(
        "--output-dir",
        type=Path,
        default=Path(__file__).resolve().parents[3] / "Artifacts" / "CanonicalJitter",
        help="Directory that receives the archive, detached manifest, and checksum.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    package_root = args.package_root.resolve()
    output_dir = args.output_dir.resolve()
    lock = load_lock(package_root / "jitter2.lock.json")
    runtime_root = package_root / "Jitter2~" / "Runtime"
    profile_text = canonical_compile_profile_text(lock)
    inputs = collect_inputs(runtime_root, lock["includedFiles"], lock["excludedFiles"])
    actual_source_hash = compute_source_content_hash(inputs, profile_text)
    if actual_source_hash != lock["sourceContentHash"]:
        raise SystemExit(
            "error: jitter2.lock.json is stale: "
            f"expected {lock['sourceContentHash']}, actual {actual_source_hash}"
        )

    compile_profile_id = sha256_bytes(profile_text.encode("utf-8"))
    stable_math_identity = {
        "apiVersion": STABLE_MATH_API_VERSION,
        "compileProfileId": compile_profile_id,
        "precision": lock["compileProfile"]["precision"],
        "sourceContentHash": actual_source_hash,
    }
    stable_math_text = json.dumps(
        stable_math_identity, sort_keys=True, ensure_ascii=True, separators=(",", ":")
    )

    source_files = {
        "Jitter2.Core.dll": package_root / "Jitter2~" / "Prebuilt" / "Jitter2.Core.dll",
        "Jitter2.Core.xml": package_root / "Jitter2~" / "Prebuilt" / "Jitter2.Core.xml",
        "System.Runtime.CompilerServices.Unsafe.dll": package_root
        / "Jitter2~"
        / "Prebuilt"
        / "System.Runtime.CompilerServices.Unsafe.dll",
        "LICENSE.md": package_root / "Jitter2~" / "LICENSE.md",
        "README.md": package_root / "Jitter2~" / "CanonicalRelease" / "README.md",
        "DIRECT_REFERENCE.md": package_root
        / "Jitter2~"
        / "CanonicalRelease"
        / "DIRECT_REFERENCE.md",
    }
    missing = [str(path) for path in source_files.values() if not path.is_file()]
    if missing:
        raise SystemExit("error: release input is missing: " + ", ".join(missing))

    file_records = [
        {
            "path": name,
            "sha256": sha256_file(path),
            "size": path.stat().st_size,
        }
        for name, path in sorted(source_files.items())
    ]
    manifest = {
        "schemaVersion": 1,
        "release": {
            "assetName": ASSET_NAME,
            "repository": REPOSITORY,
            "tag": RELEASE_TAG,
            "version": RELEASE_VERSION,
        },
        "assembly": {
            "name": lock["assemblyName"],
            "targetFramework": lock["unityAssembly"]["targetFramework"],
        },
        "provenance": {
            "patchSetId": lock["patchSetId"],
            "sourceContentHash": actual_source_hash,
            "upstreamCommit": lock["upstreamCommit"],
            "upstreamRepository": lock["upstreamRepository"],
        },
        "compileProfile": lock["compileProfile"],
        "compileProfileCanonicalText": profile_text,
        "compileProfileId": compile_profile_id,
        "integrationApiVersion": lock["integrationApiVersion"],
        "stableMath": {
            "apiVersion": STABLE_MATH_API_VERSION,
            "compatibilityId": sha256_bytes(stable_math_text.encode("utf-8")),
            "publicType": "Jitter2.LinearMath.StableMath",
        },
        "bakerRuntimeCompatibilityId": runtime_compatibility_id(
            actual_source_hash, compile_profile_id
        ),
        "installContract": {
            "automaticUpmDependency": False,
            "exactlyOneAssembly": "Jitter2.Core",
            "mode": "separate-explicit-install",
            "sameDllForUnityAndDotNet": True,
        },
        "files": file_records,
    }
    manifest_bytes = (
        json.dumps(manifest, ensure_ascii=True, indent=2, sort_keys=True) + "\n"
    ).encode("utf-8")

    output_dir.mkdir(parents=True, exist_ok=True)
    archive_path = output_dir / ASSET_NAME
    detached_manifest = output_dir / "canonical-jitter.manifest.json"
    checksum_path = output_dir / f"{ASSET_NAME}.sha256"
    detached_manifest.write_bytes(manifest_bytes)

    root = ASSET_NAME.removesuffix(".zip")
    with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, path in sorted(source_files.items()):
            info = zipfile.ZipInfo(f"{root}/{name}", ZIP_TIMESTAMP)
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            archive.writestr(info, path.read_bytes(), compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)

        info = zipfile.ZipInfo(f"{root}/canonical-jitter.manifest.json", ZIP_TIMESTAMP)
        info.compress_type = zipfile.ZIP_DEFLATED
        info.external_attr = 0o100644 << 16
        archive.writestr(info, manifest_bytes, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)

    archive_hash = sha256_file(archive_path)
    checksum_path.write_text(f"{archive_hash}  {ASSET_NAME}\n", encoding="ascii", newline="\n")
    print(f"archive={archive_path}")
    print(f"archiveSha256={archive_hash}")
    print(f"manifest={detached_manifest}")
    print(f"sourceContentHash={actual_source_hash}")
    print(f"compileProfileId={compile_profile_id}")
    print(f"stableMathCompatibilityId={manifest['stableMath']['compatibilityId']}")
    print(f"bakerRuntimeCompatibilityId={manifest['bakerRuntimeCompatibilityId']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
