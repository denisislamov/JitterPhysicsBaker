# JP-01 — отчёт реализации и регрессии

Дата: 27 августа 2026.

## Срез

- Исходный commit: `7591ba1 feat(package): unify project-owned folders`.
- Ветка: `feat/d.islamov/jitter_physics_baker_ux`.
- Задачи: `JP-01.1`, `JP-01.2`, `JP-01.3`.
- Package version: `0.0.4`; подготовлена к release, публикация фиксируется ниже после push.
- Artifact schema: `1`, не изменена.
- Итоговая revision отсутствует: изменения пока не закоммичены.

Пользовательские изменения `Assets/TutorialInfo/Icons/URP.png` и
`JITTER_PHYSICS_BAKER_JUNIOR_CODE_GUIDE.md(.meta)` не относятся к JP-01 и не изменялись.

## Что изменено

### JP-01.1 — меню

- Основной вход: `Tools/DataSakura/Jitter Physics/Open`.
- Отдельные Tools-команды Bake, Validate, Export, Setup, About, Install и Server Projection
  убраны из меню; операции остались доступны в основном окне и через существующие программные
  entrypoints.
- Старый `Show Baked Geometry Overlay` сохранён до JP-03.
- Create Asset и Add Component сгруппированы под `DataSakura/Jitter Physics/...`.

### JP-01.2 — окно

- Разделы: `Overview / Geometry / Bake / Settings / Diagnostics`.
- При ширине меньше 520 px используется `Section` popup с теми же пятью разделами.
- Artifact verify/export и диагностические проверки объединены в Diagnostics.
- Compatibility, package info и переход к явному обслуживанию находятся в Settings.
- Migration, server projection и remove находятся в `Open installation details > Advanced`.
- Remove сохраняет диалог подтверждения и удаляет только receipt-owned неизменённые файлы.

### JP-01.3 — Inspector

- Добавлен `JitterPhysicsLevelEditor` с секциями Level, Geometry Root, Settings, Bake Status,
  кнопками `Validate / Bake / Open` и foldout `Advanced`.
- Поля рисуются через `SerializedObject`/`SerializedProperty`, поэтому Unity сохраняет Undo,
  prefab overrides и multi-object editing.
- Repaint не запускает validation, bake, поиск geometry или запись assets.
- Bake заблокирован в Play Mode; Validate остаётся доступным.

## Автоматическая регрессия

| Проверка | Результат | Evidence |
|---|---|---|
| `python3 tools/verify-package-meta.py` | PASS | complete `.meta`, no LFS pointers |
| `verify-jitter2-lock.py` | PASS | source hash `d67ac0c...cce85`, 96 files |
| `test-jitter2-lock.py` | PASS | all checks passed |
| `test-dotnet.sh` | PASS | 73/73, failed 0 |
| Editor csproj build | PASS | 0 warnings, 0 errors |
| Editor.Tests csproj build | PASS | 0 warnings, 0 errors |
| Runtime Tests csproj build | PASS | 0 warnings, 0 errors |
| Unity EditMode | PASS | 78/78, failed 0, `Logs/TestResults/EditMode.xml` |
| Unity PlayMode | PASS | 52/52, failed 0, `Logs/TestResults/PlayMode.xml` |
| `git diff --check` | PASS | нет whitespace errors |

Первый sandbox-запуск `.NET` suite был прерван запретом локального socket (`SocketException:
Permission denied`). Повтор вне sandbox завершился 73/73; это ограничение test runner, а не
ошибка пакета.

## Editor-регрессия

Проверено в отдельном Unity `6000.3.19f1` Editor, не затрагивая уже открытый EFT Editor.

| Сценарий | Результат |
|---|---|
| Import/compile без `Jitter2.Core`; Console без compile/runtime errors | PASS |
| Tools содержит один корень Jitter Physics с Open; legacy overlay сохранён | PASS |
| Create Asset и Add Component находятся в `DataSakura/Jitter Physics` | PASS |
| Широкое окно показывает пять tabs | PASS |
| Узкое окно показывает `Section` popup | PASS |
| Inspector содержит требуемые секции, кнопки и Advanced | PASS |
| Создать уровень → Undo component/object → сцена снова clean | PASS |
| Открытие окна/Inspector не создаёт assets и не делает clean scene dirty | PASS |
| Advanced содержит migration/server/remove | PASS |
| Remove открывает ownership-aware confirmation; выбран Cancel | PASS |
| Тёмная тема | PASS |
| Светлая тема | NOT RUN — настройка общая с активным пользовательским EFT Editor |
| Prefab override visual scenario | NOT RUN — отдельный prefab fixture не создавался |
| Успешный GUI bake/export | NOT RUN — standalone-проект не содержит совместимый `Jitter2.Core` |

Unity при обычном открытии обновил URP asset и создал два служебных файла. Эти побочные
изменения были адресно возвращены; финальный status содержит только JP-01 и исходные
пользовательские файлы.

## Payload и совместимость

- `Contracts`, `ArtifactCodec`, `UnityArtifact`, runtime loader, world builder, binary writer,
  schema и Jitter lock не менялись.
- Golden bytes, codec/runtime tests, EditMode bake tests и PlayMode tests прошли.
- Jitter source hash до и после: `d67ac0c421687ec7308501bf4b8bcba9c33bed7845a0bfe64d4675b2326cce85`.
- Изменений формата, migration payload names, runtime protocol или dependency graph нет.

## Ограничение закрытия и NPI-01

JP-01 реализован и автоматизированная регрессия пройдена, но release-level ручной план нельзя
считать полностью выполненным из-за строк `NOT RUN` выше. До передачи в NPI требуется:

1. выполнить светлую тему и prefab override scenario в свободном Editor;
2. выполнить успешный Validate/Bake/Verify/Binary Export/Embedded Export с одной совместимой
   project-owned `Jitter2.Core`;
3. создать отдельный commit/revision и только после этого выполнять `NPI-01` в EFT;
4. в NPI записать принятую пару Custom Navigation revision + Jitter revision и повторить
   consumer import/compile/menu/old-profile/no-dirty проверки.

Custom Navigation и EFT в этом срезе не изменялись. Push, package release и NPI update не
выполнялись.
