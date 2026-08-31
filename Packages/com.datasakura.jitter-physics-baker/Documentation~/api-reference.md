# API reference

Applies to package version **0.0.12**.

[Documentation index](index.md) · [Quick start](quick-start.md) ·
[Configuration](configuration.md) · [Troubleshooting](troubleshooting.md) ·
[Recipes](recipes.md)

This reference has two levels. The first documents the supported and operationally significant
APIs, including exact signatures and lifecycle rules. The second is a compact inventory of all
**99 production public types** in the package sources (tests excluded). Small math, naming, hash,
path, and UI helpers stay in the inventory instead of being mechanically repeated.

All portable APIs are synchronous. Unless a section explicitly says otherwise, thread safety has
not been established by the package's tests; do not share mutable DTO storage or provider instances
between concurrent startup operations. Unity authoring and Editor APIs must be called on Unity's
main thread.

## Significant public API

### Assembly and dependency map

| Assembly | Namespace used below | Source | Direct dependencies |
| --- | --- | --- | --- |
| `DataSakura.JitterPhysics.Contracts` | `DataSakura.JitterPhysics.Contracts` | `Runtime/Contracts/` | None; `noEngineReferences: true` |
| `DataSakura.JitterPhysics.ArtifactCodec` | `DataSakura.JitterPhysics.ArtifactCodec` | `Runtime/ArtifactCodec/` | Contracts; `noEngineReferences: true` |
| `DataSakura.JitterPhysics.UnityArtifact` | `DataSakura.JitterPhysics.UnityArtifact` | `Runtime/UnityArtifact/` | Contracts, ArtifactCodec, UnityEngine |
| `DataSakura.JitterPhysics.Authoring` | `DataSakura.JitterPhysics.Authoring` | `Authoring/` | Contracts, UnityArtifact, UnityEngine |
| `DataSakura.JitterPhysics.Editor` | `DataSakura.JitterPhysics.Editor.Api` and advanced namespaces | `Editor/` | Contracts, ArtifactCodec, UnityArtifact, Authoring, UnityEditor |
| `DataSakura.JitterPhysics.JitterIntegration` | `DataSakura.JitterPhysics.Integration` | `JitterIntegration~/Runtime/` after explicit installation | Contracts, ArtifactCodec, consumer-owned `Jitter2.Core` |

`package.json` declares Unity 6000.3 and the JSON serialization, Physics, IMGUI, UIElements, and
UnityWebRequest modules. It does not declare Jitter2: the package remains importable without it,
and the integration assembly is installed explicitly. See [Installation](installation.md) and
[Requirements and compatibility](requirements-and-compatibility.md).

### Provider and typed-result boundary

#### `DataSakura.JitterPhysics.Contracts.IPhysicsArtifactProvider`

- **Assembly/source:** `DataSakura.JitterPhysics.Contracts`;
  `Runtime/Contracts/IPhysicsArtifactProvider.cs`.
- **Role and creator:** a consumer implements it, or constructs the file/embedded implementations
  below, to isolate delivery from startup.
- **Lifecycle and dependencies:** retained by the consumer for as long as it may load the artifact;
  it is not `IDisposable` and has no Unity or Jitter dependency.
- **Key surface:** `string Description { get; }` is a safe-to-log origin; successful loads must
  already be hash-checked, decoded, validated, manifest-cross-checked, and runtime-checked when an
  expected ID is supplied.
- **Replacement:** this interface is the supported custom content-system extension point.

| Exact signature | Parameters, return, effects, errors, and state |
| --- | --- |
| `PhysicsArtifactLoadResult Load(string expectedRuntimeCompatibilityId)` | `expectedRuntimeCompatibilityId` is the caller build's runtime ID; `null` is for inspection only. Returns either a fully checked artifact/manifest/hash/source or a typed error. The contract is synchronous. Implementations may perform I/O or cache bytes. Expected external failures should be returned; the interface cannot prevent a custom implementation from throwing. Repeatability and concurrency depend on the implementation. |

