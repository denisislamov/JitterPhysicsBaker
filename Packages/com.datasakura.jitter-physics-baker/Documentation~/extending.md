# Extending the package

Applies to package version **0.7.0**.

[Documentation index](index.md) · [Quick start](quick-start.md) ·
[Configuration](configuration.md) · [Troubleshooting](troubleshooting.md) ·
[Recipes](recipes.md)

Extend the package at its explicit boundaries. Do not fork artifact validation, world
construction, or compatibility logic into a consumer assembly: two implementations can accept
the same file and still construct different physics.

## Supported extension points

| Extension point | Use it for | Keep centralized |
| --- | --- | --- |
| `IPhysicsArtifactProvider` | Registry, bundle, memory, encrypted-container, or other delivery | Hash, decode, validation, manifest and runtime checks |
| `IJitterPhysicsRuntimePreviewSource` | Expose the geometry of the active runtime world to editor diagnostics | Artifact records; no Jitter2 types in the contract |
| `RuntimeCompatibilityInputs` | Bind a verified external Jitter2 source/profile to package semantics | `RuntimeCompatibilityId.Compute` |
| `PhysicsCompatibilityToken` | Carry compatibility through a consumer-owned transport | Exact level/hash/runtime comparison |
| `PhysicsArtifactUploadStore` | Validate and publish a remotely delivered pair | Canonical names and pair replacement |
| `JitterPhysicsEditorApi` | Integrate external editor ownership and bake orchestration | Package validation, bake, paths, digest, counts, and issues |
| `JitterPhysicsPreviewApi` | Apply a shared Scene View preview preset | The package's existing preferences and overlay state |

The builder, binary reader/writer, canonicalization rules, error codes, and installed integration
sources are shared implementation, not replaceable strategy interfaces.

## Custom artifact provider

A provider must never return a partially checked artifact. Its successful result must describe
the exact bytes it decoded and include their full SHA-256. Bad external input returns a typed
failure; null constructor arguments remain programmer errors.

This in-memory provider copies its input once, then delegates every content rule to the package:

```csharp
using System;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;

namespace MyGame.Physics
{
public sealed class MemoryPhysicsArtifactProvider : IPhysicsArtifactProvider
{
    private readonly byte[] payload;
    private readonly PhysicsArtifactManifest manifest;

    public MemoryPhysicsArtifactProvider(
        byte[] payload,
        PhysicsArtifactManifest manifest,
        string description)
    {
        if (payload == null)
        {
            throw new ArgumentNullException(nameof(payload));
        }

        this.payload = (byte[])payload.Clone();
        this.manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        Description = string.IsNullOrEmpty(description) ? "memory" : description;
    }

    public string Description { get; }

    public PhysicsArtifactLoadResult Load(string expectedRuntimeCompatibilityId)
    {
        PhysicsArtifactResult decoded = PhysicsArtifactReader.Read(
            payload,
            manifest.ArtifactHash,
            manifest);

        if (!decoded.Succeeded)
        {
            return PhysicsArtifactLoadResult.Failure(decoded.Error, Description);
        }

        if (!string.IsNullOrEmpty(expectedRuntimeCompatibilityId))
        {
            PhysicsArtifactError compatibility =
                PhysicsArtifactReader.CheckRuntimeCompatibility(
                    decoded.Artifact,
                    expectedRuntimeCompatibilityId);

            if (compatibility.IsError)
            {
                return PhysicsArtifactLoadResult.Failure(compatibility, Description);
            }
        }

        return PhysicsArtifactLoadResult.Success(
            decoded.Artifact,
            manifest,
            JitterPhysicsHash.Sha256Hex(payload),
            Description);
    }
}
}
```

The provider owns no disposable resource. A provider backed by a stream, archive, database
connection, or native handle must close that resource according to the consumer's lifecycle; the
`IPhysicsArtifactProvider` interface itself is not `IDisposable`.

Do not call a custom provider from multiple threads unless its implementation explicitly supports
that. `JitterPhysicsServerStartup` invokes it synchronously and does not catch arbitrary
exceptions thrown by consumer code.

## Runtime preview source

`IJitterPhysicsRuntimePreviewSource` intentionally exposes portable `PhysicsBodyRecord` values,
not Jitter2 bodies or Unity colliders. The implementation must report what the active runtime
world loaded. Reconstructing authoring colliders would make a runtime preview indistinguishable
from a source preview.

