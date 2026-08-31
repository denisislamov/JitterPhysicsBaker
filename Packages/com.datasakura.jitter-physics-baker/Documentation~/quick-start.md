# Quick Start

[Documentation home](index.md) · [Installation](installation.md) ·
[Editor guide](editor-guide.md) · [Troubleshooting](troubleshooting.md)

This walkthrough proves the shortest complete package path in Unity: configure Jitter2, install
the adapter, import a native UPM sample, bake deterministic static geometry, and run that artifact
in a real Jitter2 world. It is written for DataSakura Jitter Physics Baker `0.0.12` and normally
takes 5–15 minutes after the package has resolved.

## Before you begin

- Use Unity `6000.3` or later. The repository's recorded Editor revision is `6000.3.19f1`.
- Install package version `0.0.12`; see [Installation](installation.md) for the Git URL and local
  package forms.
- Exit Play Mode and wait for script compilation to finish.
- Save or commit work in the consumer project. The setup and sample commands below deliberately
  create project files.

Confirm that Package Manager shows **DataSakura Jitter Physics Baker** version `0.0.12` before
continuing.

## 1. Create the first level

Open **Tools > DataSakura > Jitter Physics Baker Window**.

In the current UI, **Settings** needs a selected `JitterPhysicsLevel`. In a new scene the window
therefore shows its empty state and the **Create Level** action.

Press **Create Level**, then save the scene.

This explicit action:

- creates a GameObject named **Jitter Physics Level**;
- adds `JitterPhysicsLevel`;
- uses the object transform as **Geometry Root**;
- creates the shared default profile when the project has none;
- assigns the project-wide **Generated Folder**;
- selects the new object and saves project assets.

It is an authoring bootstrap required by the current window layout. It does not bake an artifact.

## 2. Open installation details

With the new level selected:

1. Select **Settings** in the Baker window.
2. Expand **Advanced installation and maintenance**.
3. Press **Open installation details**.

The **Jitter Physics — Setup** window reports the discovered assembly, expected and actual source
hashes, compile profile ID, runtime compatibility ID, and receipt state. A clean project normally
starts with status `Missing`.

Opening the report does not install or update anything.

## 3. Install Jitter2

When status is `Missing`, press **Install Jitter2**. Wait for Unity to refresh and compile before
continuing.

The command installs the receipt-owned fallback under:

```text
Assets/DataSakura/ThirdParty/Jitter2/
```

It writes `Jitter2.Core.dll` and, unless the project already supplies it,
`System.Runtime.CompilerServices.Unsafe.dll`. It refuses to add a fallback when the project has an
external Jitter2 that the package does not own.

Expected result: Setup reports `Compatible` and exactly one `Jitter2.Core` is present.

## 4. Install the integration adapter

Press **Install/update integration**. Wait for Unity compilation again, then press
**Validate installation**.

The adapter is installed under:

```text
Assets/DataSakura/JitterPhysicsBaker/Integration/
```

The receipt is stored at:

```text
Assets/DataSakura/JitterPhysicsBaker/InstallationReceipt.json
```

Expected result:

- Setup remains `Compatible`;
- the receipt has no missing or modified package-owned files;
- the Console has no new compilation errors;
- the adapter can resolve `Jitter2.Core`.

Do not import the runnable samples before this step. Their runtime assembly references the
installed adapter by name, while Package Manager cannot make its standard **Import** action
conditional on that project-owned assembly.

## 5. Import Physics Baking Demos

Either press **Open Package Manager samples** in Setup or open **Window > Package Manager**.
Select **DataSakura Jitter Physics Baker**, open **Samples**, and import
**Physics Baking Demos**.

For `0.0.12`, Unity normally creates:

```text
Assets/Samples/DataSakura Jitter Physics Baker/0.0.12/Physics Baking Demos/
```

Imported samples are consumer-owned, versioned copies. Updating or removing the package does not
silently update or delete them.

This clean-project walkthrough uses the receipt-owned precompiled fallback installed in step 3. If
the project already used a compatible source-based `Jitter2.Core` asmdef instead, add
`"Jitter2.Core"` to the imported
`Runtime/DataSakura.JitterPhysics.Samples.asmdef` `references` array now and wait for compilation.
The sample scripts use Jitter2 types directly, so that reference is not inherited through the
integration asmdef. Do not edit the Package Cache copy.

## 6. Generate and bake Bouncing Ball

Run:

**Assets > DataSakura > Jitter Physics > Samples > Build and bake: Bouncing Ball**

If the current scene has unsaved changes, Unity asks whether to save them. Continuing replaces the
open scene with a new empty scene, generates the sample hierarchy, saves
`Scenes/SampleBouncingBall.unity`, creates or reuses `SampleWorldProfile.asset`, writes the baked
files under the imported sample's `Generated` folder, and assigns the artifact to the runtime
components.

