# DataSakura Jitter Physics Baker

Deterministic, editor-time baking of a level's **static** collision geometry into a
versioned, content-addressed binary artifact, plus one shared loader that rebuilds the
exact same static topology in a [Jitter2](https://github.com/notgiven688/jitterphysics2)
`World` on the Unity client and on a .NET dedicated server.

The package does **not** own the simulation. `World.Step` stays with the consumer: the
server keeps stepping its authoritative world and the client keeps predicting, exactly as
before. What the package removes is hand-written static geometry that has to be kept
identical in two code bases by hand.

## Getting started

**[Documentation~/getting-started.md](Documentation~/getting-started.md)** walks the whole
path: adding the package, providing Jitter2, marking up a level, baking it, loading it in
Unity, and running the same bytes on a dedicated server.

After installing Jitter2 and the integration adapter, import the three runnable demos from
the package's **Samples** tab in Package Manager; see `Samples~/Demos/README.md`. Setup never
creates a second sample copy under `Assets/DataSakura`.

## Status

Early development (`0.0.5`). The assembly graph, the artifact contracts and the editor
bootstrap are being built stage by stage; see `CHANGELOG.md` for what already exists.

## Requirements

- Unity 6000.3 or newer.
- A `Jitter2.Core` assembly in the project, or the fallback copy this package installs on
  request. Baking and world building require Jitter2; **importing the package does not.**

## Design in one screen

- **Bake produces descriptors, not Jitter state.** The artifact stores world settings and
  ordered body/shape records. The loader rebuilds the world through public Jitter API.
  Nothing from Jitter's internals (handles, trees, contacts, islands) is ever serialized.
- **The package core never references Jitter.** `Contracts`, `ArtifactCodec`,
  `UnityArtifact`, `Authoring` and `Editor` compile in a project that has no Jitter2 at
  all, which is what makes a clean import possible. Jitter-dependent code lives in
  `JitterIntegration~/` and is installed by an explicit command.
- **External Jitter wins.** When the project already has a compatible `Jitter2.Core`, the
  package references it by assembly name and never copies, moves or edits it. The dormant
  snapshot in `Jitter2~/` is only for projects that have none, and for CI.
- **Fail fast.** A missing, corrupt or incompatible artifact stops the client before it
  connects and stops the server before it accepts players. There is no silent fallback to
  legacy geometry and no hot reload of a running match world.
- **Determinism claim, stated precisely.** The artifact is byte-exact and the static
  topology is identical on both sides. A bit-exact `World.Step` between Unity and .NET is
  *not* claimed: the server is authoritative and reconciliation absorbs the drift.

## Layout

```text
Runtime/Contracts       artifact DTO and identity rules   (no UnityEngine)
Runtime/ArtifactCodec   binary codec, limits, SHA-256     (no UnityEngine)
Runtime/UnityArtifact   artifact asset and project paths
Authoring/              level, sources and world profile
Editor/                 bootstrap, baking, inspectors, export
Tests/                  EditMode and PlayMode tests
Jitter2~/               dormant Jitter2 reference snapshot (not compiled by Unity)
JitterIntegration~/     Jitter-dependent adapter, installed on request
Server~/                server source projection and .NET tests
Samples~/Demos  Documentation~/  tools~/
```

## Loading on a server

`JitterPhysicsServerStartup.Start(world, provider, options)` is the whole bring-up: it
resolves one `IPhysicsArtifactProvider`, checks the artifact against what the build claims
to be — runtime semantics id, the level it was launched to host, the rate it steps at —
builds the static world and only then reports `IsReady`. Connection approval is gated on
that flag, and there is no partially ready state to gate on by mistake. `SelfCheck` is the
one line a deployment smoke test looks for.

`FilePhysicsArtifactProvider` covers artifacts delivered as content: it is given a manifest
path (typically `--physics-manifest <path>`) and reads the payload named by that manifest
from the same folder. Delivering those two files — published content, a mounted volume, an
artifact registry — is the consumer's decision and the package makes no assumption about
it. See `Server~/README.md`.

## Editor entry points

- `Tools > DataSakura > Jitter Physics Baker Window` — the single authoring surface, titled
  **DS Jitter Physics** when docked. Its
  workflow matches the other DataSakura authoring packages: **Overview** explains the level
  and shows the cached readiness result, **Geometry** owns explicit static-body markup,
  **Bake** owns deterministic build and delivery, **Settings** owns shared world-profile UX,
  project/default links and server delivery, while **Diagnostics** verifies,
  exports and diagnoses the exact bytes. The five sections remain a horizontal toolbar at
  narrow widths, matching DS Navigation. Opening or repainting any section performs no
  project mutation.
- The compact `JitterPhysicsLevel` Inspector exposes Level, Geometry Root, Settings, cached
  Bake Status and explicit **Validate / Bake / Open** actions. Output details are under
  **Advanced**. The commands call the same public `JitterPhysicsBakeCommand` entry points as
  automation; removing their old Tools shortcuts does not create a second bake pipeline.
- `Project Settings > DataSakura > Jitter Physics` selects the one shared default world
  profile and the authoring/generated folders. `Preferences > DataSakura > Jitter Physics >
  Scene Preview` stores only personal display state. A level exposes **Edit / New / Make Local
  Copy**; the last action preserves values but reassigns only that level.
- The native Scene View **Jitter Physics** overlay provides read-only Sources, Baked and
  Runtime layers, level scope, Visible/X-Ray occlusion, Settings and Frame Level. Sources use
  dashed sand lines, the exact last bake uses an ochre outline/fill, runtime uses a thicker
  marked outline, and current differences use tobacco hatching with Changed/Moved/Removed
  labels. Runtime reports `No runtime data` unless an active world supplies exact records
  through `IJitterPhysicsRuntimePreviewSource`. Opening or repainting the overlay never bakes,
  hashes or creates a runtime world. The previous baked snapshot remains visible when current
  geometry is moved or removed.
- **Settings > Advanced installation and maintenance > Open installation details** installs the fallback Jitter2 copy or the Jitter
  adapter and validates the installation. Its **Advanced** foldout owns migration, server
  projection and removal. Every action is explicit, an external Jitter2 is never touched,
  and a file modified after installation stops an update instead of
  being overwritten. Integration and its receipt live in
  `Assets/DataSakura/JitterPhysicsBaker`; an explicit Advanced action safely migrates the legacy
  `Assets/DataSakura/JitterPhysics` layout when it contains only unmodified receipt-owned files.

## License

MIT, see `LICENSE.md`. Third-party components are listed in `Third Party Notices.md`.
