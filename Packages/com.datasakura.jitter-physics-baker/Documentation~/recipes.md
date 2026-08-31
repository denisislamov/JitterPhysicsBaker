# Recipes

These recipes solve common integration tasks with the current API. Full lifecycle examples are
also available in the imported **Physics Baking Demos** sample.

[Back to the manual](index.md)

## Validate and bake from another Editor tool

Use the supported Editor facade rather than invoking internal baker classes.

```csharp
using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Editor.Api;
using UnityEngine;

namespace MyGame.EditorTools
{
public static class MyPhysicsBakeAdapter
{
    public static bool Bake(JitterPhysicsLevel level)
    {
        JitterPhysicsEditorResult validation =
            JitterPhysicsEditorApi.Validate(
                level,
                JitterPhysicsLevelIdBinding.Standalone);

        if (!validation.Succeeded)
        {
            Debug.LogError(validation.Issues.Format());
            return false;
        }

        JitterPhysicsEditorResult baked =
            JitterPhysicsEditorApi.Bake(
                level,
                JitterPhysicsLevelIdBinding.Standalone);

        if (!baked.Succeeded)
        {
            Debug.LogError(baked.Issues.Format());
            return false;
        }

        Debug.Log($"Baked {baked.LevelId}: {baked.Digest}");
        return true;
    }
}
}
```

Place this file in an Editor-only assembly that references
`DataSakura.JitterPhysics.Editor`. `Validate` writes no artifact, but standalone identity
resolution may assign missing canonical IDs and dirty scene objects.

## Use an externally owned level ID

An NPI-style tool can own the content identity without adding a package dependency on the
owner assembly.

```csharp
using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Editor.Api;

namespace MyGame.EditorTools
{
public static class ExternalLevelBake
{
    public static JitterPhysicsEditorResult Bake(
        JitterPhysicsLevel level,
        string externalLevelId)
    {
        JitterPhysicsLevelIdBinding binding =
            JitterPhysicsLevelIdBinding.External(
                owner: "MyLevelPipeline",
                levelId: externalLevelId);

        return JitterPhysicsEditorApi.Bake(level, binding);
    }
}
}
```

The external ID must already be canonical. The binding applies only to that operation; it does
not silently rewrite the component's standalone ID. See [Editor API handoff](npi-editor-api.md).

## Load a Unity artifact and own the tick loop

The complete sample implementation is
`Samples~/Demos/Runtime/JitterPhysicsSampleWorld.cs`. The essential order is:

```csharp
using System;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Integration;
using DataSakura.JitterPhysics.UnityArtifact;
using Jitter2;
using UnityEngine;

namespace MyGame.Physics
{
public sealed class MyJitterWorldOwner : MonoBehaviour
{
    [SerializeField] private JitterPhysicsArtifactAsset artifact;

    private World world;
    private PhysicsArtifact loadedArtifact;
    private float accumulator;

    public PhysicsArtifactError StartWorld(string runtimeCompatibilityId)
    {
        if (world != null)
        {
            throw new InvalidOperationException("This owner already has a Jitter world.");
        }

        if (string.IsNullOrEmpty(runtimeCompatibilityId))
        {
            throw new ArgumentException(
                "Supply the verified runtime ID of this build.",
                nameof(runtimeCompatibilityId));
        }

        PhysicsArtifactResult loaded =
            JitterPhysicsArtifactLoader.Load(
                artifact,
                runtimeCompatibilityId);
        if (!loaded.Succeeded)
        {
            Debug.LogError($"{loaded.Error.Code}: {loaded.Error.Message}", this);
            enabled = false;
            return loaded.Error;
        }

        var candidate = new World();
        PhysicsWorldBuildResult built =
            JitterPhysicsWorldBuilder.Apply(candidate, loaded.Artifact);
        if (!built.Succeeded)
        {
            candidate.Dispose();
            Debug.LogError($"{built.Error.Code}: {built.Error.Message}", this);
            enabled = false;
            return built.Error;
        }

        world = candidate;
        loadedArtifact = loaded.Artifact;
        enabled = true;
        return default;
    }

    private void Update()
    {
        if (world == null)
        {
            return;
        }

        float timestep = 1f / loadedArtifact.WorldSettings.TickRate;
        accumulator += Time.deltaTime;
        while (accumulator >= timestep)
        {
            world.Step(timestep, multiThread: false);
            accumulator -= timestep;
        }
    }

    private void OnDestroy()
    {
        world?.Dispose();
        world = null;
        loadedArtifact = null;
    }
}
}
```

Install the integration adapter first and reference
`DataSakura.JitterPhysics.JitterIntegration` plus `Jitter2.Core` where required by your asmdef.
Call `StartWorld` from the consumer bootstrap with the runtime ID derived from that build's
verified Jitter2 lock/profile; never copy the expected value from the artifact being checked. A
production loop should cap catch-up steps and take its timing from the game's simulation owner
rather than assuming `Update` is authoritative.

## Load a file-delivered artifact on a server

```csharp
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Integration;
using Jitter2;

namespace MyGame.Server
{
public static class PhysicsStartup
{
    public static JitterPhysicsServerState Start(
        World world,
        string manifestPath,
        string runtimeCompatibilityId,
        string expectedLevelId,
        int tickRate)
    {
        IPhysicsArtifactProvider provider =
            new FilePhysicsArtifactProvider(manifestPath);

        var options = new JitterPhysicsServerOptions(
            runtimeCompatibilityId,
            expectedLevelId,
            tickRate);

        JitterPhysicsServerState state =
            JitterPhysicsServerStartup.Start(world, provider, options);

        System.Console.WriteLine(state.SelfCheck);
        return state;
    }
}
}
```

