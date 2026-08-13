# Jitter Physics Baker — план ручной проверки в Unity

Назначение: всё, что нельзя доказать вне редактора. Портируемая половина пакета
(`Contracts`, `ArtifactCodec`, провайдеры, world builder) прогоняется под `.NET` и в CI;
здесь остаётся то, что живёт в AssetDatabase, в компонентах сцены, в окнах редактора и в
Play Mode.

Документ написан так, чтобы по нему мог пройти человек или агент: каждый шаг — действие,
каждый результат — проверяемое утверждение. Если шаг нельзя выполнить механически, это
указано явно.

Отмечайте статус в колонке результата: `PASS`, `FAIL` (с описанием) или `SKIP` (с причиной).

## 0. Перед началом

| Шаг | Действие | Ожидаемо |
| --- | --- | --- |
| 0.1 | Закрыть Unity, если открыт | `Temp/UnityLockfile` отсутствует |
| 0.2 | `python3 tools/verify-package-meta.py` | `OK: complete .meta files, no Git LFS pointers.` |
| 0.3 | `python3 "Packages/com.datasakura.jitter-physics-baker/tools~/verify-jitter2-lock.py"` | `OK: sha256:...`, `included files: 96` |
| 0.4 | `python3 "Packages/com.datasakura.jitter-physics-baker/tools~/test-jitter2-lock.py"` | `all checks passed` |
| 0.5 | `bash "Packages/com.datasakura.jitter-physics-baker/tools~/test-dotnet.sh"` | `Failed: 0` |
| 0.6 | `bash tools/run-unity-tests.sh all` | EditMode и PlayMode: `failed=0` |

Шаг 0.6 — это и есть автоматический прогон EditMode/PlayMode. Всё, что ниже, проверяет
поведение, которое тестами не покрывается: диалоги, окна, реакция на действия человека.

## 1. Импорт и bootstrap

### MT-01. Чистый импорт без Jitter2

1. Открыть проект (в нём нет `Jitter2.Core`).
2. Дождаться окончания компиляции.

Ожидаемо: в консоли **нет** ошибок компиляции; все пять сборок пакета собраны
(`Tools > DataSakura > Jitter Physics > About` показывает `compiled` для Contracts,
ArtifactCodec, UnityArtifact, Authoring, Editor и `not present` для `Jitter2.Core`).

Это главный инвариант пакета: он импортируется в проект без физического движка.

### MT-02. Окно Setup читает и объясняет

1. `Tools > DataSakura > Jitter Physics > Setup`.

Ожидаемо: `Status: Missing`, `Baking allowed: no`, сообщение объясняет, что делать;
`Copy report JSON` кладёт в буфер валидный JSON; `Export report...` пишет файл.

## 2. Установка (installer и receipt)

### MT-03. Установка fallback Jitter2

1. В Setup нажать `Install Jitter2`.

Ожидаемо: предупреждение о том, что снапшот — непатченный upstream; файлы появились в
`Assets/DataSakura/ThirdParty/Jitter2/` вместе с `Jitter2.Core.asmdef`; создан
`Assets/DataSakura/JitterPhysics/InstallationReceipt.json`; после компиляции About
показывает `Jitter2.Core: compiled`.

Известное ограничение: снапшот не патчен под Unity (нет `JITTER_UNITY`, используются
аппаратные интринсики). Если компиляция падает — это **ожидаемый** результат текущего
релиза, зафиксируйте его как `SKIP` с текстом ошибки и переходите к MT-04 только если
`Jitter2.Core` собрался.

### MT-04. Внешний Jitter2 не трогают

1. При установленном (или своём) `Jitter2.Core` нажать `Install Jitter2` ещё раз.

Ожидаемо: отказ с объяснением «проект уже имеет Jitter2.Core, который пакет не
устанавливал». Ни один файл не изменён.

### MT-05. Установка integration

1. В Setup нажать `Install/update integration`.

Ожидаемо: в `Assets/DataSakura/JitterPhysics/Integration/` появились
`JitterPhysicsWorldBuilder.cs`, `JitterPhysicsServerStartup.cs` и asmdef; сборка
`DataSakura.JitterPhysics.JitterIntegration` компилируется; receipt дополнен компонентом
`integration`.

### MT-06. Изменённый файл не перезаписывают

1. Открыть установленный `JitterPhysicsWorldBuilder.cs` и дописать комментарий, сохранить.
2. Нажать `Validate installation`.
3. Нажать `Install/update integration`.

Ожидаемо: (2) сообщает, что файл изменён после установки, с путём; (3) **отказывается**
обновлять и перечисляет изменённые файлы; файл остался с вашей правкой.

### MT-07. Удаление щадящее

1. `Remove package-owned installation`, подтвердить.

