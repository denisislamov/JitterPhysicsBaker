# JMP-E01 — evidence канонического Jitter runtime

Дата фиксации: 2026-09-01.

Ветка: `d.islamov/jmp-e01-canonical-jitter-runtime`.

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
  `14051e8d9217ac0c6201ba90d9f50c287792c190b10b1aa99d25bc1b27bc3ae0`.
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
  `sha256:c4e11cddbf7b0263bfda638def71c0070ce83e087bb210cec2074ac2c7a82212`;
- `compileProfileId`:
  `a2925211b983330117414426be9bf8a2798ce9169c1206e1e55178f708cfa72e`.
- `buildInputHash` (profile + Runtime + Compat + canonical csproj):
  `sha256:7d3231824931f38cb8a4873b7ce5b9d2af6542f7e0973e10a8a9e0866e99c7f3`.

`build-jitter2-unity.sh` выполняет два изолированных clean build и принимает только byte-identical
результат. Зафиксированные hashes:

| Artifact | SHA-256 |
| --- | --- |
| `Jitter2.Core.dll` | `a87e1ae1f1475f0e35db8defd54a76c529b088716f2b72397fe849c05cecefee` |
| `Jitter2.Core.xml` | `223aa43183ab78484c0800b84ff7fa483943e693959fb3f592153e9ea2dadc28` |
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
| Portable/server tests | `bash Packages/com.datasakura.jitter-physics-baker/tools~/test-dotnet.sh` | PASS: 86/86 |
| Editor compile | `dotnet build DataSakura.JitterPhysics.Editor.csproj ...` | PASS: 0 warnings, 0 errors |
| Editor tests compile | `dotnet build DataSakura.JitterPhysics.Editor.Tests.csproj ...` | PASS: 0 warnings, 0 errors |
| Runtime tests compile | `dotnet build DataSakura.JitterPhysics.Tests.csproj ...` | PASS: 0 warnings, 0 errors |
| Package metadata | `python3 tools/verify-package-meta.py` | PASS: complete `.meta`, no LFS pointers |
| Unity EditMode/PlayMode | `bash tools/run-unity-tests.sh all` | BLOCKED: project is open in Editor; no fresh XML |

Первый sandboxed `.NET` test запуск был aborted, потому что VSTest не получил loopback socket
(`SocketException (13): Permission denied`). Повтор той же команды вне sandbox прошёл 86/86;
это ограничение среды запуска, не test failure.

Unity batch runner отказался запускать второй Editor над заблокированным открытым проектом.
Попытка использовать Test Runner уже открытого Editor была остановлена до любых UI-действий:
Mac locked, automatic unlock unavailable. Имеющиеся `Logs/TestResults/EditMode.xml` и
`PlayMode.xml` датированы 2026-08-28 и не считаются evidence этого эпика. Поэтому Unity EditMode,
PlayMode, clean-import и фактический Setup остаются `BLOCKED/NOT RUN`, а не объявляются зелёными.

## Acceptance status

- Clean import без Jitter: сохранён архитектурно; обязательной dependency и import-time write не
  добавлено. Фактический Unity gate фиксируется отдельно ниже после final regression.
- Separate Setup устанавливает одну проверенную `Jitter2.Core`: реализовано и покрыто lock/compile
  checks; фактический Unity Setup gate зависит от доступности Editor license.
- Unity и server DLL hashes равны: обе materialization path читают один и тот же lock-verified
  `Jitter2~/Prebuilt/Jitter2.Core.dll`; server manifest фиксирует тот же hash.

Эпик не делает `StableMath` public и не меняет его numeric behavior — это scope `JMP-E02`.