Example: [implement a content-system provider](recipes.md#implement-a-content-system-provider).

#### Result and error types

| Fully qualified type | Assembly/source | Creator, lifecycle, key members, and replacement |
| --- | --- | --- |
| `DataSakura.JitterPhysics.Contracts.PhysicsArtifactLoadResult` | Contracts; `Runtime/Contracts/IPhysicsArtifactProvider.cs` | Provider-created readonly value. Key getters: `PhysicsArtifact Artifact`, `PhysicsArtifactManifest Manifest`, `string ArtifactHash`, `string Source`, `PhysicsArtifactError Error`, `bool Succeeded`; no cleanup. Factories enforce success/failure invariants. Not replaceable, but returned by custom providers. |
| `DataSakura.JitterPhysics.Contracts.PhysicsArtifactResult` | Contracts; `Runtime/Contracts/PhysicsArtifactResult.cs` | Reader/loader-created readonly value. Key getters: `PhysicsArtifact Artifact`, `PhysicsArtifactError Error`, `bool Succeeded`; no cleanup or ambient state. |
| `DataSakura.JitterPhysics.Contracts.PhysicsArtifactError` | Contracts; `Runtime/Contracts/PhysicsArtifactResult.cs` | Readonly diagnostic value with `PhysicsArtifactErrorCode Code`, `string Message`, `string LevelId`, `string ArtifactHash`, and `bool IsError`; `default` means no error. |
| `DataSakura.JitterPhysics.Contracts.PhysicsArtifactErrorCode` | Contracts; `Runtime/Contracts/PhysicsArtifactResult.cs` | Stable branch categories: `None`, payload/format/limit/value/order/mesh errors, hash/manifest/runtime mismatches, and unavailable sources. Branch on the code, not message text. |

| Exact signature | Parameters, return, effects, errors, and state |
| --- | --- |
| `PhysicsArtifactLoadResult`: `public static PhysicsArtifactLoadResult Success(PhysicsArtifact artifact, PhysicsArtifactManifest manifest, string artifactHash, string source)` | Returns a success value. Throws `ArgumentNullException` for `artifact` and `ArgumentException` for an empty hash. Pure and repeatable. |
| `PhysicsArtifactLoadResult`: `public static PhysicsArtifactLoadResult Failure(PhysicsArtifactError error, string source)` | Returns a failure value and preserves the error hash. Throws `ArgumentException` when `error.IsError` is false. Pure and repeatable. |
| `PhysicsArtifactLoadResult`: `public static PhysicsArtifactLoadResult Failure(PhysicsArtifactErrorCode code, string message, string source, string levelId = null, string artifactHash = null)` | Constructs the error, then applies the same failure invariant. Pure and repeatable. |
| `PhysicsArtifactLoadResult`: `public override string ToString()` | Returns a single-line load summary or failure description with a shortened hash. Pure for frozen result data. |
| `PhysicsArtifactResult`: `public static PhysicsArtifactResult Success(PhysicsArtifact artifact)` | Returns success; throws `ArgumentNullException` for `artifact`. Pure and repeatable. |
| `PhysicsArtifactResult`: `public static PhysicsArtifactResult Failure(PhysicsArtifactErrorCode code, string message, string levelId = null, string artifactHash = null)` | Returns a typed failure; it does not validate that `code` is non-`None`. Pure and repeatable. |
| `PhysicsArtifactError`: `public PhysicsArtifactError(PhysicsArtifactErrorCode code, string message, string levelId = null, string artifactHash = null)` | Creates a value; a null message becomes empty. No validation or side effects. |
| `PhysicsArtifactError`: `public override string ToString()` | Returns code/message plus optional level and shortened hash. Pure. |

Expected file, network, manifest, and payload failures use these results. Programmer-contract
violations still throw. Do not synthesize `default(PhysicsArtifactResult)` or
`default(PhysicsArtifactLoadResult)`: `Succeeded` is derived only from a default non-error code, so
those zero-initialized values report success while their artifact is null. Use the factories and
provider outputs, and never call `Failure` with `PhysicsArtifactErrorCode.None`. See
[typed failures](runtime-api.md#typed-failures).

### Portable artifact model

All types in this section use namespace `DataSakura.JitterPhysics.Contracts`, assembly
`DataSakura.JitterPhysics.Contracts`, and source root `Runtime/Contracts/`. Bakers/readers create
them; providers and world builders retain them only for startup/readiness. They need no cleanup
and no Unity/Jitter types.

| Fully qualified type | Role, key properties, lifecycle, and replaceability |
| --- | --- |
| `DataSakura.JitterPhysics.Contracts.PhysicsArtifact` | Root record: `int SchemaVersion`, string runtime/level IDs, `PhysicsWorldSettings WorldSettings`, ordered `IReadOnlyList<PhysicsBodyRecord> Bodies`, and computed integer counts. Sealed but directly constructible for producers/tests. |
| `DataSakura.JitterPhysics.Contracts.PhysicsWorldSettings` | `PhysicsVector3 Gravity`, integer tick/substep/solver/relaxation values, `bool AllowDeactivation`, plus fixed single-threaded/deterministic markers. Sealed; directly constructible. |
| `DataSakura.JitterPhysics.Contracts.PhysicsBodyRecord` | `string SourceId`, portable world pose, float friction/restitution, ordered `IReadOnlyList<PhysicsShapeRecord> Shapes`. Sealed; directly constructible. |
| `DataSakura.JitterPhysics.Contracts.PhysicsShapeRecord` | `string ShapeKey`, `PhysicsShapeType ShapeType`, portable local pose, primitive dimensions, `PhysicsVector3[] Vertices`, and `int[] Indices`; created only by kind-specific factories. Sealed and not subclassable. |
| `DataSakura.JitterPhysics.Contracts.PhysicsArtifactManifest` | String schema/runtime/generator/level/hash/file name, integer counts, and tick rate. Sealed; generated from the exact payload whenever possible. |

Exact construction surface:

```text
public PhysicsArtifact(
    int schemaVersion,
    string runtimeCompatibilityId,
    string levelId,
    PhysicsWorldSettings worldSettings,
    IReadOnlyList<PhysicsBodyRecord> bodies)

public PhysicsWorldSettings(
    PhysicsVector3 gravity,
    int tickRate,
    int substepCount,
    int solverIterations,
    int relaxationIterations,
    bool allowDeactivation)

public PhysicsBodyRecord(
    string sourceId,
    PhysicsVector3 position,
    PhysicsQuaternion orientation,
    float friction,
    float restitution,
    IReadOnlyList<PhysicsShapeRecord> shapes)

public static PhysicsShapeRecord Box(
    string shapeKey,
    PhysicsVector3 localPosition,
    PhysicsQuaternion localRotation,
    PhysicsVector3 size)

public static PhysicsShapeRecord Sphere(
    string shapeKey,
    PhysicsVector3 localPosition,
    PhysicsQuaternion localRotation,
    float radius)

public static PhysicsShapeRecord Capsule(
    string shapeKey,
    PhysicsVector3 localPosition,
    PhysicsQuaternion localRotation,
    float radius,
    float length)

public static PhysicsShapeRecord Mesh(
    string shapeKey,
    PhysicsVector3 localPosition,
    PhysicsQuaternion localRotation,
    PhysicsVector3[] vertices,
    int[] indices)

public PhysicsArtifactManifest(
    string schemaVersion,
    string runtimeCompatibilityId,
    string generatorVersion,
    string levelId,
    string artifactHash,
    int bodyCount,
    int shapeCount,
    int vertexCount,
    int triangleCount,
    int tickRate,
    string fileName)
```

Constructors reject required null references but do not make the graph deeply immutable. The
artifact/body list storage and mesh arrays remain caller-owned references; freeze them after
construction. Shape factories only select a representation: canonical ranges, ordering, mesh
indices, and identifiers are enforced by the validator/writer. Each factory returns a new sealed
record and has no external side effect; `Mesh` rejects null arrays, every factory rejects a null
shape key through the private constructor, and primitive numeric validity is deferred to
validation. These portable operations have no main-thread requirement and are repeatable for
frozen inputs.

`PhysicsWorldSettings.Default` returns a new settings instance on each access. The public
`MultiThreaded` and `DeterministicSolveMode` constants describe format policy; they do not configure
a live world by themselves.

| Exact signature | Parameters, return, effects, errors, and state |
| --- | --- |
| `PhysicsArtifactManifest`: `public static PhysicsArtifactManifest ForArtifact(PhysicsArtifact artifact, string artifactHash, string generatorVersion)` | Creates a manifest from artifact counts/settings plus the supplied hash/version. Throws `ArgumentNullException` for a null artifact/required string and `ArgumentException` when naming a noncanonical level. Pure for frozen input. |
| `PhysicsArtifactManifest`: `public PhysicsArtifactManifest WithCurrentFileName()` | Returns a new manifest with the current canonical payload name; all other values are copied. Throws `ArgumentException` when `LevelId` is not canonical. Otherwise pure and repeatable. |

These operations are repeatable when their inputs are frozen. No thread affinity is encoded, but
the package has not verified concurrent access to shared mutable backing storage. Example:
[custom provider DTO usage](recipes.md#implement-a-content-system-provider). Format limits and
field semantics: [Artifact format v1](artifact-format-v1.md).

#### `DataSakura.JitterPhysics.Contracts.IJitterPhysicsRuntimePreviewSource`

- **Assembly/source:** Contracts; `Runtime/Contracts/IJitterPhysicsRuntimePreviewSource.cs`.
- **Role and creator:** a consumer runtime component implements it to expose the geometry actually
  loaded into its active world to Editor diagnostics without exposing Jitter2 types.
- **Lifecycle/dependencies:** registration/discovery is owned by consumer Editor integration; the
  implementation must not report ready until the world build succeeded. Records are treated as
  frozen snapshots. No cleanup callback exists in the contract.
- **Replacement:** supported runtime-preview extension point; it is separate from preview preference
  control (`JitterPhysicsPreviewApi`).

| Exact signature | Parameters, return, effects, errors, and state |
| --- | --- |
| `string PhysicsPreviewLevelId { get; }` | Returns the active world's level identity; getter should be side-effect free. |
| `bool IsPhysicsPreviewReady { get; }` | Returns true only after successful publication of the built world. |
| `void CopyPhysicsPreviewBodies(ICollection<PhysicsBodyRecord> destination)` | Appends the active world's portable records to `destination`. The interface declares no error result, synchronization, or null behavior; implementations must define those without rebuilding from Unity colliders. Called synchronously by Editor diagnostics on the main thread. |

Example: [runtime preview source](extending.md#runtime-preview-source).

### Read, write, validate, and manifest APIs

These types use namespace/assembly `DataSakura.JitterPhysics.ArtifactCodec` and source root
`Runtime/ArtifactCodec/`. They depend only on Contracts, have no engine reference, own no native
resources, and are static/non-replaceable. Use providers to replace delivery rather than replacing
the canonical codec.

The significant types are
`DataSakura.JitterPhysics.ArtifactCodec.PhysicsArtifactReader`,
`DataSakura.JitterPhysics.ArtifactCodec.PhysicsArtifactWriter`,
`DataSakura.JitterPhysics.ArtifactCodec.PhysicsArtifactPayload`,
`DataSakura.JitterPhysics.ArtifactCodec.PhysicsArtifactValidator`, and
`DataSakura.JitterPhysics.ArtifactCodec.PhysicsArtifactManifestCodec`. The writer creates the
sealed payload result; its key properties are `byte[] Bytes`, `string ArtifactHash`, and
`PhysicsArtifactManifest Manifest`, and it requires no cleanup.

| Exact signature | Parameters, return, effects, errors, state, and thread context |
| --- | --- |
| `PhysicsArtifactReader`: `public static PhysicsArtifactResult Read(byte[] payload, string expectedHash = null, PhysicsArtifactManifest manifest = null)` | Hashes before parsing, decodes with hard caps, validates, and optionally cross-checks hash/manifest. Returns typed failures for null/empty, oversize, corrupt, noncanonical, hash, or manifest input. Does not mutate inputs. Repeatable for frozen bytes; synchronous with no verified concurrency guarantee. |
| `PhysicsArtifactReader`: `public static PhysicsArtifactError CheckRuntimeCompatibility(PhysicsArtifact artifact, string expectedRuntimeCompatibilityId)` | Length-checked, case-insensitive hexadecimal runtime-ID comparison. Returns `IncompatibleRuntime`; throws `ArgumentNullException` for `artifact`. A null expected ID does **not** mean inspection here: it produces a mismatch. Pure and repeatable. |
| `PhysicsArtifactWriter`: `public static byte[] Write(PhysicsArtifact artifact)` | Validates and returns canonical bytes. Throws `ArgumentNullException` for null or `ArgumentException` for noncanonical producer state. No external writes; repeatable only while the DTO graph is frozen. |
| `PhysicsArtifactWriter`: `public static PhysicsArtifactPayload WriteWithManifest(PhysicsArtifact artifact, string generatorVersion)` | Returns `Bytes`, lowercase SHA-256 `ArtifactHash`, and matching `Manifest`; has the same validation exceptions as `Write`, and a null generator version fails through manifest construction. No filesystem side effect. |
| `PhysicsArtifactValidator`: `public static PhysicsArtifactError Validate(PhysicsArtifact artifact)` | Returns the first semantic/canonical error; throws `ArgumentNullException` for null. Caller-induced concurrent/deep mutation can still surface ordinary runtime exceptions, so freeze the DTO graph. Read-only and repeatable for frozen input. It does not enforce the reader's aggregate `MaxIndices` budget, so producer validation alone is not equivalent to decoding untrusted bytes. |
| `PhysicsArtifactManifestCodec`: `public static string Write(PhysicsArtifactManifest manifest)` | Returns deterministic flat JSON with LF endings; throws `ArgumentNullException` for null. No I/O. |
| `PhysicsArtifactManifestCodec`: `public static PhysicsArtifactManifest Read(string json, out string error)` | Returns a manifest and null error, or `null` plus a reason for empty, oversized, malformed, missing, duplicate-key, or wrong-value input. Additional well-formed flat keys are currently ignored. Expected malformed input is not thrown. No I/O. |

Examples: [load a Unity artifact](runtime-api.md#loading-a-unity-artifact) and
[artifact format rules](artifact-format-v1.md).

### Delivery implementations

All types below use `DataSakura.JitterPhysics.ArtifactCodec` and `Runtime/ArtifactCodec/`.

#### `DataSakura.JitterPhysics.ArtifactCodec.FilePhysicsArtifactProvider`

The consumer constructs this sealed `IPhysicsArtifactProvider`; it retains path strings, performs
synchronous I/O on each `Load`, and owns nothing disposable. The manifest is the entry point. With
no override, `FileName` must be a plain name and resolves next to the manifest.

| Exact signature | Parameters, return, effects, errors, and state |
| --- | --- |
| `public FilePhysicsArtifactProvider(string manifestPath, string payloadPath = null)` | Stores paths. Throws `ArgumentException` for an empty manifest path. The optional payload override supports delivery renames. |
| `public PhysicsArtifactLoadResult Load(string expectedRuntimeCompatibilityId)` | Reads manifest/payload, hashes, decodes, validates, cross-checks, and optionally checks runtime ID. Returns typed `SourceUnavailable`, limit, manifest, payload, hash, and runtime failures for caught external problems. Repeatable but observes current files; no snapshot/concurrency guarantee. |

`ManifestPath` exposes configuration and `Description` returns the loggable `file:` source. Example:
[file-delivered server startup](recipes.md#load-a-file-delivered-artifact-on-a-server).

#### `DataSakura.JitterPhysics.ArtifactCodec.EmbeddedPhysicsArtifactProvider`

Generated code or a consumer constructs this sealed provider with Base64 chunks and manifest JSON.
It caches restored payload bytes for its process lifetime; inputs are retained, not copied, and
there is no disposal. First-load caching is not synchronized, so concurrent first use is unverified.

| Exact signature | Parameters, return, effects, errors, and state |
| --- | --- |
| `public EmbeddedPhysicsArtifactProvider(IReadOnlyList<string> chunks, string manifestJson, string description = null)` | Retains inputs; throws `ArgumentNullException` for null chunks or manifest text. |
| `public PhysicsArtifactLoadResult Load(string expectedRuntimeCompatibilityId)` | Parses manifest, restores/caches bytes, then performs the same checks as the file provider. Returns typed failures, including invalid Base64. Repeat calls reuse the cache. |
| `public static byte[] Restore(IReadOnlyList<string> chunks)` | Concatenates and Base64-decodes chunks. Throws `ArgumentNullException` for null and `FormatException` for invalid Base64. It has no independent size cap, allocates a new array per direct call, and can surface normal allocation/range failures for an extreme caller-provided collection. |

Example: [provider choices](dedicated-server.md#providers).

#### Generated source and upload storage

| Fully qualified type | Creator/lifecycle, key properties, and replaceability |
| --- | --- |
| `DataSakura.JitterPhysics.ArtifactCodec.EmbeddedArtifactSourceOptions` | Consumer-created immutable policy: `Namespace`, `ClassName`, `ChunkLength`, `MaxEmbeddedBytes`. Checks a simple letter/digit/underscore identifier shape, Base64 alignment, and positive cap; it does not reject C# reserved keywords. |
| `DataSakura.JitterPhysics.ArtifactCodec.EmbeddedArtifactSource` | Generator-created result: `FileName`, deterministic `Code`, ordered `Chunks`, `ArtifactHash`; no cleanup. |
| `DataSakura.JitterPhysics.ArtifactCodec.PhysicsArtifactUploadResult` | Store-created result: validated `Manifest`, `PayloadPath`, `ManifestPath`, `Error`, `Succeeded`. The hash is `Manifest.ArtifactHash`; there is no direct hash property. |

| Exact signature | Parameters, return, effects, errors, state, and thread context |
| --- | --- |
| `EmbeddedArtifactSourceOptions`: `public EmbeddedArtifactSourceOptions(string @namespace, string className, int chunkLength = DefaultChunkLength, int maxEmbeddedBytes = DefaultMaxEmbeddedBytes)` | Creates immutable generation policy; defaults are 4,096 Base64 characters and 4 MiB. Invalid simple identifier shapes/alignment/cap throw `ArgumentException`, but C# keyword validity remains the caller's responsibility. Pure. |
| `EmbeddedArtifactSourceGenerator`: `public static EmbeddedArtifactSource Generate(byte[] payload, PhysicsArtifactManifest manifest, EmbeddedArtifactSourceOptions options)` | Verifies nonempty bytes, size cap, and manifest hash, then returns deterministic source. Throws `ArgumentException`/`ArgumentNullException` for invalid producer input. It does not write the returned code. |
| `PhysicsArtifactUploadStore`: `public static PhysicsArtifactUploadResult Store(byte[] payload, string manifestJson, string targetFolder, string expectedRuntimeCompatibilityId)` | Parses and fully checks untrusted delivery, requires the runtime compatibility check, canonicalizes the payload name, creates the folder, and publishes both files. Content failures and caught publication I/O failures are returned; argument/path construction failures outside the publication block are not promised as typed results. Not locked or crash-durable; concurrent writers are unsupported. |
| `PhysicsArtifactPairWriter`: `public static void Write(string payloadPath, byte[] payload, string manifestPath, string manifestJson)` | Stages two temporary files and attempts rollback if final moves fail. Parent directories must already exist. Throws argument and direct filesystem exceptions. It does not validate content, lock writers, call `fsync`, or guarantee recovery from process/host interruption; retry only after inspecting the pair. |

These producer/storage classes are static or sealed. Replace storage above them, or implement
`IPhysicsArtifactProvider` below them, without inventing another artifact encoder. Example:
[custom delivery and storage](extending.md#custom-delivery-and-storage).

### Runtime identity and peer token

Namespace/assembly: `DataSakura.JitterPhysics.ArtifactCodec`; sources
`Runtime/ArtifactCodec/RuntimeCompatibilityId.cs` and
`Runtime/ArtifactCodec/PhysicsCompatibilityToken.cs`.

`DataSakura.JitterPhysics.ArtifactCodec.RuntimeCompatibilityInputs` is a readonly value created by
the build/bootstrap path. Its key properties are schema version, Jitter source hash,
precision/compile profile, and four package semantic versions.
`DataSakura.JitterPhysics.ArtifactCodec.RuntimeCompatibilityId` is the static, nonreplaceable
canonical hash derivation.
`DataSakura.JitterPhysics.ArtifactCodec.PhysicsCompatibilityToken` is a transport-neutral readonly
value containing `LevelId`, `ArtifactHash`, and `RuntimeCompatibilityId`; the consumer carries its
bytes in its own handshake. Neither type owns resources or authenticates a peer.
`PhysicsCompatibilityToken.Magic` is a publicly exposed mutable array, so package 0.0.12 callers
must not modify it.

| Exact signature | Parameters, return, effects, errors, state, and thread context |
| --- | --- |
| `RuntimeCompatibilityInputs`: `public RuntimeCompatibilityInputs(int artifactSchemaVersion, string jitterSourceContentHash, string precisionMode, string compileProfileId, int colliderConversionVersion, int shapeConstructionVersion, int worldBuilderVersion, int worldDefaultsVersion)` | Captures explicit inputs; required strings reject null. Pure. |
| `RuntimeCompatibilityInputs`: `public static RuntimeCompatibilityInputs ForCurrentBuild(string jitterSourceContentHash, string compileProfileId)` | Injects the current package schema/semantic constants; required strings reject null through the constructor. Pure. |
| `RuntimeCompatibilityId`: `public static string Compute(RuntimeCompatibilityInputs inputs)` | Returns lowercase SHA-256 of a length-delimited canonical text. No validation against the actual installed Jitter source; bootstrap must supply the correct inputs. Pure and repeatable. |
| `PhysicsCompatibilityToken`: `public PhysicsCompatibilityToken(string levelId, string artifactHash, string runtimeCompatibilityId)` | Retains strings; rejects null but defers canonical ID/digest validation until encoding. |
| `PhysicsCompatibilityToken`: `public static PhysicsCompatibilityToken ForArtifact(PhysicsArtifact artifact, string artifactHash)` | Copies level/runtime IDs from `artifact`; null artifact throws. Hash validity is checked later by `Encode`. |
| `PhysicsCompatibilityToken`: `public byte[] Encode()` | Returns protocol-v1 bytes. Throws `InvalidOperationException` for a noncanonical level ID or a digest that is not exactly 64 valid hex characters. No external side effect. |
| `PhysicsCompatibilityToken`: `public static bool TryDecode(byte[] payload, out PhysicsCompatibilityToken token, out string error)` | Parses untrusted bytes. Returns false plus a reason for empty, oversized, truncated, bad magic/version/UTF-8/ID/length input; does not throw for those cases. |
| `PhysicsCompatibilityToken`: `public bool Matches(PhysicsCompatibilityToken expected, out string reason)` | Exact ordinal level comparison plus length-checked, case-insensitive hex digest comparison; returns mismatch reason or null. Pure. |

Do not compute a runtime ID from `default(RuntimeCompatibilityInputs)`: a default struct bypasses
the constructor's required-string checks, and `Compute` canonicalizes its null strings as empty.
Use `ForCurrentBuild` or the full constructor with verified bootstrap inputs.

Examples: [reject a mismatched peer](recipes.md#reject-a-mismatched-peer-before-spawn) and
[connection compatibility gate](dedicated-server.md#connection-compatibility-gate).

### Unity authoring model

Namespace/assembly: `DataSakura.JitterPhysics.Authoring`; source `Authoring/`; dependencies are
Contracts, UnityArtifact, and UnityEngine. Unity or the Editor creates these sealed objects;
Unity owns their destruction and serialized lifetime. They are authoring state, not a runtime
replacement for the artifact. Calls belong on Unity's main thread.

| Fully qualified type | Role and key serialized/read-only properties |
| --- | --- |
| `DataSakura.JitterPhysics.Authoring.JitterPhysicsLevel` | One scene identity and bake scope: `LevelId`, optional `GeometryRoot`, required `WorldProfile`, `GeneratedFolder`, diagnostic `LastArtifactHash`, `HasCanonicalLevelId`. |
| `DataSakura.JitterPhysics.Authoring.JitterStaticBodySource` | Explicit body marker: `SourceId`, child scope, fixed exclusion of inactive children, friction, restitution, canonical-ID state. |
| `DataSakura.JitterPhysics.Authoring.JitterPhysicsWorldProfile` | Shared authored gravity/tick/substep/solver/relaxation/deactivation settings. |
| `DataSakura.JitterPhysics.Authoring.JitterPhysicsAuthoringConstants` | Menu roots and menu order constants only; it does not define authored defaults or validation ranges. |

| Exact signature | Parameters, return, effects, errors, and state |
| --- | --- |
| `JitterPhysicsLevel`: `public string EnsureLevelId()` | Keeps a canonical ID or assigns a sanitized ID derived from serialized data/scene name. Mutates the component but does not itself call `SetDirty`; repeat calls are stable after assignment. |
| `JitterPhysicsLevel`: `public void SetLastArtifactHash(string value)` | Mutates the diagnostic serialized hash; null becomes empty. It does not save/dirty the scene itself. |
| `JitterPhysicsLevel`: `public IReadOnlyList<JitterStaticBodySource> CollectSources()` | Allocates and returns currently loaded active sources under `GeometryRoot`, or explicit sources across scene roots. Ordering is not canonicalized here. Read-only with respect to components. |
| `JitterStaticBodySource`: `public string EnsureSourceId()` | Keeps a canonical ID or assigns a sanitized one derived from the current value/name. Mutates the component; it does not call `SetDirty`. |
| `JitterStaticBodySource`: `public void SetSourceId(string value)` | Sanitizes and overwrites the serialized ID. Mutates the component; repeatability follows canonical sanitization. |
| `JitterPhysicsWorldProfile`: `public PhysicsWorldSettings ToWorldSettings()` | Returns a new portable settings object with canonicalized gravity. Read-only. The value includes `SubstepCount`, but 0.0.12's world builder does not apply that field. |

Example: [create the first level](quick-start.md#1-create-the-first-level). Authoring constraints:
[Configuration](configuration.md).

### Unity artifact handle and loader

Namespace/assembly: `DataSakura.JitterPhysics.UnityArtifact`; source root
`Runtime/UnityArtifact/`; dependencies are Contracts, ArtifactCodec, and UnityEngine.

#### `DataSakura.JitterPhysics.UnityArtifact.JitterPhysicsArtifactAsset`

The baker creates this sealed `ScriptableObject`; Unity owns its lifecycle. It references the
payload `TextAsset` and copies level/hash/runtime/schema/tick/count/generator metadata for the
Inspector. `ShortHash` and `HasPayload` are derived properties. It is not an extension point.

| Exact signature | Parameters, return, effects, errors, and state |
| --- | --- |
| `public byte[] GetPayloadBytes()` | Returns Unity's payload byte array or null. Treat the returned bytes as read-only and pass the recorded hash to a loader; ownership/copy behavior is Unity-defined. Main thread is the supported context. |
| `public void Initialize(PhysicsArtifactManifest manifest, TextAsset payloadAsset)` | Copies manifest metadata and assigns the payload; throws `ArgumentNullException` for null manifest. Mutates the asset but does not call `SetDirty`/save; public for bake infrastructure, not normal runtime use. |

#### `DataSakura.JitterPhysics.UnityArtifact.JitterPhysicsArtifactLoader`

This static, non-replaceable loader creates a portable result and retains no state.

| Exact signature | Parameters, return, effects, errors, state, and thread context |
| --- | --- |
| `public static PhysicsArtifactResult Load(JitterPhysicsArtifactAsset asset, string expectedRuntimeCompatibilityId = null)` | Re-hashes/decodes/validates the payload and checks nonempty copied level/runtime metadata plus body count when the copied count is nonzero. It does **not** cross-check copied schema, tick rate, shape/vertex/triangle counts, or generator version. A null/empty expected runtime ID is inspection mode only; simulation callers must supply it. Returns typed failures and does not build a Jitter world. Unity asset access makes the main thread the supported context. |

Example: [load a Unity artifact and own the tick loop](recipes.md#load-a-unity-artifact-and-own-the-tick-loop).

### Supported Editor integration facade

Namespace `DataSakura.JitterPhysics.Editor.Api`, assembly `DataSakura.JitterPhysics.Editor`, sources
`Editor/Api/JitterPhysicsEditorApi.cs` and `Editor/Api/JitterPhysicsPreviewApi.cs`. The assembly is
Editor-only. These static APIs retain no per-operation resource, are nonreplaceable, synchronous,
and require the Unity main thread.

#### `DataSakura.JitterPhysics.Editor.Api.JitterPhysicsEditorApi`

External tools call this facade rather than bake/install internals.
`DataSakura.JitterPhysics.Editor.Api.JitterPhysicsLevelIdBinding` is created with `Standalone` or
`External(owner, levelId)` and lives for one call. The returned
`DataSakura.JitterPhysics.Editor.Api.JitterPhysicsEditorResult` owns no cleanup and exposes
status/ownership/owner/level, trio paths, digest/size/counts, and `JitterPhysicsIssueLog`.
`Succeeded` is true for `Valid` and `Ready`; `HasCounts` only guarantees body and shape counts are
nonnegative.

| Exact signature | Parameters, return, effects, errors, and state |
| --- | --- |
| `JitterPhysicsLevelIdBinding`: `public static JitterPhysicsLevelIdBinding Standalone { get; }` | Returns the shared immutable standalone binding; side-effect free. |
| `JitterPhysicsLevelIdBinding`: `public static JitterPhysicsLevelIdBinding External(string owner, string levelId)` | Creates an immutable external-managed binding. It does not validate strings until an API operation resolves the binding. Pure. |
| `JitterPhysicsEditorApi`: `public static JitterPhysicsEditorResult Validate(JitterPhysicsLevel level, JitterPhysicsLevelIdBinding binding = null)` | Resolves identity, probes compatibility, builds/validates in memory, hashes bytes, and returns `Valid` or `Failed`. It writes no artifact files, but standalone resolution may assign/dirty the level ID and building may assign missing source IDs in memory. Normal findings use `Issues`; unexpected Unity/programmer exceptions are not a documented result contract. Repeatable only after identity is stable. |
| `JitterPhysicsEditorApi`: `public static JitterPhysicsEditorResult Bake(JitterPhysicsLevel level, JitterPhysicsLevelIdBinding binding = null)` | Resolves identity, gates setup, builds, publishes the asset/payload/manifest trio, imports/saves assets, and updates the level's last hash. Returns `Ready` only after verified publication or `Failed` with issues. Repeating an unchanged bake is intended to reproduce bytes/hash but still performs publication work. |
| `JitterPhysicsEditorApi`: `public static JitterPhysicsEditorResult ReadSummary(JitterPhysicsLevel level, JitterPhysicsLevelIdBinding binding = null)` | Resolves without assigning IDs, reads and verifies the current trio, and returns `Missing`, `Ready`, or `Failed`. It does not write files, preferences, IDs, or imports. Repeatable for unchanged project assets. |

`JitterPhysicsLevelIdBinding.Standalone` is the reusable immutable standalone binding. There are no
events or callbacks on this facade. Examples: [validate and bake](recipes.md#validate-and-bake-from-another-editor-tool),
[external level ownership](recipes.md#use-an-externally-owned-level-id), and
[Editor API handoff](npi-editor-api.md).

#### `DataSakura.JitterPhysics.Editor.Api.JitterPhysicsPreviewApi`

This is a **separate extension point** from `JitterPhysicsEditorApi`. It controls the one shared
Scene View preference snapshot; it does not validate, bake, publish runtime readiness, or register
a runtime preview source. `DataSakura.JitterPhysics.Editor.Api.JitterPhysicsPreviewState` is a
consumer-created sealed immutable value with `Sources`, `Baked`, `Runtime`, `Scope`, and
`Occlusion`.

| Exact signature | Parameters, return, effects, errors, and state |
| --- | --- |
| `JitterPhysicsPreviewState`: `public JitterPhysicsPreviewState(bool sources, bool baked, bool runtime, JitterPhysicsPreviewScope scope, JitterPhysicsPreviewOcclusion occlusion)` | Creates a complete snapshot; undefined enum values throw `ArgumentOutOfRangeException`. No external side effect. |
| `JitterPhysicsPreviewState`: `public JitterPhysicsPreviewState WithSources(bool value)` | Returns a new snapshot with only `Sources` changed; pure. |
| `JitterPhysicsPreviewState`: `public JitterPhysicsPreviewState WithBaked(bool value)` | Returns a new snapshot with only `Baked` changed; pure. |
| `JitterPhysicsPreviewState`: `public JitterPhysicsPreviewState WithRuntime(bool value)` | Returns a new snapshot with only `Runtime` changed; pure. This flag does not make a source ready. |
| `JitterPhysicsPreviewState`: `public JitterPhysicsPreviewState WithScope(JitterPhysicsPreviewScope value)` | Returns a new snapshot; constructor validation may throw for an undefined value. |
| `JitterPhysicsPreviewState`: `public JitterPhysicsPreviewState WithOcclusion(JitterPhysicsPreviewOcclusion value)` | Returns a new snapshot; constructor validation may throw for an undefined value. |
| `JitterPhysicsPreviewApi`: `public static JitterPhysicsPreviewState Current { get; }` | Reads EditorPrefs and returns a new snapshot. It does not write or repaint. An out-of-range enum value already stored in preferences propagates `ArgumentOutOfRangeException` from snapshot construction. |
| `JitterPhysicsPreviewApi`: `public static event Action Changed` | Add/remove forwards subscriptions to the shared preference service. Raised after an actual state change; subscribers must unsubscribe according to their Editor object/domain lifecycle. Callback runs synchronously on the thread applying the change (supported use is main thread). A subscriber exception propagates and can prevent the subsequent Scene View repaint. |
| `JitterPhysicsPreviewApi`: `public static void Apply(JitterPhysicsPreviewState state)` | Throws `ArgumentNullException` for null. Writes all shared EditorPrefs keys; when any value/key changes, synchronously raises `Changed` and repaints all Scene Views. Applying the same already-persisted state is a no-op. |

Example: [control the shared Scene View preview](recipes.md#control-the-shared-scene-view-preview).

### Jitter2 world construction and server startup

Namespace `DataSakura.JitterPhysics.Integration`, installed assembly
`DataSakura.JitterPhysics.JitterIntegration`, source `JitterIntegration~/Runtime/`. These APIs need
the consumer-owned `Jitter2.Core`. In the locked Jitter2 source, `World` implements `IDisposable`:
the consumer creates it, applies the artifact before stepping, and disposes it at shutdown or after
a failed candidate build. The package neither creates the world for the caller nor steps it.
Startup/build is synchronous, intended once on a fresh world before the tick loop, and not
documented or tested for concurrent calls. The same world must not be stepped or modified from
multiple external threads. Deterministic package policy requires the consumer to call Jitter's
`World.Step(dt, multiThread: false)` explicitly; Jitter's omitted argument defaults to `true`.

#### `DataSakura.JitterPhysics.Integration.JitterPhysicsWorldBuilder`

This static, nonreplaceable builder creates bodies from a validated `PhysicsArtifact`.
`DataSakura.JitterPhysics.Integration.PhysicsWorldBuildResult` is a package-created sealed result
with `Error`, body/shape counts, elapsed milliseconds, `TopologyFingerprint`, and `Succeeded`; it
owns no cleanup.

| Exact signature | Parameters, return, effects, errors, and state |
| --- | --- |
| `public static PhysicsWorldBuildResult Apply(Jitter2.World world, PhysicsArtifact artifact)` | Throws `ArgumentNullException` for null arguments. Refuses a second successful apply to the same `World` with typed `InvalidValue`; validates, mutates world settings, then creates static bodies. It does not compare the artifact runtime ID with the caller build or configure/enforce the consumer tick loop/threading flag. Construction exceptions are caught as typed `InvalidValue`. Rollback attempts to remove created bodies and suppresses secondary removal errors, while changed gravity/solve/iteration/deactivation settings are **not restored**; always dispose/discard a failed candidate world. `SubstepCount` is not applied. Mesh local pose is ignored because triangle vertices are passed directly. |
| `public static bool HasArtifact(Jitter2.World world)` | Returns false for null or a world without a completed successful apply. It is process-local bookkeeping, not inspection of arbitrary existing bodies. Read-only, but concurrent access guarantees are unverified. |

`TopologyFingerprint` is diagnostic only in 0.0.12: it omits world settings, material values, and
mesh vertex/index contents (it includes only mesh counts), so equality is not proof of complete
simulation equivalence. Example: [build the world](runtime-api.md#building-the-world).

#### `DataSakura.JitterPhysics.Integration.JitterPhysicsServerStartup`

The consumer creates `DataSakura.JitterPhysics.Integration.JitterPhysicsServerOptions`; its
constructor requires a runtime ID and can optionally enforce level and tick rate.
`DataSakura.JitterPhysics.Integration.JitterPhysicsServerState` is created only by startup and
contains artifact/hash/source/topology/count/timing/error plus `IsReady`, `LevelId`, `TickRate`, and
`SelfCheck`. It does not own or dispose the world. These sealed/static types are not replacement
points; customize delivery through `IPhysicsArtifactProvider` and keep readiness gating in the
consumer.

| Exact signature | Parameters, return, effects, errors, and state |
| --- | --- |
| `JitterPhysicsServerOptions`: `public JitterPhysicsServerOptions(string runtimeCompatibilityId, string expectedLevelId = null, int tickRate = 0)` | Throws `ArgumentException` for null/empty runtime ID. `expectedLevelId = null` accepts the artifact level; `tickRate = 0` accepts its tick rate. It does not validate canonical level ID or tick range itself. |
| `JitterPhysicsServerStartup`: `public static JitterPhysicsServerState Start(Jitter2.World world, IPhysicsArtifactProvider provider, JitterPhysicsServerOptions options)` | Throws `ArgumentNullException` for null arguments. Calls provider load, level/tick expectations, then world apply. Expected package failures return a non-ready state; custom provider exceptions are not caught. Call once on a fresh candidate world, before accepting peers. On build failure, discard the world because settings may have changed. |
| `JitterPhysicsServerState`: `public JitterPhysicsServerState RequireReady()` | Returns the same state when ready; throws `InvalidOperationException` containing the typed error otherwise. No mutation. Use only when aborting startup is the desired policy. |
| `JitterPhysicsServerState`: `public string SelfCheck { get; }` | Formats a success/failure log line with shortened hashes and invariant timing. Read-only and repeatable for the immutable state. |
| `JitterPhysicsServerState`: `public override string ToString()` | Returns `SelfCheck`; no mutation. |

Example: [dedicated-server startup contract](dedicated-server.md#startup-contract).

### Significant API limits in 0.0.12

- Portable DTO getters do not imply deep immutability; list/array storage and
  `PhysicsCompatibilityToken.Magic` can be mutated by callers.
- Thread safety is unverified. Unity APIs are main-thread-only; portable startup should be
  serialized before the simulation loop, and a Jitter world must have one external owner thread.
- The consumer owns `World.Dispose()` and must pass `multiThread: false` explicitly to `World.Step`;
  the package does not enforce the artifact's single-threaded policy at step time.
- `SubstepCount` is serialized and validated but not applied by `JitterPhysicsWorldBuilder`.
- Direct world-builder callers must load with an expected runtime ID and run their own loop at the
  artifact tick rate; `Apply` enforces neither.
- A failed build attempts to remove created bodies, suppresses removal failures, and does not
  restore changed world settings; discard the candidate world.
- Mesh local position/rotation is not applied during mesh construction; vertices must already be
  expressed in the body-local frame expected by the builder.
- `TopologyFingerprint` is incomplete as described above.
- Managed .NET portable tests are present, but IL2CPP/AOT platform acceptance is not proven by a
  completed mobile smoke gate.

See [Runtime API limitations](runtime-api.md#current-0012-runtime-limitations),
[Troubleshooting](troubleshooting.md), and [Extending](extending.md#constraints-to-preserve).

## Complete public-type inventory

The following purpose-oriented tables account for all 99 production public types in package
version 0.0.12. Assembly headings and source paths locate each short type name; detailed APIs above
use fully qualified names. Source paths are package-relative.

### Assembly: Contracts

Assembly: `DataSakura.JitterPhysics.Contracts`

Namespace: `DataSakura.JitterPhysics.Contracts`

Source root: `Runtime/Contracts/`

This assembly has no references and no UnityEngine dependency.

#### Load and error contracts

| Type | Purpose | Source |
| --- | --- | --- |
| `IPhysicsArtifactProvider` | Delivery-independent `Description` and `Load(expectedRuntimeCompatibilityId)` boundary | `Runtime/Contracts/IPhysicsArtifactProvider.cs` |
| `PhysicsArtifactLoadResult` | Validated artifact, manifest, exact payload hash, source, or typed error | `Runtime/Contracts/IPhysicsArtifactProvider.cs` |
| `PhysicsArtifactResult` | Artifact or typed decode/validation failure | `Runtime/Contracts/PhysicsArtifactResult.cs` |
| `PhysicsArtifactError` | Machine code plus loggable message, level, and hash context | `Runtime/Contracts/PhysicsArtifactResult.cs` |
| `PhysicsArtifactErrorCode` | Stable failure categories for caller branching | `Runtime/Contracts/PhysicsArtifactResult.cs` |

Expected bad external input is returned, not thrown. Constructors and factories may throw for
programmer misuse such as a null artifact in a successful result.

#### Artifact model

| Type | Purpose | Source |
| --- | --- | --- |
| `PhysicsArtifact` | Schema/runtime identity, level ID, world settings, ordered static bodies | `Runtime/Contracts/PhysicsArtifact.cs` |
| `PhysicsWorldSettings` | Gravity, tick rate, substeps, solver/relaxation iterations, deactivation | `Runtime/Contracts/PhysicsWorldSettings.cs` |
| `PhysicsBodyRecord` | Stable ID, world pose, material values, ordered shapes | `Runtime/Contracts/PhysicsBodyRecord.cs` |
| `PhysicsShapeRecord` | Factory-created Box, Sphere, Capsule, or Mesh descriptor | `Runtime/Contracts/PhysicsShapeRecord.cs` |
| `PhysicsShapeType` | Schema-v1 shape discriminant | `Runtime/Contracts/PhysicsShapeRecord.cs` |
| `PhysicsArtifactManifest` | Sidecar identity, counts, tick rate, payload name | `Runtime/Contracts/PhysicsArtifactManifest.cs` |
| `PhysicsVector3` | Engine-independent float vector | `Runtime/Contracts/PhysicsMath.cs` |
| `PhysicsQuaternion` | Engine-independent canonical quaternion | `Runtime/Contracts/PhysicsMath.cs` |

Records expose getters but are not deeply immutable: mesh arrays and caller-provided list storage
remain mutable by reference. Freeze them after construction.

#### Identity and policy

| Type | Purpose | Source |
| --- | --- | --- |
| `JitterPhysicsPackage` | Package name/version, schema, Jitter assembly name, log prefix | `Runtime/Contracts/JitterPhysicsPackage.cs` |
| `JitterPhysicsSemantics` | Behavior-version inputs to runtime compatibility | `Runtime/Contracts/JitterPhysicsSemantics.cs` |
| `JitterPhysicsIdUtility` | Canonical ASCII IDs | `Runtime/Contracts/JitterPhysicsIdUtility.cs` |
| `JitterPhysicsArtifactNaming` | Current and exact legacy payload/manifest names | `Runtime/Contracts/JitterPhysicsArtifactNaming.cs` |
| `PhysicsArtifactLimits` | Allocation, count, coordinate, extent, tick and iteration caps | `Runtime/Contracts/PhysicsArtifactLimits.cs` |
| `PhysicsCanonicalization` | Finite floats, positive zero, normalized sign-canonical quaternions | `Runtime/Contracts/PhysicsCanonicalization.cs` |
| `IJitterPhysicsRuntimePreviewSource` | Portable runtime geometry for editor diagnostics | `Runtime/Contracts/IJitterPhysicsRuntimePreviewSource.cs` |

### Assembly: ArtifactCodec

Assembly: `DataSakura.JitterPhysics.ArtifactCodec`

Namespace: `DataSakura.JitterPhysics.ArtifactCodec`

Source root: `Runtime/ArtifactCodec/`

This assembly references only Contracts and has no UnityEngine dependency.

#### Read, write, and validate

| Type | Primary API | Source |
| --- | --- | --- |
| `PhysicsArtifactReader` | `Read(...)`, `CheckRuntimeCompatibility(...)` | `Runtime/ArtifactCodec/PhysicsArtifactReader.cs` |
| `PhysicsArtifactWriter` | `Write(...)`, `WriteWithManifest(...)` | `Runtime/ArtifactCodec/PhysicsArtifactWriter.cs` |
| `PhysicsArtifactPayload` | Canonical bytes plus the manifest generated from those exact bytes | `Runtime/ArtifactCodec/PhysicsArtifactWriter.cs` |
| `PhysicsArtifactValidator` | `Validate(...)` | `Runtime/ArtifactCodec/PhysicsArtifactValidator.cs` |
| `PhysicsArtifactManifestCodec` | Deterministic `Write(...)`, typed-null `Read(..., out error)` | `Runtime/ArtifactCodec/PhysicsArtifactManifestCodec.cs` |
| `JitterPhysicsHash` | SHA-256 and ordinal hex comparison | `Runtime/ArtifactCodec/JitterPhysicsHash.cs` |

Use reader/validator APIs for inspection. Writer APIs are producer infrastructure: a runtime
consumer should load the bake output, not construct another bake path.

#### Delivery

| Type | Primary API | Source |
| --- | --- | --- |
| `FilePhysicsArtifactProvider` | Manifest-first synchronous file loading | `Runtime/ArtifactCodec/FilePhysicsArtifactProvider.cs` |
| `EmbeddedPhysicsArtifactProvider` | Generated Base64 payload loading | `Runtime/ArtifactCodec/EmbeddedPhysicsArtifactProvider.cs` |
| `EmbeddedArtifactSourceGenerator` | Deterministic generated provider source | `Runtime/ArtifactCodec/EmbeddedArtifactSourceGenerator.cs` |
| `EmbeddedArtifactSourceOptions` | Namespace, class, chunk, and size policy | `Runtime/ArtifactCodec/EmbeddedArtifactSourceGenerator.cs` |
| `EmbeddedArtifactSource` | Generated file name, source text, and validated identity summary | `Runtime/ArtifactCodec/EmbeddedArtifactSourceGenerator.cs` |
| `PhysicsArtifactUploadStore` | Validate and publish a delivered pair | `Runtime/ArtifactCodec/PhysicsArtifactUploadStore.cs` |
| `PhysicsArtifactUploadResult` | Validated manifest and stored payload/manifest paths on success, or a typed delivery failure | `Runtime/ArtifactCodec/PhysicsArtifactUploadStore.cs` |
| `PhysicsArtifactPairWriter` | Stage/replace/restore two files | `Runtime/ArtifactCodec/PhysicsArtifactPairWriter.cs` |

Provider and filesystem APIs are synchronous. `PhysicsArtifactPairWriter` handles caught move
failures but is not crash-durable or a concurrency primitive.

#### Compatibility

| Type | Primary API | Source |
| --- | --- | --- |
| `RuntimeCompatibilityInputs` | Current or explicit semantic inputs | `Runtime/ArtifactCodec/RuntimeCompatibilityId.cs` |
| `RuntimeCompatibilityId` | Canonical SHA-256 computation | `Runtime/ArtifactCodec/RuntimeCompatibilityId.cs` |
| `PhysicsCompatibilityToken` | Encode/decode/exact match for level, artifact, and runtime | `Runtime/ArtifactCodec/PhysicsCompatibilityToken.cs` |

The token is transport-neutral and is not authentication or anti-cheat.

### Assembly: Authoring

Assembly: `DataSakura.JitterPhysics.Authoring`

Namespace: `DataSakura.JitterPhysics.Authoring`

Source root: `Authoring/`

| Type | Purpose | Source |
| --- | --- | --- |
| `JitterPhysicsLevel` | One scene-level bake identity, collection scope, profile, output folder, and last successful hash | `Authoring/JitterPhysicsLevel.cs` |
| `JitterStaticBodySource` | Explicit static-body marker with stable source ID, collider scope, friction, and restitution | `Authoring/JitterStaticBodySource.cs` |
| `JitterPhysicsWorldProfile` | Shared gravity, tick, solver, relaxation, substep, and deactivation authoring asset | `Authoring/JitterPhysicsWorldProfile.cs` |
| `JitterPhysicsAuthoringConstants` | Component, asset, and Editor menu roots plus level/source menu orders | `Authoring/JitterPhysicsAuthoringConstants.cs` |

These types contain authored project state; changing them can dirty scenes/assets and can change
the next artifact. Runtime simulation code should consume a validated artifact instead of reading
the scene components as a second source of truth.

### Assembly: UnityArtifact

Assembly: `DataSakura.JitterPhysics.UnityArtifact`

Namespace: `DataSakura.JitterPhysics.UnityArtifact`

Source root: `Runtime/UnityArtifact/`

| Type | Purpose | Source |
| --- | --- | --- |
| `JitterPhysicsArtifactAsset` | Unity reference to immutable `.bytes` payload plus inspector metadata | `Runtime/UnityArtifact/JitterPhysicsArtifactAsset.cs` |
| `JitterPhysicsArtifactLoader` | Hash, decode, validate, metadata and optional runtime check | `Runtime/UnityArtifact/JitterPhysicsArtifactLoader.cs` |
| `JitterPhysicsArtifactPaths` | Current and legacy Unity asset paths | `Runtime/UnityArtifact/JitterPhysicsArtifactPaths.cs` |

Normal runtime callers use `JitterPhysicsArtifactLoader.Load`. `Initialize` and path helpers are
public for cross-assembly editor/bake infrastructure and should not be a second runtime pipeline.
The loader currently cross-checks nonempty asset level/runtime IDs and body count when its copied
value is nonzero, but not every copied Inspector field.

### Assembly: Editor

Assembly: `DataSakura.JitterPhysics.Editor`

Source root: `Editor/`

This assembly is Editor-only. The supported consumer integration facade is the `Editor/Api`
surface below. The remaining public types exist because separate package subsystems and tests
must call them; prefer the facade unless you are deliberately building package tooling.

#### Supported Editor integration API

Namespace: `DataSakura.JitterPhysics.Editor.Api`

| Type | Purpose | Source |
| --- | --- | --- |
| `JitterPhysicsEditorApi` | Validate, bake, or read a verified bake summary | `Editor/Api/JitterPhysicsEditorApi.cs` |
| `JitterPhysicsLevelIdOwnership` | Standalone versus external-managed identity mode | `Editor/Api/JitterPhysicsEditorApi.cs` |
| `JitterPhysicsLevelIdBinding` | Explicit identity/owner binding for one operation | `Editor/Api/JitterPhysicsEditorApi.cs` |
| `JitterPhysicsEditorResultStatus` | Missing, Valid, Ready, or Failed outcome | `Editor/Api/JitterPhysicsEditorApi.cs` |
| `JitterPhysicsEditorResult` | Paths, digest, counts, status, identity ownership, and issue log | `Editor/Api/JitterPhysicsEditorApi.cs` |
| `JitterPhysicsPreviewApi` | Separate extension point for reading/applying the package's one shared Scene View preview state | `Editor/Api/JitterPhysicsPreviewApi.cs` |
| `JitterPhysicsPreviewState` | Immutable Sources/Baked/Runtime, scope, and occlusion snapshot | `Editor/Api/JitterPhysicsPreviewApi.cs` |
| `JitterPhysicsPreviewScope` | Active/selected level or all loaded levels | `Editor/Api/JitterPhysicsPreviewApi.cs` |
| `JitterPhysicsPreviewOcclusion` | Visible or X-Ray preview depth behavior | `Editor/Api/JitterPhysicsPreviewApi.cs` |

See [Editor API handoff](npi-editor-api.md) and [Recipes](recipes.md) for complete facade calls.

#### Advanced installation and compatibility infrastructure

| Type | Purpose | Source |
| --- | --- | --- |
| `JitterPhysicsInstaller` | Explicit receipt-managed install/update/remove operations | `Editor/Install/JitterPhysicsInstaller.cs` |
| `JitterPhysicsInstallResult` | Written/removed paths plus the complete issue log | `Editor/Install/JitterPhysicsInstaller.cs` |
| `JitterPhysicsServerProjection` | Copies the portable/server source projection to a chosen destination | `Editor/Install/JitterPhysicsServerProjection.cs` |
| `JitterPhysicsOwnership` | Receipt ownership state for an installed file | `Editor/Install/JitterPhysicsInstallReceipt.cs` |
| `JitterPhysicsComponentIds` | Stable receipt component identifiers | `Editor/Install/JitterPhysicsInstallReceipt.cs` |
| `JitterPhysicsInstalledFile` | One installed path and recorded hash | `Editor/Install/JitterPhysicsInstallReceipt.cs` |
| `JitterPhysicsInstalledComponent` | Receipt entry for one installed component | `Editor/Install/JitterPhysicsInstallReceipt.cs` |
| `JitterPhysicsInstallReceipt` | Project-owned installation inventory | `Editor/Install/JitterPhysicsInstallReceipt.cs` |
| `JitterPhysicsSourceHasher` | Canonical Jitter2 source/profile hashing | `Editor/Bootstrap/JitterPhysicsSourceHasher.cs` |
| `JitterPhysicsSourceHasher.SourceInput` | Nested value type for one canonical source-hash input | `Editor/Bootstrap/JitterPhysicsSourceHasher.cs` |
| `JitterPhysicsLock` | Parsed pinned Jitter2 repository, source, and compile-profile contract | `Editor/Bootstrap/JitterPhysicsLock.cs` |
| `JitterPhysicsJsonKind` | Minimal lock/report JSON value kind | `Editor/Bootstrap/JitterPhysicsJsonValue.cs` |
| `JitterPhysicsJsonValue` | Minimal deterministic lock/report JSON model | `Editor/Bootstrap/JitterPhysicsJsonValue.cs` |
| `JitterPhysicsAssemblyInfo` | One discovered assembly candidate and its provenance | `Editor/Bootstrap/JitterPhysicsAssemblyProbe.cs` |
| `JitterPhysicsAssemblyProbe` | Discovers Jitter2 assemblies without modifying the project | `Editor/Bootstrap/JitterPhysicsAssemblyProbe.cs` |
| `JitterPhysicsCompatibilityStatus` | `Missing`, `Compatible`, `Incompatible`, `Duplicate`, `UnsupportedPlugin`, or `Unknown` state | `Editor/Bootstrap/JitterPhysicsCompatibilityReport.cs` |
| `JitterPhysicsCompatibilityReport` | Complete setup identity/report used to gate baking | `Editor/Bootstrap/JitterPhysicsCompatibilityReport.cs` |

#### Advanced bake, diagnostics, export, and window infrastructure

| Type | Purpose | Source |
| --- | --- | --- |
| `JitterPhysicsBuildResult` | In-memory artifact build plus issue log | `Editor/Baking/JitterPhysicsArtifactBuilder.cs` |
| `JitterPhysicsArtifactBuilder` | Converts authored sources into a canonical DTO without publishing files | `Editor/Baking/JitterPhysicsArtifactBuilder.cs` |
| `JitterPhysicsColliderKey` | Canonical structural ordering key for a collider | `Editor/Baking/JitterPhysicsColliderKey.cs` |
| `JitterPhysicsConversionStatus` | `Converted`, `UnsupportedType`, `Trigger`, `DegenerateScale`, `NotFinite`, `DegenerateShape`, `UnreadableMesh`, or `InvalidMesh` | `Editor/Baking/JitterPhysicsColliderConverter.cs` |
| `JitterPhysicsConversionResult` | Shape conversion output and diagnostic context | `Editor/Baking/JitterPhysicsColliderConverter.cs` |
| `JitterPhysicsColliderConverter` | Unity Collider to portable shape conversion | `Editor/Baking/JitterPhysicsColliderConverter.cs` |
| `JitterPhysicsBakeOutput` | Published asset/payload/manifest paths and verified identity | `Editor/Baking/JitterPhysicsBaker.cs` |
| `JitterPhysicsBakeResult` | Bake output or issue log | `Editor/Baking/JitterPhysicsBaker.cs` |
| `JitterPhysicsBaker` | Validate, write, re-read, and publish one artifact trio | `Editor/Baking/JitterPhysicsBaker.cs` |
| `JitterPhysicsIssueSeverity` | Warning or Error severity | `Editor/Baking/JitterPhysicsIssue.cs` |
| `JitterPhysicsIssue` | One authoring/bake finding with optional Unity context | `Editor/Baking/JitterPhysicsIssue.cs` |
| `JitterPhysicsIssueLog` | Ordered issue collection and formatted report | `Editor/Baking/JitterPhysicsIssue.cs` |
| `JitterPhysicsBakeCommand` | Setup-gated selected-level validation and bake commands | `Editor/Baking/JitterPhysicsBakeCommand.cs` |
| `JitterPhysicsArtifactMigration` | Explicit migration of legacy hash-addressed bake file names to current canonical names | `Editor/Baking/JitterPhysicsArtifactMigration.cs` |
| `JitterPhysicsGeometryComparer` | Sources-versus-bake diagnostic comparison | `Editor/Diagnostics/JitterPhysicsGeometryComparer.cs` |
| `JitterPhysicsServerUploadResult` | Server upload response/failure summary | `Editor/Export/JitterPhysicsServerUploader.cs` |
| `JitterPhysicsServerUploader` | Explicit verified payload/manifest HTTP upload | `Editor/Export/JitterPhysicsServerUploader.cs` |
| `JitterPhysicsArtifactDelivery` | Verified payload/manifest pair prepared for export | `Editor/Export/JitterPhysicsArtifactExporter.cs` |
| `JitterPhysicsExportResult` | Export path or issue log | `Editor/Export/JitterPhysicsArtifactExporter.cs` |
| `JitterPhysicsArtifactExporter` | Verify/export binary, manifest, and embedded source | `Editor/Export/JitterPhysicsArtifactExporter.cs` |
| `JitterPhysicsExportDefaults` | Default generated namespace/class/path helpers | `Editor/Export/JitterPhysicsArtifactExporter.cs` |
| `JitterPhysicsBakerWindow` | Main five-tab authoring window | `Editor/JitterPhysicsBakerWindow.cs` |
| `JitterPhysicsAboutWindow` | Version, links, and support information | `Editor/JitterPhysicsAboutWindow.cs` |
| `JitterPhysicsSetupWindow` | Detailed compatibility and installation UI | `Editor/JitterPhysicsSetupWindow.cs` |

These advanced APIs are synchronous and can write project or filesystem state when their method
name describes an explicit mutation. Opening/repainting the windows and creating a compatibility
report remain read-only; use [Editor guide](editor-guide.md#side-effect-reference) for the action
matrix.

### Installed assembly: JitterIntegration

Assembly: `DataSakura.JitterPhysics.JitterIntegration`

Namespace: `DataSakura.JitterPhysics.Integration`

Source root before installation: `JitterIntegration~/Runtime/`

| Type | Purpose | Source |
| --- | --- | --- |
| `JitterPhysicsWorldBuilder` | Apply one validated static artifact to a Jitter2 world | `JitterIntegration~/Runtime/JitterPhysicsWorldBuilder.cs` |
| `PhysicsWorldBuildResult` | Typed build outcome and diagnostics | `JitterIntegration~/Runtime/JitterPhysicsWorldBuilder.cs` |
| `JitterPhysicsServerStartup` | Provider → expectation checks → world build → readiness | `JitterIntegration~/Runtime/JitterPhysicsServerStartup.cs` |
| `JitterPhysicsServerOptions` | Required runtime ID, optional expected level/tick | `JitterIntegration~/Runtime/JitterPhysicsServerStartup.cs` |
| `JitterPhysicsServerState` | Readiness, loaded identity, counts, self-check, typed error | `JitterIntegration~/Runtime/JitterPhysicsServerStartup.cs` |

The integration is consumer-installed and references `Jitter2.Core` by name. The package owns no
tick loop, dynamic-body API, network transport, or server process.

### Server folder

`Server~` exports no additional production namespace. Its current test project targets .NET 10,
references the prebuilt Jitter2 assembly, and compiles the portable and integration source files
by reference. It is compatibility evidence for that test target, not a standalone server product
or proof for every server target framework.

## Important 0.0.12 behavior notes

- `SubstepCount` is not applied by the world builder.
- Failed construction only attempts body removal and does not restore changed world settings;
  discard the candidate world.
- Mesh local pose is ignored; vertices must already be body-local.
- `TopologyFingerprint` omits mesh contents and other simulation-affecting values.
- DTO storage and `PhysicsCompatibilityToken.Magic` are publicly mutable by reference.
- Build/provider initialization is not documented as thread-safe.
- IL2CPP/AOT target acceptance remains unverified by a completed mobile smoke gate.

See [Runtime API](runtime-api.md) for safe ownership patterns,
[Extending](extending.md) for supported customization, and
[Artifact format v1](artifact-format-v1.md) for serialized compatibility rules.
