#!/usr/bin/env python3
"""Regression and negative tests for the canonical Jitter release contract."""

from __future__ import annotations

import importlib.util
import subprocess
import sys
import tempfile
import warnings
import zipfile
from pathlib import Path

TOOLS_ROOT = Path(__file__).resolve().parent
PACKAGE_ROOT = TOOLS_ROOT.parent
REPOSITORY_ROOT = PACKAGE_ROOT.parents[1]
sys.path.insert(0, str(TOOLS_ROOT))


def load_verifier():
    path = TOOLS_ROOT / "verify-canonical-jitter-release.py"
    spec = importlib.util.spec_from_file_location("canonical_jitter_verifier", path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"cannot import {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def run(arguments: list[str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(arguments, check=False, capture_output=True, text=True)


def rewrite_archive(source: Path, destination: Path, mode: str) -> None:
    destination.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(source, "r") as original:
        infos = original.infolist()
        with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as target:
            for info in infos:
                if mode == "missing" and info.filename.endswith("/Jitter2.Core.dll"):
                    continue
                value = original.read(info)
                if mode == "tampered" and info.filename.endswith("/Jitter2.Core.dll"):
                    value = value[: len(value) // 2] + bytes([value[len(value) // 2] ^ 1]) + value[len(value) // 2 + 1 :]
                target.writestr(info, value, compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)

            if mode == "duplicate":
                core = next(info for info in infos if info.filename.endswith("/Jitter2.Core.dll"))
                with warnings.catch_warnings():
                    warnings.simplefilter("ignore", UserWarning)
                    target.writestr(
                        core,
                        original.read(core),
                        compress_type=zipfile.ZIP_DEFLATED,
                        compresslevel=9,
                    )


def expect_rejected(verifier, archive: Path, expected_fragment: str) -> None:
    try:
        verifier.read_and_verify_archive(archive, PACKAGE_ROOT)
    except ValueError as error:
        if expected_fragment not in str(error):
            raise AssertionError(f"wrong rejection for {archive}: {error}") from error
        print(f"PASS negative {archive.parent.name}: {error}")
        return
    raise AssertionError(f"negative archive unexpectedly passed: {archive}")


def main() -> int:
    verifier = load_verifier()
    builder = TOOLS_ROOT / "build-canonical-jitter-release.py"
    with tempfile.TemporaryDirectory(prefix="canonical-jitter-release-tests-") as temp:
        root = Path(temp)
        first = root / "first"
        second = root / "second"
        for output in (first, second):
            result = run([sys.executable, str(builder), "--package-root", str(PACKAGE_ROOT), "--output-dir", str(output)])
            if result.returncode != 0:
                raise RuntimeError(result.stdout + result.stderr)

        archive_name = verifier.EXPECTED_ASSET
        archive = first / archive_name
        if archive.read_bytes() != (second / archive_name).read_bytes():
            raise AssertionError("two clean builds produced different archive bytes")
        print(f"PASS deterministic archive: {verifier.sha256_bytes(archive.read_bytes())}")

        manifest, files = verifier.read_and_verify_archive(archive, PACKAGE_ROOT)
        print(f"PASS manifest/tag: {manifest['release']['tag']}")
        print(verifier.run_dotnet_consumer(files))

        for mode, fragment in (
            ("missing", "exactly one Jitter2.Core.dll"),
            ("duplicate", "duplicate ZIP member name"),
            ("tampered", "SHA-256 mismatch: Jitter2.Core.dll"),
        ):
            negative = root / mode / archive_name
            rewrite_archive(archive, negative, mode)
            expect_rejected(verifier, negative, fragment)

        f64_root = root / "f64-fixture"
        f64_root.mkdir()
        project = f64_root / "Jitter2.Core.csproj"
        project.write_text(
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n"
            "  <PropertyGroup>\n"
            "    <TargetFramework>netstandard2.1</TargetFramework>\n"
            "    <AssemblyName>Jitter2.Core</AssemblyName>\n"
            "    <LangVersion>latest</LangVersion>\n"
            "    <ImplicitUsings>enable</ImplicitUsings>\n"
            "    <Nullable>enable</Nullable>\n"
            "    <NuGetAudit>false</NuGetAudit>\n"
            "  </PropertyGroup>\n"
            "</Project>\n",
            encoding="utf-8",
            newline="\n",
        )
        (f64_root / "Precision.cs").write_text(
            "namespace Jitter2 { public static class Precision { public const bool IsDoublePrecision = true; } }\n"
            "namespace Jitter2.LinearMath { public static class StableMath {\n"
            "  public static double Sqrt(double value) => value == 4d ? 2d : value;\n"
            "  public static double Sin(double value) => value;\n"
            "  public static double Clamp01(double value) => value < 0d ? 0d : value > 1d ? 1d : value;\n"
            "  public static long QuantizeToInt64(double value, double scale) => (long)(value * scale);\n"
            "} }\n",
            encoding="utf-8",
            newline="\n",
        )
        result = run(
            [
                "dotnet",
                "build",
                str(project),
                "-c",
                "Release",
                "--nologo",
                "-v",
                "quiet",
            ]
        )
        if result.returncode != 0:
            raise RuntimeError("f64 fixture build failed:\n" + result.stdout + result.stderr)
        f64_files = dict(files)
        f64_files["Jitter2.Core.dll"] = (
            f64_root / "bin" / "Release" / "netstandard2.1" / "Jitter2.Core.dll"
        ).read_bytes()
        try:
            verifier.run_dotnet_consumer(f64_files)
        except ValueError as error:
            print(f"PASS negative f64: {str(error).splitlines()[0]}")
        else:
            raise AssertionError("f64 assembly unexpectedly passed the clean-consumer probe")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
