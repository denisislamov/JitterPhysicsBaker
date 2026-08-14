#!/usr/bin/env bash
# Generate the demo scene, bake it and export the artifact to Server/artifacts, without
# opening the editor.
#
# The same work is available from the editor menu
# (Tools > DataSakura > Jitter Physics > Demo). This script exists for the case where the
# editor is closed and for CI, and it calls the very same entry point rather than a second
# implementation of the steps.
#
# Usage:
#   tools/bake-demo-scene.sh [path/to/Unity]

set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
unity="${1:-}"

if [[ -z "${unity}" ]]; then
  version="$(sed -n 's/^m_EditorVersion: //p' "${project_root}/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"
  case "$(uname -s)" in
    Darwin) unity="/Applications/Unity/Hub/Editor/${version}/Unity.app/Contents/MacOS/Unity" ;;
    Linux)  unity="${HOME}/Unity/Hub/Editor/${version}/Editor/Unity" ;;
    *)      unity="unity" ;;
  esac
fi

if [[ ! -x "${unity}" ]]; then
  echo "error: Unity not found at '${unity}'." >&2
  echo "       Pass the path explicitly: tools/bake-demo-scene.sh /path/to/Unity" >&2
  exit 2
fi

if [[ -f "${project_root}/Temp/UnityLockfile" ]]; then
  echo "error: the project is open in the editor. Close it, or run the bake from the menu:" >&2
  echo "       Tools > DataSakura > Jitter Physics > Demo > Create Demo Scene And Bake" >&2
  exit 2
fi

log="${project_root}/Logs/bake-demo-scene.log"
mkdir -p "$(dirname "${log}")"

"${unity}" \
  -batchmode \
  -nographics \
  -projectPath "${project_root}" \
  -executeMethod DataSakura.JitterPhysics.Demo.Editor.JitterPhysicsDemoPipeline.RunBatch \
  -logFile "${log}" \
  -silent-crashes || {
    status=$?
    echo "Unity exited with ${status}; see ${log}" >&2
    exit "${status}"
  }

echo "Baked. Delivered artifact:"
ls -l "${project_root}/Server/artifacts"

