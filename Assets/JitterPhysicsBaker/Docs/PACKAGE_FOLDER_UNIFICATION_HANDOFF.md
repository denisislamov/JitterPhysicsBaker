# Унификация папок DataSakura — handoff Jitter Physics Baker 0.0.3

Дата: 2026-08-25

Source repository: `/Users/denisislamov/WorkProjects/Unity/PET/JitterPhysicsBaker`

Исходная ревизия: `d8a8058` (`feat(package): release importable samples`)

## Scope и статус

Этот этап обновляет source-of-truth пакета `com.datasakura.jitter-physics-baker` и готовит
публикацию `0.0.3`. Идентификатор пакета, public namespaces, assembly names, artifact schema,
Jitter2 boundary и server projection contracts не изменены.

Custom Navigation source и EFT consumer не изменялись в этом репозитории: в них уже были
чужие незакоммиченные изменения миграции. На момент аудита Custom Navigation source имел
manifest `0.6.6`, `DataSakura Custom Navigation`, но HEAD оставался `2dcf258` (`0.6.4`) с
dirty worktree. EFT consumer уже содержал новые native sample roots `0.6.6` и `0.0.3`.
Эти изменения сохранены без перезаписи.

Полный EFT server/two-client E2E этим package-source прогоном не подтверждён. Статус всей
двухпакетной consumer-миграции остаётся partial до отдельного чистого commit и строки
`[EPIC-5] PASS` в EFT repository.

## Before / after

| Назначение | До 0.0.3 | После 0.0.3 |
| --- | --- | --- |
| Integration | `Assets/DataSakura/JitterPhysics/Integration` | `Assets/DataSakura/JitterPhysicsBaker/Integration` |
| Receipt | `Assets/DataSakura/JitterPhysics/InstallationReceipt.json` | `Assets/DataSakura/JitterPhysicsBaker/InstallationReceipt.json` |
| Setup sample copy | `Assets/DataSakura/JitterPhysics/Samples` | удалён после безопасной миграции |
| Native sample | `Samples~/Demos` | `Samples~/Demos` (без изменения source path) |
| Imported sample | мог существовать второй Setup copy | `Assets/Samples/DataSakura Jitter Physics Baker/0.0.3/Physics Baking Demos` |
| Fallback Jitter2 | `Assets/DataSakura/ThirdParty/Jitter2` | без изменения |

```text
Assets/DataSakura/
├── JitterPhysicsBaker/
│   ├── Integration/
│   └── InstallationReceipt.json
└── ThirdParty/
    └── Jitter2/              # только package-owned fallback

Assets/Samples/
└── DataSakura Jitter Physics Baker/
    └── 0.0.3/
        └── Physics Baking Demos/
```

## Поведение установки и upgrade

- Fresh install: Setup устанавливает только Jitter2 prerequisite (если совместимого
  project-owned `Jitter2.Core` нет) и integration adapter. Samples импортируются штатной
  кнопкой Unity Package Manager.
- `InstallSamples()` оставлен как obsolete compatibility API, но ничего не записывает и
  возвращает типизированную ошибку с инструкцией native UPM import.
- Upgrade: кнопка `Migrate pre-0.0.3 layout` является отдельной явной мутацией. До перемещения
  проверяются receipt hashes, отсутствие destination conflict и отсутствие неучтённых файлов.
- Integration перемещается через `AssetDatabase.MoveAsset`, поэтому Unity GUID сохраняются.
- Старый sample root удаляется только если все файлы receipt-owned, не изменены и в каталоге
  нет неучтённых файлов. При конфликте операция отказывается до мутации и выводит пути.
- Повторный запуск после успешной миграции не меняет проект: legacy receipt отсутствует.
- Jitter2 в `Assets/DataSakura/ThirdParty/Jitter2` не перемещается и не дублируется.

## Сохранённые контракты

- package id: `com.datasakura.jitter-physics-baker`;
- display name: `DataSakura Jitter Physics Baker`;
- public namespaces и assembly names;
- `ArtifactSchemaVersion = 1` и canonical binary layout;
- `runtimeCompatibilityId` inputs и Jitter2 source hash;
- server bundle/level ids и networking contracts не затрагивались;
- POC content bytes не пересобирались, поэтому folder migration сама по себе не меняет
  physics/navigation payload, compatibility id или `contentId`.

## Изменённые области

- `package.json`, `JitterPhysicsPackage.PackageVersion`, CHANGELOG и guides;
- `JitterPhysicsArtifactPaths`, installer constants и receipt path;
- explicit legacy migration и Setup UI;
- удалён Setup-only `Samples~/UnityAssemblyTemplate`;
- package layout/receipt tests обновлены на native-only sample flow и новые roots.

## Фактические проверки

Из корня JitterPhysicsBaker:

```text
python3 tools/verify-package-meta.py
  PASS: complete .meta files, no Git LFS pointers

python3 Packages/com.datasakura.jitter-physics-baker/tools~/verify-jitter2-lock.py
  PASS: sha256:d67ac0c421687ec7308501bf4b8bcba9c33bed7845a0bfe64d4675b2326cce85, 96 files

python3 Packages/com.datasakura.jitter-physics-baker/tools~/test-jitter2-lock.py
  PASS: all checks passed

bash Packages/com.datasakura.jitter-physics-baker/tools~/test-dotnet.sh
  PASS: 73/73

dotnet build DataSakura.JitterPhysics.Editor.csproj --no-restore
dotnet build DataSakura.JitterPhysics.Editor.Tests.csproj --no-restore
dotnet build DataSakura.JitterPhysics.Tests.csproj --no-restore
  PASS: 0 warnings, 0 errors

bash tools/run-unity-tests.sh all
  EditMode PASS: 76/76
  PlayMode PASS: 52/52
```

Первый sandbox-запуск `.NET` testhost был заблокирован локальным socket, а первый Unity
batchmode — readonly licensing/cache database. Оба были повторены вне sandbox и прошли;
XML лежат в `Logs/TestResults/EditMode.xml` и `Logs/TestResults/PlayMode.xml`.

## Что ещё проверить в EFT

После фиксации уже существующих consumer/custom-navigation изменений:

```bash
dotnet restore EFT.Server/EFT.Server.sln
dotnet build EFT.Server/EFT.Server.sln -c Release
dotnet run --project EFT.Server/EFT.Runtime.Tests -c Release
EFT_EPIC5_SKIP_RESTORE=1 EFT.Server/Scripts/verify-epic5.sh \
  EFT.Unity/Builds/EPIC5/EFT-Epic5.app
```

Acceptance всей миграции — `[EPIC-5] PASS`, один `Jitter2.Core`, native imports обоих
samples и совпадение physics/navigation SHA-256, compatibility id и `contentId` до/после.
