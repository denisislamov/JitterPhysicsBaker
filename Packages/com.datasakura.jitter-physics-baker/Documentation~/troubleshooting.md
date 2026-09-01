# Troubleshooting

[Documentation home](index.md) · [Installation](installation.md) ·
[Quick Start](quick-start.md) · [Editor guide](editor-guide.md)

This page maps messages emitted by DataSakura Jitter Physics Baker `0.7.0` to the smallest
relevant check and fix. Terms in braces are values inserted into the source message by the
Editor or runtime.

Start with the first error in the Console or issue list. Later errors are often consequences of
that first failure. Click **Select** when an issue provides an authoring context.

## Setup and installation

| Exact message or status | Cause | Check | Fix |
| --- | --- | --- | --- |
| `Missing` | No `Jitter2.Core` was discovered. | Open **Jitter Physics — Setup** and inspect assembly definitions and hashes. | Exit Play Mode, then press **Install Jitter2**, or add one compatible project-owned source copy. |
| `Duplicate` | More than one `Jitter2.Core` definition was discovered. | Inspect every assembly definition reported by Setup. | Keep one intended Jitter2 and remove the duplicate from the consumer project. |
| `Incompatible` | The discovered source identity differs from `jitter2.lock.json`. | Compare expected and actual source hashes and compile profile IDs. | Align client and server on one supported Jitter2 before baking. |
| `UnsupportedPlugin` | A precompiled Jitter2 exists without the package receipt, so source identity cannot be proven. | Check whether the DLL belongs to another package or manual installation. | Replace it with a compatible source copy or the receipt-owned fallback. |
| `Installing while in Play Mode would reload assemblies under a running simulation. Exit Play Mode first.` | An install, update, migration, or removal was requested during Play Mode. | Check the Play button and pending Play Mode transition. | Exit Play Mode and retry the same explicit action. |
| `The package root could not be resolved, so there is nothing to copy from.` | The installed package root cannot be located. | Confirm that Package Manager resolves `com.datasakura.jitter-physics-baker`. | Restore the package dependency, let Package Manager finish, then reopen Setup. |
| `The prebuilt Jitter2 assembly is missing from the package; run tools~/build-jitter2-unity.sh.` | The package contents are incomplete. | Inspect `Jitter2~/Prebuilt/Jitter2.Core.dll` in the package source. | Reinstall a complete `0.7.0` package; run the named build script only when developing the package itself. |
| `This project already has a Jitter2.Core that the package did not install, so the fallback copy is not needed and will not be added.` | The consumer already owns a Jitter2 copy. | Read the compatibility text appended to the message. | Keep and align the external copy, or remove it deliberately before choosing the fallback. |
| `The project already provides System.Runtime.CompilerServices.Unsafe, so the package copy was not installed.` | Another package supplies the dependency. | Confirm that exactly one compatible assembly is included for the target player. | Keep the existing assembly when the player build resolves it; do not install a duplicate. |
| `'System.Runtime.CompilerServices.Unsafe.dll' is not in the project, but the installed Jitter2 assembly references it.` | The Editor can resolve Unsafe from its toolchain, but the consumer project cannot deliver it to a player. | Search the project for the assembly and run the actual player build. | Re-run **Install Jitter2** to place the receipt-owned dependency. |
| `Folder must be a project-relative path under Assets/.` | A project folder is absolute, outside `Assets`, or contains `..`. | Check **Profiles Folder** and **Generated Folder** in Project Settings. | Use `Assets` or a descendant such as `Assets/Generated/JitterPhysics`. |

If imported sample scripts cannot resolve adapter or Jitter2 types, first verify that
**Install/update integration** completed before the Package Manager sample was imported. With the
receipt-owned precompiled fallback, Jitter2 is auto-referenced. With a compatible external
source-based Jitter2, add `"Jitter2.Core"` to the `references` array of the consumer-owned imported
`Runtime/DataSakura.JitterPhysics.Samples.asmdef`; the sample uses Jitter2 types directly and asmdef
references are not transitive through the integration adapter. Do not edit the Package Cache copy.

## Selection and validation

