#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
unity="${1:-}"

if [[ -z "${unity}" ]]; then
  version="$(sed -n 's/^m_EditorVersion: //p' "${project_root}/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"
  unity="/Applications/Unity/Hub/Editor/${version}/Unity.app/Contents/MacOS/Unity"
fi

if [[ ! -x "${unity}" ]]; then
  echo "error: Unity not found at '${unity}'" >&2
  exit 2
fi

fixture_root="$(mktemp -d "${TMPDIR:-/private/tmp}/jp05-delivery.XXXXXX")"
cleanup() {
  local status=$?
  if [[ "${status}" -ne 0 ]]; then
    echo "delivery fixture failed; preserved at ${fixture_root}" >&2
    return
  fi
  case "${fixture_root}" in
    "${TMPDIR:-/private/tmp}"/jp05-delivery.*) rm -rf "${fixture_root}" ;;
    *) echo "refusing to remove unexpected fixture path '${fixture_root}'" >&2 ;;
  esac
}
trap cleanup EXIT

echo "==> isolated project: ${fixture_root}"
rsync -a --exclude Library --exclude Temp --exclude Logs \
  "${project_root}/Assets" "${project_root}/Packages" "${project_root}/ProjectSettings" \
  "${fixture_root}/"

mkdir -p "${fixture_root}/Assets/Editor"
cp "${project_root}/Packages/com.datasakura.jitter-physics-baker/tools~/fixtures/JP05DeliveryBootstrap.cs" \
  "${fixture_root}/Assets/Editor/JP05DeliveryBootstrap.cs"

run_method() {
  local method="$1"
  local log_name="$2"
  "${unity}" -batchmode -quit -projectPath "${fixture_root}" \
    -executeMethod "${method}" -logFile "${fixture_root}/${log_name}"
}

run_tests() {
  local platform="$1"
  local filter="$2"
  local result="${fixture_root}/${platform}-${filter##*.}.xml"
  "${unity}" -batchmode -projectPath "${fixture_root}" -runTests \
    -testPlatform "${platform}" -testFilter "${filter}" -testResults "${result}" \
    -logFile "${fixture_root}/${platform}-${filter##*.}.log" -silent-crashes
  python3 - "${result}" <<'PY'
import sys, xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
print("  total={} passed={} failed={} skipped={}".format(
    root.get("total"), root.get("passed"), root.get("failed"), root.get("skipped")))
if int(root.get("failed", "0")):
    raise SystemExit(1)
PY
}

echo "==> installing the single project-owned Jitter2"
run_method "DataSakura.JitterPhysics.DeliveryFixture.JP05DeliveryBootstrap.InstallJitter" \
  "install-jitter.log"

echo "==> installing the adapter against that Jitter2"
run_method "DataSakura.JitterPhysics.DeliveryFixture.JP05DeliveryBootstrap.InstallIntegration" \
  "install-integration.log"

jitter_count="$(find "${fixture_root}/Assets" -name 'Jitter2.Core.dll' -type f | wc -l | tr -d ' ')"
if [[ "${jitter_count}" != "1" ]]; then
  echo "error: expected exactly one project-owned Jitter2.Core.dll, found ${jitter_count}" >&2
  exit 1
fi

echo "==> compiling public StableMath from an external Unity assembly"
cp "${project_root}/Packages/com.datasakura.jitter-physics-baker/tools~/fixtures/CanonicalJitterUnityProbe.cs" \
  "${fixture_root}/Assets/Editor/CanonicalJitterUnityProbe.cs"
run_method "DataSakura.JitterPhysics.DeliveryFixture.CanonicalJitterUnityProbe.Run" \
  "canonical-jitter-probe.log"
if ! rg -q "CANONICAL_JITTER_UNITY_OK assembly=Jitter2.Core precision=f32 stableMath=public" \
  "${fixture_root}/canonical-jitter-probe.log"; then
  echo "error: canonical Jitter Unity probe did not emit its success marker" >&2
  exit 1
fi

sample_root="${fixture_root}/Assets/Samples/DataSakura Jitter Physics Baker/JP05/Physics Baking Demos"
mkdir -p "${sample_root}"
rsync -a "${project_root}/Packages/com.datasakura.jitter-physics-baker/Samples~/Demos/" \
  "${sample_root}/"

echo "==> compiling, generating and baking the standalone sample"
run_method "DataSakura.JitterPhysics.DeliveryFixture.JP05DeliveryBootstrap.BuildBouncingBallSample" \
  "build-sample.log"

echo "==> integrated editor API fixtures"
run_tests EditMode "DataSakura.JitterPhysics.Editor.Tests.JitterPhysicsEditorApiTests"

echo "==> imported sample runtime fixture"
run_tests PlayMode "DataSakura.JitterPhysics.Samples.Tests.JitterPhysicsSampleDeliveryTests"

echo "JP05_DELIVERY_OK"
