# Jitter-native artifact contracts

[Back to the manual](index.md)

The installable integration now owns the authoritative in-memory artifact graph in
`DataSakura.JitterPhysics.JitterNative`. Its simulation values are `JVector`, `JQuaternion`,
`JVector[]`, and the locked f32 `Real` alias. No Jitter type was added to the always-imported
assemblies, so importing the package without Jitter continues to compile and the existing
explicit **Jitter Physics - Setup** operation remains the only installation boundary.

## Canonical binary contract

`DataSakura.JitterPhysics.JitterNative.Codec.PhysicsArtifactCodec` writes schema 1 with explicit
little-endian primitives:

- `WriteReal` writes one canonical IEEE-754 binary32 value;
- vectors are three ordered scalars (`X`, `Y`, `Z`);
- quaternions are four ordered scalars (`X`, `Y`, `Z`, `W`);
- arrays are count-prefixed and retain the existing body, shape, vertex, and index ordering;
- no Jitter struct, padding, native memory, `Marshal`, or runtime-specific layout is serialized.

The schema number and golden payload are unchanged. The native writer must produce the exact
165-byte E00 fixture and SHA-256
`b53cf221453ce313ae3e2d9ff3e94b665b65a674a0f1f5e9863acb5b33835479`.

`PhysicsCanonicalization` uses the canonical Jitter `StableMath` surface. It rejects non-finite
values and degenerate quaternions, normalizes quaternion sign, and maps negative zero to positive
zero before writing. Validation still returns `PhysicsArtifactError` for external-input failures;
the writer throws only when a producer passes an invalid object graph, which is a programming
error.

## Bounded compatibility window

Schema 1 bytes do not change during source migration. Until JMP-E07 is complete, the original
Jitter-free records remain in `Runtime/Contracts` as the bootstrap and source-compatibility
surface. The native reader temporarily delegates hostile byte parsing and the mature limits,
ordering, and mesh checks to the schema 1 reader, then performs one exact f32 field conversion.
This bridge is internal and is not a second public artifact format.

The bridge and old DTO use inside installed runtime consumers have a fixed removal deadline:
**JMP-E07**. JMP-E05 migrates Unity producers and boundaries; JMP-E06 freezes compatibility and
failure policy; JMP-E07 moves the world builder, server, and samples to native records and removes
their per-record conversions. Do not add new public APIs that accept `PhysicsVector3` or
`PhysicsQuaternion` during this window.

## Source migration map

| Legacy source | Native source |
| --- | --- |
| `PhysicsVector3` | `JVector` |
| `PhysicsQuaternion` | `JQuaternion` |
| `PhysicsVector3[]` | `JVector[]` |
| simulation `float` | f32 `Real` |
| `PhysicsArtifactWriter.Write` | `JitterNative.Codec.PhysicsArtifactCodec.Write` |
| `PhysicsArtifactReader.Read` | `JitterNative.Codec.PhysicsArtifactCodec.Read` |

The known legacy consumers at the start of E04 are the authoring world profile, artifact builder,
collider converter, bake diagnostics, legacy schema codec and tests, world builder, server startup
tests, and imported samples. They are migrated by E05-E07; the compile fixture
`JitterNativeArtifactCodecTests` proves that the supported schema 1 bridge remains source- and
byte-compatible while that happens.

## Verification boundary

The portable/server suite proves native record field types, exact golden bytes and SHA-256,
native read output, stable quaternion sign, signed-zero encoding, and typed rejection of NaN,
infinity, and degenerate quaternions. Unity compilation and EditMode/PlayMode remain separate
gates because the installable sources are copied and compiled only after Setup.
