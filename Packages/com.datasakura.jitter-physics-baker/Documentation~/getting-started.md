# Getting started: choose a route

[Documentation home](index.md) ·
[Requirements and compatibility](requirements-and-compatibility.md) ·
[Installation](installation.md) · [Troubleshooting](troubleshooting.md)

This path is retained for compatibility with links from earlier package versions. The manual is
now split into focused pages so installation, authoring, runtime, and server guidance have one
maintained source each.

Choose the route that matches the result you need.

| Goal | Recommended route |
| --- | --- |
| See a working bake and runtime result in 5–15 minutes | Follow [Quick Start](quick-start.md). |
| Bake collision for an existing scene | Use the concise authoring route below, then the [Editor guide](editor-guide.md). |
| Load the artifact in an existing Unity architecture | Read [Runtime API](runtime-api.md) and [Integration](integration.md). |
| Start a dedicated server from the same artifact | Read [Dedicated server](dedicated-server.md). |
| Update an older installation | Start with [Migration and upgrading](migration-and-upgrading.md). |
| Diagnose a failure | Match the symptom in [Troubleshooting](troubleshooting.md). |

## Fastest verified sample route

This route produces a generated scene, a deterministic artifact, and a running Jitter2 result
without requiring you to design an integration first.

1. Install the pinned package release:

   ```text
   https://github.com/denisislamov/jitter-physics-baker.git#v0.7.0
   ```

2. Open **Tools > DataSakura > Jitter Physics Baker Window**.
3. In a clean scene, press **Create Level**. The current Settings workflow requires a selected
   `JitterPhysicsLevel`; this command also creates and assigns the default world profile when
   needed. Save the scene.
4. Open **Settings > Advanced installation and maintenance > Open installation details**.
5. If the status is `Missing`, press **Install Jitter2**. If a project-owned copy is already
   `Compatible`, keep it. Resolve `Incompatible`, `Duplicate`, or `UnsupportedPlugin` before
   continuing.
