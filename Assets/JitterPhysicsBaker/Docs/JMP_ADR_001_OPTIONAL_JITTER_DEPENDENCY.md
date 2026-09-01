# JMP-ADR-001. Опциональная зависимость от Jitter при прежнем explicit Setup

- Статус: принято для реализации в `JMP-E01`, уточнение допускается только новым ADR.
- Дата: 2026-09-01.
- Область: package import, Unity assembly graph, external Jitter, server projection,
  update/rollback/removal.

## Контекст

Базовый UPM-пакет обязан импортироваться в проект, где `Jitter2.Core` отсутствует. При этом
целевая миграция требует использовать `Real`, `JVector`, `JQuaternion` и публичный
`StableMath` напрямую. Always-compiled assembly не может одновременно иметь прямую ссылку на
`Jitter2.Core` и компилироваться без него.

Текущий продукт уже решает эту проблему для world builder: `Jitter2~/` и
`JitterIntegration~/` скрыты от Unity, а пользователь явно запускает Setup. Это поведение
остаётся обязательным.

## Решение

### 1. Что доступно сразу после импорта

Always-available graph остаётся Jitter-free:

- bootstrap/install/diagnostics UI;
- package identity, artifact envelope, manifest, typed errors и provider contracts, которым не
  нужны Jitter math types;
- Unity authoring data, не объявляющие Jitter-native public signatures;
- read-only проверка состояния установки.

Эти asmdef не содержат direct или transitive reference на `Jitter2.Core`. Импорт пакета не
запускает установку, копирование, создание receipt или изменение scripting defines.

### 2. Что появляется только после Setup

Jitter-native geometry contracts, их codec/adapters, bake-affecting Editor layer и runtime world
builder относятся к dormant/installable graph. Их исходники и asmdef templates хранятся в
скрытых UPM-папках и копируются в `Assets/` только явной Setup-командой.

У пользователя остаются отдельные операции:

1. `Install Jitter2` — установить package-owned canonical DLL, только если Jitter отсутствует.
2. `Install/update integration` — установить Jitter-dependent graph против уже существующей
   единственной `Jitter2.Core`.

UI может предложить составную кнопку, но внутри это две receipt-компоненты и две проверяемые
операции. Нельзя автоматически выполнять вторую операцию на import, domain reload или window
draw.

Installed asmdef имеет direct reference на `Jitter2.Core`, когда Jitter представлен source
asmdef. Для precompiled plugin запись `Jitter2.Core` удаляется из `references`, потому что Unity
подключает auto-referenced plugin напрямую. Это tailoring касается только сгенерированного
package-owned asmdef; external Jitter не редактируется.

### 3. External Jitter и duplicate policy

External Jitter всегда принадлежит consumer-проекту. Setup не копирует, не перемещает, не
патчит и не удаляет его.

Разрешён только один candidate `Jitter2.Core`. Ноль кандидатов разрешён для базового импорта,
но блокирует Jitter-dependent Setup и bake. Два и более кандидата блокируют Setup, bake и
startup до ручного устранения конфликта.

Для production parity совместимость external Jitter нельзя доказывать именем, timestamp или
версией файла. Поддерживаемый external binary должен иметь canonical DLL SHA-256 и f32 compile
profile из lock. Source-based external copy может считаться source-compatible для диагностики,
но не production-compatible, пока его собранная DLL не совпала побайтно с server DLL.

### 4. Unity/server projection

Unity client и dedicated server обязаны загружать одинаковые bytes `Jitter2.Core.dll`. Server
projection получает exact canonical DLL, а не пересобирает snapshot под другой TFM. Projection
manifest хранит SHA-256 DLL, source hash, compile profile id, precision и package version.

Mismatch блокирует startup до создания или изменения `World`. Нельзя продолжать с warning.

### 5. Ownership, update и removal

Каждая установленная компонентa имеет receipt: component id, root, package version, source
identity и SHA-256 каждого файла.

- Update разрешён только для receipt-owned и неизменённых файлов.
- Modified receipt-owned файл блокирует overwrite и сохраняется пользователю.
- Unowned conflict блокирует install; Setup не получает ownership задним числом.
- Removal удаляет только неизменённые receipt-owned файлы.
- Modified и unowned файлы никогда не удаляются.
- Запись новой версии выполняется через staging и атомарную замену; при ошибке сохраняется
  предыдущая полная компонента и receipt.

## API до и после Setup

| Состояние | Доступно | Недоступно |
|---|---|---|
| Чистый import | install/diagnostics, manifest/envelope, Jitter-free authoring | Jitter-native records, bake adapter, world builder |
| Только Jitter | Jitter API consumer-проекта | package Jitter-native layer |
| Jitter + integration | весь supported bake/runtime graph | ничего дополнительно не устанавливается автоматически |

## Отклонённые варианты

- Mandatory UPM dependency на Jitter: ломает clean import и существующий explicit Setup.
- Always-compiled conditional Jitter API: public surface меняется от define и остаётся хрупким
  при domain reload/build target switch.
- Reflection вместо direct references: теряет compile-time contract и скрывает layout mismatch.
- Копирование второго Jitter рядом с external copy: создаёт duplicate assembly/type identity.
- Пересборка Jitter отдельно для server: не доказывает exact-DLL parity.
- Перезапись external Jitter после сравнения source hash: нарушает ownership.

## Prototype evidence и ограничения

- Текущие always-compiled asmdef не ссылаются на `Jitter2.Core`.
- Hidden integration template содержит явную Jitter reference для source distribution.
- Server test project ссылается на `Jitter2~/Prebuilt/Jitter2.Core.dll`.
- `JMPE00MigrationPrototypeTests` доказывает SHA equality загруженной server-копии и prebuilt,
  а tampered bytes дают другой digest.
- Ранее `verify-jp05-delivery.sh` проходил на v0.0.11, но это не считается fresh proof текущего
  дерева.
- Fresh Unity consumer run для текущего HEAD заблокирован licensing handshake `505 Unsupported
  protocol version '1.18.1'`. До свежего XML/fixture output `JMP-P01` имеет статус `BLOCKED`, а не
  `PASS`.

## Последствия

`JMP-E01` не должен переносить Jitter reference в базовые asmdef. Миграция contracts потребует
разделить Jitter-free envelope и installable Jitter-native contracts. Любое решение, которое
заставляет базовый package.json зависеть от Jitter, противоречит этому ADR.