Ожидаемо: неизменённые файлы удалены; изменённый на шаге MT-06 файл **остался**, о чём
сказано в логе; receipt больше не содержит удалённые компоненты.

После проверки: вернуть файл в исходное состояние и переустановить integration (MT-05) —
дальнейшие сценарии его требуют.

## 3. Authoring и валидация

### MT-08. Сцена уровня

1. Новая сцена. Пустой объект `Level`, добавить компонент `Jitter Physics Level`.
2. Проверить, что `levelId` заполнился канонично (по имени сцены).
3. Создать несколько кубов/сфер/капсул под объектом `Geometry`, назначить `Geometry Root`.
4. На корни статических тел добавить `Jitter Static Body Source`.
5. Создать `JitterPhysicsWorldProfile` (`Assets > Create > ...`) и назначить в уровень.

Ожидаемо: компоненты добавляются без ошибок; `sourceId` у каждого источника заполнен и
стабилен (не меняется при переименовании объекта).

### MT-09. Окно Baker видит уровень

1. `Tools > DataSakura > Jitter Physics > Physics Baker`, вкладка `Level & Bake`.
2. Нажать `Find in scene`.

Ожидаемо: показаны level id, geometry root, профиль, число помеченных источников и папка
вывода. При отсутствии профиля или источников выводится предупреждение.

### MT-10. Валидация актionable

Проверить по одному, возвращая сцену в рабочее состояние после каждого пункта:

| Что сломать | Ожидаемо в `Validate` |
| --- | --- |
| Коллайдер помечен `Is Trigger` | ошибка, кнопка `Select` выделяет именно этот объект |
| Масштаб объекта `0` по одной оси | ошибка с путём в иерархии |
| `MeshCollider` с не-`Read/Write` мешем | ошибка с именем меша |
| Сфера с неравномерным масштабом | предупреждение о консервативном приближении, bake разрешён |
| Убрать world profile | ошибка |

Ключевая проверка: ошибка **всегда** указывает на объект, а не только на текст.

## 4. Запекание

### MT-11. Успешный bake

1. Вернуть корректную сцену, нажать `Validate and bake`.

Ожидаемо: в `Result` — level id, число тел/шейпов/треугольников, размер, время, полный
хэш и путь; в `Assets/Generated/JitterPhysics/` появились три файла:
`<levelId>.<hash12>.jphys.bytes`, `<levelId>.<hash12>.manifest.json`,
`<levelId>.artifact.asset`.

### MT-12. Повторный bake байт-в-байт

1. Ничего не меняя, нажать `Validate and bake` ещё раз.

Ожидаемо: тот же хэш; новых файлов не появилось (артефакт адресуется содержимым).

### MT-13. Порядок и имена не влияют на результат

1. Переименовать один из объектов-источников; перезапечь.
2. Переместить объект вверх/вниз в иерархии среди соседей; перезапечь.

Ожидаемо: хэш **не изменился** в обоих случаях.

### MT-14. Геометрия влияет

1. Сдвинуть один коллайдер на 0.5 по X; перезапечь.

Ожидаемо: хэш изменился, старый файл остался, новый добавился.

### MT-15. Play Mode запрещён

1. Войти в Play Mode, открыть Baker, нажать `Validate and bake`.

Ожидаемо: кнопка недоступна, показано объяснение; `Validate` при этом работает.

### MT-16. Неудачный bake не портит прошлый артефакт

1. Сломать сцену (например, `Is Trigger` на коллайдере), нажать `Validate and bake`.

Ожидаемо: ошибка, сообщение «previously baked artifact was left untouched»; файлы прошлого
артефакта на диске не изменились (сверить дату и размер).

## 5. Артефакты и экспорт

### MT-17. Инспекция и verify

1. Вкладка `Artifacts`, выбрать артефакт, нажать `Verify`.

Ожидаемо: сообщение о том, что артефакт перехэшировался и декодировался, с числом тел.

### MT-18. Порча payload ловится

1. Открыть `.jphys.bytes` в hex-редакторе, изменить один байт, сохранить, `Verify`.

Ожидаемо: ошибка `HashMismatch` (или `BadMagic`), артефакт не считается валидным.
Затем перезапечь уровень, чтобы восстановить файл.

### MT-19. Экспорт точных байтов

1. `Export payload and manifest...`, выбрать пустую папку вне проекта.

Ожидаемо: в папке два файла с каноничными именами; SHA-256 payload совпадает с хэшем в
окне (`shasum -a 256 <file>`).

### MT-20. Экспорт embedded provider

1. `Export embedded provider (.g.cs)...`, выбрать папку.

Ожидаемо: файл `<Level>Artifact.g.cs`; в нём есть `LevelId`, `ArtifactHash`, чанки base64,
и **нет** даты/имени машины. Повторный экспорт даёт побайтово тот же файл (`diff` пустой).

### MT-21. Удаление артефакта — только выбранного

