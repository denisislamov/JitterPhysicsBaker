# JMP-E09 — release candidate report 0.7.0

Дата: 2026-09-02.

Ветка: `d.islamov/jmp-e00-baseline-adrs`.

Baseline E09: `161d41d` (`JMP-E08`).

Release-preparation candidate: `02ff3cf`.

Итоговый статус: **BLOCKED, release tag не разрешён evidence contract**.

## Release identity

| Поле | Значение |
| --- | --- |
| Package version | `0.7.0` |
| Previous published version | `0.0.12` |
| Artifact schema | `1` |
| Runtime compatibility ID | `71e9d01f4006a8e1d097beb047efa8b8aabbe24895cb8d50531c764031c9aa4b` |
| Jitter source hash | `sha256:ca940ca6483ffcedf65854719396cec2d9e038cc43c01e7d35d147cd70766940` |
| Compile profile | `a2925211b983330117414426be9bf8a2798ce9169c1206e1e55178f708cfa72e`, `f32`, `netstandard2.1`, scalar shim |
| `Jitter2.Core.dll` | `sha256:1e0aea7a6da1e3887ce90eabe6b508341870b62992b2c79d09382586db3e0321` |
| Re-bake | Обязателен для всех affected levels |

## Public contract changes

- Canonical public `Jitter2.LinearMath.StableMath` API.
- Installed Jitter-native artifact graph using `JVector`, `JQuaternion` and `Real`.
- `JitterPhysicsWorldBuilder.Apply(World, JitterNative.PhysicsArtifact)`.
- `PhysicsWorldBuildResult.RequiresWorldDiscard`.
- `JitterNativeUnityArtifactLoader.Load` for simulating Unity consumers.
- Successful providers return exact bytes in `PhysicsArtifactLoadResult.Payload`; the old success
  overload remains source-compatible for inspection but is rejected by native server startup.
- Schema remains 1; runtime identity changes, so old artifacts are not runtime-compatible.

## Real aliases

- Canonical Jitter2 profile: `Jitter2~/Runtime/Precision.cs` and locked `f32` compile profile.
- Installed integration files define `using Real = System.Single` only when the server project has
  not supplied its single global alias.
- `Server~/Tests/DataSakura.JitterPhysics.Server.Tests.csproj` declares the exact `System.Single`
  `Real` alias for the projected compile graph.
- Unity authoring/presentation and sample timing remain allowlisted boundaries; they do not define
  authoritative artifact scalar storage.

## Artifact decision

The legacy `0.0.12` writer and native writer produce equal schema-one payload bytes and canonical
manifest bytes for the frozen fixture. Schema is not bumped. Runtime ID is bumped because Jitter
source/profile identity changed. Payload, manifest and `.physics.asset` are an atomic delivery
unit; re-bake and re-export are mandatory.

## Source audit result

Production check: `110` files, `1810` reviewed findings, `0` unapproved, `0` stale entries.
Remaining matches are explicit boundaries/fixtures:

| Allowlist owner | Findings | Reason class |
| --- | ---: | --- |
| Unity authoring/editor | 367 | serialized authoring, UI and Scene View presentation |
| Unity-to-Jitter adapter | 48 | single explicit conversion boundary |
| Schema-one bootstrap/codec | 183 | published Jitter-free compatibility and hostile parsing |
| Native Real/profile/runtime telemetry | 22 | f32 layout checks and Stopwatch diagnostics |
| Consumer samples | 458 | Unity input, presentation, timing and gameplay-owned dynamics |
| Portable/Unity fixtures | 729 | legacy, tampered, f64 and boundary tests |

Every class has exact policy path/rules, owner and reason. A new finding changes the reviewed hash;
an unused entry fails validation.

## Gate matrix

| Gate | Результат | Evidence |
| --- | --- | --- |
| `git diff --check` | PASS | exit 0 |
| Package `.meta` / LFS | PASS | complete `.meta`, no LFS pointers |
| Jitter lock verification | PASS | 96 files, 1 canonical patch, 3 binary artifacts |
| Lock negative fixtures | PASS | tampered server DLL rejected |
| Source audit self-tests | PASS | 12/12 |
| Source audit enforcement | PASS | 110 files, 1810 findings, debt 0, stale 0 |
| Editor csproj build | PASS | 0 warnings, 0 errors |
| Editor.Tests csproj build | PASS | 0 warnings, 0 errors |
| Runtime.Tests csproj build | PASS | 0 warnings, 0 errors |
| Portable/server full suite | PASS | 119/119 |
| StableMath filtered suite | PASS | 7/7 |
| Codec/schema filtered suite | PASS | 27/27 |
| World/server/consumer filtered suite | PASS | 25/25 |
| Unity EditMode | BLOCKED | project open in Editor; earlier attempt also hit Licensing protocol 505; no fresh XML |
| Unity PlayMode | BLOCKED | EditMode prerequisite did not run; no fresh XML |
| Manual changed Editor scenarios | NOT RUN | active Editor state was not mutated automatically |
| Clean import without Jitter | NOT RUN on exact RC | static assembly contract only is not consumer evidence |
| Package-owned Jitter + integration | NOT RUN on exact RC | requires clean Unity consumer |
| External Jitter + integration-only | NOT RUN | requires clean Unity consumer |
| Combined Baker/Custom Navigation | NOT RUN | no consumer mutation authorized/performed |
| Exactly-one installed Jitter inventory | NOT RUN on exact RC | portable process fixture PASS is not installed Unity evidence |
| Mono/IL2CPP player smoke | NOT RUN | no exact RC player build |
| First/repeat bake | NOT RUN on exact RC | no fresh Unity bake |
| Update/rollback from `0.0.12` | DOCUMENTED, NOT RUN | upgrade guide is not execution evidence |
| Remote CI | NOT RUN | branch not pushed when this report was written |

## Stop decision

Repository rules require fresh Unity XML, manual Editor scenarios after Editor changes, exact clean
consumer compilation and applicable player/repeat-bake evidence before release. Therefore:

- branch commits may be pushed with this honest blocked report;
- `tools/publish-package.sh v0.7.0` must not run yet;
- no `v0.7.0` tag may be created locally or remotely;
- old XML from 2026-09-01 is not reused.

## Unrelated work confirmation

The following paths remain outside commits/staging:

- `Assets/Settings/Mobile_RPAsset.asset`;
- `Assets/Generated.meta`;
- `Assets/JitterPhysicsBaker/Docs/JITTER_PHYSICS_BAKER_JUNIOR_CODE_GUIDE.md` and `.meta`;
- `Assets/JitterPhysicsBaker/Docs/JITTER_PHYSICS_BAKER_USER_FRIENDLY_PACKAGE_PROPOSALS.md` and
  `.meta`.

## Steps to unblock publication

1. Save and close the Unity project.
2. Restore a Licensing Client compatible with Unity `6000.3.19f1`.
3. Run EditMode and PlayMode and inspect fresh XML.
4. Run the changed manual Editor scenarios.
5. Verify clean no-Jitter import, package-owned and external-Jitter Setup flows.
6. Verify the combined Baker/Custom Navigation consumer and exactly one installed Jitter.
7. Build/smoke the required player/IL2CPP target.
8. Perform two equal bakes and the `0.0.12 -> 0.7.0` update/rollback scenario.
9. Repeat final mandatory checks, then publish and verify branch, package main and tag remotely.
