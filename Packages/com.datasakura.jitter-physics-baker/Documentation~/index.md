# DataSakura Jitter Physics Baker manual

DataSakura Jitter Physics Baker converts explicitly marked Unity colliders into one
deterministic, versioned binary artifact. The Unity client and a .NET match server validate
the same bytes and use the same `JitterPhysicsWorldBuilder` implementation to create their
static Jitter2 geometry.

The package owns authoring, validation, baking, artifact delivery contracts, and static-world
construction. Your game still owns dynamic bodies, networking, connection approval, scene
lifetime, and every call to `World.Step`.

> **Important:** The current package version is **0.0.12** and the artifact schema is **1**.
> Package version, artifact schema, and runtime compatibility are separate identities. An
> ordinary documentation update can change the first without changing the other two.

## Start here

| Goal | Page |
| --- | --- |
| See whether the package fits your project | [Requirements and compatibility](requirements-and-compatibility.md) |
| Add or remove the package | [Installation](installation.md) |
| Run a verified sample in 5–15 minutes | [Quick Start](quick-start.md) |
| Author and bake a real level | [Editor guide](editor-guide.md) |
| Understand the data flow and ownership | [Concepts and architecture](concepts-and-architecture.md) |
| Inspect optional assemblies and f32 preflight | [Installable assembly graph](installable-assembly-graph.md) |
| Migrate to Jitter-native artifact records | [Jitter-native contracts](jitter-native-contracts.md) |
| Configure levels, sources, profiles, and folders | [Configuration](configuration.md) |
| Load and step a world in Unity | [Runtime API](runtime-api.md) |
| Integrate with an existing game architecture | [Integration guide](integration.md) |
| Integrate a dedicated .NET server | [Dedicated server](dedicated-server.md) |
| Implement a provider or Editor adapter | [Extending the package](extending.md) |
| Copy a focused, tested pattern | [Recipes](recipes.md) |
| Look up a public type | [API reference](api-reference.md) |
| Diagnose an error | [Troubleshooting](troubleshooting.md) |
| Update, migrate, roll back, or refresh samples | [Migration and upgrading](migration-and-upgrading.md) |
| Inspect the schema 1 wire format | [Artifact format v1](artifact-format-v1.md) |
| Call the package from an NPI-style Editor tool | [Editor API handoff](npi-editor-api.md) |

## What the package does

- Imports without a `Jitter2.Core` assembly, so the project remains compilable before explicit
  setup.
- Marks only intended static geometry through `JitterPhysicsLevel` and
  `JitterStaticBodySource`.
- Converts `BoxCollider`, `SphereCollider`, `CapsuleCollider`, and `MeshCollider` in the
  Editor.
- Writes canonical little-endian bytes, a JSON manifest, and a Unity artifact asset.
- Supplies loaders and providers that verify SHA-256, schema, limits, and ordering. Manifest
  metadata and the expected runtime identity are enforced when the caller supplies them; a
  simulating consumer must supply its independently derived runtime ID before building a world.
- Supplies file-based and generated embedded providers for a .NET server.
- Uses one Jitter-dependent builder implementation on Unity and .NET.
- Exposes a transport-independent compatibility token containing level ID, artifact hash, and
  runtime compatibility ID.
- Provides a Scene View overlay for comparing Sources, the saved Bake, and active Runtime
  records.

## What it does not do

- It does not bake at runtime.
- It does not discover and bake every collider automatically; unmarked colliders are ignored.
- It does not serialize dynamic bodies, contacts, broadphase state, handles, islands, or a
  running simulation.
- It does not own a tick loop, networking transport, dependency-injection container, content
  delivery system, character controller, or server executable.
- It does not support merging or hot-reloading a second artifact into an existing Jitter
  `World`.
- It does not claim bit-exact `World.Step` results across Unity and .NET runtimes.
- Its compatibility token detects mismatched honest peers; it is not authentication or an
  anti-cheat protocol.

## Architecture at a glance

