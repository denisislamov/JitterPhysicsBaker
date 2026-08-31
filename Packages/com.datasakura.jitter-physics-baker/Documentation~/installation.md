# Installation

[Documentation home](index.md) ·
[Requirements and compatibility](requirements-and-compatibility.md) ·
[Quick Start](quick-start.md) · [Troubleshooting](troubleshooting.md)

Install the core package first, choose exactly one compatible Jitter2, and then install the
Jitter-dependent adapter. Importing the core package does not run an installer and does not write
files under `Assets/`.

The examples on this page pin package version `0.0.12`. Pin a release tag in production instead of
tracking `main`, so every developer and build machine resolves the same source tree.

## Before you begin

- Use Unity `6000.3` or later. The exact verified editor revision is `6000.3.19f1`; later editor
  versions require their own acceptance run.
- Decide whether the project will use its existing Jitter2 source copy or the package-owned
  fallback. Do not install two copies.
- Commit or back up the consumer project before installing generated integration files.
- If this is an upgrade, read [Migration and upgrading](migration-and-upgrading.md) first.

See [Requirements and compatibility](requirements-and-compatibility.md) for the distinction
between declared and verified targets.

## Install from a Git URL

This is the recommended installation for a normal consumer project.

1. Open **Window > Package Manager**.
2. Select the **+** menu.
3. Choose **Install package from git URL...**.
4. Enter:

   ```text
   https://github.com/denisislamov/jitter-physics-baker.git#v0.0.12
   ```

5. Wait for Package Manager resolution and Unity compilation to finish.
6. Select **DataSakura Jitter Physics Baker** in Package Manager and confirm that version
   `0.0.12` is shown.

Expected result: the package appears under **Packages** and the project compiles even when no
`Jitter2.Core` exists yet. Baking remains unavailable until Jitter2 is configured.

### Pin through `Packages/manifest.json`

The equivalent manifest entry is:

```json
{
  "dependencies": {
    "com.datasakura.jitter-physics-baker": "https://github.com/denisislamov/jitter-physics-baker.git#v0.0.12"
  }
}
```

Keep `Packages/packages-lock.json` under version control in the consumer project. Review both the
manifest and lock-file change when updating the tag.

## Install from a local checkout

Use a local dependency while developing the package and consumer together. The path must point to
the folder that directly contains `package.json`.

### Add it through Package Manager

1. Open **Window > Package Manager**.
2. Select the **+** menu.
3. Choose **Install package from disk...**.
4. Select the package's `package.json` itself. In this development repository it is
   `Packages/com.datasakura.jitter-physics-baker/package.json`.
5. Wait for Package Manager resolution and Unity compilation to finish.
6. Select **DataSakura Jitter Physics Baker** and confirm version `0.0.12`.

Expected result: the package appears under **Packages** and the project compiles without Jitter2,
just as for the Git install. Continue with the explicit Jitter2 and integration setup below.

If Unity cannot add the package or shows a different package, verify that the selected file is the
`package.json` inside the package folder, not the Unity project root or a parent repository folder.

### Add it through the manifest

For a standalone package checkout next to the consumer project:

```json
"com.datasakura.jitter-physics-baker": "file:../../jitter-physics-baker"
```

For this development monorepo, point at its package subtree instead:

```json
"com.datasakura.jitter-physics-baker": "file:../../JitterPhysicsBaker/Packages/com.datasakura.jitter-physics-baker"
```

Adjust the relative path to the consumer's actual directory layout. A local dependency follows
the files in that checkout and is not an immutable release. Replace it with a tagged Git URL
before producing a reproducible build.

After editing `Packages/manifest.json`, wait for Unity to update `Packages/packages-lock.json`, then
select the package in Package Manager and confirm version `0.0.12`. If resolution fails, resolve the
`file:` path relative to the consumer project's `Packages` folder and confirm that its target
contains this package's `package.json`.

To update a local dependency, update the referenced checkout and let Package Manager re-resolve it;
review the resulting package, lock-file, integration, and migration changes before continuing. To
remove or replace it safely, first follow **Safe removal** below, then remove or replace its Package
Manager/manifest reference. A local checkout is external consumer-owned content and is never
deleted by package removal.

## Distribution methods not certified here

- The project does not distribute a `.unitypackage`.
- A manually embedded copy under `Packages/com.datasakura.jitter-physics-baker` is a generic Unity
  Package Manager technique, but it is not a separately verified release path in this package.
- Copying only selected package folders is unsupported. It breaks assembly, license, dormant
  source, sample, documentation, and tooling boundaries.

## First-time setup in the Editor

Open **Tools > DataSakura > Jitter Physics Baker Window**.

> [!IMPORTANT]
> In the current UI, the **Settings** section requires a selected `JitterPhysicsLevel`. In a clean
> scene, press **Create Level** first. This explicitly creates a **Jitter Physics Level** GameObject,
> creates the default world-profile asset when needed, assigns it, and saves project assets. Save
> the scene after this step.

After a level is selected:

1. Select **Settings**.
2. Expand **Advanced installation and maintenance**.
3. Press **Open installation details**.
4. Read the compatibility status before running an install action.

Opening the package, settings provider, or installation details is read-only. Files are written
only after an explicit button press.

## Choose one Jitter2

