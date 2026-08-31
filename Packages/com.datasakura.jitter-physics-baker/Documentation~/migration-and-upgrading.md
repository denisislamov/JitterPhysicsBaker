# Migration and upgrading

[Documentation home](index.md) · [Installation](installation.md) ·
[Configuration](configuration.md) · [Troubleshooting](troubleshooting.md)

An update can affect four independently owned layers:

1. the immutable UPM package;
2. receipt-managed fallback and integration files copied into the Unity project;
3. projected sources copied into a dedicated-server project;
4. versioned samples imported into `Assets/Samples`.

Updating the Git tag changes only the first layer. The other layers are explicit copies and do not
silently update themselves.

## Decide whether a re-bake is required

Use the release notes and the runtime compatibility report, not the package version alone.

| Change | Copied-component update | Artifact re-bake |
| --- | --- | --- |
| Documentation-only patch | Receipt/projection refresh may be reported; see the `0.0.12` note below. | No, while schema and `runtimeCompatibilityId` are unchanged. |
| Editor UI or documentation with no runtime-semantic change | Only if receipt-managed source content or its recorded package version changed. | No. |
| Integration/world-builder semantic change | Update Unity integration and server projection together. | Yes; the runtime compatibility id must change. |
| Jitter2 source or compile-profile change | Update every client/server Jitter2 copy and the adapter/projection as required. | Yes. |
| Artifact binary-layout change | Update all readers/writers together and follow the schema migration notes. | Normally yes; an unsupported schema is rejected. |
| Level geometry or world settings change | No package installation change by itself. | Yes for the affected level. |

The compatibility credential is:

```text
artifactHash + runtimeCompatibilityId
```

Do not substitute `TopologyFingerprint` or package SemVer. The fingerprint is diagnostic, and
package SemVer is not an input to `runtimeCompatibilityId`.

## Before updating

1. Commit or back up the Unity project, including:
   - `Packages/manifest.json` and `Packages/packages-lock.json`;
   - scenes and world-profile assets;
   - baked `.physics.asset`, `.physics.bytes`, and `.physics.manifest.json` files;
   - `Assets/DataSakura/JitterPhysicsBaker/InstallationReceipt.json`;
   - any consumer server projection and its `JitterPhysics.projection.json`.
2. Record the currently pinned package tag and the compatibility report.
3. Record whether Jitter2 is external or package-owned. Never let an update replace an external
   engine copy.
4. Identify locally modified integration, projection, and imported-sample files.
5. Finish or preserve unrelated consumer work before changing package dependencies.

> [!IMPORTANT]
> Do not delete a receipt to make an update appear clean. Without it, the installer cannot
> distinguish package-owned content from consumer-authored files.

## Update the UPM package

For the `0.0.12` release, change the pinned dependency to:

```json
"com.datasakura.jitter-physics-baker": "https://github.com/denisislamov/jitter-physics-baker.git#v0.0.12"
```

Then:

1. Let Package Manager resolve the new tag and finish compilation.
2. Confirm **DataSakura Jitter Physics Baker 0.0.12** in Package Manager.
3. Open **Tools > DataSakura > Jitter Physics Baker Window** and select the relevant level.
4. Open **Settings > Advanced installation and maintenance > Open installation details**.
5. Review the Jitter2 compatibility status before running an update action.
6. Press **Validate installation** and interpret the result using the next section.

If Package Manager retains an unexpected revision, inspect both `manifest.json` and
`packages-lock.json`; do not delete the entire `Library` folder as a first response. See
[Troubleshooting](troubleshooting.md).

## The `0.0.12` documentation-release nuance

This release changes package documentation and synchronizes release-version metadata. It does not
change the artifact schema or runtime-semantic inputs, so existing compatible artifacts do not
need to be re-baked solely because the package SemVer became `0.0.12`.

However, the current receipt model records package SemVer per copied component. Consequently,
**Validate installation** can report that a component was installed by an earlier package even
when its content hash and runtime compatibility remain valid.

Treat the signals separately:

| Signal | Meaning | Action for `0.0.12` |
| --- | --- | --- |
| Jitter2 status is `Compatible` and only the recorded package version is old | The physics source identity still matches. | No re-bake. Do not replace an external Jitter2. |
| Integration component reports the previous package version | The copied adapter receipt is stale. | Run **Install/update integration** if the files are unmodified, then validate again. No re-bake is required for this version-only refresh. |
| Package-owned fallback reports the previous package version but remains `Compatible` | The fallback source hash and receipt-owned files still match, but the per-component SemVer is old. | Do not remove and reinstall working physics solely to silence a docs-only warning. Record the warning; no re-bake is required. |
| Server projection differs from the package | Projected source or its generated manifest is stale. The portable package-version constant is part of the projection. | Re-run **Install server runtime sources...** into the same folder after confirming projected files are unmodified, then rebuild the server. No re-bake is required while runtime id is unchanged. |
| Runtime compatibility is stale or incompatible | Runtime semantics, Jitter source identity, or compile profile does not match. | Stop; align all runtimes and re-bake before accepting clients. |

The fallback **Install Jitter2** action is available for a missing Jitter2, not as a general
version-only refresh button. The compatibility status and source hash are therefore more
meaningful than a package-version-only warning for this documentation release.

