# JMP-E03 — evidence Real и устанавливаемого assembly graph

Дата фиксации: 2026-09-01.

Ветка: `d.islamov/jmp-e00-baseline-adrs`.

Baseline эпика: `87917a721f1028e41bd4e959e00d03412fbd57a5` (`JMP-E02`).

## Реализованный graph

Always-available `Contracts`, `ArtifactCodec`, `UnityArtifact`, `Authoring`, `Editor` остаются
Jitter-free и образуют проверяемый acyclic graph. Скрытый `JitterIntegration~/` остаётся dormant
до прежней explicit Setup-команды.

После Setup integration получает direct reference:

- external source Jitter: `references: [ ..., "Jitter2.Core" ]`;
- package prebuilt: `overrideReferences: true` и
  `precompiledReferences: ["Jitter2.Core.dll"]`;
- server: direct MSBuild `Reference` с `HintPath` на exact projected DLL.

Обязательный `Server~/Tests/DataSakura.JitterPhysics.Server.Tests.csproj` теперь отслеживается git
через точечное исключение из общего Unity `*.csproj` ignore. Поэтому `test-dotnet.sh` работает на
clean clone, а не зависит от случайно оставшегося локального generated project.

Tailoring применяется только к создаваемому package-owned integration asmdef. External Jitter не
редактируется. Публичные `JitterRuntimeProfile`, `JitterPhysicsWorldBuilder` и server startup API
до Setup физически находятся в скрытой папке и Unity их не импортирует.

Native UPM sample остаётся отдельным imported consumer и в baseline использует transitive Jitter
edge. Его direct-reference/no-Jitter readiness migration относится к `JMP-E05`; соответствующий
subtask и epic acceptance E03 пока не закрываются досрочно.

## Real/f32 policy

- Lock обязан объявлять `precision = f32`; другое значение блокирует Setup/bake до записи.
- Unity C#9 source использует точный локальный `using Real = System.Single`.
- .NET project/projection использует один MSBuild alias и symbol
  `DATASAKURA_SERVER_GLOBAL_REAL`, исключающий двойное объявление.
- `JitterRuntimeProfile.VerifyCanonicalF32` проверяет `Precision.IsDoublePrecision == false`,
  scalar fields `JVector/JQuaternion == System.Single`, размеры 12/16 bytes.
- World builder и server startup выполняют preflight до artifact provider load и до world mutation.
- f64/layout mismatch возвращает typed `IncompatibleRuntime`, а не warning/fallback.
- `double` разрешён для telemetry milliseconds; он не входит в artifact/topology identity.

## Projection identity

Projection manifest schema повышена до 3 и теперь фиксирует:

- Jitter source content hash;
- compile profile ID;
- precision;
- integration API version;
- exact Jitter DLL SHA-256;
- hashes всех projected files.

## Regression status

| Gate | Результат |
| --- | --- |
| `git diff --check` | PASS |
| Package metadata/LFS | PASS |
| Jitter source/profile/binary lock | PASS: 96 files, 1 canonical patch, 3 artifacts |
| Lock invariant/negative tests | PASS, включая tampered server DLL |
| Portable/server suite | PASS: 99/99 |
| Editor, Editor.Tests, Runtime.Tests compile | PASS: 0 warnings, 0 errors |
| Unity EditMode/PlayMode | BLOCKED: batch завис до import/tests на Licensing Client, fresh XML не создан |
| IL2CPP | NOT RUN |

Portable suite включает positive f32 preflight, negative f64/scalar/layout fixtures и exact alias
policy. Unity batch log остановился после `Failed to connect to LicenseClient`/запуска отдельного
Licensing Client; сохранённые XML от 2026-08-28 не использовались. Были остановлены только три
запущенных этим regression batch PID, пользовательский Editor и Unity Hub не затрагивались.