| Status | Meaning | Next step |
| --- | --- | --- |
| `Missing` | No `Jitter2.Core` was found. | Press **Install Jitter2**, or add one compatible source copy yourself. |
| `Compatible` | The detected source copy or receipt-owned fallback matches the package lock. | Keep it; do not install a duplicate. |
| `Incompatible` | The source identity differs from this package release. | Align the project and server on the supported Jitter2 before baking. |
| `Duplicate` | Multiple `Jitter2.Core` definitions were found. | Remove all but the intended project-owned copy. |
| `UnsupportedPlugin` | A precompiled plugin exists without enough provenance to verify its sources. | Replace it with a compatible source copy or the receipt-owned fallback. |

### Use an existing source copy

If the report says `Compatible`, the package uses that copy by assembly name. It does not copy,
move, modify, update, or remove external Jitter2 files.

Compatibility is based on the canonical source hash and compile profile pinned by
`jitter2.lock.json`, not only on an upstream version label.

### Install the bundled fallback

When the report says `Missing`, press **Install Jitter2**.

The package installs a compiled `netstandard2.1` fallback under:

```text
Assets/DataSakura/ThirdParty/Jitter2/
```

It includes `Jitter2.Core.dll` and, when the project does not already provide it,
`System.Runtime.CompilerServices.Unsafe.dll`. The files and their hashes are recorded in the
package installation receipt.

Expected result: after Unity refreshes and compiles, the compatibility status is `Compatible`.

Do not copy the dormant `Jitter2~/` sources directly into `Assets/`. The installer builds the
Unity-compatible fallback deliberately; the dormant source snapshot is not an embedded Unity
assembly.

## Install the Jitter integration adapter

After Jitter2 is compatible, press **Install/update integration** in the same installation window.

The adapter is copied to:

```text
Assets/DataSakura/JitterPhysicsBaker/Integration/
```

The generated assembly references `Jitter2.Core` by assembly name. The installer also maintains
the `DATASAKURA_JITTER_INTEGRATION` scripting define used by optional consumer/sample code.

Install Jitter2 first. Installing Jitter-dependent sources with no `Jitter2.Core` would otherwise
produce missing-type compilation errors.

The deterministic receipt is stored at:

```text
Assets/DataSakura/JitterPhysicsBaker/InstallationReceipt.json
```

The installer updates only files that still match their recorded hashes. If a package-owned copy
was modified locally, the update is refused and the affected paths are reported instead of being
overwritten.

## Verify the installation

In the installation details window, press **Validate installation**.

Verify all of the following:

- the Jitter2 status is `Compatible`;
- exactly one `Jitter2.Core` exists;
- the integration assembly compiles;
- the receipt reports no missing or modified package-owned files;
- the Console has no new compilation errors;
- **Validate** on the package's Overview or Bake workflow reports the actual authoring issues rather
  than a missing/incompatible Jitter2 setup error.

Validation reads the receipt and hashes; it does not repair files. Use the matching explicit
install/update action after reviewing the report.

If setup is correct, continue with [Quick Start](quick-start.md). World profiles and project
folders are documented in [Configuration](configuration.md).

## Install the runnable samples

Install Jitter2 and the integration adapter before importing the samples. The sample runtime
assembly references the installed adapter by name, while Unity's standard sample **Import** button
cannot enforce that prerequisite.

1. Open **Window > Package Manager**.
2. Select **DataSakura Jitter Physics Baker**.
3. Open **Samples**.
4. Import **Physics Baking Demos**.

Unity imports this release to:

```text
Assets/Samples/DataSakura Jitter Physics Baker/0.0.12/Physics Baking Demos/
```

The sample runtime scripts use Jitter2 types directly. The receipt-owned fallback is an
auto-referenced precompiled plugin. If the project instead uses a compatible source-based
`Jitter2.Core` asmdef, add `"Jitter2.Core"` to the imported consumer copy's
`Runtime/DataSakura.JitterPhysics.Samples.asmdef` `references` array and wait for Unity to compile.
Asmdef references are not transitive through the installed integration adapter. Do not edit the
Package Cache copy; imported samples are consumer-owned.

Then run a sample command under:

**Assets > DataSakura > Jitter Physics > Samples**

For example, choose **Build and bake: Bouncing Ball**, enter Play Mode, and press Space.

Imported samples are versioned consumer copies. Updating the package does not overwrite an older
sample folder or local edits. See [Migration and upgrading](migration-and-upgrading.md).

## Install server runtime sources

The UPM package is not a standalone server executable. It projects the portable contracts, codec,
and shared world builder into an SDK-style consumer server project.

Open installation details, expand **Advanced**, and press
**Install server runtime sources...**. Choose a folder inside the consumer server source tree.

The projection is receipt-managed, contains a hashed `JitterPhysics.projection.json`, and refuses
to overwrite modified projected files. Build and startup requirements are documented in
[Dedicated server integration](dedicated-server.md).

## Safe removal

Removing the UPM reference does not automatically remove files that were explicitly installed
under `Assets/` or projected into a server repository.

Before removing the package:

1. Back up or commit the consumer project.
2. Open installation details and expand **Advanced**.
3. Press **Remove package-owned installation**.
4. Review the confirmation and press **Remove**.
5. Check the report for files that were retained because they had been modified.
6. Remove the package reference through Package Manager or `Packages/manifest.json`.

This operation removes unmodified receipt-owned fallback and integration files. It does not touch
an external Jitter2, imported samples, baked artifacts, or server copies. Remove or retain those
consumer-owned items deliberately.

If installation or compilation does not match the expected result, use
[Troubleshooting](troubleshooting.md) before deleting files manually.
