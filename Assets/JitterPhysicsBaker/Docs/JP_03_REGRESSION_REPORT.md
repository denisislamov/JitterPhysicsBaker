# JP-03 — отчёт реализации и регрессии

Дата проверки: 2026-08-27

Ветка: `feat/d.islamov/jitter_physics_baker_ux`

## Реализованный объём

- **JP-03.1:** добавлен штатный Scene View Overlay `Jitter Physics` с независимыми слоями
  `Sources`, `Baked`, `Runtime`, выбором scope, режимами `Visible`/`X-Ray`, переходом в
  Preferences и `Frame Level`.
- Старый ключ `DataSakura.JitterPhysics.Editor.ShowBakedGeometryOverlay` сохранён как
  единственное personal-состояние слоя Baked. Отдельный toggle удалён из окна и Preferences.
- **JP-03.2:** применена muted-палитра эпика; слои дополнительно отличаются пунктиром,
  заливкой, толщиной, маркерами, штриховкой и двойным error-контуром.
- **JP-03.3:** сохранённый bake сравнивается с текущими collider records. Перемещённые и
  удалённые формы остаются видимыми и получают `Moved`/`Removed`; artifact decode и collider
  conversion кешируются вне Scene View `Repaint`.
- Runtime отображается только из активного `IJitterPhysicsRuntimePreviewSource`. Контракт
  находится в переносимой Contracts assembly и не ссылается на Jitter2. При отсутствии
  provider Overlay показывает `No runtime data` и не подменяет runtime Unity Colliders.

## Автоматическая регрессия

| Проверка | Результат |
| --- | --- |
| `python3 tools/verify-package-meta.py` | PASS |
| `verify-jitter2-lock.py` | PASS, 96 файлов, SHA-256 `d67ac0c421687ec7308501bf4b8bcba9c33bed7845a0bfe64d4675b2326cce85` |
| `test-jitter2-lock.py` | PASS |
| `test-dotnet.sh` | PASS, 76/76 |
| Editor assembly build | PASS, 0 warnings, 0 errors |
| Editor Tests assembly build | PASS, 0 warnings, 0 errors |
| Runtime Tests assembly build | PASS, 0 warnings, 0 errors |
| Sample runtime-provider standalone compile | PASS; ожидаемое предупреждение о Unity `SerializeField` |
| Unity EditMode/PlayMode | NOT RUN: batchmode остановился на инициализации Licensing Client, новый XML не создан |

## Ручная проверка Editor

- Полный сценарий добавлен в `MANUAL_TEST_PLAN.md` как MT-30.
- Совместный просмотр с navigation и Unity Collider gizmos на светлой, тёмной и
  текстурированной сцене: **НЕ ВЫПОЛНЕН**.
- Две Scene View, большая сцена, reload/close cleanup и Profiler-проверка отсутствия полного
  bake/hash на `Repaint`: **НЕ ВЫПОЛНЕНЫ**.
- Документ является checklist, а не свидетельством прохождения этих ручных пунктов.

## Граница поставки

- Для публикации JP-03 версия пакета поднята с `0.0.7` до `0.0.8`.
- Формат артефакта и `ArtifactSchemaVersion` не изменены.
- Пакет по-прежнему импортируется без Jitter2, Custom Navigation, NPI и EFT.
- Релиз поставляет реализацию JP-03 и автоматические проверки, но не объявляет ручную
  визуальную регрессию завершённой.
- Пользовательские незакоммиченные изменения вне JP-03 сохранены.
