# JMP-E01 — evidence канонического Jitter runtime

Дата фиксации: 2026-09-01.

Общая migration-ветка: `d.islamov/jitter-math-precision-migration`.

Baseline: commit `83bc13c682b4dca93d2d47a715ab45a2a4885074` (`JMP-E00`).

## Результат эпика

Сохранён прежний пользовательский flow:

1. UPM-пакет импортируется без обязательной зависимости от Jitter и без записей в проект.
2. Package-owned `Jitter2.Core.dll` материализуется только явным действием Setup.
3. Jitter-dependent integration устанавливается отдельным явным действием после compatibility
   check.
4. Внешняя consumer-owned копия Jitter не копируется, не исправляется и не перезаписывается.
5. Server projection создаётся отдельной явной командой и теперь получает те же проверенные bytes
   `Jitter2.Core.dll`, что использует Unity Setup.

## Canonical source и consumer patch

- Upstream repository: `https://github.com/notgiven688/jitterphysics2`.
- Upstream commit: `c15bc6abfdda90a936975979a42f7a54a211084e` (tag `2.8.9`).
- Upstream-проверка показала, что на этом commit отсутствует
  `src/Jitter2/LinearMath/StableMath.cs`.
- Единственный consumer-only source patch явно записан в `consumerPatches` lock-файла:
  `LinearMath/StableMath.cs`, SHA-256
  `f5aedf4c135d61325170ab09ff95026db5e0f0e28c6b61ae5f197b576abb465d`.
- Sync сохраняет этот файл только после проверки hash, заменяет upstream snapshot, восстанавливает
  подтверждённый patch и заново применяет 19 netstandard2.1 call-site patches.
- Included set: `**/*.cs`, `**/csc.rsp`; excluded set lock-файла исключает metadata, asmdefs,
  tests, `bin` и `obj`.
- Verifier отклоняет generated/vendor/неожиданные файлы внутри `Jitter2~/Runtime`.

## Compile profile и binary identity

Lock schema повышена до `2`, `patchSetId` — до
`unity-netstandard21-stablemath-v2`.

Канонический compile profile фиксирует:

- `precision = f32`;
- `targetFramework = netstandard2.1`;
- `allowUnsafe = true`;
- `unityDefine = ""`;
- `intrinsicsProfile = scalar-shim`;
- `polyfillProfile = netstandard21`;
- `languageVersion = latest`;
- `deterministic = true`;
- `continuousIntegrationBuild = true`.

Идентификаторы после relock:

- `sourceContentHash`:
  `sha256:749c79e40c4965cd455ca80a2d1d1c80a24eb580eb7b721e07adc78b41c82762`;
- `compileProfileId`:
  `a2925211b983330117414426be9bf8a2798ce9169c1206e1e55178f708cfa72e`.
- `buildInputHash` (profile + Runtime + Compat + canonical csproj):
  `sha256:cceac9a4d53f454f5cb558db55295cc4770c89fe1c26e5c6219f7586f68fc555`.

`build-jitter2-unity.sh` выполняет два изолированных clean build и принимает только byte-identical
результат. Зафиксированные hashes:

| Artifact | SHA-256 |
| --- | --- |
| `Jitter2.Core.dll` | `944666bbe73dfce5ffc5bfb18569fb0004f50e767dcbb8b471dde15242023ca6` |
| `Jitter2.Core.xml` | `be7115c897ac58357cd8155dd1cb91bb6d35f5b31b72841102b43eec83877fa0` |
| `System.Runtime.CompilerServices.Unsafe.dll` 6.0.0 | `01748200f2400c742aa689f1f5101bd6298efdfd92c00c18f4fa473847235ba9` |

Текущая policy требует полного byte identity. Допустимых различий PE metadata нет: любое отличие
между двумя clean build завершает build ошибкой до обновления `Prebuilt`.

## Setup и server runtime