## Update copied Unity integration

Use **Install/update integration** rather than copying package sources by hand.

The updater:

- compares every receipt-owned file with its recorded hash;
- refuses the update when a recorded file was modified locally;
- stages the new content before replacing existing files;
- updates the assembly definition and scripting define with the integration;
- leaves an external Jitter2 untouched.

When an update is refused, preserve the local changes and decide whether to upstream them, move
them into a consumer-owned extension, or deliberately take ownership of the copy. Do not overwrite
them just to obtain a green status.

If a future release requires a new fallback Jitter2 and the current fallback is incompatible,
back up the project, remove only the unmodified package-owned installation through the Advanced
action, then install Jitter2 and the integration again in that order. Follow that release's
specific migration notes and re-bake requirements.

## Update a server projection

Open installation details, expand **Advanced**, and run
**Install server runtime sources...** against the same projection root.

The projection updater verifies receipt-owned file hashes and refuses to overwrite locally
modified projected sources. After updating:

1. build and test the consumer server;
2. verify the projection against the package;
3. start the server with a known artifact;
4. confirm it reports readiness before connection approval;
5. compare both compatibility values with the Unity client.

See [Dedicated server integration](dedicated-server.md). A successful Unity package test does not
replace this server gate.

## Update imported samples

Unity imports samples as versioned copies:

```text
Assets/Samples/DataSakura Jitter Physics Baker/<package-version>/Physics Baking Demos/
```

An update does not modify or remove an older imported folder. After installing `0.0.12`, choose
one of these explicitly:

- **Keep** the old sample when it is only reference material and still meets your needs.
- **Import** the `0.0.12` sample alongside it to inspect the new version.
- **Compare** and migrate local sample changes before removing an older copy.

Never assume an imported sample is package-owned after import. It is a consumer copy and may
contain local work.

## Migrate the pre-`0.0.3` project layout

Current receipt-managed integration files live under:

```text
Assets/DataSakura/JitterPhysicsBaker/
```

Versions before `0.0.3` used:

```text
Assets/DataSakura/JitterPhysics/
```

To migrate, open installation details, expand **Advanced**, and press
**Migrate pre-0.0.3 layout**.

The migration is explicit and conflict-safe. It refuses to proceed when:

- a receipt-owned file was modified;
- the destination already contains a conflicting asset;
- an unrecorded file may be consumer-authored;
- the receipt cannot establish ownership.

When it is safe, Unity's asset move API moves complete asset folders so `.meta` files and GUIDs
remain stable. Rerunning an already completed migration is expected to be harmless.

Do not reproduce this migration with Finder, Explorer, or a shell copy: changing or duplicating
`.meta` files can break serialized scene and prefab references.

## Migrate legacy baked-artifact names

Current bakes use one stable trio per level:

```text
<level-id>.physics.asset
<level-id>.physics.bytes
<level-id>.physics.manifest.json
```

Readers can recognize exact legacy hash-addressed delivery names, but current writes and exports
use the stable names. When the Bake section reports legacy files, press
**Migrate Legacy Bake Files**.

The migration moves the complete Unity asset set and preserves payload bytes and Unity metadata.
Verify the resulting artifact before deleting backups. Do not rename only the payload: the asset,
manifest, and payload are one validated delivery.

## Serialized assets and public API

Version `0.0.12` does not intentionally rename public namespaces, assembly names, serialized
fields, component types, or artifact schema. Existing scenes and world profiles therefore do not
need a serialized-data migration solely for this update.

For future releases:

- inspect `CHANGELOG.md` before updating;
- treat a schema or runtime compatibility change as a coordinated client/server release;
- do not delete obsolete-looking public types solely because the current repository has no caller;
- compile consumer Editor asmdefs that use `JitterPhysicsEditorApi`;
- keep the package, copied Unity adapter, and projected server sources on one compatible release.

Extension boundaries are documented in [Extending](extending.md), and settings ownership is
documented in [Configuration](configuration.md).

## Verify the upgraded project

Record these results separately:

1. Package Manager resolves the intended tag.
2. The project imports and compiles with the intended Jitter2 mode.
3. **Validate installation** reports understood, reviewed results.
4. Package Edit Mode and Play Mode tests pass.
5. A representative level validates and produces repeatable artifact bytes.
6. Existing artifacts either retain a matching runtime id or are deliberately re-baked.
7. The consumer player build passes for its actual scripting backend and platform.
8. The server projection builds and its startup self-check succeeds.
9. Client and server compare the same artifact hash and runtime compatibility id.
10. Consumer networking/E2E gates pass when the release changes their runtime path.

Do not label steps 7–10 passed from package-only evidence.

## Roll back

1. Restore or pin the previous Git tag in `Packages/manifest.json`.
2. Let Package Manager restore the matching lock entry and compile.
3. Restore compatible copied integration and server-projection sources using the previous package's
   explicit installer, provided receipt-owned files are unmodified.
4. Restore artifacts baked for that runtime id, or re-bake them with the rolled-back toolchain.
5. Keep newer imported sample folders until any local work has been compared and preserved.
6. Re-run the same verification gates used for the update.

A package rollback alone does not roll back copied sources, imported samples, or server content.
If rollback produces a different runtime compatibility id, refuse mixed-version connections until
client, server, and artifacts agree again.
