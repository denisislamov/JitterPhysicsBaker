#!/usr/bin/env python3
"""Refresh the <Compile Include> lists in the Unity-generated csproj files.

Unity regenerates these projects itself, but only while the editor is running, so a file
added from outside is invisible to `dotnet build` until somebody opens the editor. That
turns a typo in Editor-only code into a ten-minute round trip.

The projects are not tracked by git, so rewriting them is safe: Unity overwrites the
result the next time it starts.

    python3 tools/dev-refresh-csproj.py
    dotnet build DataSakura.JitterPhysics.Editor.csproj -v q --nologo
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PACKAGE = "Packages/com.datasakura.jitter-physics-baker"

PROJECTS = {
    "DataSakura.JitterPhysics.Editor.csproj": f"{PACKAGE}/Editor",
    "DataSakura.JitterPhysics.ArtifactCodec.csproj": f"{PACKAGE}/Runtime/ArtifactCodec",
    "DataSakura.JitterPhysics.Contracts.csproj": f"{PACKAGE}/Runtime/Contracts",
    "DataSakura.JitterPhysics.UnityArtifact.csproj": f"{PACKAGE}/Runtime/UnityArtifact",
    "DataSakura.JitterPhysics.Authoring.csproj": f"{PACKAGE}/Authoring",
    "DataSakura.JitterPhysics.Tests.csproj": f"{PACKAGE}/Tests/Runtime",
    "DataSakura.JitterPhysics.Editor.Tests.csproj": f"{PACKAGE}/Tests/Editor",
}


def main() -> int:
    missing = 0

    for project, folder in PROJECTS.items():
        path = ROOT / project
        if not path.exists():
            print(f"skip {project}: not generated yet, open the project in Unity once")
            missing += 1
            continue

        sources = sorted(
            str(p.relative_to(ROOT)).replace("\\", "/")
            for p in (ROOT / folder).rglob("*.cs")
        )

        text = path.read_text(encoding="utf-8-sig")
        block = "\n".join(f'    <Compile Include="{s}" />' for s in sources)

        # One block, replaced whole: the generated projects list every file explicitly and
        # keep them in a single ItemGroup.
        updated, count = re.subn(
            r"(?:[ \t]*<Compile Include=\"[^\"]+\" />\n)+", block + "\n", text, count=1
        )

        if count == 0:
            print(f"warn: no <Compile> block in {project}")
            continue

        path.write_text("\ufeff" + updated, encoding="utf-8")
        print(f"{project}: {len(sources)} files")

    return 0 if missing == 0 else 1


if __name__ == "__main__":
    sys.exit(main())