- Setup до любой записи проверяет все prebuilt artifacts по lock.
- Receipt продолжает фиксировать package ownership и hash каждого материализованного файла.
- Compatibility report требует, чтобы receipt-owned `Jitter2.Core.dll` совпадал и со своим
  receipt, и с текущим lock.
- Duplicate, incompatible и unowned Jitter по-прежнему блокируют fallback install.
- Server projection содержит `JitterRuntime/Jitter2.Core.dll`, XML docs, pinned Unsafe DLL и
  `JitterPhysics.Runtime.props` с direct references.
- `JitterPhysics.projection.json` schema 2 фиксирует source hash, Jitter DLL hash и hashes всех
  projected files; verifier сравнивает и содержимое manifest.
- При переданном `expectedJitterAssemblySha256` server startup хеширует фактически загруженный
  `Jitter2.Core.dll` до provider load и до mutation мира.
- Negative tests отклоняют изменённую DLL как в portable lock verifier, так и на server startup.
- Production projection не зависит от Unity `Library/PackageCache`.

## Выполненные проверки

| Gate | Команда | Результат |
| --- | --- | --- |
| Два clean Jitter build | `bash Packages/com.datasakura.jitter-physics-baker/tools~/build-jitter2-unity.sh` | PASS: все три artifact byte-identical |
| Source/profile/patch/binary lock | `python3 Packages/com.datasakura.jitter-physics-baker/tools~/verify-jitter2-lock.py` | PASS: 96 files, 1 consumer patch, 3 artifacts |
| Lock invariants, Compat drift и tamper negative | `python3 Packages/com.datasakura.jitter-physics-baker/tools~/test-jitter2-lock.py` | PASS |
| Portable/server tests | `bash Packages/com.datasakura.jitter-physics-baker/tools~/test-dotnet.sh` | PASS: 89/89 |
| Editor compile | `dotnet build DataSakura.JitterPhysics.Editor.csproj ...` | PASS: 0 warnings, 0 errors |
| Editor tests compile | `dotnet build DataSakura.JitterPhysics.Editor.Tests.csproj ...` | PASS: 0 warnings, 0 errors |
| Runtime tests compile | `dotnet build DataSakura.JitterPhysics.Tests.csproj ...` | PASS: 0 warnings, 0 errors |
| Package metadata | `python3 tools/verify-package-meta.py` | PASS: complete `.meta`, no LFS pointers |
| Unity EditMode/PlayMode | `bash tools/run-unity-tests.sh all` | PASS: EditMode 97/97; PlayMode 57/57 |
| Isolated separate-install delivery | `bash tools/verify-jp05-delivery.sh` | PASS: EditMode 7/7; PlayMode 1/1; exactly one Jitter DLL; external public StableMath compile |

Первый sandboxed `.NET` test запуск был aborted, потому что VSTest не получил loopback socket
(`SocketException (13): Permission denied`). Повтор той же команды вне sandbox прошёл 86/86;
это ограничение среды запуска, не test failure.

Финальная regression общей migration-ветки выполнена в отдельном worktree. Isolated delivery
сначала установил Jitter явным действием, затем integration, подтвердил ровно одну DLL и public
StableMath из внешней Unity assembly. После этого полный проект прошёл свежие EditMode и PlayMode
наборы без failures/skips.

## Acceptance status

- Clean import без Jitter: сохранён архитектурно; обязательной dependency и import-time write не
  добавлено. Фактический Unity gate фиксируется отдельно ниже после final regression.
- Separate Setup устанавливает одну проверенную `Jitter2.Core`: реализовано и покрыто lock/compile
  checks; фактический Unity Setup gate зависит от доступности Editor license.
- Unity и server DLL hashes равны: обе materialization path читают один и тот же lock-verified
  `Jitter2~/Prebuilt/Jitter2.Core.dll`; server manifest фиксирует тот же hash.

В общей migration-ветке поверх E01 добавлен canonical release prerequisite P00: `StableMath`
теперь является public API, а его numeric behavior и compatibility identities закреплены
отдельными release и regression tests.