```csharp
using System;
using System.Collections.Generic;
using DataSakura.JitterPhysics.Contracts;
using UnityEngine;

namespace MyGame.Physics
{
[DisallowMultipleComponent]
public sealed class LoadedArtifactPreviewSource : MonoBehaviour,
    IJitterPhysicsRuntimePreviewSource
{
    private PhysicsArtifact artifact;
    private bool ready;

    public string PhysicsPreviewLevelId => artifact?.LevelId;

    public bool IsPhysicsPreviewReady => ready && artifact != null;

    public void PublishBuiltArtifact(PhysicsArtifact loadedArtifact)
    {
        artifact = loadedArtifact ?? throw new ArgumentNullException(nameof(loadedArtifact));
        ready = true;
    }

    public void Clear()
    {
        ready = false;
        artifact = null;
    }

    private void OnDisable()
    {
        Clear();
    }

    public void CopyPhysicsPreviewBodies(ICollection<PhysicsBodyRecord> destination)
    {
        if (destination == null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        if (!IsPhysicsPreviewReady)
        {
            return;
        }

        for (int index = 0; index < artifact.Bodies.Count; index++)
        {
            destination.Add(artifact.Bodies[index]);
        }
    }
}
}
```

The example copies record references into the destination, as the contract requires. Those
records contain arrays and caller-provided list references that are not deeply immutable. Treat
them as frozen after load and do not mutate them while the editor is reading a preview. Call
`PublishBuiltArtifact` only after `JitterPhysicsWorldBuilder.Apply` succeeds; clear the source
before disposing or replacing that world. Add the component to the active world-owner GameObject;
the overlay discovers active `MonoBehaviour` implementations automatically, so no service
registration is required. Disabling/destroying the component clears its ready state.

## Custom delivery and storage

Use `PhysicsArtifactUploadStore.Store` when untrusted payload and manifest text arrive together.
It validates before writing, accepts an exact legacy payload name, and publishes the current
canonical names. It uses synchronous filesystem APIs and has no per-folder concurrency lock.

`PhysicsArtifactPairWriter` stages and restores a pair when a caught move fails. It is not a
database transaction: it does not provide fsync durability, recovery after process termination,
or coordination between concurrent writers. Put external locking and deployment-level atomicity
around it when multiple processes can publish the same level.

For small generated-source delivery, `EmbeddedArtifactSourceGenerator` defaults to a 4 MiB cap.
The generated provider re-hashes and validates at runtime; compilation into the binary does not
make the bytes trusted. Large production levels should remain content and use a provider.

## Adding artifact data or shape kinds

Artifact schema v1 supports only static Box, Sphere, Capsule, and Mesh records. Adding a field,
changing field order/encoding, or adding a shape changes the binary interpretation and requires:

1. an `ArtifactSchemaVersion` bump;
2. matching reader and writer changes;
3. new golden bytes and corrupt-input coverage;
4. builder support on both Unity and server paths;
5. manifest and compatibility review;
6. migration/re-bake documentation.

Changing behavior without changing bytes still requires the matching semantic version constant
to be bumped so that `RuntimeCompatibilityId` changes. Examples include collider conversion,
shape construction, body creation order, or world defaults.

Do not serialize Jitter2 handles, broadphase nodes, contacts, islands, acceleration structures,
timestamps, absolute paths, Unity instance IDs, or hash-table enumeration order.

## API ownership guidance

Use these as normal consumer entry points:

- `JitterNativeUnityArtifactLoader.Load` for simulation, or the Jitter-free
  `JitterPhysicsArtifactLoader.Load` for inspection;
- `IPhysicsArtifactProvider` and the supplied providers;
- `JitterPhysicsWorldBuilder.Apply`;
- `JitterPhysicsServerStartup.Start`;
- typed result/error values;
- `PhysicsCompatibilityToken`.

Treat DTO constructors, writer/generator/storage APIs, `JitterPhysicsArtifactAsset.Initialize`,
and path helpers as advanced producer/editor infrastructure. They are public because separate
package assemblies need them, not because runtime callers should rebuild the bake pipeline.

Internal codec classes and copied integration implementation files are not extension points.
Update the receipt-managed integration through package tooling rather than editing the installed
copy. If a project intentionally maintains a fork, it must own runtime compatibility versioning,
cross-runtime tests, and update conflicts.

## Constraints to preserve

- Build into a fresh candidate world and dispose it after any failure because settings rollback
  is partial in 0.7.0.
- Do not rely on `SubstepCount` until builder support is implemented and compatibility-versioned.
- Bake mesh local transforms into vertices; the current builder ignores mesh local pose fields.
- Compare full artifact hash plus runtime ID. `TopologyFingerprint` omits mesh contents and other
  simulation data.
- Freeze DTO storage before hashing, writing, previewing, or building.
- Keep startup/world mutation single-threaded.
- Run a target IL2CPP player smoke before advertising AOT/platform support; source inspection is
  not acceptance evidence.

See [API reference](api-reference.md) for the grouped public surface and
[Artifact format v1](artifact-format-v1.md) before extending serialized data.
