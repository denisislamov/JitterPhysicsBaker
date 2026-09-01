# Dedicated server

Applies to package version **0.7.0**.

[Documentation index](index.md) · [Quick start](quick-start.md) ·
[Configuration](configuration.md) · [Troubleshooting](troubleshooting.md) ·
[Recipes](recipes.md)

The package does not ship a physics server executable or own a network transport. It supplies
portable artifact code, the same Jitter2 world-builder source used by Unity, and the exact
lock-verified Jitter DLL distribution installed by Unity Setup. A consumer compiles the owned
projection source into its match server, references the projected DLL, and keeps ownership of startup, ticks,
connections, dynamic bodies, and shutdown.

`Server~` currently contains a .NET 10 test project, not a production service. It compiles
Contracts, ArtifactCodec, and `JitterIntegration~` from their package source paths and references
the same prebuilt Jitter2 assembly used by the fallback Unity installation.

## Startup contract

Import `JitterPhysics.Runtime.props` from the explicit server projection. It references
`JitterRuntime/Jitter2.Core.dll` and its pinned Unsafe dependency; do not add another Jitter
package/reference or compile Jitter independently. The production build must not resolve anything
from Unity `Library/PackageCache`.

`JitterPhysicsServerStartup.Start` owns this order:

1. hash the loaded `Jitter2.Core.dll` when the expected projection hash is supplied;
2. refuse a stale/tampered DLL before asking the artifact provider or mutating the world;
3. ask one `IPhysicsArtifactProvider` for a fully checked artifact;
4. enforce the server build's runtime compatibility ID;
5. optionally enforce the level requested by launch configuration;
6. optionally enforce the tick rate of the actual server loop;
7. build static geometry into a fresh world;
8. return `IsReady == true` only after the complete build succeeds.

Do not open connection approval, spawn players, create dynamic match bodies, or start stepping
before readiness. `ExpectedLevelId == null` and `TickRate == 0` deliberately accept the
artifact's values; production launchers should set both when they know them.

## File-delivered smoke startup

This complete console program validates startup and then disposes the world. A real match server
keeps the same world alive for its match lifetime and disposes it during shutdown.

```csharp
using System;
using System.Globalization;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Integration;
using Jitter2;

namespace MyGame.Server
{
internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 3 || args.Length > 5)
        {
            Console.Error.WriteLine(
                "Usage: physics-smoke <manifestPath> <runtimeId> <jitterDllSha256> [expectedLevelId] [tickRate]");
            return 64;
        }

        string manifestPath = args[0];
        string runtimeCompatibilityId = args[1];
        string jitterAssemblySha256 = args[2];
        string expectedLevelId = args.Length >= 4 ? args[3] : null;
        int tickRate = 0;

        if (args.Length == 5
            && !int.TryParse(
                args[4],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out tickRate))
        {
            Console.Error.WriteLine("tickRate must be an integer.");
            return 64;
        }

        IPhysicsArtifactProvider provider =
            new FilePhysicsArtifactProvider(manifestPath);

        var options = new JitterPhysicsServerOptions(
            runtimeCompatibilityId,
            expectedLevelId,
            tickRate,
            jitterAssemblySha256);

        using var world = new World();
        JitterPhysicsServerState state = JitterPhysicsServerStartup.Start(
            world,
            provider,
            options);

        Console.WriteLine(state.SelfCheck);
        if (!state.IsReady)
        {
            return 2;
        }

        state.RequireReady();
        return 0;
    }
}
}
```

The runtime ID must come from the verified source/profile/package semantics. The Jitter DLL hash
must come from `jitterAssemblySha256` in the verified `JitterPhysics.projection.json`, without the
lock's optional `sha256:` prefix. Neither value comes from the artifact being loaded; adopting the
file's identity would make the compatibility check circular.

## Providers

`FilePhysicsArtifactProvider` takes a manifest path because the manifest supplies the expected
hash, counts, tick rate, and payload name. By default the payload must be a plain file name in the
same directory. An explicit payload path supports delivery systems that rename content in
transit.

`EmbeddedPhysicsArtifactProvider` is created by deterministic generated source. It is intended
for proof-of-concept and small-level delivery; generation defaults to a 4 MiB payload cap. It
restores Base64 once, then re-hashes and validates exactly as a file provider does.

