# Configuration

[Documentation home](index.md) · [Editor guide](editor-guide.md) ·
[Runtime API](runtime-api.md) · [Troubleshooting](troubleshooting.md)

This reference records the defaults and accepted ranges in DataSakura Jitter Physics Baker
`0.7.0`. World and source values are content: they affect baked bytes. Project folders and
personal preview preferences do not describe simulation, but they control where content is
created and how it is inspected.

## Project settings

Open **Project Settings > DataSakura > Jitter Physics**.

| Field | Default | Rules |
| --- | --- | --- |
| **Default World Profile** | None | Assigned by an explicit asset selection, **Create Defaults**, or **Create Level** when no default exists. |
| **Profiles Folder** | `Assets/JitterPhysics/Settings` | Must be `Assets` or a descendant and must not contain `..`. |
| **Generated Folder** | `Assets/Generated/JitterPhysics` | Must be `Assets` or a descendant and must not contain `..`. |

The settings are stored in:

```text
ProjectSettings/DataSakuraJitterPhysicsSettings.asset
```

Opening Project Settings creates no assets. **Create Defaults** creates or selects:

```text
Assets/JitterPhysics/Settings/JitterPhysicsWorldDefaults.asset
```

Changing a project default does not silently rewrite existing levels or profiles.

## JitterPhysicsLevel

Add with **Add Component > DataSakura > Jitter Physics > Jitter Physics Level**.

| Field | Initial/default value | Behavior |
| --- | --- | --- |
| **Level Id** | Empty in serialized data; Reset derives it from the scene name | Persistent level, artifact, and handshake identity. Validation can normalize and assign it. |
| **Geometry Root** | Own transform after Reset | Collect sources below this transform. When null, search all roots in the level's scene, but still collect only explicit sources. |
| **World Profile** | None | Required. No implicit runtime fallback is used. |
| **Generated Folder** | `Assets/Generated/JitterPhysics` | Destination for the stable artifact triplet. |
| **Last Artifact Hash** | Empty | Diagnostic value updated after a successful bake; read-only in Advanced Inspector UI. |

Use one intentional level definition per scene. Source collection excludes inactive objects.

Treat **Level Id** as content identity, not a display label. Changing it changes artifact names and
the value used for client/server agreement.

## JitterStaticBodySource

Add with **Add Component > DataSakura > Jitter Physics > Jitter Static Body Source**.

| Field | Default | Accepted range and behavior |
| --- | --- | --- |
| **Source Id** | Canonical value derived from the GameObject name on Reset | Persistent body identity. Renaming the GameObject does not change an existing ID. Duplicate IDs block baking. |
| **Include Children** | `true` | Includes enabled colliders on active child objects. Inactive children are always excluded. |
| **Friction** | `0.2` | `0.0`–`1.0`. Written on the static body. |
| **Restitution** | `0.0` | `0.0`–`1.0`. Written on the static body. |

Each source becomes one static body. Supported shape inputs are `BoxCollider`, `SphereCollider`,
`CapsuleCollider`, and `MeshCollider`.

Conversion rules that affect authoring:

- triggers are rejected;
- disabled colliders and inactive objects are ignored;
- every primitive extent must be at least `1e-5`;
- non-uniform sphere scale uses the largest axis and emits a warning;
- `MeshCollider.sharedMesh` must exist, be readable, and contain triangle indices;
- mirrored mesh transforms have their triangle winding corrected during baking;
- source bodies and shapes are ordered canonically by stable IDs and shape keys, not hierarchy
  enumeration order.

## JitterPhysicsWorldProfile

Create with **Assets > Create > DataSakura > Jitter Physics > World Profile**.

| Field | Default | Accepted value |
| --- | --- | --- |
| **Gravity** | `(0, -9.81, 0)` | Every component must be finite. NaN or infinity is reset to the default during `OnValidate`. |
| **Tick Rate** | `30` | Integer `1`–`1000`. Consumers step with `1 / Tick Rate`. |
| **Substep Count** | `1` | Integer `1`–`64`. Serialized into the artifact; see the limitation below. |
| **Solver Iterations** | `6` | Integer `1`–`256`. |
| **Relaxation Iterations** | `4` | Integer `0`–`256`. |
| **Allow Deactivation** | `true` | Enables sleeping when applied by the current runtime builder. |

The profile is a shared asset. **Edit** changes the shared instance. Use **Make Local Copy** before
changing values for only one level.

