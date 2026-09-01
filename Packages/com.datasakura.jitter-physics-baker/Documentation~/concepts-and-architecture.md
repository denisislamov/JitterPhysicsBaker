# Concepts and architecture

This page explains the boundaries behind the authoring workflow. Read it before replacing a
provider, moving the tick loop, projecting sources into a server, or changing artifact
delivery.

[Back to the manual](index.md)

## The problem being solved

A networked game commonly has two independent descriptions of static collision geometry:
Unity colliders for a predicting client and hand-written or exported Jitter2 geometry for an
authoritative server. They can look correct in isolation while differing in a wall transform,
mesh, material, creation order, or world setting.

This package makes the level artifact the shared description. The Editor creates canonical
records and bytes once. Client and server independently verify those bytes and call the same
builder. The server remains authoritative; the package removes avoidable topology drift rather
than promising cross-runtime floating-point identity.

## Vocabulary

| Term | Meaning |
| --- | --- |
| Level | One `JitterPhysicsLevel` and the explicitly marked sources it collects. |
| Source | One `JitterStaticBodySource`; its accepted colliders become one static body. |
| World profile | Shared authoring values baked into `PhysicsWorldSettings`. |
| Artifact | Canonical schema 1 bytes representing world settings and ordered static records. |
| Manifest | Human-readable metadata paired with the payload, including its full SHA-256. |
| Artifact asset | Unity `ScriptableObject` that references the exact `.bytes` payload and repeats inspection metadata. |
| Runtime compatibility ID | Derived identity of schema, Jitter source, compile profile, and conversion/build semantics. |
| Provider | `IPhysicsArtifactProvider` implementation that returns a fully checked artifact or a typed failure. |
| World builder | The shared Jitter-dependent conversion from artifact records to static Jitter2 bodies and shapes. |
| Topology fingerprint | A diagnostic hash of selected created-record properties; not the primary compatibility proof. |

## Data flow

```text
Authoring                       Immutable delivery                 Runtime

Unity Colliders                level.physics.bytes               Unity asset loader
      |                         level.physics.manifest.json             |
marked Source records          level.physics.asset                     |
      |                                  |                               |
canonical sort + validation             +----- File/Embedded provider --+
      |                                                                  |
artifact DTO -> canonical writer -> SHA-256 -> strict reader/validator --+
                                                                         |
                                                               artifact DTO
                                                                         |
                                                          shared world builder
                                                                         |
                                                        static Jitter2 topology
```

The writer never serializes Jitter internals. Handles, broadphase trees, contacts, islands,
sleep state, and dynamic bodies are runtime state and are intentionally absent.

## Assembly boundaries

| Assembly | Unity dependency | Jitter dependency | Purpose |
| --- | --- | --- | --- |
| `DataSakura.JitterPhysics.Contracts` | None | None | DTOs, errors, limits, IDs, provider/preview interfaces. |
| `DataSakura.JitterPhysics.ArtifactCodec` | None | None | Canonical codec, validation, SHA-256, manifests, providers, tokens. |
| `DataSakura.JitterPhysics.UnityArtifact` | Yes | None | Artifact `ScriptableObject`, paths, and Unity loader. |
| `DataSakura.JitterPhysics.Authoring` | Yes | None | Level, source, and world-profile authoring types. |
| `DataSakura.JitterPhysics.Editor` | Editor only | None | Setup, baking, export, diagnostics, overlay, and Editor API. |
| `DataSakura.JitterPhysics.JitterIntegration` | Installed into `Assets/` | `Jitter2.Core` | World builder and server startup. |

`Contracts`, `ArtifactCodec`, `UnityArtifact`, `Authoring`, and `Editor` must compile when the
project has no Jitter2. Unity ignores folders ending in `~`, which is why the adapter stays in
`JitterIntegration~` until the user explicitly installs it.

The installed adapter always has a direct compile edge to Jitter: a named asmdef reference for a
source distribution or an exact precompiled reference for `Jitter2.Core.dll`. Its f32/layout
preflight runs before artifact loading or world mutation. See the
[installable assembly graph](installable-assembly-graph.md) for the full contract and alias policy.

