# Jitter2 snapshot provenance

This folder holds the Jitter2 reference snapshot. Unity never imports it (the folder name
ends with `~`). It is used for three things:

1. the sources, kept as close to upstream as possible, for audit and for re-syncing;
2. the netstandard2.1 assembly built from them, which is what actually gets installed;
3. the compatibility shims that assembly needs, in `Compat/`.

`jitter2.lock.json` records what the snapshot was produced from and how it was compiled.

## Current snapshot

| Field | Value |
| --- | --- |
| Upstream | <https://github.com/notgiven688/jitterphysics2> |
| Tag | `2.8.9` |
| Commit | `c15bc6abfdda90a936975979a42f7a54a211084e` |
| Library path | `src/Jitter2` |
| Files | 95 upstream `.cs` + 1 canonical DataSakura `.cs` patch |
| Included set | `**/*.cs`, `**/csc.rsp` |
| Excluded set | metadata, asmdefs, build output, tests (see `jitter2.lock.json`) |
| Patch set | `unity-netstandard21-stablemath-public-v3` |
| Built assembly | `Prebuilt/Jitter2.Core.dll` (netstandard2.1) |

Reproduce with:

```sh
python3 tools~/sync-jitter2.py --ref 2.8.9
bash tools~/build-jitter2-unity.sh
python3 tools~/verify-jitter2-lock.py
```

The sync tool replaces only `Jitter2~/Runtime`, re-applies the 19 netstandard2.1 call-site
patches, and restores the one lock-declared canonical file after verifying its pre-sync hash. It
does not write to a consumer's external Jitter checkout.

## Canonical DataSakura source patch

The pinned upstream commit has no `src/Jitter2/LinearMath/StableMath.cs`. The file at
`Runtime/LinearMath/StableMath.cs` is therefore an explicit maintained part of the canonical
DataSakura Jitter distribution, not an upstream file and not a consumer-local duplicate. Its
complete reason and SHA-256 are recorded in `canonicalPatches` in the lock. E02 defines its public
supported contract in `STABLE_MATH.md`; changing that API or numerical behavior requires a new
patch-set/source/runtime identity.

## Why the package ships an assembly and not sources

Unity compiles game assemblies at C# 9, and it ignores `-langversion` in an assembly's
`csc.rsp`. The snapshot is written in a later language — file-scoped namespaces, primary
constructors, `scoped` parameters, constant interpolated strings — so handing Unity the
sources produces several hundred parse errors before anything else is even considered.

That limit applies to sources Unity compiles, not to an assembly it loads. So the package
compiles the snapshot itself, with a current compiler, and installs the result as a managed
plugin. The language problem disappears completely; what remains is the framework gap,
because Unity's surface is .NET Standard 2.1.

The same assembly is used by the Unity client and the dedicated server. That is not
convenience. Jitter2 carries two implementations of its contact solver and support
mapping, and picks between them on `Vector128.IsHardwareAccelerated`. If the server
compiled the sources for a modern runtime it would take the accelerated path while the
client took the scalar one, and the two would produce different simulations from the same
artifact — the failure this package exists to prevent.

## Compatibility shims (`Compat/`, additive, no upstream code touched)

| Shim | Replaces | Why it is needed |
| --- | --- | --- |
| `Vector128Shim.cs` | `System.Runtime.Intrinsics` | Added in .NET 5; absent from netstandard2.1. Implemented in software, so `IsHardwareAccelerated` is `false` and Jitter2 takes its own scalar paths. |
| `NetStandardShims.cs` | `IsExternalInit`, `SkipLocalsInitAttribute` | Compiler contracts, satisfied by any declaration. |
| `NetStandardShims.cs` | `PriorityQueue<,>` | Added in .NET 6. A binary min-heap. |
| `InteropShims.cs` | `NativeMemory` | Added in .NET 6. Backed by `Marshal.AllocHGlobal`, with manual alignment. |
| `InteropShims.cs` | `CollectionsMarshal.AsSpan` | Added in .NET 5. Must alias the list's storage, since callers sort through it; the layout assumption is verified at first use and throws rather than reading arbitrary memory. |

`Vector128` and the shim types are `internal`. A public shim would put a type named
`System.Runtime.Intrinsics.Vector128` in the assembly's surface, which collides by name
with the real one for any consumer targeting .NET 5 or later — the server among them.

Only `TreeBox` runs on the shim in anger; it compares and subtracts lane-wise, and IEEE-754
makes those operations identical whether a CPU does four at once or one at a time.