1. `Delete this artifact`, прочитать диалог.

Ожидаемо: в диалоге перечислены ровно три пути удаляемого артефакта; после подтверждения
другие артефакты остались на месте.

## 6. Диагностика

### MT-22. Детерминизм

1. Вкладка `Diagnostics`, `Repeat-bake determinism check`.

Ожидаемо: `Deterministic: ... identical bytes`.

### MT-23. Round-trip

1. `Codec round-trip of every baked artifact`.

Ожидаемо: для каждого артефакта `OK ... re-encodes identically`.

### MT-24. Совместимость рантайма

1. `Runtime compatibility of every baked artifact`.

Ожидаемо: `OK` для артефактов, запечённых текущей сборкой. Если подменить Jitter2 на другую
ревизию — те же артефакты помечаются `STALE` (это и есть защита от «клиент и сервер думают,
что совместимы»).

## 7. Server projection

### MT-25. Установка проекции

1. `Setup > Install server runtime sources...`, выбрать пустую папку.

Ожидаемо: в папке подпапки `Contracts/`, `ArtifactCodec/`, `Integration/` и
`JitterPhysics.projection.json` со списком файлов и хэшей; ни одного файла с `UnityEngine`.

### MT-26. Verify ловит расхождение

1. `Install/Verify Server Runtime Sources...` на той же папке → ожидаемо: совпадает.
2. Изменить один `.cs` в папке проекции, повторить verify.

Ожидаемо: ошибка с именем файла и объяснением, что сервер собрал бы другой loader.

## 8. Runtime в Unity

Для этих сценариев нужна установленная integration (MT-05) и компилирующийся `Jitter2.Core`.

### MT-27. Построение мира из артефакта

Создать временный скрипт в `Assets/` и повесить на пустой объект в сцене:

```csharp
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Integration;
using DataSakura.JitterPhysics.UnityArtifact;
using Jitter2;
using UnityEngine;

public sealed class JitterPhysicsSmoke : MonoBehaviour
{
    public JitterPhysicsArtifactAsset artifact;

    private void Start()
    {
        PhysicsArtifactResult loaded = JitterPhysicsArtifactLoader.Load(artifact);
        if (!loaded.Succeeded)
        {
            Debug.LogError(loaded.Error.ToString());
            return;
        }

        var world = new World();
        PhysicsWorldBuildResult build = JitterPhysicsWorldBuilder.Apply(world, loaded.Artifact);

        Debug.Log($"bodies={build.BodyCount} shapes={build.ShapeCount} " +
                  $"fingerprint={build.TopologyFingerprint} ok={build.Succeeded}");
    }
}
```

1. Назначить артефакт, войти в Play Mode.

Ожидаемо: в консоли `ok=True`, число тел равно числу источников, fingerprint — 64 hex-символа.

### MT-28. Паритет топологии Unity ↔ .NET

Это закрывает единственное утверждение, которое нельзя проверить с одной стороны.

1. Записать `fingerprint` из MT-27.
2. Экспортировать артефакт (MT-19) в отдельную папку.
3. Прогнать тот же артефакт под `.NET`:

```bash
cd "Packages/com.datasakura.jitter-physics-baker/Server~/Tests"
dotnet test --filter FullyQualifiedName~TopologyFingerprint
```

либо временно добавить тест, который читает экспортированный манифест через
`FilePhysicsArtifactProvider`, строит мир и печатает fingerprint.

Ожидаемо: строки совпадают символ в символ. Расхождение означает, что клиент и сервер
строят разную статическую геометрию — это блокер релиза, а не косметика.

## 9. Отчёт

Скопировать таблицу и заполнить:

```text
MT-01 импорт без Jitter2            : 
MT-02 окно Setup                    : 
MT-03 установка Jitter2             : 
MT-04 внешний Jitter не трогают     : 
MT-05 установка integration         : 
MT-06 изменённый файл не переписан  : 
MT-07 щадящее удаление              : 
MT-08 authoring-компоненты          : 
MT-09 окно Baker видит уровень      : 
MT-10 валидация actionable          : 
MT-11 успешный bake                 : 
MT-12 повторный bake байт-в-байт    : 
MT-13 имена и порядок не влияют     : 
MT-14 геометрия влияет              : 
MT-15 Play Mode запрещён            : 
MT-16 неудачный bake безопасен      : 
MT-17 verify артефакта              : 
MT-18 порча payload ловится         : 
MT-19 экспорт точных байтов         : 
MT-20 экспорт embedded provider     : 
MT-21 удаление только выбранного    : 
MT-22 детерминизм                   : 
MT-23 round-trip                    : 
MT-24 совместимость рантайма        : 
MT-25 установка проекции            : 
MT-26 verify проекции               : 
MT-27 построение мира               : 
MT-28 паритет топологии             : 
```