| Exact message or status | Cause | Check | Fix |
| --- | --- | --- | --- |
| `[ ]  No level selected` | The Baker window has no active `JitterPhysicsLevel`. | Inspect the **Jitter Physics Level** selector. | Select an existing level or press **Create Level**. |
| `[ ]  Not validated - press Validate` | No one-shot validation result exists for the selected level. | Confirm the intended level is selected. | Press **Validate**. Review any ID assignments before saving the scene. |
| `No JitterPhysicsLevel was supplied.` | A validate/bake API call received no level. | Inspect the caller or current scene selection. | Pass or select the intended `JitterPhysicsLevel`. |
| `The runtime compatibility id is unavailable. Baking requires a Jitter2 copy that matches jitter2.lock.json; see the Setup window.` | Setup cannot prove compatible runtime semantics. | Open Setup and inspect Status, hashes, and duplicates. | Install or align exactly one compatible Jitter2, then validate again. |
| `The level id '{levelId}' is not canonical.` | The Level ID cannot be used as a stable artifact identity. | Select the level from the issue context and inspect **Level Id**. | Set a canonical unique ID, then validate again. |
| `No world profile is assigned. The world settings are part of the artifact, so there is no safe default to fall back on.` | **World Profile** is empty. | Inspect the selected level's Overview or Inspector. | Assign a profile, or use **New** / **Make Local Copy**. |
| `The level contains no static bodies. Mark the geometry with JitterStaticBodySource before baking.` | No valid explicit sources were collected. | Check Geometry Root, source placement, and active hierarchy state. | Add `JitterStaticBodySource` to intended active geometry below the root. |
| `The source id '{sourceId}' is not canonical.` | A source has an unusable stable identity. | Click **Select** and inspect **Source Id**. | Assign a canonical unique Source ID. |
| `Duplicate Source Id '{sourceId}': '{sourceName}' and '{previousName}' both use it.` | A duplicated GameObject copied the persistent Source ID. | Use **Select** and compare both named objects. | Change **Jitter Static Body Source > Source Id** on one object to a unique canonical value. |
| `'{sourceId}' has no convertible colliders. A static body without collision geometry is never intended; remove the source or add a collider.` | Every collider under the source was absent, disabled, inactive, or rejected. | Select the source and inspect its colliders and **Include Children**. | Add a supported enabled collider, activate it, or remove the unused source. |
| `Validation found {errorCount} error(s). Nothing was written.` | At least one blocking authoring issue exists. | Read the complete issue list and use **Select** on each contextual issue. | Correct every error and validate again. Warnings alone do not block baking. |

Validation writes no artifact files. It can normalize and assign empty Level/Source IDs, so a
dirty scene immediately after validation can be expected authoring state rather than an artifact
write.

## Collider conversion

| Exact message | Cause | Check | Fix |
| --- | --- | --- | --- |
| `Triggers describe volumes for gameplay, not collision geometry.` | A marked collider has **Is Trigger** enabled. | Select the issue context and inspect the Collider. | Disable **Is Trigger** for collision geometry or remove that collider/source from the bake. |
| `{colliderType} is not supported by artifact schema 1.` | The source contains an unsupported Collider subclass. | Inspect the contextual component type. | Use `BoxCollider`, `SphereCollider`, `CapsuleCollider`, or `MeshCollider`. |
| `The transform contains NaN or infinity.` | A collider transform or scale is not finite. | Inspect transform values and scripts that assign them. | Restore finite position, rotation, and scale values. |
| `The scaled size {size} has an extent of zero.` | A BoxCollider becomes degenerate after scale. | Inspect Box Size and transform scale. | Give every scaled axis an extent of at least `1e-5`. |
| `The scaled radius {radius} is zero.` | A SphereCollider or CapsuleCollider becomes degenerate after scale. | Inspect Radius and transform scale. | Use a scaled radius of at least `1e-5`. |
| `Non-uniform scale {scale} cannot be represented by a sphere. The largest axis was used, so the shape is a conservative over-approximation (radius {radius}).` | A sphere is non-uniformly scaled. | Compare the three lossy-scale axes. | Accept the warning and conservative larger sphere, or author uniform scale/different geometry. |
| `The mesh collider has no mesh assigned.` | `MeshCollider.sharedMesh` is null. | Inspect the MeshCollider. | Assign the intended mesh or remove the collider/source. |
| `'{meshName}' is not readable. Enable Read/Write in the model import settings; the baker cannot read vertex data otherwise.` | Unity does not expose the mesh vertices to the baker. | Select the mesh asset and inspect its import settings. | Enable **Read/Write**, apply the importer, and validate again. |
| `'{meshName}' has no triangles.` | The mesh contains no usable vertices or triangle indices. | Inspect the imported mesh data. | Supply a triangle mesh with vertices and indices. |
| `'{meshName}' has {indexCount} indices, which is not a multiple of three.` | Triangle index data is malformed. | Inspect the source mesh generation/import. | Regenerate the mesh with complete triangle triplets. |
| `Vertex {vertexIndex} of '{meshName}' is NaN or infinite after transformation.` | Mesh data or its transform produces a non-finite point. | Inspect that mesh and transform chain. | Repair the source vertices or transforms and reimport. |

## Bake, artifact, export, and upload

