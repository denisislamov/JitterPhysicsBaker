# Editor guide

[Documentation home](index.md) · [Quick Start](quick-start.md) ·
[Configuration](configuration.md) · [Troubleshooting](troubleshooting.md)

This page describes the Editor workflow and visible side effects of DataSakura Jitter Physics
Baker `0.7.0`. The package performs no install, bake, export, upload, or removal on import or
selection. Those operations begin only from an explicit command.

## Entry points

The package has one Tools entry:

**Tools > DataSakura > Jitter Physics Baker Window**

The window title is **DS Jitter Physics**. Its tabs are **Overview**, **Geometry**, **Bake**,
**Settings**, and **Diagnostics**.

Authoring objects are available from:

- **Add Component > DataSakura > Jitter Physics > Jitter Physics Level**;
- **Add Component > DataSakura > Jitter Physics > Jitter Static Body Source**;
- **Assets > Create > DataSakura > Jitter Physics > World Profile**.

Project and personal settings are available from:

- **Project Settings > DataSakura > Jitter Physics**;
- **Preferences > DataSakura > Jitter Physics > Scene Preview**.

Sample commands appear under **Assets > DataSakura > Jitter Physics > Samples** only after the
native UPM sample has been imported.

## Shared window controls

The horizontal tab toolbar remains visible at the top. Below it, **Jitter Physics Level** selects
the level operated on by all tabs. The **Validate** action is a one-shot check; nothing validates
continuously in the background.

The status line uses these states:

- `[ ]  No level selected`
- `[ ]  Not validated - press Validate`
- `[X]  N errors - bake blocked`
- `[!]  N warnings`
- `[v]  Ready to bake`

After validation, the line includes its time. `(data changed)` means the displayed result is stale
because tracked authoring data changed.

If there is no level, press **Create Level**. This creates and selects a **Jitter Physics Level**
GameObject, assigns its transform as **Geometry Root**, creates the shared default profile when
needed, assigns the project generated folder, and saves project assets. Save the scene yourself.

## Overview

**Overview** combines a read-only summary with the primary authoring references.

The summary shows:

- **Level ID**;
- **Geometry root**;
- **World profile**;
- **Static body sources**;
- **Output folder**;
- **Last artifact**.

The editable level setup contains **Level Id**, **Geometry Root**, **World Profile**, and
**Generated Folder**. A level without a world profile cannot validate or bake.

Profile actions are explicit:

- **Edit** selects the assigned profile. The UI warns when loaded levels share it.
- **New** creates a profile from the project default and assigns it to this level.
- **Make Local Copy** copies every value into `<Profiles Folder>/<level-id>_WorldProfile.asset`
  and reassigns only this level. The reassignment supports Undo and prefab overrides.

### Validation behavior

Validation converts the current authoring data in memory, reports every issue, and selects the
offending object when **Select** is available. It does not create or replace the three baked
artifact files.

Validation is not completely mutation-free: an empty or non-canonical **Level Id** or
**Source Id** can be normalized and assigned to the authoring object. Save or revert those scene
changes deliberately.

Expected successful state: `[v]  Ready to bake`, optionally with warnings that do not block the
bake.

## Level Inspector

The custom `JitterPhysicsLevel` Inspector contains **Level**, **Geometry Root**, **Settings**, and
**Bake Status** sections. **Validate**, **Bake**, and **Open** call the same package workflow as the
main window. **Bake** is disabled in Play Mode.

Expand **Advanced** to edit **Generated Folder** and inspect the read-only
**Last Artifact Hash**.

The Inspector action layout becomes compact below 330 pixels and horizontal at wider Inspector
sizes. The operation semantics do not change.

## Geometry

Only explicitly marked `JitterStaticBodySource` objects are baked. **Geometry** lists the sources
under the selected level's Geometry Root and shows their shape summary.

Each source exposes:

- **Source Id** — persistent body identity and canonical ordering key;
- **Include Children** — include enabled colliders on active child objects;
- **Friction** and **Restitution** — material values written on the static body;
- **Select** — select the authoring object;
- **Remove** — immediately remove the component through Unity Undo, without a confirmation dialog.

