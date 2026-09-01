# Artifact format v1

Applies to package version **0.7.0** and artifact schema **1**.

[Documentation index](index.md) · [Quick start](quick-start.md) ·
[Configuration](configuration.md) · [Troubleshooting](troubleshooting.md) ·
[Recipes](recipes.md)

An artifact is a deterministic binary payload plus a UTF-8 JSON manifest. The payload is the
authoritative simulation description. The manifest supplies delivery identity and cheap
cross-checks. Neither file contains timestamps, absolute paths, Unity instance IDs, Jitter2
runtime handles, or serialized broadphase/solver state.

Changing this binary layout requires an artifact schema version bump. Changing how the same
records are interpreted requires a runtime semantics version bump even when the schema stays 1.

## Identity terms

| Identity | Meaning | Changes when |
| --- | --- | --- |
| Package version | UPM release that produced tooling/content | Package release changes |
| Artifact schema version | Binary layout that a reader can parse | A field/type/order/encoding changes |
| Artifact hash | Lowercase hex SHA-256 of the exact payload bytes | Any payload byte changes |
| Runtime compatibility ID | SHA-256 of schema, Jitter2 source/profile, and package semantic versions | World interpretation changes |

A safe runtime match requires both the full artifact hash and the runtime compatibility ID. A
matching schema alone proves only that the bytes are parseable.

## Byte order and primitives

- All integers and IEEE-754 `float32` values are little-endian.
- Strings are strict UTF-8 without a BOM, prefixed by a little-endian `uint16` byte length.
- Strings are capped at 512 UTF-8 bytes.
- Booleans are one byte and accept only `0` or `1` where specified.
- Negative zero, NaN, and infinity are rejected.
- Quaternions are normalized and sign-canonical in `(x, y, z, w)` order.
- The reader rejects unread trailing bytes.

The codec writes byte order explicitly rather than depending on `BinaryWriter` or host
endianness. Manifest output uses invariant culture, fixed key order, and LF line endings.

## Payload layout

Fields appear exactly in this order.

### Header and world settings

| Field | Encoding | Rule |
| --- | --- | --- |
| Magic | 4 bytes | ASCII `JPHY` |
| Schema version | `uint16` | Must equal `1` |
| Reserved | `uint16` | Must be `0` |
| Runtime compatibility ID | 32 raw bytes | SHA-256 digest represented as 64 hex characters in memory |
| Level ID | string | Canonical lowercase ASCII ID |
| Gravity | 3 × `float32` | Finite vector |
| Tick rate | `int32` | 1 through 1,000 |
| Substep count | `int32` | 1 through 64 |
| Solver iterations | `int32` | 1 through 256 |
| Relaxation iterations | `int32` | 0 through 256 |
| Allow deactivation | `uint8` | `0` or `1` |
| Solve mode | `uint8` | `1`, deterministic only |
| Multi-threaded | `uint8` | `0`, single-threaded only |
| Body count | `int32` | 0 through 65,536 |

`SubstepCount` is part of schema 1 and therefore part of the payload hash. In package 0.7.0 the
world builder does not assign it to `World.SubstepCount`; this is a runtime implementation gap,
not permission to remove or reinterpret the field.

### Body record

Each body contains:

| Field | Encoding | Rule |
| --- | --- | --- |
| Source ID | string | Canonical; bodies strictly ascending by ordinal ID |
| Position | 3 × `float32` | Each component magnitude at most 1,000,000 |
| Orientation | 4 × `float32` | Normalized and sign-canonical |
| Friction | `float32` | 0 through 100 |
| Restitution | `float32` | 0 through 1 |
| Shape count | `int32` | 1 through 4,096 for a valid body |

Strictly ascending order proves uniqueness and fixes Jitter2 body creation order. The validator
does not silently sort or repair producer output.

### Shape record

Every shape begins with:

| Field | Encoding | Rule |
| --- | --- | --- |
| Shape key | string | Non-empty; shapes strictly ascending by ordinal key within the body |
| Shape type | `uint8` | One of the schema-v1 values below |
| Local position | 3 × `float32` | Supported coordinate range |
| Local rotation | 4 × `float32` | Normalized and sign-canonical |

The type-specific tail is:

| Value | Shape | Tail |
| --- | --- | --- |
| `1` | Box | Full size: 3 × positive `float32` |
| `2` | Sphere | Positive radius: `float32` |
| `3` | Capsule | Positive radius + non-negative cylinder length: 2 × `float32` |
| `4` | Mesh | Vertex count, vertices, index count, indices |

`0` is reserved as `None` and is rejected. Primitive extents are capped at 100,000.

For a mesh:

- vertex count is at most 1,000,000 per mesh;
- index count is at most 3,000,000 per mesh and must be a multiple of three;
- every index must address the vertex array;
- a triangle may not repeat a vertex index;
- total level vertices are capped at 4,000,000;
- total level indices are capped at 12,000,000.

Mesh vertices are currently consumed as body-local coordinates. Although schema 1 carries local
position and rotation for every shape, package 0.7.0 does not apply those two fields to mesh
construction and validation does not require identity. Producers must bake the mesh transform
into the vertex data and emit an identity mesh-local pose.

