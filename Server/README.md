# Jitter Physics Web Viewer

A standalone .NET dedicated-server example for the
`com.datasakura.jitter-physics-baker` package. It loads a baked level artifact, rebuilds
the static world with the package's shared loader, steps it on a fixed timestep, and
renders the result — static geometry plus falling dynamic bodies — in a browser.

Nothing here references Unity. The project compiles the package's portable sources
(`Runtime/Contracts`, `Runtime/ArtifactCodec`, `JitterIntegration~/Runtime`) and the locked
Jitter2 snapshot **by reference**, which is the whole point: it proves the same code the
Unity client bakes against also builds and runs a server with no engine present.

## Layout

```
Server/
├── JitterPhysicsWebViewer/        the web server (ASP.NET Core minimal API + three.js page)
│   ├── Program.cs                 startup order: load → verify → build world → serve
│   ├── JitterLock.cs              derives the runtime compatibility id from jitter2.lock.json
│   ├── PhysicsSimulation.cs       the fixed-step tick loop and body spawning
│   ├── LevelView.cs               projects the decoded artifact into render data
│   └── wwwroot/index.html         the viewer
├── tools/GenerateSampleArtifact/  headless seed generator (no Unity needed)
└── artifacts/                     delivered artifacts (git-ignored, regenerated on demand)
```

## Running it

### 1. Get an artifact into `Server/artifacts/`

**From Unity (the source of truth):**

`Tools > DataSakura > Jitter Physics > Demo > Create Demo Scene And Bake` builds the demo
scene, bakes it and exports the exact bytes here. Or, with the editor closed:

```sh
tools/bake-demo-scene.sh
```

**Without Unity (a seed, for a clean checkout or CI):**

```sh
dotnet run --project Server/tools/GenerateSampleArtifact
```

This writes the same demo arena using the package's own writer. It is a stand-in until a
Unity bake replaces it; both produce a file the loader accepts.

### 2. Run the server

```sh
dotnet run --project Server/JitterPhysicsWebViewer
```

Then open the URL it prints (default `http://localhost:5000`). Drop spheres and boxes with
the buttons and watch them settle on the baked geometry; resting bodies grey out as Jitter
deactivates them.

The server refuses to serve a level it could not load, and prints a one-line self-check on
startup that a deployment smoke test can grep for:

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
| `GET /api/status` | Self-check, identity hashes, counts, runtime id. |
| `GET /api/level` | The static level, once, for the page to build meshes from. |
| `GET /api/state` | The current dynamic bodies; polled each frame. |
| `POST /api/spawn?type=sphere\|box&count=N` | Drop bodies. |
| `POST /api/reset` | Remove all dynamic bodies; the static level is untouched. |

## Why a seed generator exists

"Green in Unity" says nothing about the server: it compiles the same sources with a
different compiler and runtime. The generator lets this project — and CI — build and run the
full load → world-build → step → render path with no editor in the loop. The authoritative
artifact still comes from the Unity bake; the two share the codec, so an identical scene
produces identical bytes.

