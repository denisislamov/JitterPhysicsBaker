# JP-02 — отчёт реализации и регрессии

Дата проверки: 2026-08-27

Ветка: `feat/d.islamov/jitter_physics_baker_ux`

Базовая ревизия: `68c0050`

Ревизия реализации: `d54ea81`

## Реализованный объём

- **JP-02.1:** добавлены нативные страницы `Project Settings/DataSakura/Jitter Physics` и `Preferences/DataSakura/Jitter Physics/Scene Preview`. Общие настройки проекта отделены от персональной настройки предпросмотра Scene View.
- **JP-02.2:** у профиля мира появились явные действия `Edit`, `New` и `Make Local Copy`. Локальная копия сохраняет все значения исходного профиля, назначается только выбранному уровню и поддерживает Undo и prefab override.
- **JP-02.3:** установка, удаление и сведения о совместимости Jitter2 перенесены в сворачиваемый блок `Advanced installation and maintenance`. Открытие окна и `Validate` не устанавливают и не удаляют файлы.
- Создание default-профиля выполняется только явными командами `Create Defaults` или `Create Level`. Простое открытие Settings не создаёт assets.
- Основной authoring UI не меняет формат артефакта, схему, runtime-контракты или правила владения внешним Jitter2.

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
| Unity EditMode | PASS, 83/83 |
| Unity PlayMode | PASS, 55/55 |

Новые EditMode-тесты проверяют уникальные provider paths/scopes и preference key, отсутствие неявного создания defaults, независимость personal preview от project settings, точное копирование профиля, изоляцию выбранного уровня, Undo и prefab override.

## Ручная проверка Editor

- Проект открыт отдельным Unity Editor и импортирован без ошибок компиляции: **PASS**.
- Интерактивная визуальная проверка обеих Settings-страниц и всех кнопок: **НЕ ВЫПОЛНЕНА**. Одновременно был открыт пользовательский EFT Unity Editor, а UI automation не могла адресовать два Unity-процесса раздельно. Пользовательский Editor не закрывался и не изменялся.
- Поведенческие сценарии страниц и кнопок покрыты реальными Unity EditMode-тестами; пошаговые визуальные сценарии добавлены в `MANUAL_TEST_PLAN.md` как MT-09B, MT-09C и MT-09D.

## Граница поставки

- Текущая опубликованная версия пакета остаётся `0.0.6`; изменения записаны в секции `[Unreleased]`.
- Публикация новой версии и интеграционная регрессия в EFT/NPI не выполнялись: для них требуется отдельная команда после принятия JP-02.
- Пользовательские незакоммиченные изменения вне JP-02 сохранены.