```text
Unity scene
  JitterPhysicsLevel + JitterStaticBodySource + WorldProfile
                              |
                    Validate and bake (Editor)
                              v
       .physics.bytes + .physics.manifest.json + .physics.asset
                |                                  |
        server IPhysicsArtifactProvider       Unity artifact asset
                |                                  |
                +---------- strict load/verify ----+
                                   |
                          PhysicsArtifact DTO
                                   |
                    JitterPhysicsWorldBuilder.Apply
                                   |
                       static Jitter2 World topology
                                   |
             consumer-owned dynamic bodies, tick loop, networking
```

The engine-independent `Contracts` and `ArtifactCodec` assemblies form the portable boundary.
Unity-specific authoring and artifact assets sit above it. Jitter-dependent code remains in
`JitterIntegration~` until an explicit setup command installs it into the consumer project.
See [Concepts and architecture](concepts-and-architecture.md) for the complete assembly and
lifecycle model.

## Five rules that prevent the most expensive mistakes

1. Install or provide one compatible `Jitter2.Core`, then install the integration adapter
   before importing the runnable samples.
2. Treat `Level ID` and every `Source ID` as persistent network/content identities, not labels.
3. Build static geometry into a new world before creating dynamic bodies or taking the first
   simulation step.
4. Step at `1f / artifact.WorldSettings.TickRate` with `multiThread: false`; do not substitute
   Unity's fixed timestep.
5. Compare both the artifact hash and runtime compatibility ID before accepting a client or
   starting a match.

## Current verified and unverified scope

The development project declares Unity 6000.3 and is authored with Unity 6000.3.19f1. The
portable sources are exercised by a .NET 10 test project. The package has no render-pipeline
dependency and no asmdef platform exclusions, but that is not proof for every player target.
In particular, the current release does not claim a completed IL2CPP/mobile acceptance run.

See [Requirements and compatibility](requirements-and-compatibility.md) for the evidence
boundary and [Troubleshooting](troubleshooting.md) for player-build checks.

## Known correctness limitations in 0.0.12

These are implementation facts, not future promises:

- `SubstepCount` is serialized and validated but is not currently applied by
  `JitterPhysicsWorldBuilder`.
- A failed `Apply` removes bodies created during that call, but world settings assigned before
  the failure are not restored. Discard the failed world.
- `TopologyFingerprint` does not hash mesh vertex/index contents, materials, or world settings.
  Use `artifactHash + runtimeCompatibilityId` as the compatibility proof.
- Mesh vertices must already be body-local; the current builder does not apply a mesh record's
  `LocalPosition` or `LocalRotation`.
- The bake pair writer protects payload/manifest replacement, but `.physics.asset` is updated
  afterward. A late Unity import or asset-update failure can leave a mixed-generation trio even
  though the current failure text says the previous artifact remained in place. Never deliver a
  failed bake; re-bake and verify all three files.

The detailed consequences and safe call patterns are in [Runtime API](runtime-api.md).

## Package contents

```text
Runtime/Contracts/       portable DTOs, errors, limits, compatibility token
Runtime/ArtifactCodec/   canonical codec, validation, hashes, providers
Runtime/UnityArtifact/   Unity asset handle and loader
Authoring/               level, source, and world-profile components
Editor/                  authoring window, installer, baking, export, diagnostics
JitterIntegration~/      Jitter-dependent builder and server startup sources
Jitter2~/                pinned dormant fallback snapshot and prebuilt assemblies
Samples~/Demos/          importable runnable samples and compiled examples
Server~/                 server integration notes and .NET verification project
Documentation~/          this manual
tools~/                  package validation and portable test helpers
```

## Support information

When reporting a problem, include:

- Unity version and target platform;
- package version and installation source;
- the compatibility report JSON from **Jitter Physics — Setup**;
- level ID, short artifact hash, and short runtime compatibility ID;
- the complete typed error code and message;
- whether the failure occurred during import, validation, bake, load, world build, or step;
- a minimal scene or artifact pair when reproduction requires project data.

Do not include secrets such as upload tokens in diagnostics.
