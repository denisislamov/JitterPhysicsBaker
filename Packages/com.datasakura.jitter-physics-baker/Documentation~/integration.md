# Integration

Applies to package version **0.7.0**.

[Documentation index](index.md) · [Quick start](quick-start.md) ·
[Configuration](configuration.md) · [Troubleshooting](troubleshooting.md) ·
[Recipes](recipes.md)

This page describes the boundary between the portable package and Jitter2. For load/build API
details, see [Runtime API](runtime-api.md). For process startup and connection approval, see
[Dedicated server](dedicated-server.md).

## Why integration is installed explicitly

The package must import into a Unity project that has no Jitter2. Its always-compiled assemblies
therefore have no `Jitter2.Core` reference. The Jitter-dependent source files live under
`JitterIntegration~`, which Unity ignores.

After the project has exactly one compatible Jitter2 assembly, use the package's explicit
installation action to copy those sources and the asmdef template into the project-owned
integration folder. The installed assembly references `Jitter2.Core` by assembly name, not by
asset GUID, so a compatible external copy can live anywhere in the consumer project.

Do not copy the integration source by hand. The installer records the written files and hashes in
its receipt, updates only receipt-owned unchanged files, and reports local modifications instead
of overwriting them.

## Assembly graph

```text
DataSakura.JitterPhysics.Contracts
                ^
                |
DataSakura.JitterPhysics.ArtifactCodec
                ^
                |
DataSakura.JitterPhysics.JitterIntegration ----> Jitter2.Core
```

`UnityArtifact`, authoring, and editor assemblies sit beside this path and remain Jitter-free.
The integration assembly has no networking dependency and no dependency on a particular game.

Avoid cycles in projects whose own Jitter2 assembly references game code. Put the call to
`JitterPhysicsWorldBuilder` in a higher-level assembly that can reference both Jitter2 and the
installed integration assembly; do not make those two lower-level assemblies reference each
other.

## Jitter2 selection

The setup UI distinguishes four relevant states:

| State | Meaning | Safe action |
| --- | --- | --- |
| Missing | No `Jitter2.Core` was found | Install the package fallback or add the project's Jitter2 |
| Compatible | One copy matches the package lock/profile | Keep it; install or update integration |
| Incompatible | One copy has different source/profile identity | Decide which physics build is authoritative, then re-bake |
| Duplicate | More than one assembly can resolve as `Jitter2.Core` | Remove the ambiguity before installing integration |

An external Jitter2 always wins. The package does not copy, move, or edit it. The fallback is a
prebuilt `netstandard2.1` managed plugin using the locked `f32` scalar-shim profile. It requires
`System.Runtime.CompilerServices.Unsafe.dll` in player builds unless the project already supplies
a compatible copy.

## Runtime compatibility identity

Schema compatibility only means that a build can parse the bytes. Runtime compatibility means
that it will construct the same world semantics. The runtime ID includes:

- artifact schema version;
- canonical Jitter2 source content hash;
- precision mode;
- compile profile ID;
- collider conversion version;
- shape construction version;
- world builder version;
- world-defaults version.

Use the lock's verified source hash and the actual compile profile. Do not invent or manually
persist a replacement ID.

```csharp
using System;
using DataSakura.JitterPhysics.ArtifactCodec;

namespace MyGame.Physics
{
public static class PhysicsRuntimeIdentity
{
    public static string Compute(string jitterSourceContentHash, string compileProfileId)
    {
        if (string.IsNullOrEmpty(jitterSourceContentHash))
        {
            throw new ArgumentException(
                "The verified Jitter2 source content hash is required.",
                nameof(jitterSourceContentHash));
        }

        if (string.IsNullOrEmpty(compileProfileId))
        {
            throw new ArgumentException(
                "The Jitter2 compile profile ID is required.",
                nameof(compileProfileId));
        }

        RuntimeCompatibilityInputs inputs = RuntimeCompatibilityInputs.ForCurrentBuild(
            jitterSourceContentHash,
            compileProfileId);

        return RuntimeCompatibilityId.Compute(inputs);
    }
}
}
```

The method owns no disposable resource. Its caller is responsible for obtaining the two inputs
from a verified installation/lock rather than free-form user text.

## Startup order

Use the same order on Unity clients and dedicated servers:

1. resolve the runtime compatibility ID of the build;
2. load through a Unity asset or an `IPhysicsArtifactProvider`;
3. reject typed load/compatibility errors;
4. create a new Jitter2 `World`;
5. apply the artifact before creating dynamic bodies;
6. reject a failed build and dispose that candidate world;
7. only then enable stepping, prediction, spawning, or connection approval.