| Exact message | Cause | Check | Fix |
| --- | --- | --- | --- |
| `Baking is not available in Play Mode. Exit Play Mode and bake from the authored scene state.` | A bake was requested while simulation state can differ from authored state. | Check Play Mode. | Exit Play Mode and bake the saved authored scene. |
| `This level still uses legacy bake file names. Run 'Migrate Legacy Bake Files' from the Bake tab before baking so Unity references and payload bytes are preserved.` | Old hash-suffixed artifact files were detected. | Open the Bake tab and review the migration notice. | Press **Migrate Legacy Bake Files**, review the result, then bake. |
| `Bake failed; the previously baked artifact was left untouched.` | Validation, conversion, verification, or writing failed. The message overstates late-failure rollback: payload/manifest publication happens before Unity imports them and updates `.physics.asset`. | Read the preceding issue first, then verify the payload, manifest, and Unity asset as one trio. A late import failure can leave the new pair beside the previous asset. | Do not deliver or run the trio after a failed bake. Correct the first cause, re-bake, and run **Verify**; restore all three files from one known bake if retry is impossible. |
| `No artifact was selected.` | An export operation has no selected artifact. | Open Diagnostics and inspect **Artifact**. | Select the intended baked artifact. |
| `Artifact '{artifactName}' has no payload; re-bake the level.` | The Unity artifact asset has lost its payload reference. | Use **Select asset** and inspect the payload reference. | Re-bake the owning level. |
| `The manifest of '{levelId}' is missing next to its payload; re-bake the level.` | The payload/manifest pair is incomplete. | Use **Binary** and **Manifest** in the Build summary. | Restore both files from the same bake or re-bake. |
| `Refusing to export an artifact that does not decode: {error}` | Payload, hash, schema, manifest, or limits validation failed. | Press **Verify** and retain the typed error code/message. | Re-bake from trusted authoring data; do not export the rejected bytes. |
| `Upload refused: the current artifact did not pass local verification.` | Local artifact preparation failed. | Verify the selected artifact in Diagnostics. | Repair or re-bake it before uploading. |
| `No verified artifact is available.` | The uploader received no successful verified delivery. | Check artifact selection and verification. | Select and verify a complete artifact pair. |
| `Server URL must be an absolute HTTP(S) URL.` | **Base URL** is relative or uses another scheme. | Inspect Settings > Base URL. | Enter an absolute `http://` or `https://` URL. |
| `Artifact uploaded. Restart the server to load it.` | The endpoint accepted the artifact and returned no custom message. | Confirm the response and server storage independently. | Restart or reload the server according to its own lifecycle. |
| `Upload failed ({responseCode}): {requestError}` | HTTP transport or the endpoint failed. | Record status, request error, server logs, URL, timeout, and authentication policy. | Correct the endpoint/network/token issue, then retry the same verified bytes. |
| `Upload response failed: {exceptionMessage}` | The response could not be parsed or processed. | Capture the complete exception and raw server behavior. | Fix the endpoint response or client/server contract before retrying. |

## Scene View preview

| Exact message | Cause | Check | Fix |
| --- | --- | --- | --- |
| `No active JitterPhysicsLevel.` | The current Scope finds no active or selected level. | Check the level selection and **Scope**. | Select a level or switch Scope to all loaded levels. |
| `No runtime data` | No active `IJitterPhysicsRuntimePreviewSource` is publishing records. | Enter Play Mode and verify the runtime world reached ready state. | Start the intended runtime provider; do not treat Unity Colliders as runtime records. |
| `Preview unavailable: {cacheError}` | Preview cache rebuilding failed. | Capture the complete appended cache error and current scene state. | Correct that source/artifact error, then trigger a hierarchy/project refresh. |

All preview layers default to off. If nothing is drawn and there is no message, enable
**Sources**, **Baked**, or **Runtime** explicitly and check **Visible** versus **X-Ray**.

## Bouncing Ball sample

| Exact message | Cause | Check | Fix |
| --- | --- | --- | --- |
| `No JitterPhysicsLevel in the open scene. Build a sample scene first.` | A sample validate/bake command was run in a scene without a level. | Inspect the open scene hierarchy. | Run **Build and bake: Bouncing Ball** or open a generated sample scene. |
| `No artifact is assigned. Bake the sample level first.` | `JitterPhysicsSampleWorld` has no artifact. | Inspect **Jitter Physics Runtime > Artifact**. | Re-run the sample build-and-bake command or assign the matching artifact. |
| `No artifact assigned.` | The verification component has no artifact. | Inspect `JitterPhysicsArtifactVerificationSample`. | Assign the same artifact used by `JitterPhysicsSampleWorld`. |
| `Artifact verification: FAILED` | At least one payload/hash/decode/metadata check failed. | Read the detailed Game view report and the preceding Console error. | Re-bake and reassign the complete artifact; do not continue from a failed runtime world. |
| `[JitterPhysics] Bouncing Ball sample is ready. Press Play, then Space to drop a ball.` | Generation and bake completed. | Confirm the generated scene and artifact paths. | Enter Play Mode. |
| `Artifact verification: PASSED` | Payload, hash, decode, Level ID, body count, and tick-rate checks passed. | Confirm the Console also reports `sample world ready`. | Use Space and Backspace to exercise collision and cleanup. |