Select a GameObject under the Geometry Root and press **Add Source to _name_**. When there is no
eligible selection, the disabled action reads **Select a GameObject to add a source**. A source
outside the configured root is reported rather than silently included.

Supported components are `BoxCollider`, `SphereCollider`, `CapsuleCollider`, and `MeshCollider`.
Disabled colliders and inactive objects are excluded. Triggers and unsupported collider types are
errors. A non-uniformly scaled sphere is converted with its largest scale axis and reported as a
warning.

## Bake

The delivery actions are:

- **Build for Client** — validate and write one deterministic artifact for Unity;
- **Upload to Server** — send those exact locally verified bytes by HTTP;
- **Export to Folder** — copy the same payload and manifest for offline delivery.

**Build for Client** is disabled when no compatible Jitter2 is available, a legacy migration is
required, Unity is in Play Mode, or an upload is in progress. In Play Mode the window shows:

```text
Play Mode: Build for Client is disabled because the scene belongs to the simulation.
```

A successful bake creates or updates stable names under **Generated Folder**:

```text
<level-id>.physics.bytes
<level-id>.physics.manifest.json
<level-id>.physics.asset
```

The Unity asset is updated in place so existing serialized references survive a successful
re-bake. The payload and manifest are staged and verified first, and their pair writer restores
the previous pair when a caught replacement operation fails. Unity imports that published pair
before updating `.physics.asset`, however. A later import or asset-update failure can therefore
leave a new payload/manifest beside the previous Unity asset. Treat any failed bake as unusable,
verify all three files, and re-bake before delivery. The current failure text saying the previous
artifact was left in place is stronger than this late-failure guarantee.

The Build summary provides **Asset**, **Binary**, **Manifest**, **Details**, and
**Copy Diagnostics**. If old hash-suffixed files remain, use **Migrate Legacy Bake Files** before
baking.

### Remove baked physics

**Remove baked physics** opens **Remove baked physics?** and lists the exact asset, payload, and
manifest paths. **Delete Files** removes those local generated files and clears the level's last
artifact hash. **Cancel** changes nothing. The dialog explicitly notes that server copies are not
removed.

## Settings

**Settings** embeds the assigned world profile Inspector and links to:

- **Open Project Settings**;
- **Open Scene Preview Preferences**.

Server delivery fields are **Base URL**, **Timeout (seconds)**, and **Upload token**. These are
Editor delivery preferences; they are not baked world settings.

Expand **Advanced installation and maintenance** to view the compatibility summary, package,
schema, and runtime identity. Available actions include **Open installation details**,
**Copy compatibility JSON**, and **About package**.

The current main window requires a selected level before **Settings** is shown. In a clean scene,
use **Create Level** first.

## Jitter Physics — Setup

The Setup window displays **Status**, **Baking allowed**, expected and actual source hashes,
compile profile ID, runtime compatibility ID, hashed files, and discovered assembly definitions.
Use **Refresh**, **Copy report JSON**, or **Export report...** to inspect or share the report.

Installation actions are:

- **Validate installation**;
- **Install Jitter2** when status is `Missing` and the release contains a Unity-compatible fallback;
- **Install/update integration** after Jitter2 exists;
- **Open Package Manager samples**.

Under **Advanced**, **Migrate pre-0.0.3 layout**, **Install server runtime sources...**, and
**Remove package-owned installation** are explicit maintenance operations. Receipt-owned files
are overwritten or removed only when their current hashes still match the receipt. External and
locally modified files are retained and reported.

All install, update, migration, and removal actions are refused in Play Mode because they can
reload assemblies.

## Diagnostics

**Diagnostics** discovers baked artifact assets and exposes the selected artifact's Level ID,
schema, tick rate, contents, generator version, payload hash, and runtime compatibility ID.

Artifact actions:

- **Select asset**;
- **Copy hash**;
- **Verify**;
- **Export payload and manifest...**;
- **Export embedded provider (.g.cs)...**;
- **Delete this artifact**.