An existing compatible Jitter2 is referenced by assembly name. The installer must not copy,
move, or modify it. If no Jitter2 exists, the user can explicitly install the pinned fallback
prebuilt assembly.

## Authoring ownership

### `JitterPhysicsLevel`

One level is expected per scene. It owns the level ID, collection scope, world profile,
generated folder, and last successful artifact hash. A geometry root limits collection to its
active descendants; a null root searches the whole scene but still includes only explicit
sources.

### `JitterStaticBodySource`

A source groups accepted colliders into one static body and owns a stable source ID, friction,
and restitution. Inactive objects are excluded. `Include Children` changes collider grouping,
not whether arbitrary unmarked objects elsewhere are baked.

Level and source IDs are canonical lowercase content identities. Renaming a GameObject does
not intentionally rename an existing ID. Duplicate or non-canonical IDs block a successful
bake because ordering and client/server identity would be ambiguous.

### `JitterPhysicsWorldProfile`

Profiles are assets so several levels can deliberately share the same values. Gravity, tick
rate, substep count, solver iterations, relaxation iterations, and deactivation are stored in
the artifact. Deterministic solve mode and single-threaded stepping are format invariants rather
than Inspector options.

> **Known limitation:** Version 0.7.0 stores `SubstepCount`, but the current world builder does
> not apply it to the Jitter world. Do not use that field as evidence of active runtime substeps.

## Validation and baking lifecycle

1. Resolve the level and its identity.
2. Find explicitly marked sources within the level scope.
3. Ensure standalone level/source IDs are canonical and unique.
4. Convert supported active colliders to portable shape records.
5. Canonically order bodies and shapes.
6. Validate settings, values, counts, mesh indices, and hard limits.
7. Write canonical bytes and compute SHA-256.
8. Build a manifest from the validated artifact and hash.
9. Publish the payload/manifest pair and Unity asset.
10. Update the level's cached last-artifact hash only after success.

Validation does not write artifact files. It is not universally side-effect free: the
standalone validation/bake path may assign missing canonical IDs and mark affected Unity
objects dirty. Save the scene after accepting those identity repairs.

Artifact publication stages the payload and manifest together and restores the previous pair
when a caught replacement failure occurs. It is not a database transaction and does not claim
power-loss atomicity or concurrent-writer serialization.

## Three independent versions

| Identity | Changes when | Consumer action |
| --- | --- | --- |
| Package SemVer | Package source, docs, or API release changes. | Update the UPM reference and receipt-managed copies. |
| Artifact schema | Binary layout changes. | Use a reader that supports the schema and re-bake when required. |
| Runtime compatibility ID | Jitter source/compile profile or conversion/build semantics change. | Re-bake and deploy matching client/server builds. |

Package version is recorded for diagnostics but is not an input to the runtime compatibility
ID. Therefore the documentation-only 0.0.12 update does not by itself invalidate 0.0.11
artifact bytes. General updates must be decided by the actual runtime ID, not by SemVer alone.

## Canonical artifact identity

The schema 1 writer fixes byte order, string encoding, record order, floating-point
canonicalization, and quaternion sign. It excludes timestamps, absolute paths, Unity instance
IDs, asset GUIDs, and unordered collection enumeration. Repeating a bake of unchanged source
data must therefore yield the same bytes and SHA-256.

The manifest and Unity asset repeat metadata for inspection, but the loader does not trust
those copies. It re-hashes and decodes the payload and checks the metadata it supports. File
providers enter through the manifest so hash, counts, level, tick rate, and file naming can be
cross-checked before startup.

See [Artifact format v1](artifact-format-v1.md) for limits and layout.

## Runtime startup lifecycle

### Unity client

1. Obtain a `JitterPhysicsArtifactAsset` through the game's normal scene/content flow.
2. After explicit Setup, call `JitterNativeUnityArtifactLoader.Load` with the independently
   derived runtime ID of this simulating build.
3. Create a new Jitter `World`.
4. Call `JitterPhysicsWorldBuilder.Apply` once.
5. Only after success, create dynamic bodies and expose the world to gameplay.
6. Step at the artifact tick rate with `multiThread: false`.
7. Dispose the world when its owning scene/session ends.

