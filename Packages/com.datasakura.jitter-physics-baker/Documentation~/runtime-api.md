# Runtime API

Applies to package version **0.0.12**.

[Documentation index](index.md) · [Quick start](quick-start.md) ·
[Configuration](configuration.md) · [Troubleshooting](troubleshooting.md) ·
[Recipes](recipes.md)

The runtime API has two deliberately separate stages:

1. obtain, hash, decode, and validate an artifact without referencing Jitter2;
2. apply the validated records to a consumer-owned Jitter2 `World`.

Keeping those stages separate lets inspection tools and clean package imports use the artifact
format without installing Jitter2. It also gives callers one point at which to stop before any
physics state is mutated.

## Assemblies and namespaces

| Assembly | Namespace | Responsibility | Jitter2 reference |
| --- | --- | --- | --- |
| `DataSakura.JitterPhysics.Contracts` | `DataSakura.JitterPhysics.Contracts` | DTOs, identifiers, limits, typed results, provider contracts | No |
| `DataSakura.JitterPhysics.ArtifactCodec` | `DataSakura.JitterPhysics.ArtifactCodec` | Canonical binary codec, manifests, SHA-256, providers, compatibility tokens | No |
| `DataSakura.JitterPhysics.UnityArtifact` | `DataSakura.JitterPhysics.UnityArtifact` | Unity `ScriptableObject` handle and Unity-side loader | No |
| `DataSakura.JitterPhysics.JitterIntegration` | `DataSakura.JitterPhysics.Integration` | Shared Jitter2 world builder and server startup gate | Yes |

The integration assembly is not compiled from `JitterIntegration~` in place. Install it
explicitly after the project has exactly one compatible `Jitter2.Core`; see
[Integration](integration.md).

## Loading a Unity artifact

`JitterPhysicsArtifactLoader.Load` treats both the serialized asset metadata and its `TextAsset`
payload as untrusted. It hashes and decodes the payload, validates the artifact, checks selected
asset metadata, and optionally checks the runtime compatibility ID.

Always supply the runtime compatibility ID in code that will simulate the artifact. Omitting it
is appropriate only for an inspection tool that intentionally reads artifacts it cannot run.

The following component owns the `World`, disposes it on every failure, and exposes one step to
an existing fixed-tick scheduler. It does not assume that Unity `FixedUpdate` has the artifact's
tick rate.

```csharp
using System;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Integration;
using DataSakura.JitterPhysics.JitterNative.UnityBoundary;
using DataSakura.JitterPhysics.UnityArtifact;
using Jitter2;
using UnityEngine;
using NativeReadResult = DataSakura.JitterPhysics.JitterNative.Codec.PhysicsArtifactResult;

namespace MyGame.Physics
{
public sealed class JitterArtifactWorldOwner : MonoBehaviour
{
    private World world;
    private float timeStep;

    public PhysicsArtifactError StartWorld(
        JitterPhysicsArtifactAsset artifactAsset,
        string runtimeCompatibilityId)
    {
        if (world != null)
        {
            throw new InvalidOperationException("This owner already has a Jitter world.");
        }

        if (string.IsNullOrEmpty(runtimeCompatibilityId))
        {
            throw new ArgumentException(
                "A simulating client must provide its runtime compatibility ID.",
                nameof(runtimeCompatibilityId));
        }

        NativeReadResult loaded = JitterNativeUnityArtifactLoader.Load(
            artifactAsset,
            runtimeCompatibilityId);

        if (!loaded.Succeeded)
        {
            return loaded.Error;
        }

        var candidate = new World();
        PhysicsWorldBuildResult built = JitterPhysicsWorldBuilder.Apply(
            candidate,
            loaded.Artifact);

        if (!built.Succeeded)
        {
            candidate.Dispose();
            return built.Error;
        }

        world = candidate;
        timeStep = 1f / loaded.Artifact.WorldSettings.TickRate;
        return default;
    }

    public void StepOnce()
    {
        if (world == null)
        {
            throw new InvalidOperationException("StartWorld must succeed before stepping.");
        }

        world.Step(timeStep, multiThread: false);
    }

    private void OnDestroy()
    {
        world?.Dispose();
        world = null;
    }
}
}
```

Call `StepOnce` from the scheduler that owns the game's authoritative or prediction tick. Do not
silently substitute `Time.fixedDeltaTime`: the artifact's tick rate is part of its validated
simulation contract.

## Provider-based loading

`IPhysicsArtifactProvider` is the delivery boundary used outside a Unity asset. A successful
provider result means that the payload has already been hashed, decoded, validated, checked
against its manifest, and, when requested, checked against the caller's runtime ID.