> [!IMPORTANT]
> In `0.7.0`, **Substep Count** is serialized and validated but
> `JitterPhysicsWorldBuilder` does not currently assign it to the rebuilt Jitter2 world. Values
> greater than `1` therefore do not change that world. Keep it at `1` when integration depends on
> behavior documented by this release, and do not claim that a higher value has been applied.

World settings are baked into the artifact. Changing a profile does not update an existing
artifact; validate and bake again, then redeliver the new bytes to every consumer.

## Profile actions

The Overview tab and Level Inspector expose:

- **Edit** — select the current profile and warn when loaded levels share it;
- **New** — create and assign a new profile based on the project default;
- **Make Local Copy** — copy all values to
  `<Profiles Folder>/<level-id>_WorldProfile.asset`, assign it to this level, and record Undo.

Prefab instances retain their normal Unity override semantics.

## Generated artifact paths

For Level ID `arena`, the default Generated Folder receives:

```text
Assets/Generated/JitterPhysics/arena.physics.bytes
Assets/Generated/JitterPhysics/arena.physics.manifest.json
Assets/Generated/JitterPhysics/arena.physics.asset
```

The binary is the canonical payload. The manifest carries counts, hash, tick rate, and runtime
compatibility metadata. The Unity asset is a stable serialized reference to the payload and its
summary. Do not deliver only one member of the payload/manifest pair.

## Server upload preferences

These fields are in the Baker window's **Settings** tab and are stored as project-scoped Editor
preferences:

| Field | Default | Accepted value |
| --- | --- | --- |
| **Base URL** | `http://127.0.0.1:5000` | Absolute `http` or `https` URL. Upload appends `/api/artifacts`. |
| **Timeout (seconds)** | `10` | Clamped to `1`–`120`. |
| **Upload token** | Empty | Sent as `X-Jitter-Physics-Token` when non-empty. Do not include it in reports. |

Upload uses existing verified bytes. It does not bake and does not tell an already running server
to rebuild its world unless that server implements such behavior itself.

## Scene Preview preferences

Open **Preferences > DataSakura > Jitter Physics > Scene Preview**, or press **Settings** in the
Scene View overlay.

| Setting | Default |
| --- | --- |
| **Sources** | Off |
| **Baked** | Off |
| **Runtime** | Off |
| **Scope** | Active or selected level |
| **Occlusion** | Visible |

These values are stored in EditorPrefs for the current user. Resetting them deletes those keys and
repaints Scene View; it does not modify project assets or artifact identity.

## Sample-specific defaults

Generated demo scenes intentionally use a faster profile than a new project level:

| Sample value | Default |
| --- | --- |
| Tick Rate | `60` |
| Substep Count | `1` |
| Solver Iterations | `6` |
| Relaxation Iterations | `4` |
| Allow Deactivation | `true` |
| Generated source friction | `0.4` |
| Generated source restitution | `0.1` |

`JitterPhysicsSampleWorld` defaults **Step Automatically** to `true` and
**Max Catch Up Steps** to `4`, with an accepted range of `1`–`8`.

The Bouncing Ball component defaults are:

| Field | Default | Range |
| --- | --- | --- |
| Spawn Point | `(0, 12, 0)` | Unbounded Vector3 |
| Spawn Spread | `3` | Serialized float |
| Initial Balls | `5` | `0`–`32` |
| Radius | `0.5` | `0.1`–`2` |
| Mass | `1` | `0.1`–`20` |
| Restitution | `0.75` | `0`–`0.99` |
| Friction | `0.3` | `0`–`2` |
| Drop Key | `Space` | Unity `KeyCode` |
| Clear Key | `Backspace` | Unity `KeyCode` |

These dynamic-body sample values are demonstrations; they are not part of the static artifact
format.

## Artifact safety limits

The codec validates these hard caps before allocating or building a world:

| Limit | Value |
| --- | ---: |
| Artifact payload | 64 MiB |
| Static bodies per level | 65,536 |
| Shapes per body | 4,096 |
| Shapes per level | 262,144 |
| Vertices per mesh | 1,000,000 |
| Indices per mesh | 3,000,000 |
| Vertices per level | 4,000,000 |
| Indices per level | 12,000,000 |
| Canonical Level/Source ID | 64 lowercase ASCII characters |
| Serialized string or shape-key length | 512 UTF-8 bytes |
| Coordinate magnitude | `1e6` |
| Primitive extent | `1e5` maximum, `1e-5` minimum in Editor conversion |

These are rejection boundaries, not recommended production budgets. Profile and optimize a real
consumer level independently.
