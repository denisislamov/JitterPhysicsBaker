# JMP-E00. Baseline и evidence прототипов

Дата снятия: 2026-09-01. Все факты ниже относятся к локальному checkout
`/Users/denisislamov/WorkProjects/Unity/PET/JitterPhysicsBaker`.

## 1. Git baseline

- Рабочая ветка: `d.islamov/jmp-e00-baseline-adrs`.
- HEAD: `b8f622a5a9eb95af7e10000fbfcd4e9d83ebc5fe`.
- Ветка создана от локальной `feat/d.islamov/jitter_physics_baker_ux`.
- Remote `origin/feat/d.islamov/jitter_physics_baker_ux` на момент проверки:
  `d8a8058783ec5f8a219b813ccb12e40b43c4ab40`.
- Новая ветка на remote отсутствовала.
- Следовательно, текущая работа намеренно не содержит более новый remote commit; merge/rebase
  без решения пользователя не выполнялся.

Unrelated untracked baseline, который нельзя включать в scoped commit:

- `JITTER_PHYSICS_BAKER_JUNIOR_CODE_GUIDE.md` и `.meta`;
- `JITTER_PHYSICS_BAKER_USER_FRIENDLY_PACKAGE_PROPOSALS.md` и `.meta`;
- orphan `JMP_P01_CANONICAL_JITTER2_DISTRIBUTION_GUIDE.md.meta`.

Task-owned документы, уже существовавшие до кода эпика:

- `JITTER_MATH_PRECISION_MIGRATION_DECOMPOSITION.md` и `.meta`;
- `JMP_P00_EXECUTABLE_SOURCE_AUDIT_GUIDE.md` и `.meta`.

## 2. Release identity baseline

| Поле | Значение |
|---|---|
| `package.json` | `0.0.12` |
| `JitterPhysicsPackage.PackageVersion` | `0.0.12` |
| latest changelog section | `0.0.12`, 2026-08-31; `Unreleased` пуст |
| artifact schema | `1` |
| Jitter upstream commit | `c15bc6abfdda90a936975979a42f7a54a211084e` |
| patch set | `unity-netstandard21-v1` |
| precision | `f32` |
| target | `netstandard2.1` |
| intrinsics | `scalar-shim` |
| Jitter source hash | `sha256:d67ac0c421687ec7308501bf4b8bcba9c33bed7845a0bfe64d4675b2326cce85` |
| compile profile id | `9e724df81fb24d55e6136d35174c721457231606bd602464dbc35b017da73643` |
| runtime compatibility id | `ca8283611d3221120e69e23c4c028720537de4867f1de53df3752db85cd32006` |
| `Jitter2.Core.dll` SHA-256 | `1d668cd1fd8a9d0b2293b543e1932d57bf1da5d6d7c474a5bc78a8a793124499` |
| `Unsafe.dll` SHA-256 | `01748200f2400c742aa689f1f5101bd6298efdfd92c00c18f4fa473847235ba9` |

## 3. Assembly graph baseline

Always-compiled package graph не содержит `Jitter2.Core`:

```text
Contracts
└─ no package references
ArtifactCodec
└─ Contracts
UnityArtifact
├─ Contracts
└─ ArtifactCodec
Authoring
├─ Contracts
└─ UnityArtifact
Editor [Editor only]
├─ Contracts
├─ ArtifactCodec
├─ UnityArtifact
└─ Authoring
```

Dormant/installable integration template ссылается на:

- `DataSakura.JitterPhysics.Contracts`;
- `DataSakura.JitterPhysics.ArtifactCodec`;
- `Jitter2.Core` для source-based distribution.

Для precompiled plugin installer удаляет только Jitter asmdef reference из сгенерированного
integration asmdef; external DLL не меняется.

Server test project имеет explicit `Reference Include="Jitter2.Core"` с HintPath
`../../Jitter2~/Prebuilt/Jitter2.Core.dll`. Loaded copy в test output совпала с prebuilt по
SHA-256.

Source/precompiled candidates baseline:

- canonical source: `Jitter2~/Runtime`, скрыт от Unity;
- canonical prebuilt: `Jitter2~/Prebuilt/Jitter2.Core.dll`;
- build output и obj copies под `Jitter2~/StandaloneUnity`, не package delivery candidates;
- server testhost copy под `Server~/Tests/bin`, generated output;
- `Assets/` не содержит установленной `Jitter2.Core.dll` до Setup.

## 4. JMP-P00 — executable source audit

Добавлены read-only tool, policy и unit tests:

- `tools~/audit-jitter-math.py`;
- `tools~/jitter-math-audit-policy.json`;
- `tools~/test-jitter-math-audit.py`.

Inventory command:

```sh
python3 "Packages/com.datasakura.jitter-physics-baker/tools~/audit-jitter-math.py" \
  inventory \
  --policy "Packages/com.datasakura.jitter-physics-baker/tools~/jitter-math-audit-policy.json" \
  --json-report <evidence>/inventory.json \
  --markdown-report <evidence>/inventory.md
```

Baseline:

| Metric | Count |
|---|---:|
| files scanned | 99 |
| raw candidates | 776 |
| raw candidates in comments/strings/chars | 22 |
| code findings | 1,583 |
| must migrate | 262 |
| allowed | 764 |
| legacy/test fixture | 557 |
| ambiguous | 0 |

Rule counts:

| Rule | Count |
|---|---:|
| `PhysicsVector3` | 141 |
| `PhysicsQuaternion` | 87 |
| Unity math types | 300 |
| `Mathf` | 42 |
| selected `System.Math` | 7 |
| f32 scalar declarations | 166 |
| f64 scalar declarations | 11 |
| explicit f/d literals | 829 |

Findings hash:
`sha256:565d1fb388a7b098a8e2b75b81321e9bf873b922eead6961882afd02efe8370f`.

Classifier categories: simulation, serialization, telemetry, Unity boundary, test fixture.
Vendor snapshot исключён из owned migration inventory и отдельно идентифицирован policy root.
Каждый finding содержит stable id, repository-relative path, symbol, line/column для display,
reason, owner, impact, disposition и planned epic. Inline suppression markers сами являются
ошибкой.

Unit tests: `9/9 PASS`. Они покрывают comments, ordinary/verbatim/raw strings, char literals,
unterminated input, forbidden contract math, deterministic report, invalid policy path и новый
finding, меняющий reviewed hash, strict unknown policy field и изменение reviewed
classification. Baseline `inventory` завершился exit `0`; `check` намеренно
остаётся красным, пока 262 migration-debt findings не устранены последующими эпиками.

## 5. JMP-P01 — clean import и separately installed graph

Принята модель из `JMP-ADR-001`. Static graph доказывает clean-import boundary, а текущий root
является состоянием без установленного Jitter в `Assets`.

Fresh disposable Unity consumer и реальные installer negative cases не завершены. Команда
`bash tools/run-unity-tests.sh all` дважды не дошла до test execution:

- sandbox attempt: readonly database и licensing initialization failure;
- permitted attempt: `HandshakeResponse` code `505`,
  `Unsupported protocol version '1.18.1'`, затем reconnect loop;
- runner был остановлен после отсутствия нового XML; exit `130` относится к остановке
  зависшего процесса, не к тестам.

Старые XML от 2026-08-28 не используются. `JMP-P01` status: `BLOCKED`.

## 6. JMP-P02 — StableMath feasibility

Current snapshot:

- тип `Jitter2.LinearMath.StableMath` — `internal`;
- supported internal entry points: `SinCos`, `Sin`, `Cos`, `Atan2`, `Acos`, `Asin`;
- `Acos`/`Asin` вызывают `MathR.Sqrt`; это migration debt;
- отсутствуют required public `Abs`, `Min`, `Max`, `Clamp`, `Clamp01`, deterministic `Sqrt`,
  `Lerp`, rounding и quantization.

Target determinism contract принят:

- production `Real=f32`;
- никакого вызова platform `Math/MathF/libm` в обязательных deterministic methods;
- canonical quiet NaN — `0x7fc00000` на public boundary;
- infinity вне domain trig/sqrt даёт canonical NaN;
- `Abs(-0)` и artifact/quantization output канонизируются в `+0`;
- rounding — midpoint-to-even;
- clamp/quantization явно валидируют bounds/step;
- допустимые errors задаются per-method golden fixtures, а не общим epsilon;
- `Sqrt` должен иметь owned bit-defined f32 implementation; текущий `MathR.Sqrt` нельзя
  считать финальным.