## Canonical floats and quaternions

The writer maps `-0.0f` to `+0.0f` and does not round any other finite float. Rounding would move
geometry and hide a producer difference.

The Jitter-native writer normalizes through the canonical f32 `StableMath` surface, then chooses
one of the equivalent `q`/`-q` encodings. The first non-zero component in
`w, x, y, z` order must be positive. The reader accepts unit length within `1e-4` and rejects a
non-canonical sign or any negative-zero component.

## Manifest

The canonical manifest is a flat JSON object with these fields in writer order:

| Field | JSON type | Meaning |
| --- | --- | --- |
| `schemaVersion` | string | Artifact schema description |
| `runtimeCompatibilityId` | string | Runtime semantics identity |
| `generatorVersion` | string | Package/tool version that produced the pair |
| `levelId` | string | Canonical level ID |
| `artifactHash` | string | SHA-256 of the payload |
| `bodyCount` | integer | Decoded body count |
| `shapeCount` | integer | Artifact shape-record count |
| `vertexCount` | integer | Total mesh vertices |
| `triangleCount` | integer | Total mesh triangles |
| `tickRate` | integer | Authored tick rate |
| `fileName` | string | Current or exact supported legacy payload name |

The manifest parser requires all listed fields, rejects duplicate keys, malformed flat JSON, and
content after the closing brace. It currently accepts additional fields and does not interpret
them.

The reader cross-checks payload hash, level ID, runtime ID, payload file name, body/shape/vertex/
triangle counts, and tick rate. The decoded payload independently enforces schema 1. In 0.7.0,
the manifest's `schemaVersion` string and `generatorVersion` are descriptive and are not compared
with the decoded artifact/package version.

The public manifest codec applies its 8,192 limit to string characters. File and upload paths
also enforce the limit in UTF-8 bytes before parsing. A custom or embedded source should enforce
the byte limit before accepting untrusted manifest text.

## Current and legacy names

Current canonical names are stable and human-readable:

- `<levelId>.physics.bytes`
- `<levelId>.physics.manifest.json`

The reader also recognizes the exact legacy hash-addressed payload name for the supplied full
hash. Upload storage rewrites an accepted legacy pair to current names. Arbitrary manifest file
names are rejected.

The file provider treats `fileName` as untrusted. Without an explicit payload override it accepts
only a plain file name in the manifest's own directory, never an absolute path or path traversal.

## Validation order

`PhysicsArtifactReader.Read` uses this order:

1. reject null/empty and payloads over 64 MiB;
2. compute the actual SHA-256;
3. compare an expected hash and manifest hash when supplied;
4. verify magic, schema, reserved field, and deterministic/single-thread markers;
5. decode strings/counts under allocation caps;
6. reject truncation, invalid UTF-8/floats/quaternions, and trailing bytes;
7. run semantic validation on the complete DTO;
8. cross-check the manifest;
9. return the artifact only after every check succeeds.

Runtime compatibility is a separate check. This allows inspection tooling to decode an artifact
from another runtime while providers and simulating clients reject it before world construction.

## Mutation and concurrency

Canonical output assumes stable input for the duration of validation and writing. Schema DTOs are
not deeply immutable: mesh arrays and underlying lists can be changed by code that retained a
reference, and `PhysicsArtifactPayload.Bytes` exposes its array. Do not mutate them after
construction or while another thread hashes, writes, previews, or builds them.

`PhysicsCompatibilityToken.Magic` is also a public array in 0.7.0. Treat it as a constant and
never modify it.

## JMP migration decision

The v0.0.12 writer and the Jitter-native migration writer produce the exact same full payload,
SHA-256, and canonical manifest for the frozen fixture. Schema therefore remains **1**. This does
not imply runtime compatibility: the canonical Jitter source hash and compile profile changed, so
the derived runtime compatibility ID changed from
`ca8283611d3221120e69e23c4c028720537de4867f1de53df3752db85cd32006` to
`71e9d01f4006a8e1d097beb047efa8b8aabbe24895cb8d50531c764031c9aa4b`.

Old schema-one bytes remain parseable for offline inspection, but a simulating client/server pair
must reject the old runtime ID with `IncompatibleRuntime`. Re-bake every level with the aligned
Unity integration, update the server projection, and re-export the full delivery unit.

The delivery unit is the payload, canonical manifest, and `.physics.asset`. Bake verifies all
three after import and restores the previous three files on any late failure. Export/upload also
cross-check every asset metadata field against the payload and manifest before returning bytes.

## Fingerprints are not artifact identity

`PhysicsWorldBuildResult.TopologyFingerprint` is not stored in schema 1 and is not used by the
artifact reader. In 0.7.0 it includes record order and selected shape metadata, but only mesh
array lengths rather than mesh contents, and it omits material/world settings. It is suitable for
a repeatable smoke diagnostic only. Never substitute it for `artifactHash` plus
`runtimeCompatibilityId`.

See [Runtime API](runtime-api.md) for loading and [Extending](extending.md) before proposing a
schema or semantics change.
