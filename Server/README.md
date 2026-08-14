# Jitter Physics Web Viewer

A standalone .NET dedicated-server example for the
`com.datasakura.jitter-physics-baker` package. It loads baked level artifacts, rebuilds each
static world with the package's shared loader, steps them on a fixed timestep, and renders
the result — static geometry plus falling dynamic bodies — in a browser. A dropdown switches
between levels.

Nothing here references Unity. The project compiles the package's portable sources
(`Runtime/Contracts`, `Runtime/ArtifactCodec`, `JitterIntegration~/Runtime`) and references the
built Jitter2 assembly, which is the whole point: it proves the same code the Unity client
bakes against also builds and runs a server with no engine present.

## The demo levels

Five levels ship as **committed Unity scenes** under
`Assets/JitterPhysicsBaker/Demo/Scenes/` — you open, edit and bake them like any scene:

| Scene | Level id | What it shows |
| --- | --- | --- |
| `JitterDemoArena.unity` | `demo_arena` | Every collider type, including a mesh hill |
| `DemoTower.unity` | `demo_tower` | A staggered box tower dropped balls can topple |
| `DemoRamps.unity` | `demo_ramps` | A descending run of ramps a ball rolls down |
| `DemoBowl.unity` | `demo_bowl` | An octagonal bowl balls settle into |
| `DemoPillars.unity` | `demo_pillars` | A walled field of capsule pillars and crates |

The four box/sphere/capsule scenes and their seed artifacts come from one definition,
`Server/demo-levels.json`, so a scene and the artifact the server shows for it cannot describe
different geometry. `tools/author-demo-scenes.py` writes the committed `.unity` files from that
definition; the arena is authored separately because it carries a mesh collider.

### Playing the scenes in Unity

The committed scenes are safe to keep in the development project before Jitter is installed:
their runtime assembly is gated by `DATASAKURA_JITTER_INTEGRATION`. Use **Tools > DataSakura >
Jitter Physics > Setup** to install Jitter2 and then the integration adapter; the installer adds
the gate only after the adapter was written successfully. Next run **Demo > Bake All Demo Scenes**,
open any demo scene and enter Play Mode. Baking also assigns the generated artifact to the runtime
component, so the same scene keeps working when it is included in a standalone player.

The panel in the top-left corner is the runtime control surface. It can drop spheres or boxes,
clear dynamic bodies, pause/resume the Jitter world and toggle automatic drops. It intentionally
uses IMGUI rather than Unity's legacy input API, so the controls work in projects configured for
the new Input System as well.

## Layout

```
Server/
├── JitterPhysicsWebViewer/        the web server (ASP.NET Core minimal API + three.js page)
│   ├── Program.cs                 hosts every level: load → verify → build world → serve
│   ├── HostedLevel.cs             one level's world, view and tick loop
│   ├── JitterLock.cs              derives the runtime compatibility id from jitter2.lock.json
│   ├── PhysicsSimulation.cs       the fixed-step tick loop and body spawning
│   ├── LevelView.cs               projects the decoded artifact into render data
│   └── wwwroot/index.html         the viewer, with a level picker
├── tools/GenerateSampleArtifact/  headless seed generator (no Unity needed)
├── demo-levels.json               the level definitions (scenes + seeds share them)
└── artifacts/                     delivered artifacts (git-ignored, regenerated on demand)
```

## Running it

### 1. Get artifacts into `Server/artifacts/`

**From Unity (the source of truth):**

`Tools > DataSakura > Jitter Physics > Demo > Bake All Demo Scenes` opens each committed
scene, bakes it and exports the exact bytes here — one artifact per level.

**Without Unity (seeds, for a clean checkout or CI):**

```sh
dotnet run --project Server/tools/GenerateSampleArtifact
```

This writes all five levels from `demo-levels.json` using the package's own writer. Seeds are
a stand-in until a Unity bake replaces them; both produce files the loader accepts.

### 2. Run the server

```sh
dotnet run --project Server/JitterPhysicsWebViewer
```

Then open the URL it prints (default `http://localhost:5000`). Pick a level from the dropdown,
drop spheres and boxes with the buttons and watch them settle on the baked geometry; resting
bodies grey out as Jitter deactivates them.

Every level is loaded, verified and built before the port opens. If any one fails, the server
refuses to start rather than quietly host a subset. It prints a one-line self-check per level
that a deployment smoke test can grep for:

```
[JitterPhysics] physics self-check OK level=demo_arena artifact=8e5fa6f77ee3 topology=08669a69170a bodies=14 shapes=527 triangles=512 tickRate=60 elapsedMs=15.1
```

### Command-line options

| Option | Meaning |
|---|---|
| `--manifest <path>` | Load this exact manifest instead of scanning `artifacts/`. |
| `--artifacts <dir>` | Scan this folder for the single manifest to load. |
| `--level <id>` | Refuse to start unless the artifact's level id matches. |
| `--tick-rate <hz>` | Refuse to start unless the artifact's tick rate matches. |

The last two exist so a launcher that knows what it is hosting turns "the wrong artifact was
delivered" into a startup failure instead of a match on the wrong map.

## HTTP API

| Route | Purpose |
|---|---|
| `GET /api/levels` | The catalogue the level picker is built from. |
| `GET /api/status/{id}` | Self-check, identity hashes, counts, runtime id for one level. |
| `GET /api/level/{id}` | The static level, once, for the page to build meshes from. |
| `GET /api/state/{id}` | That level's current dynamic bodies; polled each frame. |
| `POST /api/spawn/{id}?type=sphere\|box&count=N` | Drop bodies into one level. |
| `POST /api/reset/{id}` | Remove that level's dynamic bodies; the static level is untouched. |

## Why a seed generator exists

"Green in Unity" says nothing about the server: it compiles the same sources with a
different compiler and runtime. The generator lets this project — and CI — build and run the
full load → world-build → step → render path with no editor in the loop. The authoritative
artifact still comes from the Unity bake; the two share the codec, so an identical scene
produces identical bytes.

