# AGENTS.md

Инструкции для агента, работающего с этим репозиторием.

## Что это

Unity-проект разработки UPM-пакета `com.datasakura.jitter-physics-baker`: детерминированное
запекание статической геометрии уровня в бинарный артефакт и один общий загрузчик, который
строит ту же статическую топологию в Jitter2-мире на клиенте Unity и на выделенном `.NET`
сервере.

- Пакет: `Packages/com.datasakura.jitter-physics-baker/`
- ТЗ: `Assets/JitterPhysicsBaker/Docs/JITTER_PHYSICS_PACKAGE_SPEC.md`
- Backlog и статус: `Assets/JitterPhysicsBaker/Docs/JITTER_PHYSICS_PACKAGE_DECOMPOSITION.md`
- Ручные сценарии в редакторе: `Assets/JitterPhysicsBaker/Docs/MANUAL_TEST_PLAN.md`

## Проверки перед коммитом

Запускать из корня репозитория. Все четыре обязаны быть зелёными.

```sh
python3 tools/verify-package-meta.py
python3 "Packages/com.datasakura.jitter-physics-baker/tools~/verify-jitter2-lock.py"
python3 "Packages/com.datasakura.jitter-physics-baker/tools~/test-jitter2-lock.py"
bash "Packages/com.datasakura.jitter-physics-baker/tools~/test-dotnet.sh"
```

Последняя команда компилирует `Contracts`, `ArtifactCodec`, `JitterIntegration~` и снапшот
Jitter2 обычным .NET SDK и прогоняет общие тесты. Это единственный способ доказать, что
портируемая половина пакета действительно не зависит от движка.

## Проверка в движке

```sh
bash tools/run-unity-tests.sh all           # EditMode + PlayMode в batch mode
bash tools/run-unity-tests.sh editmode      # только EditMode
```

Требования: редактор закрыт (Unity не откроет заблокированный проект), путь к Unity берётся
из `ProjectSettings/ProjectVersion.txt`, результаты — в `Logs/TestResults/*.xml`.

Всё, что нельзя проверить тестами (диалоги, окна, реакция на действия автора, установка в
проект, экспорт), описано пошагово в `MANUAL_TEST_PLAN.md`. Проходить его целиком нужно
перед релизом и после изменений в `Editor/`.

## Компиляция Editor-кода без Unity

Unity-проекты `*.csproj` в корне генерируются редактором и содержат фиксированные списки
файлов, поэтому новый файл в них не попадёт, пока редактор не откроют. Чтобы быстро
проверить, что Editor-код компилируется:

```sh
python3 tools/dev-refresh-csproj.py
dotnet build DataSakura.JitterPhysics.Editor.csproj -v q --nologo
dotnet build DataSakura.JitterPhysics.Editor.Tests.csproj -v q --nologo
dotnet build DataSakura.JitterPhysics.Tests.csproj -v q --nologo
```

Последние две команды обязательны при правке тестов из `Tests/`. `Server~/Tests` собирается
с NUnit 4 из NuGet, а Unity поставляет свой NUnit 3, и конструкции вроде `Is.AnyOf` есть
только в первом: тест, зелёный под `.NET`, может не компилироваться в редакторе. Эти сборки
ссылаются ровно на те сборки, которые использует Unity, поэтому ловят расхождение сразу.

`*.csproj` не отслеживаются git, править их безопасно: Unity перезапишет результат.

Файлы, добавленные в пакет мимо редактора, остаются без `.meta`, и
`verify-package-meta.py` это поймает. Создать недостающие:

```sh
python3 tools/dev-make-meta.py
```

## Правила, которые нельзя нарушать

1. **Пакет импортируется в проект без Jitter2.** `Contracts`, `ArtifactCodec`,
   `UnityArtifact`, `Authoring` и `Editor` не ссылаются на `Jitter2.Core` ни прямо, ни через
   asmdef. Jitter-зависимый код живёт в `JitterIntegration~/` и ставится явной командой.
2. **Артефакт байт-детерминирован.** Никаких timestamp, абсолютных путей, instance id, GUID
   и порядка перечисления хеш-таблиц в бинаре. Два запекания неизменной сцены обязаны дать
   один и тот же SHA-256.
3. **Формат менять только с bump-ом схемы.** Layout зафиксирован golden-bytes тестом;
   изменение писателя без изменения `ArtifactSchemaVersion` — ошибка.
4. **Внешний Jitter2 неприкосновенен.** Пакет ссылается на него по имени сборки и никогда не
   копирует, не двигает и не редактирует чужие файлы.
5. **Ничего не мутируется без явной команды.** Никаких записей из `[InitializeOnLoad]`,
   импорта или отрисовки окна.
6. **Загрузка fail-fast.** Некорректный артефакт даёт типизированную ошибку и не оставляет
   частично построенный мир.

## Стиль

- Публичные типы и члены — с XML-doc на английском; комментарии объясняют **почему**, а не
  что делает строка.
- Ошибки, которые может вызвать внешний ввод (файл, сеть, чужой манифест), возвращаются как
  типизированный результат, а не бросаются исключением. Исключение допустимо только для
  ошибки программиста внутри пакета.
- Документация пакета (`Packages/**`) — на английском, документы проекта
  (`Assets/JitterPhysicsBaker/Docs/**`) — на русском.
- Сообщения коммитов: `type(scope): summary` + список изменений с объяснением причины.
- Отмечать задачу выполненной в декомпозиции можно только после фактического прогона; если
  прогон не выполнялся, это указывается прямо в статусе.