The match host must test `state.IsReady` before enabling connection approval. Dispose the world
through the match/session owner. See [Dedicated server](dedicated-server.md).

## Reject a mismatched peer before spawn

```csharp
using DataSakura.JitterPhysics.ArtifactCodec;

namespace MyGame.Networking
{
public static class PeerPhysicsCompatibility
{
    public static bool IsCompatible(
        byte[] peerPayload,
        PhysicsCompatibilityToken expected,
        out string reason)
    {
        if (!PhysicsCompatibilityToken.TryDecode(
                peerPayload,
                out PhysicsCompatibilityToken peer,
                out reason))
        {
            return false;
        }

        return peer.Matches(expected, out reason);
    }
}
}
```

Create `expected` with
`PhysicsCompatibilityToken.ForArtifact(serverState.Artifact, serverState.ArtifactHash)`. The
token is a correctness handshake, not authentication; protect it with the security model of
your transport.

## Implement a content-system provider

Convert expected delivery failures into a typed result and return only fully checked data.

```csharp
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;

namespace MyGame.Physics
{
public sealed class MemoryArtifactProvider : IPhysicsArtifactProvider
{
    private readonly byte[] payload;
    private readonly PhysicsArtifactManifest manifest;

    public MemoryArtifactProvider(
        byte[] payload,
        PhysicsArtifactManifest manifest)
    {
        this.payload = payload == null ? null : (byte[])payload.Clone();
        this.manifest = manifest;
    }

    public string Description => "memory:level-catalog";

    public PhysicsArtifactLoadResult Load(
        string expectedRuntimeCompatibilityId)
    {
        if (payload == null || manifest == null)
        {
            return PhysicsArtifactLoadResult.Failure(
                PhysicsArtifactErrorCode.SourceUnavailable,
                "The level catalog did not provide an artifact pair.",
                Description);
        }

        PhysicsArtifactResult decoded =
            PhysicsArtifactReader.Read(
                payload,
                manifest.ArtifactHash,
                manifest);
        if (!decoded.Succeeded)
        {
            return PhysicsArtifactLoadResult.Failure(
                decoded.Error,
                Description);
        }

        if (!string.IsNullOrEmpty(expectedRuntimeCompatibilityId))
        {
            PhysicsArtifactError compatibility =
                PhysicsArtifactReader.CheckRuntimeCompatibility(
                    decoded.Artifact,
                    expectedRuntimeCompatibilityId);
            if (compatibility.IsError)
            {
                return PhysicsArtifactLoadResult.Failure(
                    compatibility,
                    Description);
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

Treat `byte[]`, DTO lists, and mesh arrays as immutable after validation. The current DTO API
does not defensively copy every input.

## Control the shared Scene View preview

```csharp
using DataSakura.JitterPhysics.Editor.Api;

namespace MyGame.EditorTools
{
public static class PhysicsPreviewPreset
{
    public static void ShowBakeComparison()
    {
        JitterPhysicsPreviewState next =
            JitterPhysicsPreviewApi.Current
                .WithSources(true)
                .WithBaked(true)
                .WithRuntime(false)
                .WithScope(JitterPhysicsPreviewScope.ActiveOrSelectedLevel)
                .WithOcclusion(JitterPhysicsPreviewOcclusion.XRay);

        JitterPhysicsPreviewApi.Apply(next);
    }
}
}
```

This belongs in an Editor-only assembly. Reading `Current` is side-effect free; `Apply` changes
the existing personal preview state and raises `JitterPhysicsPreviewApi.Changed`.

## React to shared preview changes

An external Editor window can observe the package overlay without polling. Subscribe and
unsubscribe with the window lifecycle so domain reloads and closed windows do not retain a stale
delegate.

```csharp
using DataSakura.JitterPhysics.Editor.Api;
using UnityEditor;

namespace MyGame.EditorTools
{
public sealed class PhysicsPreviewMonitorWindow : EditorWindow
{
    [MenuItem("Window/My Game/Physics Preview Monitor")]
    private static void Open()
    {
        GetWindow<PhysicsPreviewMonitorWindow>("Physics Preview");
    }

    private void OnEnable()
    {
        JitterPhysicsPreviewApi.Changed += Repaint;
    }

    private void OnDisable()
    {
        JitterPhysicsPreviewApi.Changed -= Repaint;
    }

    private void OnGUI()
    {
        JitterPhysicsPreviewState state = JitterPhysicsPreviewApi.Current;
        EditorGUILayout.LabelField("Sources", state.Sources.ToString());
        EditorGUILayout.LabelField("Baked", state.Baked.ToString());
        EditorGUILayout.LabelField("Runtime", state.Runtime.ToString());
    }
}
}
```

Open **Window > My Game > Physics Preview Monitor**, then toggle a layer in the **Jitter
Physics** Scene View overlay. The monitor repaints with the same state. The callback runs on the
Editor main thread because the package changes preview preferences from Editor UI/API calls.

## Test a consumer integration

At minimum, automate these independent checks:

1. Import the package with no Jitter2 and compile.
2. Install exactly one Jitter2 and the adapter, then validate the receipt.
3. Build an unchanged scene twice and compare payload bytes/SHA-256.
4. Load the artifact in Unity and build a world.
5. Load the same payload/manifest in a plain .NET process.
6. Compare artifact hash and runtime compatibility ID across both consumers.
7. Run the target player/backend gate separately; a successful Editor test is not IL2CPP proof.

The package's own verification commands are listed in
[Troubleshooting](troubleshooting.md#package-maintainer-checks).
