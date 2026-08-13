#!/usr/bin/env python3
"""Write the .meta files Unity would write, for package assets added outside the editor.

Unity generates these on import, but `tools/verify-package-meta.py` fails before that
happens, and a package published without them installs broken: UPM clones into an
immutable folder where Unity cannot repair anything.

Only missing .meta files are created, and only inside the package. Hidden folders (those
ending with `~`) are skipped because Unity never sees them.

    python3 tools/dev-make-meta.py
    python3 tools/verify-package-meta.py
"""

import os
import sys
import uuid

PACKAGE = "Packages/com.datasakura.jitter-physics-baker"


def write(path: str, is_folder: bool) -> None:
    with open(path + ".meta", "w", encoding="utf-8", newline="\n") as handle:
        handle.write("fileFormatVersion: 2\n")
        handle.write(f"guid: {uuid.uuid4().hex}\n")

        if is_folder:
            # A folder .meta needs its importer block; a script .meta may be guid-only,
            # which is what Unity 6.3 itself writes for default importer settings.
            handle.write("folderAsset: yes\n")
            handle.write("DefaultImporter:\n")
            handle.write("  externalObjects: {}\n")
            handle.write("  userData: \n")
            handle.write("  assetBundleName: \n")
            handle.write("  assetBundleVariant: \n")


def main() -> int:
    if not os.path.isdir(PACKAGE):
        print(f"error: {PACKAGE} not found - run from the repository root.")
        return 2

    created = 0

    for dirpath, dirnames, filenames in os.walk(PACKAGE):
        if any(part.endswith("~") for part in dirpath.split(os.sep)):
            continue

        for name in dirnames:
            if name.endswith("~"):
                continue

            folder = os.path.join(dirpath, name)
            if not os.path.exists(folder + ".meta"):
                write(folder, is_folder=True)
                created += 1
                print("meta for folder", folder)

        for name in filenames:
            if name.endswith(".meta") or name.startswith("."):
                continue

            path = os.path.join(dirpath, name)
            if not os.path.exists(path + ".meta"):
                write(path, is_folder=False)
                created += 1
                print("meta for", path)

    print(f"{created} .meta file(s) created")
    return 0


if __name__ == "__main__":
    sys.exit(main())

