# DataSakura Jitter Physics Baker

DataSakura Jitter Physics Baker turns explicitly marked Unity colliders into a deterministic,
SHA-256-verified artifact, then rebuilds the same static Jitter2 topology on a Unity client and a
.NET dedicated server through one shared world builder.

The package owns authoring, validation, baking, artifact verification, and static-world
construction. Your game continues to own dynamic bodies, networking, scene lifetime, connection
approval, and every call to `World.Step`.

Current package version: **0.7.0**. Artifact schema: **1**.

## Highlights

- Byte-deterministic Editor bake with a canonical binary format and manifest.
- Explicit `JitterPhysicsLevel` and `JitterStaticBodySource` authoring; unmarked colliders are
  ignored.
- `BoxCollider`, `SphereCollider`, `CapsuleCollider`, and `MeshCollider` conversion.
- Typed, fail-fast artifact and compatibility validation before a world is accepted.
- One portable contracts/codec boundary and one Jitter-dependent builder for Unity and .NET.
- Receipt-managed fallback Jitter2, Unity integration adapter, and server source projection.
- Scene View Sources/Baked/Runtime comparison without baking or writing during repaint.
- Importable Bouncing Ball, FPS Shooter, and Artifact Verification samples.

## Requirements

- Unity `6000.3` minimum; the exact verified Editor revision is `6000.3.19f1`.
- Exactly one compatible `Jitter2.Core` is required to bake or build a world. The core package
  imports without Jitter2.
- The portable server harness targets .NET 10; the consumer owns the server host and tick loop.

IL2CPP, individual player platforms, and Built-in/URP/HDRP player matrices are not certified by
Editor tests alone. See [Requirements and compatibility](Documentation~/requirements-and-compatibility.md)
for the precise verified and unverified scope.

## Install

In **Window > Package Manager**, choose **Install package from git URL...** and enter:

```text
https://github.com/denisislamov/jitter-physics-baker.git#v0.7.0
```

Then open **Tools > DataSakura > Jitter Physics Baker Window**. In a clean scene, press
**Create Level** before opening **Settings**. Use **Open installation details** to select one
compatible Jitter2 and install the integration adapter explicitly.

The complete Git, local-development, Jitter2, removal, and verification procedures are in
[Installation](Documentation~/installation.md).

## Quick Start

For a working result in 5–15 minutes:

1. Install the tagged package, one compatible Jitter2, and the integration adapter.
2. Import **Physics Baking Demos** from the package's **Samples** tab.
3. Run **Assets > DataSakura > Jitter Physics > Samples > Build and bake: Bouncing Ball**.
4. Enter Play Mode and press Space.

The ball should collide with the baked static surfaces, and Artifact Verification should pass.
Follow the detailed [Quick Start](Documentation~/quick-start.md) for exact expected results and
diagnosis steps.

## Documentation and samples

- [Full manual](Documentation~/index.md)
- [Concepts and architecture](Documentation~/concepts-and-architecture.md)
- [Editor guide](Documentation~/editor-guide.md)
- [Runtime API](Documentation~/runtime-api.md)
- [Dedicated-server integration](Documentation~/dedicated-server.md)
- [Migration and upgrading](Documentation~/migration-and-upgrading.md)
- [Troubleshooting](Documentation~/troubleshooting.md)
- [Physics Baking Demos](Samples~/Demos/README.md)
- [Changelog](CHANGELOG.md)

## Important limits

- The package does not bake at runtime and does not serialize a running Jitter2 world.
- It does not provide a tick loop, character controller, networking transport, prediction system,
  content service, or standalone server executable.
- `SubstepCount` is currently serialized but not applied by the shared world builder.
- After a failed world apply, discard that world; rollback removes created bodies but does not
  restore every changed world setting.
- After any failed Editor bake, re-bake and verify the complete payload/manifest/Unity-asset trio;
  a late Unity import failure can occur after the pair was replaced.
- `TopologyFingerprint` is diagnostic. Use `artifactHash + runtimeCompatibilityId` for
  client/server compatibility.
- Bit-identical `World.Step` output across Unity and .NET runtimes is not claimed.

## License

The package is licensed under the [MIT License](LICENSE.md). Redistributed Jitter2 files retain
their own MIT notice; see [Third Party Notices](Third%20Party%20Notices.md).
