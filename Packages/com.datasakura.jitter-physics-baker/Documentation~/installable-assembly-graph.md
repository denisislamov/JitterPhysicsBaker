# Installable assembly graph and precision profile

[Back to the manual](index.md)

The package deliberately has two compile-time graphs. Importing the UPM package does not install
Jitter, copy integration sources, create a receipt, or change scripting defines. The second graph
appears only after the existing explicit Setup operation.

## Graph before Setup

```text
Contracts <- ArtifactCodec <- UnityArtifact <- Authoring
    ^              ^               ^             ^
    +--------------+---------------+-------------+-- Editor (Editor only)
```

These five assemblies are always available and contain no direct or transitive `Jitter2.Core`
reference. The graph is checked for cycles. Setup/diagnostics UI remains usable when Jitter is
missing and reports an actionable `Missing` state instead of producing a compiler error.

The public Jitter-native APIs below are deliberately unavailable before Setup:

- `Jitter2.LinearMath.StableMath` and Jitter math/runtime types;
- `DataSakura.JitterPhysics.Integration.JitterRuntimeProfile`;
- `DataSakura.JitterPhysics.Integration.JitterPhysicsWorldBuilder`;
- `DataSakura.JitterPhysics.Integration.JitterPhysicsServerStartup` and its state/options types.

Envelope, manifest, installation diagnostics, and typed error APIs remain available. Later
migration stages move the geometry records and their codec into the installable side without
changing this bootstrap rule.

## Graph after Setup

```text
                         direct source asmdef edge
Contracts + Codec  <--- Integration ----------------> Jitter2.Core
                         direct precompiled DLL edge

server projection: projected sources -> JitterPhysics.Runtime.props -> exact Jitter2.Core.dll
```

For a source-based external Jitter, the generated integration asmdef keeps
`"Jitter2.Core"` in `references`. For the package-owned prebuilt runtime, the generated asmdef
uses `overrideReferences: true` and `precompiledReferences: ["Jitter2.Core.dll"]`. Both forms are
direct compile edges. The installer tailors only its generated, receipt-owned asmdef and never
edits an external Jitter distribution.

The server projection carries the exact lock-verified DLL and a props file with a direct
`Reference`/`HintPath`. Projection manifest schema 3 records source hash, compile-profile ID,
precision, integration API version, and DLL SHA-256.

## One f32 policy

The supported scalar profile is `Real = System.Single`, lock precision `f32`.

Unity 6000.3 compiles consumer scripts as C# 9, so C# 10 `global using` cannot be the portable
source mechanism. Every installed Unity compilation unit that uses the alias declares exactly:

```csharp
using Real = System.Single;
```

The modern .NET server project/projection declares the same alias once through MSBuild
`<Using Include="System.Single" Alias="Real" />` and defines
`DATASAKURA_SERVER_GLOBAL_REAL`, preventing a duplicate local alias. `USE_DOUBLE_PRECISION` is
not enabled by any owned integration project or asmdef.

Three independent preflights enforce the profile:

1. Setup and bake reject a lock whose precision is not exactly `f32`.
2. `JitterRuntimeProfile.VerifyCanonicalF32` checks `Precision.IsDoublePrecision`, scalar field
   types, `JVector` size 12, and `JQuaternion` size 16.
3. Server startup and world application call that preflight before provider load or world mutation.

An unsupported profile returns `PhysicsArtifactErrorCode.IncompatibleRuntime`; f64 is not a
fallback mode. It remains unsupported until separate artifact, network, Unity, and IL2CPP gates
exist.

## Non-simulation exceptions

`double` remains valid for telemetry such as `Stopwatch.Elapsed.TotalMilliseconds`; telemetry is
not serialized into the artifact and does not affect topology. Artifact serialization continues
to write explicitly defined f32 bits rather than runtime struct memory. Hashes, identifiers,
counts, schema numbers, and tick rates retain their documented string/integer representations.
