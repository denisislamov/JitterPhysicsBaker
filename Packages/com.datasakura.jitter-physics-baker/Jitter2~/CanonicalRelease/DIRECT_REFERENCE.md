# Separate-install direct-reference contract

## Unity

Install Jitter explicitly before a consumer that exposes Jitter types is compiled. The existing
Jitter Physics Baker Setup flow remains supported: **Install Jitter2** copies the canonical managed
plugin into the consumer project. A consumer that does not use Baker may instead extract this exact
release archive into a project-owned plugin folder.

There must be exactly one `Jitter2.Core.dll` under `Assets/`. An assembly definition that uses
`JVector`, `JQuaternion`, or `StableMath` declares a direct precompiled reference to
`Jitter2.Core.dll`. Custom Navigation must not copy the DLL into its package and must not rely on a
transitive Jitter Physics Baker reference.

Keep `System.Runtime.CompilerServices.Unsafe.dll` beside the plugin unless the project already owns
the same compatible assembly. Do not install a second copy.

## .NET

Extract the same approved archive and reference its exact `Jitter2.Core.dll` through an explicit
`Reference`/`HintPath`. Copy the same DLL to the server output; do not rebuild Jitter sources for the
server and do not resolve it through Jitter Physics Baker.

```xml
<Reference Include="Jitter2.Core">
  <HintPath>path/to/approved/DataSakura.Jitter2.Core/Jitter2.Core.dll</HintPath>
  <Private>true</Private>
</Reference>
```

Before use, compare the archive checksum, every manifest file hash, `precision=f32`, compile profile
ID, source-content hash, StableMath compatibility ID, and the expected immutable Git tag. Missing,
duplicate, f64, or mismatched assemblies are hard failures.
