#!/usr/bin/env bash
# Run the Unity EditMode and PlayMode tests without opening the editor.
#
# The package's portable half is covered by `tools~/test-dotnet.sh`, which needs no Unity at
# all. Everything that touches the AssetDatabase, authoring components or the editor windows
# can only be proven here, and only with the editor closed: Unity refuses to open a project
# that is already open, and a batch run that silently attaches to a running editor is worse
# than one that fails.
#
# Usage:
#   tools/run-unity-tests.sh [editmode|playmode|all] [path/to/Unity]

set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
mode="${1:-all}"
unity="${2:-}"

if [[ -z "${unity}" ]]; then
  # The version the project was authored with. A different minor version usually works, but it
  # reimports the whole Library folder, so it is worth being explicit about.
  version="$(sed -n 's/^m_EditorVersion: //p' "${project_root}/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"
  case "$(uname -s)" in
    Darwin) unity="/Applications/Unity/Hub/Editor/${version}/Unity.app/Contents/MacOS/Unity" ;;
    Linux)  unity="${HOME}/Unity/Hub/Editor/${version}/Editor/Unity" ;;
    *)      unity="unity" ;;
  esac
fi

if [[ ! -x "${unity}" ]]; then
  echo "error: Unity not found at '${unity}'." >&2
  echo "       Pass the path explicitly: tools/run-unity-tests.sh ${mode} /path/to/Unity" >&2
  exit 2
fi

if [[ -f "${project_root}/Temp/UnityLockfile" ]]; then
  echo "error: the project is open in the editor. Close it first; Unity cannot run a batch" >&2
  echo "       test run against a locked project." >&2
  exit 2
fi

results_dir="${project_root}/Logs/TestResults"
mkdir -p "${results_dir}"

run_mode() {
  local platform="$1"
  echo "== ${platform} =="

  # -batchmode without -nographics: PlayMode tests still need a graphics device on some
  # platforms, and a missing one shows up as an unrelated crash.
  "${unity}" \
    -batchmode \
    -runTests \
    -projectPath "${project_root}" \
    -testPlatform "${platform}" \
    -testResults "${results_dir}/${platform}.xml" \
    -logFile "${project_root}/Logs/unity-tests-${platform}.log" \
    -silent-crashes || {
      status=$?
      echo "Unity exited with ${status}; see Logs/unity-tests-${platform}.log" >&2
      return "${status}"
    }

  python3 - "${results_dir}/${platform}.xml" <<'PY'
import sys, xml.etree.ElementTree as ET
root = ET.parse(sys.argv[1]).getroot()
print("  total={} passed={} failed={} skipped={}".format(
    root.get("total"), root.get("passed"), root.get("failed"), root.get("skipped")))
for case in root.iter("test-case"):
    if case.get("result") not in ("Passed", "Skipped", "Inconclusive"):
        print("  FAILED", case.get("fullname"))
PY
}

case "${mode}" in
  editmode) run_mode EditMode ;;
  playmode) run_mode PlayMode ;;
  all)      run_mode EditMode; run_mode PlayMode ;;
  *) echo "usage: $0 [editmode|playmode|all] [path/to/Unity]" >&2; exit 2 ;;
esac