`JitterPhysicsServerStartup` owns this ordering on the server; a Unity consumer must orchestrate
the same sequence explicitly, as shown in [Runtime API](runtime-api.md#loading-a-unity-artifact).
The package does not own the simulation loop. Step at `1 / artifact.WorldSettings.TickRate` and
pass `multiThread: false` from the consumer's existing tick scheduler.

## Lifecycle and ownership

- One successfully applied artifact is allowed per `World`.
- `JitterPhysicsWorldBuilder.HasArtifact` reports only worlds successfully marked by this builder.
- There is no package unload or hot-reload API. Dispose the entire world on a level change.
- Static bodies belong to the world. The result exposes diagnostics, not body handles.
- The consumer owns dynamic bodies, constraints, ticks, network state, and reconciliation.
- Build and step the world from one ownership context; do not race `Apply` with another apply or
  with `World.Step`.

## Consumer architecture patterns

### Assembly definitions

Reference only the layers used by that consumer assembly:

| Consumer code | Required asmdef references |
| --- | --- |
| Portable file/content inspection | `DataSakura.JitterPhysics.Contracts`, `DataSakura.JitterPhysics.ArtifactCodec` |
| Unity artifact loading without simulation | `DataSakura.JitterPhysics.Contracts`, `DataSakura.JitterPhysics.UnityArtifact` |
| Unity world owner that names Jitter types | `DataSakura.JitterPhysics.Contracts`, `DataSakura.JitterPhysics.UnityArtifact`, `DataSakura.JitterPhysics.JitterIntegration`, `Jitter2.Core` |
| External Editor bake adapter | `DataSakura.JitterPhysics.Authoring`, `DataSakura.JitterPhysics.Editor` and **Editor** platform only |

The installed integration assembly also sets `DATASAKURA_JITTER_INTEGRATION`; use that define to
exclude optional consumer code until setup has installed the adapter. Do not make the always
compiled core package depend on that optional assembly.

### Dependency injection and services

The package has no DI integration or service locator. Register a consumer-owned world/session
owner in the project's container, inject the selected `JitterPhysicsArtifactAsset` or
`IPhysicsArtifactProvider`, and have that owner call the package APIs in the startup order above.
Do not register `World` as ready until load and `Apply` both succeed. Dispose the world from the
same session scope that created it.

### Addressables and other content systems

The package has no Addressables dependency. A Unity content layer may load a
`JitterPhysicsArtifactAsset`, retain its handle for the world lifetime, and pass the asset to the
installed `JitterNativeUnityArtifactLoader`. A server content layer should implement
`IPhysicsArtifactProvider` and return a fully hash/manifest/runtime-checked result with exact
payload bytes. Releasing
an Addressables handle while the asset is still needed is a consumer lifecycle bug; the package
does not retain that handle for you.

### Scene changes, domain reload, and UI

Treat a scene/session change as a world replacement: stop ticks, stop accepting work that uses
the world, dispose it, release content handles, then load/build the next candidate. Unity domain
reload destroys managed world owners; their `OnDestroy`/container disposal path must release the
Jitter world. Editor tools that subscribe to `JitterPhysicsPreviewApi.Changed` must unsubscribe in
`OnDisable`; see [Recipes](recipes.md#react-to-shared-preview-changes).

The package exposes typed results rather than a runtime UI. Translate an error code/message into
the game's own loading/error screen; do not let UI code construct or step the world. The package
does not serialize a running world or integrate with a save system. Persist the game's level ID
and content/version selection, then rebuild physics from a validated artifact after load.

### Async and cancellation

Current package load, provider, hashing, file, build, and step APIs are synchronous and accept no
`CancellationToken`. Put cancellable download/Addressables work in the consumer layer. After that
work finishes, check cancellation before invoking the synchronous loader/builder, and never run
Unity asset access or Jitter world mutation concurrently with the simulation owner. There is no
package callback to unsubscribe from for runtime startup.

## Determinism boundary

The package guarantees canonical artifact bytes and validates that both sides use the same full
artifact hash and runtime semantics identity. It deliberately does not claim bit-exact physics
ticks across Unity/IL2CPP and .NET JIT runtimes. The server remains authoritative.

`TopologyFingerprint` is useful for a repeatable smoke log, but in 0.7.0 it is incomplete: mesh
contents, world settings, friction, and restitution are not all represented. It must not replace
the artifact hash/runtime ID pair in a handshake or release gate.

## Current integration gaps

- `SubstepCount` is present in the artifact but is not assigned to the Jitter2 world by the
  current builder.
- A build exception rolls back created bodies and settings. Discard the candidate world when
  `RequiresWorldDiscard` reports incomplete restoration.
- Mesh local translation and rotation are not applied. Mesh producers must supply body-local
  vertices and an identity mesh-local pose.
- Public artifact arrays/list references are not deeply immutable. Freeze ownership before load
  or build; never mutate them concurrently.
- Package assemblies have no platform exclusions and avoid reflection/runtime code generation,
  but the repository's IL2CPP mobile smoke acceptance item remains uncompleted. Verify the exact
  target, backend, stripping configuration, Jitter2 plugin, and `Unsafe` dependency in a player
  build before claiming support.

## Updating the package

Updating the UPM reference does not automatically replace receipt-managed integration files or
re-bake artifacts. After an update:

1. inspect setup status;
2. update the integration only through its explicit component action;
3. preserve or review any locally modified installed copy;
4. validate the Jitter2 lock/profile and `Unsafe` dependency;
5. re-bake when runtime semantics changed;
6. run Unity, server, target-player, and consumer acceptance as separate gates.

See [Troubleshooting](troubleshooting.md) for missing/duplicate/incompatible assembly cases and
[Recipes](recipes.md) for concrete update and smoke workflows.
