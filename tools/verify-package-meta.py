#!/usr/bin/env python3
"""
Validates the UPM package before it is published to its standalone repository.

Two failure modes are invisible in this project but break every consumer that
installs the package from a git URL, because Unity Package Manager clones the
package into an IMMUTABLE folder (Library/PackageCache) and does it WITHOUT
Git LFS support:

1. Truncated .meta files - one that has no importer block at all. Unity repairs
   those in writable packages; in PackageCache it cannot, so assets are never
   imported correctly and asmdef references fail with CS0246. Scripts are
   exempt: Unity 6.3 writes a guid-only .meta for a .cs file with default
   importer settings.

2. Git LFS pointers - an LFS-tracked binary arrives as a ~130-byte text stub
   instead of the real content. For a baked `.jphys.bytes` fixture that means a
   hash mismatch at load time; for a managed plugin it means CS0246.

Run from the repository root:
    python3 tools/verify-package-meta.py
"""

import os
import sys

PACKAGE_ROOT = "Packages/com.datasakura.jitter-physics-baker"

# Hidden folders (Jitter2~, JitterIntegration~, Server~, Samples~, tools~,
# Documentation~) are invisible to Unity and need no .meta, but their binaries
# must still not be LFS pointers.
HIDDEN_SUFFIX = "~"

LFS_MAGIC = b"version https://git-lfs.github.com/spec/"

BINARY_EXTENSIONS = (".dll", ".so", ".dylib", ".bytes", ".a")

# Script assets whose .meta may legitimately contain nothing but a guid.
SCRIPT_EXTENSIONS = (".cs",)


def main() -> int:
    if not os.path.isdir(PACKAGE_ROOT):
        print(f"error: {PACKAGE_ROOT} not found - run from the repository root.")
        return 2

    missing, orphan, broken, pointers = [], [], [], []

    for dirpath, dirnames, filenames in os.walk(PACKAGE_ROOT):
        visible = not any(part.endswith(HIDDEN_SUFFIX)
                          for part in dirpath.split(os.sep))

        for name in dirnames:
            if visible and not name.endswith(HIDDEN_SUFFIX):
                folder = os.path.join(dirpath, name)
                if not os.path.exists(folder + ".meta"):
                    missing.append(folder + "/")

        for name in filenames:
            path = os.path.join(dirpath, name)

            # An LFS pointer anywhere in the package is fatal for UPM.
            if name.lower().endswith(BINARY_EXTENSIONS):
                with open(path, "rb") as handle:
                    if handle.read(len(LFS_MAGIC)) == LFS_MAGIC:
                        pointers.append(path)

            if not visible:
                continue

            if name.endswith(".meta"):
                asset = path[:-len(".meta")]
                if not os.path.exists(asset):
                    orphan.append(path)
                    continue
                text = open(path, encoding="utf-8", errors="ignore").read()
                if "guid:" not in text:
                    broken.append(path)
                    continue
                # Unity 6.3 stops writing the MonoImporter block for scripts whose
                # importer settings are all default, so a guid-only .cs.meta is what
                # the editor itself produces and is valid. Every other importer still
                # writes its block, and a .meta without one is genuinely truncated.
                if not asset.endswith(SCRIPT_EXTENSIONS) \
                        and "Importer:" not in text and "folderAsset" not in text:
                    broken.append(path)
            else:
                # Dot files are ignored by Unity, so they need no .meta.
                if name.startswith("."):
                    continue
                if not os.path.exists(path + ".meta"):
                    missing.append(path)

    for title, items in (
        ("Git LFS pointers (UPM cannot resolve these)", pointers),
        ("Assets without a .meta", missing),
        ("Orphan .meta (asset is gone)", orphan),
        ("Truncated .meta (no importer block)", broken),
    ):
        if items:
            print(f"\n{title}: {len(items)}")
            for item in sorted(items):
                print(f"  {item}")

    if pointers or missing or orphan or broken:
        print("\nFAILED")
        return 1

    print("OK: complete .meta files, no Git LFS pointers.")
    return 0


if __name__ == "__main__":
    sys.exit(main())


