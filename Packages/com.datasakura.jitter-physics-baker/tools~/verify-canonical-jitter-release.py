#!/usr/bin/env python3
"""Verify canonical Jitter archive identity and compile a clean external .NET consumer."""

from __future__ import annotations

import argparse
import hashlib
import html
import json
import subprocess
import tempfile
import zipfile
from pathlib import Path, PurePosixPath

from jitter2_lock_common import canonical_compile_profile_text, load_lock

EXPECTED_VERSION = "2.8.9-datasakura.1-rc.1"
EXPECTED_TAG = f"jitter-v{EXPECTED_VERSION}"
EXPECTED_ASSET = f"DataSakura.Jitter2.Core-{EXPECTED_VERSION}.zip"
EXPECTED_TIMESTAMP = (1980, 1, 1, 0, 0, 0)


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


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


def fail(message: str) -> None:
    raise ValueError(message)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path, help="Canonical Jitter release ZIP.")
    parser.add_argument(
        "--package-root",
        type=Path,
        default=Path(__file__).resolve().parent.parent,
        help="Source package root used to cross-check jitter2.lock.json.",
    )
    parser.add_argument("--skip-dotnet", action="store_true", help="Skip the clean .NET consumer probe.")
    return parser.parse_args()


def read_and_verify_archive(archive_path: Path, package_root: Path) -> tuple[dict, dict[str, bytes]]:
    if archive_path.name != EXPECTED_ASSET:
        fail(f"unexpected asset name: {archive_path.name}")

    expected_root = EXPECTED_ASSET.removesuffix(".zip")
    with zipfile.ZipFile(archive_path, "r") as archive:
        infos = archive.infolist()
        if len({info.filename for info in infos}) != len(infos):
            fail("duplicate ZIP member name")

        files: dict[str, bytes] = {}
        for info in infos:
            path = PurePosixPath(info.filename)
            if path.is_absolute() or ".." in path.parts or len(path.parts) != 2:
                fail(f"unsafe or noncanonical ZIP path: {info.filename}")
            if path.parts[0] != expected_root:
                fail(f"unexpected archive root: {path.parts[0]}")
            if info.date_time != EXPECTED_TIMESTAMP:
                fail(f"non-deterministic ZIP timestamp: {info.filename}")
            files[path.name] = archive.read(info)

    if sum(1 for name in files if name == "Jitter2.Core.dll") != 1:
        fail("archive must contain exactly one Jitter2.Core.dll")
    manifest_name = "canonical-jitter.manifest.json"
    if manifest_name not in files:
        fail("canonical-jitter.manifest.json is missing")
    manifest = json.loads(files[manifest_name].decode("utf-8"))

    release = manifest.get("release", {})
    if release.get("assetName") != EXPECTED_ASSET:
        fail("manifest asset name mismatch")
    if release.get("version") != EXPECTED_VERSION:
        fail("manifest version mismatch")
    if release.get("tag") != EXPECTED_TAG:
        fail("manifest tag mismatch")
    if manifest.get("assembly", {}).get("name") != "Jitter2.Core":
        fail("assembly name mismatch")
    if manifest.get("compileProfile", {}).get("precision") != "f32":
        fail("canonical release must declare precision=f32")
    contract = manifest.get("installContract", {})
    if contract.get("mode") != "separate-explicit-install":
        fail("separate install contract is missing")
    if contract.get("automaticUpmDependency") is not False:
        fail("automatic UPM dependency is forbidden")
    if contract.get("sameDllForUnityAndDotNet") is not True:
        fail("Unity/.NET exact-DLL parity is missing")

    expected_files = {record["path"]: record for record in manifest.get("files", [])}
    actual_payload_names = set(files) - {manifest_name}
    if set(expected_files) != actual_payload_names:
        fail(
            "manifest/archive file inventory mismatch: "
            f"manifest={sorted(expected_files)}, archive={sorted(actual_payload_names)}"
        )
    for name, record in expected_files.items():
        value = files[name]
        if record.get("size") != len(value):
            fail(f"size mismatch: {name}")
        if record.get("sha256") != sha256_bytes(value):
            fail(f"SHA-256 mismatch: {name}")

    profile_text = canonical_compile_profile_text(manifest)
    if manifest.get("compileProfileCanonicalText") != profile_text:
        fail("compile profile canonical text mismatch")
    compile_profile_id = sha256_bytes(profile_text.encode("utf-8"))
    if manifest.get("compileProfileId") != compile_profile_id:
        fail("compileProfileId mismatch")

    stable_math_identity = {
        "apiVersion": manifest.get("stableMath", {}).get("apiVersion"),
        "compileProfileId": compile_profile_id,
        "precision": "f32",
        "sourceContentHash": manifest.get("provenance", {}).get("sourceContentHash"),
    }
    stable_math_text = json.dumps(
        stable_math_identity, sort_keys=True, ensure_ascii=True, separators=(",", ":")
    )
    if manifest.get("stableMath", {}).get("compatibilityId") != sha256_bytes(
        stable_math_text.encode("utf-8")
    ):
        fail("StableMath compatibility ID mismatch")
    source_hash = manifest["provenance"]["sourceContentHash"]
    if manifest.get("bakerRuntimeCompatibilityId") != runtime_compatibility_id(
        source_hash, compile_profile_id
    ):
        fail("baker runtimeCompatibilityId mismatch")

    lock = load_lock(package_root / "jitter2.lock.json")
    if source_hash != lock.get("sourceContentHash"):
        fail("release source hash does not match jitter2.lock.json")
    if compile_profile_id != sha256_bytes(canonical_compile_profile_text(lock).encode("utf-8")):
        fail("release compile profile does not match jitter2.lock.json")
    if manifest.get("provenance", {}).get("upstreamCommit") != lock.get("upstreamCommit"):
        fail("upstream commit mismatch")
    return manifest, files