Successful providers must also return the exact validated bytes in
`PhysicsArtifactLoadResult.Payload`. The installable integration decodes those bytes directly into
the Jitter-native record graph. A custom provider that uses the older success overload without raw
bytes remains source-compatible for inspection tools, but server startup rejects it with an
actionable `SourceUnavailable` error before mutating the world.

The package supplies:

- `FilePhysicsArtifactProvider` for a manifest and payload delivered as files;
- `EmbeddedPhysicsArtifactProvider` for deterministic generated source containing Base64 chunks.

`FilePhysicsArtifactProvider` normally resolves the payload name from the manifest's directory.
It refuses absolute paths, directory separators, `.` and `..` from that untrusted field. Pass the
optional payload path only when a delivery system deliberately renamed the payload.

Provider loading is synchronous. File reads and hashing occur on the calling thread. Resolve and
load the provider during startup, not from a render callback or while the world is stepping.

## Building the world

`JitterPhysicsWorldBuilder.Apply` performs these operations synchronously:

1. reject null, invalid, or already-applied Jitter-native artifacts;
2. apply supported world settings;
3. create bodies in ascending `SourceId` order;
4. create shapes in ascending `ShapeKey` order;
5. assign pose and material values, then make each body static;
6. record that the world has an artifact and return diagnostics.

No portable vector/quaternion DTO conversion occurs in the simulation path. For primitive
records, a non-identity local pose becomes a Jitter2 `TransformedShape`. A mesh is
expanded to one Jitter2 `TriangleShape` per triangle, so `PhysicsWorldBuildResult.ShapeCount` can
be greater than `PhysicsArtifact.ShapeCount`.

Build static geometry before creating dynamic bodies and before the first step. `Apply` is once
per `World`; there is no merge, unload, or hot-reload operation. Create and dispose a new world
when changing levels.

## Typed failures

Expected external failures use `PhysicsArtifactError`, not exceptions. Check `Succeeded` or
`IsReady` before reading a success payload.

| Result | Used by |
| --- | --- |
| `PhysicsArtifactResult` | decode, validation, Unity artifact load |
| `PhysicsArtifactLoadResult` | artifact providers |
| `PhysicsArtifactUploadResult` | validated remote delivery storage |
| `PhysicsWorldBuildResult` | Jitter2 world construction |
| `JitterPhysicsServerState` | dedicated-server startup |

`PhysicsArtifactErrorCode` distinguishes empty input, bad magic, unsupported schema, truncation,
trailing bytes, limit violations, invalid values/order/meshes, hash and manifest mismatches,
runtime incompatibility, and unavailable sources. Log `Error.ToString()` for the code, message,
level, and short hash.

Null arguments, invalid locally constructed options, a request to write a non-canonical DTO, and
`RequireReady()` on a failed server state are programmer errors and may throw.

## Current 0.0.12 runtime limitations

These are implementation facts, not future promises:

- `PhysicsWorldSettings.SubstepCount` is serialized and validated, but the current world builder
  does not assign `World.SubstepCount`. Values above one therefore do not affect the built world.
- If Jitter2 throws after world settings were assigned, the builder removes bodies created by
  that attempt and restores gravity, solve mode, solver iterations, and deactivation. Check
  `PhysicsWorldBuildResult.RequiresWorldDiscard`: when cleanup cannot prove full restoration,
  dispose the world and create a new one before continuing.
- `TopologyFingerprint` is a reproducible diagnostic, not a compatibility proof. For meshes it
  includes vertex/index counts but not their contents; it also omits world settings and material
  values. Use the full artifact hash together with the runtime compatibility ID for acceptance.
- Mesh vertices are consumed as body-local coordinates. The current mesh builder does not apply
  `PhysicsShapeRecord.LocalPosition` or `LocalRotation`, and validation does not require those
  fields to be identity. Producers must bake mesh transforms into the vertices.
- Artifact DTOs are only conventionally immutable. Mesh arrays and caller-supplied read-only list
  interfaces can still refer to mutable storage. Do not mutate or concurrently rewrite records,
  payload arrays, manifests, or compatibility-token magic after construction.
- World construction is not a concurrent operation. Do not call `Apply` twice in parallel for the
  same world. The embedded provider's first Base64 restore is also not synchronized.
- The runtime code avoids reflection and runtime code generation, but IL2CPP/AOT support has not
  been demonstrated by a completed mobile IL2CPP smoke gate in this repository. Treat target
  player validation as a consumer/release requirement, not an already proven package guarantee.

## Next reading

- [Integration](integration.md) explains installation, assembly ownership, and lifecycle.
- [Dedicated server](dedicated-server.md) covers readiness and connection approval.
- [Artifact format v1](artifact-format-v1.md) defines the bytes and validation order.
- [API reference](api-reference.md) groups the supported public surface by purpose.
- [Extending](extending.md) covers custom providers and preview sources.