6. Press **Install/update integration**, then **Validate installation**.
7. Open **Window > Package Manager**, select **DataSakura Jitter Physics Baker**, open **Samples**,
   and import **Physics Baking Demos**.
   If the project uses a source-based `Jitter2.Core` asmdef rather than the receipt-owned fallback,
   add `"Jitter2.Core"` to the imported Runtime sample asmdef's `references` array before continuing;
   the direct sample reference is documented in [Quick Start](quick-start.md#the-imported-sample-cannot-resolve-jitter2-or-the-integration).
8. Run **Assets > DataSakura > Jitter Physics > Samples > Build and bake: Bouncing Ball**.
9. Enter Play Mode and press Space.

Expected result: the generated ball collides with the baked floor, ramp, and primitives, then
eventually sleeps. The Artifact Verification component reports that the loaded bytes passed its
payload hash, decode/schema, Level ID, body-count, and tick-rate checks. It displays the payload's
runtime compatibility ID for comparison; the sample does not prove that ID against an independent
build identity.

If the sample assembly cannot resolve the integration, return to step 6. For detailed steps,
expected Console output, and the two most common setup errors, use
[Quick Start](quick-start.md).

## Author and bake your own level

After completing [Installation](installation.md):

1. Create or select one `JitterPhysicsLevel` for the scene.
2. Assign its Level ID, Geometry Root, World Profile, and Generated Folder. The Level ID is a
   persistent content/network identity, not a display label.
3. Add `JitterStaticBodySource` only to objects that should become static Jitter2 bodies. The
   package converts marked `BoxCollider`, `SphereCollider`, `CapsuleCollider`, and
   `MeshCollider` geometry; unmarked colliders are ignored.
4. Keep each generated Source ID stable. Renaming a GameObject does not require a new ID.
5. Press **Validate** before baking. It writes no artifact files, but it can normalize and assign
   empty Level/Source IDs and dirty those authoring objects. Review that scene change, resolve every
   error, and review warnings.
6. Press **Build for Client**.

A successful bake writes one delivery trio:

```text
<level-id>.physics.asset
<level-id>.physics.bytes
<level-id>.physics.manifest.json
```

The binary is the canonical payload, the manifest carries its full hash and compatibility
metadata, and the Unity asset provides a stable project reference. Treat all three as one unit.

Use **Diagnostics > Repeat-bake determinism check** after changing authoring or package code. Two
bakes of unchanged input must produce identical bytes and SHA-256.

Field defaults, folder ownership, shared-profile behavior, and the current `SubstepCount`
limitation are documented in [Configuration](configuration.md). Every window, Inspector action,
overlay layer, and expected result is documented in the [Editor guide](editor-guide.md).

## Load the artifact in Unity

The consumer owns the Jitter2 world and tick loop:

1. After explicit Setup, load and validate the `JitterPhysicsArtifactAsset` through
   `JitterNativeUnityArtifactLoader.Load(...)`.
2. Create a new Jitter2 `World`.
3. Apply the artifact once with `JitterPhysicsWorldBuilder.Apply(...)`.
4. Refuse startup on a typed load or build error.
5. Create dynamic bodies only after the static artifact succeeds.
6. Step at `1f / artifact.WorldSettings.TickRate`; do not substitute Unity's fixed timestep.

After a failed apply, inspect `RequiresWorldDiscard`; discard the world when complete restoration
could not be proven. `TopologyFingerprint` is diagnostic; compare
`artifactHash + runtimeCompatibilityId` for compatibility.

The complete call sequence, ownership rules, cleanup, and compilable examples are in
[Runtime API](runtime-api.md). Assembly references and integration patterns are in
[Integration](integration.md), with focused variants in [Recipes](recipes.md).

## Bring up a dedicated server

The package supplies portable sources and startup contracts, not a server executable.

1. In installation details, expand **Advanced** and run
   **Install server runtime sources...** into an SDK-style server source folder.
2. Import `JitterPhysics.Runtime.props` from that projection; do not compile or resolve a second
   server-owned Jitter2.
3. Deliver the `.physics.bytes` and `.physics.manifest.json` pair, or use a generated embedded
   provider.
4. Pass `jitterAssemblySha256` from `JitterPhysics.projection.json` in
   `JitterPhysicsServerOptions`, then call `JitterPhysicsServerStartup.Start(...)` before enabling
   connection approval.
5. Require `IsReady`; log the self-check only after successful load, compatibility validation,
   and static-world construction.
6. Carry both compatibility values in the consumer's own handshake and refuse a mismatch before
   spawning a player.

The server keeps ownership of `World.Step`, networking, authoritative state, deployment, and
content delivery. Continue with [Dedicated server](dedicated-server.md).

## Update or migrate

Imported samples, installed integration files, and server-projected sources are copies; changing
the UPM tag does not overwrite them automatically.

For `0.7.0`, the schema remains 1 but runtime compatibility changes. Update the separately
installed Jitter runtime, integration and server projection together, then re-bake and re-export
every affected level. Never combine a `0.0.12` payload/manifest with the `0.7.0` runtime.

Use [Migration and upgrading](migration-and-upgrading.md) for the safe decision table, pre-`0.0.3`
layout migration, legacy bake-name migration, sample lifecycle, verification, and rollback.

## If the expected result is missing

| Symptom | First check |
| --- | --- |
| Package imports but Bake is blocked | Confirm exactly one Jitter2 and a `Compatible` status. |
| Settings shows only **Create Level** | Create/select a level; Settings currently requires one. |
| Imported sample has a missing assembly | Install/update the integration before importing or compiling the sample. |
| Artifact is reported stale | Compare runtime compatibility IDs and re-bake only after aligning Jitter2 and runtime semantics. |
| World build fails | Discard the failed world and inspect the typed error before retrying. |
| Player build fails but Editor works | Treat the player backend/platform as a separate gate; inspect AOT, stripping, and managed-plugin errors. |

Continue with the full symptom-to-fix table in [Troubleshooting](troubleshooting.md).