def run_dotnet_consumer(files: dict[str, bytes]) -> str:
    with tempfile.TemporaryDirectory(prefix="canonical-jitter-consumer-") as temp:
        root = Path(temp)
        dll = root / "Jitter2.Core.dll"
        unsafe = root / "System.Runtime.CompilerServices.Unsafe.dll"
        dll.write_bytes(files[dll.name])
        unsafe.write_bytes(files[unsafe.name])

        project = root / "Consumer.csproj"
        project.write_text(
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
            "  <PropertyGroup>\n"
            "    <OutputType>Exe</OutputType>\n"
            "    <TargetFramework>net10.0</TargetFramework>\n"
            "    <ImplicitUsings>enable</ImplicitUsings>\n"
            "    <Nullable>enable</Nullable>\n"
            "    <NuGetAudit>false</NuGetAudit>\n"
            "  </PropertyGroup>\n"
            "  <ItemGroup>\n"
            f"    <Reference Include=\"Jitter2.Core\"><HintPath>{html.escape(str(dll))}</HintPath><Private>true</Private></Reference>\n"
            f"    <Reference Include=\"System.Runtime.CompilerServices.Unsafe\"><HintPath>{html.escape(str(unsafe))}</HintPath><Private>true</Private></Reference>\n"
            "  </ItemGroup>\n"
            "</Project>\n",
            encoding="utf-8",
            newline="\n",
        )
        (root / "Program.cs").write_text(
            "using Jitter2;\n"
            "using Jitter2.LinearMath;\n"
            "if (Precision.IsDoublePrecision) throw new Exception(\"f64 is forbidden\");\n"
            "if (!typeof(StableMath).IsPublic) throw new Exception(\"StableMath is not public\");\n"
            "var value = StableMath.Sqrt(4f) + StableMath.Sin(0f) + StableMath.Clamp01(0f);\n"
            "long quantized = StableMath.QuantizeToInt64(-1.5f, 1f);\n"
            "if (value != 2f || quantized != -2) throw new Exception(\"StableMath contract mismatch\");\n"
            "Console.WriteLine($\"CANONICAL_JITTER_OK assembly={typeof(Precision).Assembly.GetName().Name} precision=f32 stableMath=public\");\n",
            encoding="utf-8",
            newline="\n",
        )
        result = subprocess.run(
            ["dotnet", "run", "--project", str(project), "-c", "Release", "--nologo"],
            check=False,
            capture_output=True,
            text=True,
        )
        output = (result.stdout + result.stderr).strip()
        if result.returncode != 0:
            fail(f"clean .NET consumer failed ({result.returncode}):\n{output}")
        if "CANONICAL_JITTER_OK assembly=Jitter2.Core precision=f32 stableMath=public" not in output:
            fail(f"clean .NET consumer returned unexpected output:\n{output}")
        return output


def main() -> int:
    args = parse_args()
    archive = args.archive.resolve()
    manifest, files = read_and_verify_archive(archive, args.package_root.resolve())
    print(f"OK archiveSha256={sha256_bytes(archive.read_bytes())}")
    print(f"OK tag={manifest['release']['tag']}")
    print(f"OK sourceContentHash={manifest['provenance']['sourceContentHash']}")
    print(f"OK compileProfileId={manifest['compileProfileId']}")
    print(f"OK stableMathCompatibilityId={manifest['stableMath']['compatibilityId']}")
    if not args.skip_dotnet:
        print(run_dotnet_consumer(files))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (ValueError, KeyError, json.JSONDecodeError, zipfile.BadZipFile) as error:
        raise SystemExit(f"ERROR: {error}")