The sample scene generator saves under its imported `Scenes` folder but does not add the scene to
Build Settings. Add `SampleBouncingBall` explicitly before running a player or fixture that loads
it by scene name.

## Runtime load and startup

| Exact message | Cause | Check | Fix |
| --- | --- | --- | --- |
| `No Unity artifact payload was supplied.` | The native Unity runtime loader received a null asset or missing payload. | Inspect the consumer component or startup code that calls `JitterNativeUnityArtifactLoader.Load`. | Assign the intended `.physics.asset` before startup and fail the scene/match startup while it is null. |
| `Artifact asset '{assetName}' has no payload assigned. The .bytes file was probably deleted or excluded from the build.` | The Unity artifact asset has no serialized payload reference. | Select the asset and confirm that the matching `.physics.bytes` exists and is included in the project/build. | Restore the complete artifact trio from one bake or re-bake and reassign the asset. |
| `Artifact was baked for runtime {artifactRuntimeId}, this build is {expectedRuntimeId}.` | The artifact and current client/server build use different runtime semantics. | Compare the full runtime compatibility IDs and the exact package/Jitter2 sources on both peers. | Align the runtimes, re-bake with that runtime, and redeploy the same payload/manifest pair. |
| `This world already has the static artifact of level '{levelId}' applied. A new artifact needs a new world; hot reloading a running match world is not supported.` | `JitterPhysicsWorldBuilder.Apply` was called a second time for the same world. | Trace world ownership and every `Apply` call before the first `World.Step`. | Apply once to a new candidate world. For a new level or artifact, dispose the old world and construct a new one. |
| `Building the world failed...` | Jitter2 threw while static bodies were being created. The builder attempted to restore bodies and settings. | Retain the typed error and inspect `RequiresWorldDiscard`. | When discard is required, dispose the candidate world; otherwise retry only after correcting the artifact/runtime. |
| `This server was launched to host '{expectedLevelId}', but the artifact describes '{artifactLevelId}'.` | The mounted artifact does not match the server's expected level. | Compare the launcher's `ExpectedLevelId`, mounted payload/manifest, and decoded Level ID. | Mount the correct delivery trio or correct the launch configuration; do not accept clients while startup is failed. |
| `This server steps at {serverTickRate} Hz, the level was baked for {artifactTickRate} Hz; prediction assumes both sides agree.` | The server loop tick rate differs from the baked profile. | Compare `JitterPhysicsServerOptions.TickRate` with `artifact.WorldSettings.TickRate`. | Run the server at the baked tick rate or intentionally change the profile, re-bake, and redeploy to every peer. |
| `[JitterPhysics] Static physics world is not ready: {error}` | `RequireReady()` was called after provider, expectation, or world-build startup failed. | Inspect `state.Error` and `state.Source`; diagnose that first typed failure rather than this wrapper. | Fix the underlying load/build mismatch and call gameplay or connection approval only when `state.IsReady` is true. |

Load and world construction are synchronous startup operations. Create a new world, load and verify
the artifact, call `Apply` once, and only then create gameplay bodies or call `World.Step`. Do not
race those operations with additive-scene activation, another startup path, or the simulation
loop. A scene transition that needs different static geometry needs a new world; this release has
no merge, unload, or hot-reload operation.

## Package maintainer checks

Run these commands from the development repository root before publishing a package change:

```sh
python3 tools/verify-package-meta.py
python3 "Packages/com.datasakura.jitter-physics-baker/tools~/verify-jitter2-lock.py"
python3 "Packages/com.datasakura.jitter-physics-baker/tools~/test-jitter2-lock.py"
bash "Packages/com.datasakura.jitter-physics-baker/tools~/test-dotnet.sh"
bash tools/run-unity-tests.sh all
```

The first four prove package layout, the pinned Jitter2 identity, portable compilation, and
shared .NET tests. The last command is a separate Unity EditMode/PlayMode gate and requires the
development project to be unlocked by another Editor process. Run target-player and consumer
installation acceptance separately; neither is implied by these commands.

## Information to attach to a report

Include the Unity and package versions, Setup report JSON, complete first error, level ID, short
artifact hash, short runtime compatibility ID, and whether the failure occurred during setup,
validation, bake, load, build, step, export, or upload. Do not include the upload token.
