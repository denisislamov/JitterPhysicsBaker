# JP-01 — отчёт реализации и регрессии

Дата: 27 августа 2026.

## Срез

- Исходный commit: `7591ba1 feat(package): unify project-owned folders`.
- Ветка: `feat/d.islamov/jitter_physics_baker_ux`.
- Задачи: `JP-01.1`, `JP-01.2`, `JP-01.3`.
- Package version: `0.0.4`; опубликована в standalone package repository.
- Artifact schema: `1`, не изменена.
- Source revision: `f0bd412f93870ebf307782dcc6d67f128169fdb9`.
- Standalone package revision и tag `v0.0.4`:
  `7a9e2177f7aeb01c616e72aa68dc5987d16a9a55`.
- Install URL: `https://github.com/denisislamov/jitter-physics-baker.git#v0.0.4`.

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
3. в NPI записать принятую пару Custom Navigation revision + Jitter revision и повторить
   consumer import/compile/menu/old-profile/no-dirty проверки.

Standalone package `main` и tag `v0.0.4` опубликованы и независимо сверены через remote refs.
Source branch монорепозитория не отправлялась. Custom Navigation и EFT в этом срезе не
изменялись; NPI update не выполнялся.

## Follow-up по скриншотам — `0.0.5`

После проверки `0.0.4` в общем Unity-проекте интерфейс дополнительно выровнен с Custom
Navigation:

- dock-tab переименован из `Jitter Physics` в `DS Jitter Physics`; параллельный пакет
  использует `DS Navigation`;
- `Tools > DataSakura > Jitter Physics` теперь один leaf-item, который сразу открывает окно;
- прежний отдельный `Show Baked Geometry Overlay` удалён из Tools, а его состояние и функция
  сохранены как явный toggle в Diagnostics;
- demo/sample команды перенесены из Tools в `Assets > DataSakura > Jitter Physics`;
- пять разделов всегда рисуются горизонтальным toolbar, включая узкую dock-панель; `Section`
  popup больше не используется;
- удаление выбранного physics artifact по-прежнему находится в Diagnostics, показывает
  точный список `.asset`, `.bytes` и `.json` и выполняется только после подтверждения.

### Регрессия follow-up

| Проверка | Результат |
|---|---|
| Editor csproj build | PASS — 0 warnings, 0 errors |
| Editor.Tests csproj build | PASS — 0 warnings, 0 errors |
| Runtime Tests csproj build | PASS — 0 warnings, 0 errors |
| package `.meta` / LFS | PASS |
| Jitter2 lock + lock tests | PASS — hash `d67ac0c...cce85`, 96 files |
| portable `.NET` | PASS — 73/73 |
| Unity EditMode | PASS — 78/78 |
| Unity PlayMode | PASS — 52/52 |
| Live Tools menu | PASS — один `Jitter Physics` entry |
| Live dock title | PASS — `DS Jitter Physics` |
| Live narrow tabs | PASS — пять горизонтальных tabs, без popup |
| Live Diagnostics overlay toggle | PASS — выключен и повторно включён |

Первый повторный EditMode запуск не дошёл до Test Framework из-за несовместимого зависшего
Unity Licensing Client (`Unsupported protocol version '1.18.1'`). После закрытия Unity Hub и
перезапуска versioned helper тот же checkout завершил EditMode 78/78; XML обновлён. Это
диагностика окружения, а не test failure пакета.

Версия `0.0.5` опубликована и независимо проверена:

- source commit: `31135a88c951309197d3442d88128b72c2b233da`;
- standalone `main` и tag `v0.0.5`:
  `9ed357d30e5fc749bebfa034dc576913d07156d7`;
- install URL: `https://github.com/denisislamov/jitter-physics-baker.git#v0.0.5`.

## Bake delivery follow-up — `0.0.6`

По Navigation-образцу вкладка Bake стала компактным контуром доставки:

- `Build for Client`, `Upload to Server`, `Export to Folder` используют один уже
  существующий детерминированный bake/export pipeline;
- текущий client artifact показан отдельной строкой;
- `Remove baked physics` перечисляет в confirmation точные `.artifact.asset`,
  `.jphys.bytes` и `.manifest.json`; удаление очищает last hash уровня, но не затрагивает
  экспортированные или загруженные серверные копии;
- world profile и локальные server preferences перенесены в Settings; token хранится только
  в `EditorPrefs`;
- sample WebViewer получил `POST /api/artifacts`: payload, manifest, hash, runtime id и
  канонические имена проверяются до атомарной записи. Живой world не заменяется, ответ явно
  требует restart.

### Регрессия `0.0.6`

| Проверка | Результат |
|---|---|
| package `.meta` / LFS | PASS |
| Jitter2 lock + lock tests | PASS — hash `d67ac0c...cce85`, 96 files |
| portable `.NET` | PASS — 76/76 |
| Editor csproj | PASS — 0 warnings, 0 errors |
| Editor.Tests csproj | PASS — 0 warnings, 0 errors |
| Runtime Tests csproj | PASS — 0 warnings, 0 errors |
| WebViewer build | PASS — 0 warnings, 0 errors |
| Unity EditMode | PASS — 78/78 |
| Unity PlayMode | PASS — 55/55 |
| HTTP upload E2E | PASS — `demo_arena`, HTTP 200, hash совпал, `restartRequired: true` |
| Обычный Editor import/compile | PASS — Unity `6000.3.19f1`, compile errors отсутствуют |
| Интерактивный dialog/UI проход | NOT RUN — два Unity процесса нельзя было адресовать раздельно через accessibility; пользовательский EFT Editor не закрывался |

Первый sandbox `.NET` run был остановлен запретом loopback socket; разрешённый повтор прошёл
76/76. Первый sandbox Unity run не подключился к Licensing Client; разрешённые повторы
дважды прошли 78/78 и 55/55. Автоматические URP/SceneTemplate/Generated.meta изменения
после запуска редактора адресно убраны, исходные пользовательские `URP.png` и junior guide
не затронуты.

Source feature commit: `407244e`. Публикация standalone `v0.0.6` не выполнялась: внешний
push был остановлен средой до запуска и требует отдельного подтверждения конкретного GitHub
назначения `https://github.com/denisislamov/jitter-physics-baker.git`.