Implement another `IPhysicsArtifactProvider` for a registry, bundle, or content system. A custom
provider must return only fully validated artifacts, including the exact validated payload bytes
in `PhysicsArtifactLoadResult.Payload`, and must convert expected source failures to
`PhysicsArtifactLoadResult.Failure`. Startup decodes those bytes directly into Jitter-native
records and rejects a provider without them before world mutation. The startup method does not
catch arbitrary exceptions from consumer provider code.

See [Extending](extending.md) for a complete provider implementation.

## Readiness state and logs

On success, `JitterPhysicsServerState` contains:

- the decoded artifact and its full payload hash;
- provider source description;
- level and authored tick rate;
- created body and Jitter2 shape counts;
- build duration;
- diagnostic topology fingerprint.

On failure, `Artifact` is null, counts are zero, and `Error` carries a typed reason. `SelfCheck`
returns one line beginning with the package log prefix and either `physics self-check OK` or
`physics self-check FAILED`. Successful output uses short hashes for readable logs; retain the
full artifact/runtime values separately for handshake and evidence.

`RequireReady()` is an intentional throwing guard for control flows in which ignoring readiness
would be a programming error. It does not replace the normal typed check.

## Connection compatibility gate

`PhysicsCompatibilityToken` carries the peer's level ID, artifact hash, and runtime compatibility
ID without depending on a transport. Decode untrusted bytes and compare all three fields before
spawn or connection approval.

```csharp
using System;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Integration;

namespace MyGame.Server
{
public static class PhysicsConnectionGate
{
    public static bool MatchesReadyServer(
        byte[] peerPayload,
        JitterPhysicsServerState serverState,
        out string reason)
    {
        if (serverState == null)
        {
            throw new ArgumentNullException(nameof(serverState));
        }

        if (!serverState.IsReady)
        {
            reason = "server physics is not ready: " + serverState.Error;
            return false;
        }

        if (!PhysicsCompatibilityToken.TryDecode(
                peerPayload,
                out PhysicsCompatibilityToken peer,
                out string decodeError))
        {
            reason = decodeError;
            return false;
        }

        PhysicsCompatibilityToken expected = PhysicsCompatibilityToken.ForArtifact(
            serverState.Artifact,
            serverState.ArtifactHash);

        return peer.Matches(expected, out reason);
    }
}
}
```

This method owns no disposable resource. The transport owns `peerPayload` and the policy for
logging or exposing `reason`. The token is a correctness check between honest peers, not
authentication, authorization, integrity protection, or anti-cheat.

## Tick loop

After readiness, step with the artifact's rate and single-threaded solving:

- timestep: `1f / state.TickRate`;
- Jitter2 call: `world.Step(timestep, multiThread: false)`;
- order: static artifact first, then consumer dynamic bodies, then the first step.

The package does not schedule ticks or reconcile client prediction. It also does not claim
bit-exact `World.Step` output between Unity/IL2CPP and .NET JIT runtimes. The server remains
authoritative even when both sides begin with identical validated artifact bytes.

## Failure and cleanup behavior

Failures before world construction leave geometry untouched. During construction, the builder
catches Jitter2 exceptions, removes bodies created by that attempt and restores the previous
world settings. If `PhysicsWorldBuildResult.RequiresWorldDiscard` is true, cleanup could not prove
full restoration: dispose that world and create a new one before continuing.

The builder also rejects a second artifact on the same world. Level changes require a new world.
There is no package-level hot reload or unload operation.

## Threading and deployment constraints

- Provider load, hashing, file reads, and world construction are synchronous.
- Do not race startup against another startup or `World.Step` for the same world.
- Do not run concurrent pair writers against the same target paths without external locking.
- Freeze mutable DTO/list/array storage before provider load or build.
- The embedded provider's first lazy Base64 restore is not synchronized.
- `FilePhysicsArtifactProvider` requires filesystem access appropriate to the host/container.

The repository test harness targets .NET 10. That proves compilation and tests for the configured
harness, not every consumer target framework, container image, CPU floating-point environment,
or deployment filesystem.

## Diagnostic limitations in 0.7.0

- `SubstepCount` is validated but not applied by the world builder.
- `TopologyFingerprint` omits mesh vertex/index contents, materials, and world settings. Do not
  use it for connection approval.
- Mesh local position/rotation are ignored; mesh vertices must be body-local.
- A failed apply reports whether the caller must discard the world; do not ignore that flag.

Use [Troubleshooting](troubleshooting.md) for startup error codes and
[Recipes](recipes.md) for deployment and two-peer verification flows.
