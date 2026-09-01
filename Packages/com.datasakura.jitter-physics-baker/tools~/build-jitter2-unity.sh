#!/usr/bin/env bash
#
# Builds the Unity-facing Jitter2.Core assembly and stages it for installation.
#
# Unity cannot compile the snapshot: it fixes game assemblies at C# 9, and the snapshot is written
# in a later language. That limit applies to sources Unity compiles, not to an assembly it loads,
# so the snapshot is compiled here instead and shipped as a managed plugin.
#
# Run this after `sync-jitter2.py` or after editing anything under `Jitter2~/`, then commit the
# result together with the refreshed `jitter2.lock.json`.

set -euo pipefail

PACKAGE_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "dotnet SDK not found; it is required to build the Unity assembly." >&2
    exit 1
fi

echo "==> applying netstandard2.1 patches"
python3 "${PACKAGE_ROOT}/tools~/patch-jitter2-netstandard.py"

echo "==> running two isolated clean builds and staging only byte-identical outputs"
python3 "${PACKAGE_ROOT}/tools~/build-jitter2-reproducible.py" --stage