.NET 10 characterization зафиксировал 19 bit fixtures, включая `-0`, subnormal,
quadrant boundaries, gameplay magnitude, NaN и inverse-trig boundaries. Например:

- `Sin(3fc90fdb) = 3f800000`;
- `Sin(40490fdb) = 80000000`;
- `Sin(00000001) = 00000001`;
- `Atan2(bf800000,bf800000) = c016cbe4`;
- `Acos(00000000) = 3fc90fdb`.

Unity Editor и IL2CPP evidence не получены из-за licensing blocker. Status:
`.NET feasibility PASS`, cross-runtime completion `BLOCKED`.

## 7. JMP-P03 — artifact byte compatibility

Независимый schema-1 golden test уже строит expected bytes field-by-field. Новый prototype
сравнил legacy DTO и Jitter f32 component streams:

- `PhysicsVector3` ↔ `JVector`: equal;
- `PhysicsQuaternion` ↔ `JQuaternion`: equal.

Minimal box fixture:

- length: 165 bytes;
- SHA-256: `b53cf221453ce313ae3e2d9ff3e94b665b65a674a0f1f5e9863acb5b33835479`;
- manifest schema: `1`;
- generator: `0.0.12`;
- body/shape: `1/1`;
- tick rate: `30`.

Решение: schema 1 retain условно принято при полном equality после реализации. Новый Jitter
source hash меняет runtime id и требует re-bake. См. `JMP-ADR-002`.

Полный new production codec в E00 не создавался; это запрещённая преждевременная реализация.
Unity `.physics.asset` и repeat-bake evidence блокированы лицензией.

## 8. JMP-P04 — precision/layout/parity

`JMPE00MigrationPrototypeTests` автоматически доказал на .NET 10:

- `Precision.IsDoublePrecision == false`;
- `sizeof(Real) == 4` через фактические signatures/fields f32 assembly;
- `Marshal.SizeOf<JVector>() == 12`, offsets `0/4/8`;
- `Marshal.SizeOf<JQuaternion>() == 16`, offsets `0/4/8/12`;
- server loaded DLL bytes равны canonical prebuilt bytes;
- tampered DLL имеет другой SHA-256;
- f64 runtime profile получает другой runtime id;
- f64/current mismatch возвращает `IncompatibleRuntime` до world construction.

Duplicate/unowned Unity fixture и equality именно установленного Unity plugin остаются
`BLOCKED`, потому что Setup не запустился. Наличие prebuilt в UPM не подменяет installed-plugin
evidence.

## 9. Baseline command results

| Command | Result |
|---|---|
| `python3 tools/verify-package-meta.py` | exit 0, complete meta, no LFS pointers |
| `python3 tools~/verify-jitter2-lock.py` | exit 0, 96 included files, expected hash |
| `python3 tools~/test-jitter2-lock.py` | exit 0, all checks passed |
| `bash tools~/test-dotnet.sh` | exit 0, 78/78 |
| filtered `JMPE00MigrationPrototypeTests` | exit 0, 7/7 |
| `bash tools/run-unity-tests.sh all` | BLOCKED before tests; no fresh XML |

После всех task-owned изменений полный `tools~/test-dotnet.sh` повторён: exit `0`, `85/85`.
Offline builds `DataSakura.JitterPhysics.Editor.csproj`,
`DataSakura.JitterPhysics.Editor.Tests.csproj` и `DataSakura.JitterPhysics.Tests.csproj`
завершились с `0 warnings / 0 errors`. `git diff --check` завершился exit `0`.

## 10. Epic status

Архитектурные решения приняты, P00 и portable части P02–P04 реализованы. Epic нельзя отметить
полностью завершённым: P01, Unity portion P02/P04, EditMode, PlayMode, isolated consumer и
repeat-bake остаются blocked одной внешней причиной — несовместимость Unity Licensing Client
protocol. Следующий эпик начинать нельзя до команды пользователя и закрытия/явного изменения
этого gate.