### Dedicated server

1. Construct one `IPhysicsArtifactProvider` from launch/content configuration.
2. Create `JitterPhysicsServerOptions` with build runtime ID, expected level, and actual tick
   rate.
3. Call `JitterPhysicsServerStartup.Start` on a new world.
4. Log `SelfCheck`.
5. Gate connection approval on `IsReady`.
6. Compare a `PhysicsCompatibilityToken` before player spawn.
7. Let the match loop own stepping and disposal.

There is no separate physics HTTP service in this architecture. The projected sources compile
inside the match server against its Jitter2 assembly.

## Failure model

Bad external input normally becomes a `PhysicsArtifactError` inside a result object. Callers
should branch on `Succeeded` or `IsReady` and log `Code` plus `Message`; they should not parse
English text to select recovery behavior.

Programmer errors can still throw, including invalid constructor arguments, null required
objects, invalid generated-source options, direct pair-writer I/O, and `RequireReady()` on a
failed server state. A custom provider is responsible for converting its own expected I/O and
content failures into `PhysicsArtifactLoadResult.Failure`; the startup method does not catch an
arbitrary exception thrown by provider code.

## World ownership and rollback

`JitterPhysicsWorldBuilder.Apply` is synchronous and intended to run before the consumer's tick
loop starts. The builder tracks successful application per world and refuses a second
artifact. It has no unload, merge, or hot-reload API.

If shape/body creation throws, bodies created during the attempt are removed and the prior world
settings are restored. If `RequiresWorldDiscard` is true, cleanup could not prove full restoration;
dispose that world and create a fresh one before adding gameplay bodies.

All world mutation belongs to one simulation owner. Do not call `Apply`, `World.Step`, or
provider first-load paths concurrently. The package supplies no locks around a Jitter world.

## Compatibility proof and diagnostics

Use both:

- `artifactHash` to prove the peers loaded identical bytes;
- `runtimeCompatibilityId` to prove those bytes are interpreted with matching pinned runtime
  semantics.

`TopologyFingerprint` is useful in logs but is not complete in 0.7.0. Mesh fingerprints
include vertex/index counts rather than their contents, and the value omits material and world
settings. Do not substitute it for the hash/runtime-ID pair.

## Scene View runtime preview

The overlay's Runtime layer searches active `MonoBehaviour` instances implementing
`IJitterPhysicsRuntimePreviewSource` during Play Mode. A provider reports a level ID, readiness,
and copies the exact records represented by its active world. Artifact records are not deeply
immutable, so the runtime owner must freeze that storage while the preview reads it. The overlay
does not rebuild Runtime records from Unity colliders.

The sample world implements this interface. A custom runtime can implement it without exposing
Jitter types to the always-compiled Editor assembly; see [Extending the package](extending.md).

## Deliberately absent integrations

- No dependency-injection framework is required. Register a provider/world owner in your own
  container if desired.
- No Addressables integration is built in. Resolve the artifact asset first, then call the same
  loader.
- No save-game model is provided. Persist gameplay state, not a reconstructed static world.
- No async/cancellation APIs are present. File providers perform blocking I/O; schedule startup
  according to the consumer's threading and loading policy, then mutate the world on its owner.
- No `link.xml` is shipped. Current scoped package APIs avoid reflection/runtime code generation,
  but IL2CPP acceptance is not yet claimed by this release.

## Safe extension boundary

Recommended extension points are `IPhysicsArtifactProvider`,
`IJitterPhysicsRuntimePreviewSource`, `JitterPhysicsEditorApi`, `JitterPhysicsPreviewApi`, and
consumer-owned orchestration above the world builder. Portable DTOs and codec types are public
for server/tooling use, but changing or mutating their data after validation can invalidate
canonical assumptions.

Do not copy the baker, writer, or builder into a consumer namespace. A copied pipeline can pass
today's tests and drift silently on the next package update.

Next: [Editor guide](editor-guide.md), [Runtime API](runtime-api.md), or
[Integration guide](integration.md).