The embedded provider defaults to namespace `DataSakura.JitterPhysics.Generated`. It is intended
for controlled server delivery; changing the artifact requires regenerating and recompiling that
source.

Project-wide diagnostics are:

- **Repeat-bake determinism check** — bakes twice and requires identical bytes;
- **Codec round-trip of every baked artifact** — requires every artifact to decode and re-encode
  identically;
- **Runtime compatibility of every baked artifact** — reports `OK` or `STALE` for the current
  Jitter2/runtime semantics;
- **Copy report**.

Diagnostics verify existing artifacts. Export and upload refuse a payload or manifest that fails
local verification.

## Scene View preview

Open the native **Jitter Physics** overlay in Scene View.

Controls:

- **Sources** — current Unity authoring geometry;
- **Baked** — records decoded from the selected saved artifact;
- **Runtime** — records from an active `IJitterPhysicsRuntimePreviewSource`;
- **Scope** — active/selected level or all loaded levels;
- **Occlusion** — **Visible** or **X-Ray**;
- **Frame Level**;
- **Settings**.

All three layers default to off. Scope defaults to active/selected level and occlusion defaults to
Visible. These personal choices live in EditorPrefs and do not change scenes, profiles, artifacts,
or compatibility identity.

The overlay distinguishes current, baked, moved, changed, removed, runtime, and invalid records by
line style, width, fill, hatching, markers, and color. It does not draw Unity Colliders as fake
runtime geometry. Without an active provider, Runtime reports `No runtime data`.

## Imported samples

Install Jitter2 and the integration adapter before importing **Physics Baking Demos** from Package
Manager. The imported sample adds these commands:

- **Build and bake: Bouncing Ball**;
- **Build and bake: FPS Shooter**;
- **Bake level in the open scene**;
- **Verify determinism: bake the open level twice**;
- **Validate level in the open scene**.

The two build commands ask about unsaved scene changes, create a new scene, generate authoring
objects, save under the imported sample's `Scenes` folder, bake under its `Generated` folder, add
runtime components, assign the artifact, and save again. They do not run on import and do not add
the generated scene to Build Settings.

Expected Bouncing Ball result: the Console reports that the sample is ready; Play Mode reports
`sample world ready`; the Game view shows `Artifact verification: PASSED`; Space drops a ball and
Backspace clears the dynamic balls.

Expected FPS Shooter result: the Console reports that the sample is ready; Play Mode accepts WASD
movement and left-mouse firing and displays the sample runtime status.

## Side-effect reference

| Action | Persistent effect |
| --- | --- |
| Open the Baker, Setup, Settings, Diagnostics, or overlay | None. |
| **Create Level** | Creates a scene object; may create and assign the shared default profile; saves project assets. |
| **Validate** | Writes no artifact files; may normalize and assign empty Level/Source IDs and dirty their scene objects. |
| Add/remove a source | Adds or removes a component through Unity Undo. |
| Profile **New** / **Make Local Copy** | Creates an asset and reassigns the level. |
| **Build for Client** | Creates or replaces the three stable artifact files and updates Last Artifact Hash. |
| **Export to Folder** | Writes a verified payload and manifest to the selected external folder. |
| **Upload to Server** | Sends the verified payload and manifest; it does not modify the server's running world. |
| **Repeat-bake determinism check** | Writes no artifact files; it builds bytes in memory and can normalize empty authoring IDs. |
| **Export report...** | Writes the current Setup report as JSON to the selected filesystem path. |
| **Install Jitter2**, **Install/update integration**, project-layout migration, or package-owned removal | Changes reviewed receipt-owned files under the Unity project and may change the integration scripting define. |
| **Install server runtime sources...** | Writes projected sources and `JitterPhysics.projection.json` to the selected external folder, then records that projection in the project receipt. |
| Import a sample | Creates a versioned consumer copy under `Assets/Samples`. |
| Build a sample scene | Replaces the open scene after confirmation, then creates scene, profile, and generated artifact files. |
| Preview toggles | Update personal EditorPrefs only. |