The Console reports:

```text
[JitterPhysics] Bouncing Ball sample is ready. Press Play, then Space to drop a ball.
```

Inspect the generated scene before entering Play Mode. The command has performed the same
component/field assignments that a project integration must own explicitly:

| Object or asset | Required assignment produced by the command |
| --- | --- |
| **Jitter Physics Level** | `JitterPhysicsLevel` with **Level Id** `sample_bouncing_ball`, **Geometry Root** set to **Baked Geometry**, the sample profile assigned, and the imported sample's **Generated Folder** |
| Direct children of **Baked Geometry** | `JitterStaticBodySource` with a stable sanitized **Source Id**, **Include Children** enabled, **Friction** `0.4`, and **Restitution** `0.1` |
| **SampleWorldProfile** | Gravity `(0, -9.81, 0)`, tick rate `60`, substeps `1`, solver iterations `6`, relaxation iterations `4`, and deactivation enabled |
| **Jitter Physics Runtime** | `JitterPhysicsSampleWorld`, body views, bouncing-ball controller, and artifact verification, all pointing at the artifact produced by this bake |

The minimal consumer code is the imported, compiled
[`JitterPhysicsSampleWorld.cs`](../Samples~/Demos/Runtime/JitterPhysicsSampleWorld.cs): it loads
the assigned asset, creates a candidate `World`, applies static geometry once, steps at the
artifact tick rate, publishes runtime-preview records only after success, and disposes the world
in `OnDestroy`. Use the stricter build-runtime-ID injection pattern from
[Runtime API](runtime-api.md#loading-a-unity-artifact) when adapting it to production.

The scene generator does not add this scene to Build Settings. That is not required for this
Editor Play Mode walkthrough; add it explicitly before using a build workflow that requires the
scene there.

## 7. Enter Play Mode

Press **Play**.

The runtime loads and verifies the same baked bytes, creates a new Jitter2 world, applies the
static bodies, and only then spawns dynamic balls. The expected Console line begins with:

```text
[JitterPhysics] sample world ready level=sample_bouncing_ball
```

The Game view must show:

```text
Artifact verification: PASSED
```

The sample starts with five balls. Use:

- **Space** — drop another ball;
- **Backspace** — clear the dynamic balls.

The balls must collide with the baked floor, ramp, walls, step, sphere, and capsule. A moving ball
or a green Unity Console alone is not sufficient: confirm the on-screen verification result and
the `sample world ready` line.

## 8. Validate and inspect the result

Exit Play Mode. Return to the Baker window and press **Validate**.

Validation does not write `.physics.bytes`, `.physics.manifest.json`, or `.physics.asset` files.
It can, however, assign canonical values to an empty **Level Id** or **Source Id** and mark those
authoring objects dirty. Review and save that scene change intentionally.

For the generated sample, validation should report no blocking errors. Open **Diagnostics** to
verify the artifact, run the repeat-bake determinism check, and inspect its full hash and runtime
compatibility ID.

## Two common setup failures

### Setup does not become `Compatible`

Symptom: Setup remains `Missing`, `Incompatible`, `Duplicate`, or `UnsupportedPlugin`, or validation
reports that the runtime compatibility ID is unavailable.

Check the discovered `Jitter2.Core` entries, expected/actual source hashes, and receipt state in
**Jitter Physics — Setup**. Keep exactly one compatible Jitter2. For a clean project, press
**Install Jitter2**; for a consumer-owned copy, align it with `jitter2.lock.json`. Then press
**Install/update integration**, wait for compilation, and press **Validate installation** again.

### The imported sample cannot resolve Jitter2 or the integration

Install the integration before importing the sample and confirm that
`Assets/DataSakura/JitterPhysicsBaker/Integration/` exists. If the project uses the receipt-owned
precompiled Jitter2 fallback, wait for compilation; its plugin is auto-referenced.

If the project instead uses a compatible source-based `Jitter2.Core` asmdef, open the imported
consumer copy at
`Assets/Samples/DataSakura Jitter Physics Baker/0.0.12/Physics Baking Demos/Runtime/`
and add `"Jitter2.Core"` to the `references` array in
`DataSakura.JitterPhysics.Samples.asmdef`. The sample scripts use Jitter2 types directly, and asmdef
references are not transitive through `DataSakura.JitterPhysics.JitterIntegration`. Wait for Unity
to compile, then run **Build and bake: Bouncing Ball** again. Do not edit the Package Cache copy.

## Next steps

- Use [Editor guide](editor-guide.md) to author a project level rather than a generated demo.
- Use [Configuration](configuration.md) before changing world or material values.
- Use [Runtime API](runtime-api.md) before adding the package to a consumer-owned tick loop.
- Use [Troubleshooting](troubleshooting.md) if any expected message above differs.
