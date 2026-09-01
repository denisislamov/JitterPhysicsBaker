# Unity-to-Jitter boundary and native baking

[Back to the manual](index.md)

Unity authoring crosses into Jitter math in exactly one installable class:
`DataSakura.JitterPhysics.JitterNative.UnityBoundary.UnityJitterMathAdapter`. The class lives in
`JitterIntegration~`, so it is dormant on package import and is copied only by the existing
explicit **Install Integration** Setup action. Jitter itself is still installed separately first;
the package does not embed, move, or silently install it.

## Coordinate and transform policy

- Unity X, Y, and Z components map directly to Jitter X, Y, and Z. No axis is swapped.
- Quaternion components map in X, Y, Z, W order and are normalized/sign-canonicalized once.
- Body positions and rotations are world-space values.
- Primitive shape poses are relative to the static-body root.
- Primitive dimensions consume absolute `lossyScale` once.
- A sphere under non-uniform scale uses the largest axis and emits a warning.
- Capsule records are Y-aligned; X/Z directions are represented by one local rotation correction.
- Mesh vertices consume the complete `body.worldToLocalMatrix * collider.localToWorldMatrix`
  once. A negative determinant swaps the last two indices of every triangle once.
- No scale is retained in an authoritative record and no conversion is repeated below this
  boundary.

`JitterNativeColliderConverter` produces native Box, Sphere, Capsule, and Mesh records.
`JitterNativeUnityArtifactBuilder` preserves stable Source IDs, structural collider keys, ordinal
body/shape ordering, and the existing mesh vertex/index policy. It validates the complete native
graph before exposing it.

## Editor bootstrap and installed path

The always-imported Editor assembly cannot hold a compile-time Jitter reference. After Setup,
`JitterNativeBuildBridge` locates the installed builder only when the user explicitly validates or
bakes. It asks the installed builder for the native graph, encodes it with the native codec, and
accepts it back only after the schema-one reader verifies the bytes. There is no work in
`InitializeOnLoad`, asset import, or Scene View repaint.

The old Jitter-free builder remains a bounded no-Jitter validation/migration fixture through
JMP-E07. A real Bake command with a compatible Jitter runtime but no installed integration now
fails with an actionable Setup error instead of silently using legacy math.

The native diagnostics comparer works on `JVector` and `JQuaternion` after the boundary. The
Scene View continues to cache Sources, Baked, Runtime, Added, Changed, Moved, and Removed layers;
conversion and hashing happen during explicit cache refresh, never while drawing Repaint.

## Artifact bridge

The Unity artifact asset still stores payload bytes and manifest metadata, not Unity or Jitter
math structs. Payload/manifest/hash verification, synchronous import/reimport, moved or removed
asset diagnostics, and staged pair replacement remain unchanged. A late failure while updating the
third `.physics.asset` member remains an explicitly documented limitation; do not deliver a failed
bake, and re-bake/verify the complete trio.

## Verification

The E05 installed-state probe copies the locked Jitter DLL and the integration sources to a unique
temporary Assets folder, runs Unity tests, then removes only that folder. This exercises the same
precompiled-reference asmdef selected by Setup. Existing numerical fixtures then run through the
native bridge: Box full size/scale, conservative Sphere scale, Capsule axis/length, mesh transform
and winding, stable ordering, duplicate IDs, first/repeat equality, artifact writes, and preview
cache behavior. A second clean run after removal proves the no-Jitter package graph still compiles.