## Applied patches (`tools~/patch-jitter2-netstandard.py`)

Only where a shim cannot help: static members added to types that already exist, which
cannot be extended from outside. Every edit is local and behaviour-preserving, and the
script is idempotent, so a re-sync re-applies them and fails loudly if one no longer
matches.

| Site | Change | Reason |
| --- | --- | --- |
| 4 files | `MethodImplOptions.AggressiveOptimization` → `(MethodImplOptions)512` | Enum member from .NET Core 3.0. A JIT hint with no observable semantics. |
| `DynamicTree.cs` | `double.Min` → `Math.Min` | Generic math, .NET 7. |
| `TreeBox.cs` | `VectorMin`/`VectorMax` go through a pointer, and become `internal` | Ref-safety rejects returning a ref derived from `this`; `internal` keeps the shim out of the public surface. |
| `RigidBody.cs`, `World.cs`, `TransformedShape.cs`, `TriangleShape.cs` | throw helpers spelled out longhand | `ArgumentNullException.ThrowIfNull` (.NET 6), `ArgumentOutOfRangeException.ThrowIf*` (.NET 8), `ObjectDisposedException.ThrowIf` (.NET 7). Same exception, same parameter name. |
| `World.Deterministic.cs` | `Enum.IsDefined(value)` → `Enum.IsDefined(typeof(SolveMode), value)` | Generic overload, .NET 5. |
| `ThreadPool.cs` | `OperatingSystem.IsWindows()` → `RuntimeInformation.IsOSPlatform` | .NET 5. |
| `World.cs` | `Interlocked` on `ulong` reinterpreted as `long` | Only the signed overloads exist in netstandard2.1; two's complement makes the result identical. |

## Dependency shipped alongside

`System.Runtime.CompilerServices.Unsafe.dll` is not part of netstandard2.1 and Unity does
not deliver it to players, so it travels with the plugin. The installer skips it when the
project already provides one, because two copies of the same assembly is a conflict Unity
reports far from its cause.

The dependency is pinned to NuGet `System.Runtime.CompilerServices.Unsafe` `6.0.0`; its exact DLL
hash is part of `unityAssembly.artifacts` in the lock.

## Reproducible binary policy

The canonical profile is f32, `netstandard2.1`, unsafe enabled, no Unity define, scalar
intrinsics shim, netstandard2.1 polyfills, latest compiler language, deterministic build and
continuous-integration build enabled. `Jitter2.Core.csproj` is checked against those scalar lock
fields by `verify-jitter2-lock.py`.

`unityAssembly.buildInputHash` additionally hashes the compile profile, `Runtime`, `Compat`, and
the canonical csproj. This keeps an edited shim/project from pairing with an old binary while the
upstream-only `sourceContentHash` still happens to match an external source distribution.

`build-jitter2-unity.sh` is the only documented staging command. It delegates to
`build-jitter2-reproducible.py`, which creates two isolated clean source trees, disables shared
compiler/build servers, builds both, and requires byte-identical `Jitter2.Core.dll`, XML docs and
the pinned Unsafe dependency. Only after all three hashes match does it replace `Prebuilt` and
refresh `unityAssembly.artifacts`. PE metadata differences are not allowed by the current policy;
any byte difference fails the build instead of being normalized or waived.

## Precision

The lock declares `"precision": "f32"`. `Real` is a global using in `Precision.cs`, which
reaches only code compiled together with the snapshot; projects that reference the built
assembly restate the alias themselves.

## Update procedure

1. Pin the revision to sync from.
2. Run `tools~/sync-jitter2.py` with `--ref` (upstream) or `--source` (a local fork).
3. The sync command preserves the declared canonical StableMath patch and applies the
   netstandard2.1 call-site patch set; investigate any reported mismatch.
4. Refresh `sourceContentHash` with `tools~/hash-jitter2.py` after reviewing the source/profile
   change.
5. Run `tools~/build-jitter2-unity.sh`; it performs two clean builds, stages only matching bytes,
   and refreshes the binary hashes in the lock.
6. Verify source/profile/patch/binary consistency with `tools~/verify-jitter2-lock.py`.
7. Run `tools~/test-dotnet.sh`.
8. Release the package and the consumer lock update as one atomic change.

Any of these changes `sourceContentHash` and therefore `runtimeCompatibilityId`, which is
intended: a client and a server built against different Jitter sources must not be able to
claim compatibility, and existing artifacts must be re-baked.
