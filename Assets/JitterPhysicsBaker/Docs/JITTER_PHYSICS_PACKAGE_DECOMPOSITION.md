# Jitter Physics Baker Package — декомпозиция работ

Статус: рабочий backlog для реализации.

Источник требований: [JITTER_PHYSICS_PACKAGE_SPEC.md](JITTER_PHYSICS_PACKAGE_SPEC.md).

Документ переводит единое ТЗ в иерархию:

```text
Epic
└── Task
    └── Subtask
```

Идентификаторы предварительные и могут быть заменены Jira/Linear ID при импорте.

## 1. Правила backlog-а

- `P0` — блокирует standalone package или EFT POC.
- `P1` — необходимо для релиза `v0.1.0`, но не блокирует ранний technical slice.
- `P2` — post-v1 discovery/production backlog.
- Каждая Task должна завершаться кодом/артефактом и проверяемыми acceptance criteria.
- Tests, документация и безопасный update path входят в Task, а не оставляются «на потом».
- Изменения существующего EFT Jitter2, `Jitter2.Unity.csproj`, `EFT.Runtime.csproj`, Dockerfile и `Jitter.Netick.Adapter` не входят в scope.
- Календарные оценки не фиксируются до завершения Epic JP-E00 и определения состава команды.

## 2. Release gates

### Gate A — Architecture/Foundation Ready

Завершены JP-E00–JP-E03:

- подтверждён baseline;
- package импортируется без Jitter;
- dormant Jitter/installer/lock работают;
- artifact contracts/codec зафиксированы golden tests.

### Gate B — Standalone Package `v0.1.0-rc`

Завершены JP-E04–JP-E09:

- authoring/bake/runtime world builder готовы;
- Unity и `.NET` строят одинаковую static topology;
- server source delivery работает;
- clean project/dev project/samples/CI проходят;
- package не зависит от EFT/Netick.

### Gate C — EFT POC Accepted

Завершены JP-E10–JP-E11:

- package подключён в EFT без миграции Jitter2/server build;
- client/server загружают exact artifact;
- Netick handshake проверяет artifact hash и runtime ID;
- runtime Jitter prediction/authority сохраняются;
- Shooter E2E и Docker smoke проходят.

## 3. Dependency map

```mermaid
flowchart LR
    E00["JP-E00 Baseline & spikes"] --> E01["JP-E01 Repository bootstrap"]
    E00 --> E02["JP-E02 Jitter snapshot & installer"]
    E01 --> E03["JP-E03 Artifact core"]
    E02 --> E05["JP-E05 Jitter world builder"]
    E03 --> E04["JP-E04 Authoring & conversion"]
    E03 --> E05
    E02 --> E06["JP-E06 Editor UX"]
    E04 --> E06
    E05 --> E07["JP-E07 Server delivery"]
    E06 --> E08["JP-E08 Dev project & samples"]
    E07 --> E08
    E08 --> E09["JP-E09 CI & standalone release"]
    E09 --> E10["JP-E10 EFT integration"]
    E10 --> E11["JP-E11 EFT verification"]
    E11 --> E12["JP-E12 Production backlog"]
```

Критический путь:

```text
JP-E00 -> JP-E01/JP-E02 -> JP-E03 -> JP-E05 -> JP-E07
       -> JP-E08 -> JP-E09 -> JP-E10 -> JP-E11
```

## JP-E00. Baseline, characterization и architecture spikes

Приоритет: P0.

Результат эпика: подтверждены факты проекта и рискованные части архитектуры до основной реализации.

### JP-T00.1. Зафиксировать baseline EFT и Custom Navigation

Зависимости: нет.

Subtasks:

- [ ] Проверить Unity version, `.NET 10`, tick rate и текущие Jitter compile profiles.
- [ ] Зафиксировать client flow: world creation, prediction, rollback, `World.Step`.
- [ ] Зафиксировать server flow: world creation, static setup, connection approval, `World.Step`.
- [ ] Найти все call sites `ShooterMotor.BuildStaticWorld`.
- [ ] Изучить Custom Navigation package layout, artifact flow, `Server~` и installers.
- [ ] Запустить и записать baseline Unity/server/Docker commands и результаты.

Acceptance criteria:

- [ ] Есть baseline report с командами, версиями и текущей runtime sequence.
- [ ] Зафиксированы исходные build/test failures, если они существуют.
- [ ] Ни один production/source файл не изменён ради baseline.

### JP-T00.2. Characterization текущего `JitterBody` collider mapping

Зависимости: JP-T00.1.

Subtasks:

- [ ] Зафиксировать Box center/size/rotation/scale semantics.
- [ ] Зафиксировать Sphere uniform/non-uniform scale semantics.
- [ ] Зафиксировать Capsule direction X/Y/Z и cylinder length semantics.
- [ ] Зафиксировать Mesh transform/winding/index policy.
- [ ] Добавить characterization fixtures/tests либо точный executable report.

Acceptance criteria:

- [ ] Новые package converters можно сравнить с текущим поведением по числовым fixtures.
- [ ] Все намеренные отличия требуют отдельного решения, а не появляются неявно.

### JP-T00.3. Spike clean-import/bootstrap

Зависимости: JP-T00.1.

Subtasks:

- [ ] Создать минимальный Jitter-free bootstrap asmdef.
- [ ] Подтвердить, что dormant `JitterIntegration~/` не импортируется Unity.
- [ ] Проверить clean project без `Jitter2.Core`.
- [ ] Проверить активацию integration после появления `Jitter2.Core`.

Acceptance criteria:

- [ ] Package можно импортировать без missing assembly errors до installer-а.
- [ ] Принята финальная assembly activation strategy.

### JP-T00.4. Spike server source и embedded artifact delivery

Зависимости: JP-T00.1.

Subtasks:

- [ ] Проверить SDK default compile glob внутри `EFT.Server/EFT.Runtime/`.
- [ ] Собрать минимальную generated source projection без ProjectReference.
- [ ] Создать прототип embedded exact-bytes `.g.cs` provider.
- [ ] Проверить chunking, восстановление bytes и SHA-256.
- [ ] Определить POC size cap и failure UX.
- [ ] Подтвердить сборку текущим Dockerfile.

Acceptance criteria:

- [ ] Прототип не требует изменений `EFT.Runtime.csproj`, `Jitter2.Unity.csproj` или Dockerfile.
- [ ] Восстановленные bytes byte-for-byte совпадают с input artifact.
- [ ] Oversized payload отклоняется до генерации/компиляции.

Epic acceptance:

- [ ] Все четыре spikes закрыты и отражены в implementation plan.
- [ ] Не осталось неизвестного P0-риска в bootstrap/server delivery.

## JP-E01. Standalone repository и package bootstrap

Приоритет: P0.

Результат эпика: отдельный Git UPM package и Unity dev project существуют и компилируются без Jitter.

### JP-T01.1. Создать package repository

Зависимости: JP-E00.

Subtasks:

- [ ] Создать `package.json` для `com.datasakura.jitter-physics-baker`.
- [ ] Добавить README, CHANGELOG, LICENSE, Third Party Notices.
- [ ] Добавить `.gitignore` и `.gitattributes` с line-ending policy.
- [ ] Создать `Runtime`, `Authoring`, `Editor`, `Tests`, `Samples~`, `Server~`, `Documentation~`, `tools~`.
- [ ] Зафиксировать minimal Unity version и SemVer policy.

Acceptance criteria:

- [ ] UPM распознаёт package по local path.
- [ ] Repository не содержит `Library`, `Temp`, `obj`, `bin`, secrets или случайные binaries.

### JP-T01.2. Создать Jitter-free assembly graph

Зависимости: JP-T01.1, JP-T00.3.

Subtasks:

- [ ] Создать `DataSakura.JitterPhysics.Contracts`.
- [ ] Создать `DataSakura.JitterPhysics.ArtifactCodec`.
- [ ] Создать `DataSakura.JitterPhysics.UnityArtifact`.
- [ ] Создать `DataSakura.JitterPhysics.Authoring`.
- [ ] Создать `DataSakura.JitterPhysics.Editor`.
- [ ] Настроить `noEngineReferences` и Editor-only boundaries.
- [ ] Проверить отсутствие references на EFT, Netick и Jitter.

Acceptance criteria:

- [ ] Assembly graph компилируется без установленного Jitter.
- [ ] Runtime Contracts/Codec не зависят от UnityEngine.
- [ ] Editor assembly не попадает в player build.

### JP-T01.3. Создать отдельный Unity dev/QA project

Зависимости: JP-T01.1.

Subtasks:

- [ ] Создать Unity project поддерживаемой версии.
- [ ] Подключить package через local `file:` dependency.
- [ ] Настроить Test Framework.
- [ ] Добавить clean-import fixture без Jitter.
- [ ] Описать local developer bootstrap.

Acceptance criteria:

- [ ] Clean checkout dev project открывается и компилируется.
- [ ] Package source редактируется без копирования в `Assets`.

### JP-T01.4. Добавить CI skeleton

Зависимости: JP-T01.1, JP-T01.3.

Subtasks:

- [ ] Repository/package validation job.
- [ ] Unity clean-import job.
- [ ] Placeholder EditMode/PlayMode jobs.
- [ ] Placeholder `.NET 10` job.
- [ ] Artifact/reports retention policy.

Acceptance criteria:

- [ ] CI запускается на PR и показывает отдельные package/Unity/.NET stages.

Epic acceptance:

- [ ] Package импортируется в clean project без Jitter и compile errors.
- [ ] Базовый repository/assembly layout соответствует исходному ТЗ.

## JP-E02. Dormant Jitter2, lock и installer

Приоритет: P0.

Результат эпика: package безопасно использует внешний Jitter либо устанавливает fallback, контролируя exact compatibility.

### JP-T02.1. Создать `Jitter2~/` snapshot

Зависимости: JP-E00, JP-E01.

Subtasks:

- [ ] Синхронизировать текущие EFT Jitter `.cs` sources в `Jitter2~/Runtime`.
- [ ] Добавить upstream commit, `PATCHES.md`, license и provenance.
- [ ] Создать standalone Unity asmdef template без EFT/Netick references.
- [ ] Зафиксировать Unity/.NET compile profiles.
- [ ] Проверить, что Unity не импортирует snapshot.

Acceptance criteria:

- [ ] Snapshot соответствует принятой EFT revision.
- [ ] Fallback asmdef создаёт assembly `Jitter2.Core`.
- [ ] В package import не появляется вторая Jitter assembly.

### JP-T02.2. Реализовать canonical source hash и lock

Зависимости: JP-T02.1.

Subtasks:

- [ ] Реализовать include/exclude traversal.
- [ ] Нормализовать paths, ordering, encoding и line endings.
- [ ] Включить `.cs`, `csc.rsp` и canonical compile profile.
- [ ] Исключить consumer asmdef/meta/build output.
- [ ] Сгенерировать `jitter2.lock.json`.
- [ ] Реализовать `verify-jitter2-lock`.
- [ ] Добавить known-hash tests.

Acceptance criteria:

- [ ] Hash одинаков на поддерживаемых ОС.
- [ ] Изменение любого compile-relevant input меняет hash.
- [ ] Consumer-specific asmdef path/reference не меняет source identity.

### JP-T02.3. Реализовать Jitter discovery/compatibility report

Зависимости: JP-T02.2, JP-E01.

Subtasks:

- [ ] Искать `Jitter2.Core` через compilation metadata и AssetDatabase.
- [ ] Различать source asmdef и precompiled plugin.
- [ ] Показывать все найденные paths.
- [ ] Считать actual source hash/compile profile.
- [ ] Классифицировать `Missing`, `Compatible`, `Incompatible`, `Duplicate`, `UnsupportedPlugin`.
- [ ] Добавить machine-readable result для CI.

Acceptance criteria:

- [ ] Detection не зависит от folder path.
- [ ] Duplicate/incompatible result содержит actionable diagnostics.

### JP-T02.4. Реализовать `Install Jitter2 into Project`

Зависимости: JP-T02.1, JP-T02.3.

Subtasks:

- [ ] Сделать staging copy dormant snapshot-а.
- [ ] Применить standalone asmdef template.
- [ ] Проверить hash staging и final copy.
- [ ] Заблокировать операцию при существующем `Jitter2.Core`.
- [ ] Выполнить `AssetDatabase.Refresh` и post-install validation.

Acceptance criteria:

- [ ] Новый проект получает ровно одну совместимую `Jitter2.Core`.
- [ ] Existing external Jitter никогда не перезаписывается.

### JP-T02.5. Реализовать Jitter integration installer

Зависимости: JP-T02.3, JP-T02.4, JP-E01.

Subtasks:

- [ ] Подготовить `JitterIntegration~/UnityAssemblyTemplate`.
- [ ] Создать asmdef references по names, включая `Jitter2.Core`.
- [ ] Устанавливать integration отдельно от Jitter.
- [ ] Проверять version/hash installed projection.
- [ ] Не создавать dependency cycle с `EFT.Runtime`.

Acceptance criteria:

- [ ] С compatible external Jitter устанавливается только adapter.
- [ ] С fallback Jitter adapter компилируется после установки.
- [ ] Clean import до установки остаётся рабочим.

### JP-T02.6. Реализовать receipt/update/uninstall lifecycle

Зависимости: JP-T02.4, JP-T02.5.

Subtasks:

- [ ] Записывать ownership, package version, paths и file hashes.
- [ ] Сделать idempotent повторную установку.
- [ ] Обновлять только неизменённые owned files.
- [ ] Сохранять изменённые пользователем файлы.
- [ ] Удалять только owned + unchanged files.
- [ ] Добавить interrupted-install recovery.

Acceptance criteria:

- [ ] Update/uninstall не удаляют внешний или изменённый код.
- [ ] Import/`InitializeOnLoad` не выполняют mutation.

### JP-T02.7. Автоматизировать sync EFT -> snapshot

Зависимости: JP-T02.1, JP-T02.2.

Subtasks:

- [ ] Реализовать `sync-jitter2 --source`.
- [ ] Генерировать diff/provenance report.
- [ ] Требовать lock regeneration.
- [ ] Запрещать manual drift snapshot-а в CI.

Acceptance criteria:

- [ ] Один command воспроизводимо синхронизирует accepted EFT revision.
- [ ] CI ловит правку snapshot-а без source/provenance/lock update.

Epic acceptance:

- [ ] External, fallback, duplicate и incompatible scenarios покрыты tests.
- [ ] Jitter в EFT не изменён.
- [ ] Gate A Jitter requirements выполнены.

## JP-E03. Artifact Contracts, codec и compatibility token

Приоритет: P0.

Результат эпика: зафиксирован безопасный deterministic artifact v1 без зависимости на Unity/Jitter.

### JP-T03.1. Спроектировать DTO и schema v1

Зависимости: JP-E01, JP-T00.2.

Subtasks:

- [ ] World settings DTO.
- [ ] Body record DTO.
- [ ] Box/Sphere/Capsule/TriangleMesh shape DTO.
- [ ] Stable source/shape IDs.
- [ ] Manifest DTO.
- [ ] Safety limits/config.
- [ ] Документировать numeric tags/header layout.

Acceptance criteria:

- [ ] DTO не содержит Unity/Jitter types.
- [ ] Artifact не содержит runtime Jitter internals.

### JP-T03.2. Реализовать canonical writer

Зависимости: JP-T03.1.

Subtasks:

- [ ] Little-endian primitive writer.
- [ ] Bounded UTF-8 encoding.
- [ ] Float finite/`-0` normalization.
- [ ] Quaternion normalization/sign convention.
- [ ] Canonical records ordering contract.
- [ ] SHA-256 canonical binary.

Acceptance criteria:

- [ ] Repeat write одного DTO даёт exact bytes/hash.
- [ ] Writer отклоняет invalid values до создания final artifact.

### JP-T03.3. Реализовать bounded reader/validator

Зависимости: JP-T03.1.

Subtasks:

- [ ] Проверять hash до parse.
- [ ] Проверять magic/schema/precision/endianness/runtime ID.
- [ ] Проверять counts/lengths до allocation.
- [ ] Проверять IDs, floats, quaternions, mesh indices.
- [ ] Отклонять trailing garbage.
- [ ] Возвращать typed errors.

Acceptance criteria:

- [ ] Corrupt/truncated/oversized inputs не вызывают unbounded allocations.
- [ ] Reader не изменяет Jitter world и не зависит от Jitter.

### JP-T03.4. Реализовать manifest и artifact identity

Зависимости: JP-T03.2, JP-T03.3.

Subtasks:

- [ ] Создать deterministic manifest fields.
- [ ] Реализовать binary/manifest cross-check.
- [ ] Создать content-addressed filenames.
- [ ] Исключить timestamps/machine paths из identity.
- [ ] Создать Unity artifact metadata contract.

Acceptance criteria:

- [ ] Manifest не может подменить binary identity.
- [ ] Client/server artifactHash — SHA-256 одних bytes.

### JP-T03.5. Реализовать `runtimeCompatibilityId`

Зависимости: JP-T02.2, JP-T03.1.

Subtasks:

- [ ] Canonicalize formula inputs.
- [ ] Включить schema, Jitter source hash, compile/precision profile.
- [ ] Включить collider/shape/world-builder semantics versions.
- [ ] Исключить manual override.
- [ ] Добавить known-vector tests.

Acceptance criteria:

- [ ] Любое runtime-semantic изменение меняет ID.
- [ ] ID одинаков в Editor, Unity runtime и `.NET` tests.

### JP-T03.6. Реализовать transport-agnostic compatibility token

Зависимости: JP-T03.4, JP-T03.5.

Subtasks:

- [ ] Кодировать magic/version/levelId.
- [ ] Добавить artifact SHA-256.
- [ ] Добавить runtimeCompatibilityId.
- [ ] Ограничить payload/string lengths.
- [ ] Реализовать strict parser и typed errors.

Acceptance criteria:

- [ ] Token codec не зависит от Netick.
- [ ] Missing/truncated/oversized/unknown-version payload отклоняется.

### JP-T03.7. Зафиксировать golden/corrupt test suite

Зависимости: JP-T03.2–JP-T03.6.

Subtasks:

- [ ] Golden minimal box bytes.
- [ ] Roundtrip all shapes/settings.
- [ ] `-0/+0`, `q/-q` fixtures.
- [ ] One-field-change hash fixtures.
- [ ] Corrupt matrix.
- [ ] Manifest mismatch fixtures.

Acceptance criteria:

- [ ] Golden bytes нельзя изменить без schema bump.
- [ ] Gate A artifact requirements выполнены.

## JP-E04. Unity authoring, collider conversion и deterministic bake

Приоритет: P0.

Результат эпика: designer может явно описать static world и получить deterministic artifact.

### JP-T04.1. Реализовать authoring components

Зависимости: JP-E01, JP-T03.1.

Subtasks:

- [ ] `JitterPhysicsLevel`.
- [ ] `JitterStaticBodySource`.
- [ ] `JitterPhysicsWorldProfile`.
- [ ] Stable serialized `sourceId` generation/repair policy.
- [ ] Inspector validation hooks.

Acceptance criteria:

- [ ] В bake попадают только explicit sources.
- [ ] 0 или >1 active level даёт actionable error.

### JP-T04.2. Реализовать deterministic source collection

Зависимости: JP-T04.1.

Subtasks:

- [ ] Traversal `geometryRoot`/sources.
- [ ] Canonical source order по `sourceId`.
- [ ] Canonical collider key: path/sibling/component/type.
- [ ] Duplicate ID/key diagnostics.
- [ ] Inactive/disabled policy.

Acceptance criteria:

- [ ] Collection order не зависит от Unity instance ID или hash enumeration.

### JP-T04.3. Реализовать primitive converters

Зависимости: JP-T00.2, JP-T04.2, JP-T03.1.

Subtasks:

- [ ] Box converter.
- [ ] Sphere converter + non-uniform warning.
- [ ] Capsule X/Y/Z converter.
- [ ] Center/local pose/scale conversion.
- [ ] Shear/zero/NaN/negative-scale validation.

Acceptance criteria:

- [ ] Converters проходят characterization fixtures текущего `JitterBody`.

### JP-T04.4. Реализовать MeshCollider converter

Зависимости: JP-T04.2, JP-T03.1.

Subtasks:

- [ ] Получить readable mesh data в Editor.
- [ ] Применить full transform matrix.
- [ ] Исправить winding при negative determinant.
- [ ] Проверить indices/triangle count.
- [ ] Отклонить degenerate/unreadable/invalid mesh.

Acceptance criteria:

- [ ] Mesh fixture даёт deterministic vertices/indices.
- [ ] Raycast orientation ожидаема после world build.

### JP-T04.5. Реализовать validator

Зависимости: JP-T04.1–JP-T04.4, JP-T02.3.

Subtasks:

- [ ] Setup/Jitter compatibility validation.
- [ ] Authoring/ID validation.
- [ ] Collider/transform/mesh validation.
- [ ] World profile/tick/precision validation.
- [ ] Issue severity, object path, ping/select metadata.
- [ ] Блокировать bake на error.

Acceptance criteria:

- [ ] Unsupported input не пропускается silently.
- [ ] Incompatible Jitter hash блокирует bake.

### JP-T04.6. Реализовать deterministic bake pipeline

Зависимости: JP-E03, JP-T04.5.

Subtasks:

- [ ] Collect -> convert -> canonicalize -> write -> hash.
- [ ] Создать manifest.
- [ ] Создать `JitterPhysicsArtifactAsset` + `TextAsset`.
- [ ] Использовать temp + atomic replace.
- [ ] Не выполнять bake в Play Mode.
- [ ] Сохранять previous valid artifact при failure.

Acceptance criteria:

- [ ] Repeat bake exact.
- [ ] One semantic source change меняет hash.
- [ ] Runtime повторно проверяет asset binary hash.

Epic acceptance:

- [ ] Box/Sphere/Capsule/Mesh baking проходит EditMode fixtures.
- [ ] Designer получает actionable validation и stable artifact.

## JP-E05. Jitter world builder и runtime integration source

Приоритет: P0.

Результат эпика: один source set строит static Jitter topology в Unity и `.NET`.

### JP-T05.1. Подготовить Jitter integration source boundary

Зависимости: JP-E02, JP-E03.

Subtasks:

- [ ] Создать `JitterIntegration~/Runtime`.
- [ ] Исключить UnityEngine/Editor/Netick dependencies.
- [ ] Настроить Unity assembly template reference по имени `Jitter2.Core`.
- [ ] Настроить `.NET` source inclusion against consumer Jitter assembly.

Acceptance criteria:

- [ ] Один adapter source set компилируется в Unity и `.NET` fixtures.

### JP-T05.2. Реализовать descriptor -> Jitter shapes

Зависимости: JP-T05.1, JP-E03.

Subtasks:

- [ ] Box/Sphere/Capsule construction.
- [ ] TriangleMesh/TriangleShape construction.
- [ ] Local pose/material application.
- [ ] Создание strictly в artifact order.

Acceptance criteria:

- [ ] Runtime types/counts соответствуют artifact records.

### JP-T05.3. Реализовать static world builder

Зависимости: JP-T05.2.

Subtasks:

- [ ] Применить/проверить world settings.
- [ ] Создать static bodies before dynamic bodies.
- [ ] Реализовать Ready state.
- [ ] Реализовать duplicate Apply guard.
- [ ] Реализовать failure rollback/dispose policy.

Acceptance criteria:

- [ ] Partial world никогда не становится Ready.
- [ ] Повторный Apply не создаёт duplicate statics.

### JP-T05.4. Реализовать metrics и topology fingerprint

Зависимости: JP-T05.3.

Subtasks:

- [ ] Body/shape/triangle counts.
- [ ] Elapsed time.
- [ ] Artifact/runtime IDs.
- [ ] Canonical topology fingerprint.
- [ ] Safe diagnostic formatting.

Acceptance criteria:

- [ ] Unity и `.NET` дают exact одинаковый fingerprint.

### JP-T05.5. Добавить world-builder tests

Зависимости: JP-T05.2–JP-T05.4.

Subtasks:

- [ ] Load all supported shapes.
- [ ] Raycast fixtures.
- [ ] Falling body/resting ground.
- [ ] Dynamic body vs cover.
- [ ] Duplicate Apply.
- [ ] Failure/rollback.
- [ ] Settings/tick validation.

Acceptance criteria:

- [ ] Tests проходят против dormant Jitter в `.NET` и установленного Jitter в Unity.

Epic acceptance:

- [ ] Shared loader/world builder готов для client/server integration.
- [ ] Package не владеет и не вызывает tick loop.

## JP-E06. Editor UX, export и diagnostics

Приоритет: P1.

Результат эпика: setup/bake/export доступны через безопасный и диагностируемый Editor workflow.

### JP-T06.1. Реализовать Setup UI

Зависимости: JP-E02.

Subtasks:

- [ ] Показать Jitter path/type/ownership/hash/status.
- [ ] Добавить installer/update/uninstall actions.
- [ ] Показать package/schema/runtime IDs.
- [ ] Добавить compatibility report export.

Acceptance criteria:

- [ ] User понимает, какая Jitter copy активна и почему операция заблокирована.

### JP-T06.2. Реализовать Level & Sources / Bake UI

Зависимости: JP-E04.

Subtasks:

- [ ] Level/profile/source selection/status.
- [ ] Validation issue list + ping/select.
- [ ] `Validate`, `Validate + Bake`, `Bake for Client`.
- [ ] Counts/size/time/hash output.

Acceptance criteria:

- [ ] Полный bake workflow выполняется без ручного вызова scripts.

### JP-T06.3. Реализовать artifact management/export

Зависимости: JP-T04.6.

Subtasks:

- [ ] Inspect generated artifacts.
- [ ] Export exact binary + manifest без rebake.
- [ ] Delete только explicit выбранного artifact-а.
- [ ] Copy hashes/report.
- [ ] Safe temp/replace and previous-valid preservation.

Acceptance criteria:

- [ ] Exported server bytes exact совпадают с client artifact.

### JP-T06.4. Реализовать diagnostics

Зависимости: JP-E03, JP-E05.

Subtasks:

- [ ] Codec roundtrip.
- [ ] Repeat determinism check.
- [ ] Runtime compatibility check.
- [ ] Topology fingerprint display.
- [ ] Short/full hash logging policy.

Acceptance criteria:

- [ ] Основные mismatch причины диагностируются без запуска match.

Epic acceptance:

- [ ] Editor mutations происходят только по explicit actions.
- [ ] Failed action не повреждает existing artifacts/installations.

## JP-E07. Server source delivery и `.NET` runtime

Приоритет: P0.

Результат эпика: generic server может встроить shared loader без PackageCache и precompiled Jitter-dependent DLL.

### JP-T07.1. Создать server runtime source projection

Зависимости: JP-E03, JP-E05, JP-T00.4.

Subtasks:

- [ ] Определить source list Contracts/Codec/Builder/Providers.
- [ ] Реализовать projection manifest.
- [ ] Копировать sources в configurable consumer project folder.
- [ ] Исключить Unity-only source.
- [ ] Проверить отсутствие PackageCache paths.

Acceptance criteria:

- [ ] Consumer `.NET 10` project компилирует projection против своей Jitter assembly.

### JP-T07.2. Реализовать server projection installer lifecycle

Зависимости: JP-T07.1, JP-T02.6.

Subtasks:

- [ ] Явный target folder selection.
- [ ] Staging copy.
- [ ] Receipt/package version/file hashes.
- [ ] Idempotent update.
- [ ] Modified-file protection/uninstall.
- [ ] CI validation command.

Acceptance criteria:

- [ ] Package update не оставляет незаметно устаревший server runtime.

### JP-T07.3. Реализовать file artifact provider

Зависимости: JP-E03.

Subtasks:

- [ ] Load immutable binary/manifest path.
- [ ] Validate before returning artifact.
- [ ] Typed missing/corrupt errors.
- [ ] Документировать mount/content/registry integration boundary.

Acceptance criteria:

- [ ] Provider не предполагает EFT path или deploy system.

### JP-T07.4. Реализовать embedded exact-bytes provider/export

Зависимости: JP-T00.4, JP-T06.3.

Subtasks:

- [ ] Generate `.g.cs` from already baked bytes.
- [ ] Разбить payload на safe chunks.
- [ ] Восстановить bytes один раз при startup.
- [ ] Повторно проверить SHA-256 и manifest.
- [ ] Реализовать configurable hard size cap.
- [ ] Добавить determinism и oversized tests.

Acceptance criteria:

- [ ] Input/output bytes exact.
- [ ] Генератор не делает rebake.
- [ ] Provider подходит для EFT POC без build-file changes.

### JP-T07.5. Реализовать server startup API/self-check

Зависимости: JP-T07.3, JP-T07.4, JP-E05.

Subtasks:

- [ ] Resolve selected provider.
- [ ] Validate artifact/runtime/tick settings.
- [ ] Build static world до accept-enabled.
- [ ] Вернуть Ready/typed failure.
- [ ] Вывести safe self-check metrics.

Acceptance criteria:

- [ ] Server не включает connection approval без Ready world.
- [ ] Package не запускает отдельный physics service.

### JP-T07.6. Создать `Server~/Tests`

Зависимости: JP-T02.1, JP-E03, JP-E05, JP-T07.1.

Subtasks:

- [ ] `.NET 10` project.
- [ ] Direct compile `Jitter2~/Runtime`.
- [ ] Применить lock compile profile.
- [ ] Подключить package sources ссылками.
- [ ] Codec/world-builder/provider tests.
- [ ] Golden topology parity fixture.

Acceptance criteria:

- [ ] Dormant snapshot компилируется и проходит runtime tests в CI.

Epic acceptance:

- [ ] Server delivery не требует `Library/PackageCache` или отдельный HTTP service.
- [ ] EFT-compatible no-build-file path доказан fixture-ом.

## JP-E08. Standalone dev project, samples и documentation

Приоритет: P1.

Результат эпика: package проверяется и демонстрируется без EFT/Netick.

### JP-T08.1. Настроить два dev-project режима

Зависимости: JP-E02, JP-E05.

Subtasks:

- [ ] Fixture `Installed Fallback`.
- [ ] Fixture `External Jitter`.
- [ ] Automated setup/reset scripts.
- [ ] Compile/PlayMode smoke обоих режимов.

Acceptance criteria:

- [ ] Оба режима используют один package API и проходят CI.

### JP-T08.2. Создать standalone demo fixtures

Зависимости: JP-E04, JP-E05, JP-E06.

Subtasks:

- [ ] Static primitives scene.
- [ ] Static triangle mesh scene.
- [ ] Runtime load + falling body.
- [ ] Determinism/topology diagnostics.
- [ ] Stress static mesh fixture.
- [ ] Генерировать scenes/assets scripts, если GUID fixtures хрупкие.

Acceptance criteria:

- [ ] Demo показывает bake -> load -> runtime Jitter step без EFT/Netick.

### JP-T08.3. Подготовить Samples~

Зависимости: JP-T08.2.

Subtasks:

- [ ] Static World Baking.
- [ ] Runtime Loading.
- [ ] Cross Runtime Parity.
- [ ] Netick Integration Reference без hard dependency/проприетарного code.
- [ ] Проверить optional import/uninstall.

Acceptance criteria:

- [ ] Samples не выполняют project mutations автоматически.

### JP-T08.4. Написать package documentation

Зависимости: JP-E02–JP-E07.

Subtasks:

- [ ] Authoring guide.
- [ ] Artifact format v1.
- [ ] Installing/upgrading Jitter2.
- [ ] Runtime integration.
- [ ] Server source/provider integration.
- [ ] Compatibility/versioning policy.
- [ ] Cross-runtime determinism disclaimer.

Acceptance criteria:

- [ ] Новый consumer может установить и запустить demo по документации.

Epic acceptance:

- [ ] Standalone vertical slice работает без EFT.
- [ ] Все public APIs/workflows документированы.

## JP-E09. Quality gates, CI/CD и standalone release

Приоритет: P0/P1.

Результат эпика: package проходит Standalone DoD и выпускается как pinned Git tag.

### JP-T09.1. Завершить Unity test pipeline

Зависимости: JP-E01–JP-E08.

Subtasks:

- [ ] EditMode tests.
- [ ] PlayMode/runtime smoke.
- [ ] Clean-import/fallback/external modes.
- [ ] IL2CPP mobile smoke.
- [ ] Test artifact cleanup.

Acceptance criteria:

- [ ] Unity jobs стабильны на clean runner.

### JP-T09.2. Завершить `.NET`/cross-runtime pipeline

Зависимости: JP-E03, JP-E05, JP-E07.

Subtasks:

- [ ] `dotnet build/test Server~/Tests`.
- [ ] Exact artifact decode parity.
- [ ] Exact topology fingerprint parity.
- [ ] Provider exact-bytes parity.
- [ ] Declared-tolerance physics comparison.

Acceptance criteria:

- [ ] Unity и `.NET` используют exact artifact и static topology.

### JP-T09.3. Добавить determinism CI

Зависимости: JP-E04, JP-T09.1.

Subtasks:

- [ ] Repeat bake на clean runner.
- [ ] Bake минимум на двух поддерживаемых ОС.
- [ ] Compare SHA-256/golden bytes.
- [ ] Блокировать golden change без schema decision.

Acceptance criteria:

- [ ] Nondeterministic bake блокирует merge.

### JP-T09.4. Добавить release validation

Зависимости: JP-T09.1–JP-T09.3.

Subtasks:

- [ ] SemVer/package.json/tag consistency.
- [ ] Changelog requirement.
- [ ] Licenses/Third Party Notices.
- [ ] Snapshot/lock/provenance consistency.
- [ ] Smoke install Git tag в clean project.
- [ ] Package content hygiene.

Acceptance criteria:

- [ ] Release tag воспроизводимо устанавливается и проходит smoke.

### JP-T09.5. Выпустить `v0.1.0-rc`

Зависимости: JP-T09.4, completion JP-E01–JP-E08.

Subtasks:

- [ ] Пройти standalone DoD checklist.
- [ ] Зафиксировать known limitations.
- [ ] Создать release notes.
- [ ] Опубликовать pinned tag/commit для EFT integration.

Acceptance criteria:

- [ ] Gate B пройден.
- [ ] EFT integration не используется для сокрытия standalone failures.

## JP-E10. Интеграция package-а в EFT

Приоритет: P0 после Gate B.

Результат эпика: EFT использует package artifact/static loader, сохраняя текущий Jitter runtime и server build wiring.

### JP-T10.1. Подключить pinned UPM package

Зависимости: Gate B.

Subtasks:

- [ ] Добавить Git tag/commit dependency.
- [ ] Запустить compatibility validation.
- [ ] Подтвердить `external-compatible` для текущего EFT Jitter.
- [ ] Зафиксировать actual source hash/runtime ID.

Acceptance criteria:

- [ ] Package import не копирует/не меняет EFT Jitter.

### JP-T10.2. Установить Unity Jitter integration

Зависимости: JP-T10.1.

Subtasks:

- [ ] Install только `JitterIntegration`.
- [ ] Подключить вызов из верхней assembly без cycle.
- [ ] Не менять `EFT.Runtime -> JitterIntegration` dependency.
- [ ] Validate receipt/projection.

Acceptance criteria:

- [ ] Existing `Jitter.Netick.Adapter` остаётся рабочим и на месте.

### JP-T10.3. Установить server runtime projection

Зависимости: JP-T10.1, JP-E07.

Subtasks:

- [ ] Export в `EFT.Server/EFT.Runtime/JitterPhysics/`.
- [ ] Validate SDK default source compilation.
- [ ] Validate receipt against UPM version.
- [ ] Build Release без csproj changes.

Acceptance criteria:

- [ ] `EFT.Runtime.csproj` и `Jitter2.Unity.csproj` не изменены.
- [ ] Server продолжает компилировать current Unity Jitter sources.

### JP-T10.4. Авторить и испечь Shooter static world

Зависимости: JP-T10.1, JP-E04, JP-E06.

Subtasks:

- [ ] Добавить `JitterPhysicsLevel`/profile.
- [ ] Разметить ground + минимум два cover source-а.
- [ ] Bake client artifact.
- [ ] Export embedded server artifact.
- [ ] Зафиксировать hash/runtime ID/counts/size.

Acceptance criteria:

- [ ] Embedded provider восстанавливает exact client bytes/hash.

### JP-T10.5. Интегрировать client initialization

Зависимости: JP-T10.2, JP-T10.4.

Subtasks:

- [ ] Выбрать artifact по level ID до connect.
- [ ] Validate artifact/runtime/tick settings.
- [ ] Подготовить compatibility token.
- [ ] Применить static world после `new World`.
- [ ] Завершить до registry/adopt/dynamic bodies/first step.
- [ ] Fail connect при initialization failure.

Acceptance criteria:

- [ ] `ArtifactValidated < StaticApplied < DynamicBodies < FirstPredictedStep` доказан test/log.
- [ ] Prediction/rollback tick loop не изменён.

### JP-T10.6. Интегрировать server initialization

Зависимости: JP-T10.3, JP-T10.4.

Subtasks:

- [ ] Resolve embedded provider.
- [ ] Validate exact bytes/runtime/tick.
- [ ] Применить static world до gameplay/accept.
- [ ] Вывести self-check.
- [ ] Fail-fast без valid artifact.

Acceptance criteria:

- [ ] `ArtifactValidated < StaticWorldBuilt < SelfCheck < AcceptEnabled` доказан.
- [ ] Authoritative `World.Step` остаётся в текущем server tick loop.

### JP-T10.7. Интегрировать Netick handshake

Зависимости: JP-T03.6, JP-T10.5, JP-T10.6.

Subtasks:

- [ ] Client connection data token.
- [ ] Server strict parser в connection approval.
- [ ] Compare protocol/level/artifact/runtime ID.
- [ ] Safe refusal reason.
- [ ] Initial connect + reconnect tests.

Acceptance criteria:

- [ ] Mismatch отклоняется до player spawn.
- [ ] Package core не получает Netick dependency.

### JP-T10.8. Переключить artifact mode без удаления legacy path

Зависимости: JP-T10.5, JP-T10.6.

Subtasks:

- [ ] Добавить explicit artifact/legacy dev mode.
- [ ] Не вызывать `ShooterMotor.BuildStaticWorld` в artifact mode.
- [ ] Добавить duplicate statics guard/count check.
- [ ] Сохранить legacy fallback только вне acceptance path.

Acceptance criteria:

- [ ] Client/server не создают двойную static geometry.

Epic acceptance:

- [ ] Jitter2 folder, `Jitter2.Unity.csproj`, `EFT.Runtime.csproj`, Dockerfile и `Jitter.Netick.Adapter` не изменены/не перенесены.
- [ ] Runtime Jitter продолжает работать на обеих сторонах.

## JP-E11. EFT end-to-end verification и приёмка

Приоритет: P0.

Результат эпика: Gate C подтверждён воспроизводимыми доказательствами.

### JP-T11.1. Проверить happy-path Shooter gameplay

Зависимости: JP-E10.

Subtasks:

- [ ] Server physics self-check before accept.
- [ ] Client/server одинаковые short/full IDs.
- [ ] Player rests on ground.
- [ ] Movement/cover collision.
- [ ] Grounded/jump.
- [ ] Projectile vs static.
- [ ] Player hit/dynamic regression.

Acceptance criteria:

- [ ] Gameplay работает только на baked static geometry в artifact mode.

### JP-T11.2. Проверить fail-fast и mismatch matrix

Зависимости: JP-E10.

Subtasks:

- [ ] Missing client artifact.
- [ ] Missing server artifact.
- [ ] Corrupt bytes/manifest.
- [ ] Different artifact hash.
- [ ] Different runtimeCompatibilityId.
- [ ] Invalid/oversized handshake.

Acceptance criteria:

- [ ] Ни один invalid scenario не допускает player spawn/physics step с partial world.

### JP-T11.3. Проверить prediction и cross-runtime divergence

Зависимости: JP-T11.1.

Subtasks:

- [ ] 60 секунд movement вдоль static geometry с artificial latency/loss.
- [ ] Проверить отсутствие systematic falling/pushing/teleporting.
- [ ] Offline 300-tick identical input fixture Unity/.NET.
- [ ] Записать max/mean position/rotation/velocity error.
- [ ] Проверить одинаковый obstacle outcome.

Acceptance criteria:

- [ ] Drift bounded в объявленном tolerance.
- [ ] Server authority/reconciliation остаются рабочими.

### JP-T11.4. Выполнить performance smoke

Зависимости: JP-T11.1.

Subtasks:

- [ ] Artifact size/counts.
- [ ] Bake/validation/load time.
- [ ] Managed allocation/peak memory, где возможно.
- [ ] First/steady tick comparison.
- [ ] Проверить отсутствие artifact per-tick allocations.

Acceptance criteria:

- [ ] Shooter binary < 1 MiB.
- [ ] Load/world build < 100 ms на принятой dev machine.
- [ ] Нет заметной fixed-tick regression.

### JP-T11.5. Проверить Release и текущий Docker flow

Зависимости: JP-E10.

Subtasks:

- [ ] `dotnet build EFT.Server/EFT.Server.sln -c Release`.
- [ ] Unity compile/EditMode/PlayMode.
- [ ] Current Docker image build без Dockerfile changes.
- [ ] Container start/self-check.
- [ ] Existing Netick weave/NanoSockets smoke.

Acceptance criteria:

- [ ] Текущий container запускает server с embedded artifact и self-check.

### JP-T11.6. Подготовить verification report и sign-off

Зависимости: JP-T11.1–JP-T11.5.

Subtasks:

- [ ] Commands и raw results.
- [ ] Package/Jitter/artifact/runtime IDs.
- [ ] Counts/size/timings.
- [ ] Topology parity evidence.
- [ ] Mismatch refusal evidence.
- [ ] Prediction/dynamic regression evidence.
- [ ] Known limitations/production spikes.

Acceptance criteria:

- [ ] Gate C checklist подписан ответственными за engine/networking/QA.

## JP-E12. Production discovery и post-v1 backlog

Приоритет: P2; не блокирует `v0.1.0`/EFT POC.

### JP-T12.1. Mesh scalability на representative map

Subtasks:

- [ ] Измерить vertices/triangles/proxies/artifact/load/memory/tick.
- [ ] Сравнить simplified meshes, chunks и tiled sources.
- [ ] Выбрать production representation.

### JP-T12.2. Large-world/origin strategy

Subtasks:

- [ ] Определить coordinate bounds production maps.
- [ ] Проверить float32 precision.
- [ ] Спроектировать origin shift identity для artifact/client/server.

### JP-T12.3. Multi-world server

Subtasks:

- [ ] Artifact registry/cache.
- [ ] Несколько independent `World` в process.
- [ ] Global ID/order determinism tests.
- [ ] Lifecycle/memory isolation.

### JP-T12.4. Production artifact delivery

Subtasks:

- [ ] Выбрать file/content/mount/object registry.
- [ ] Immutable version selection/rollback.
- [ ] Signature/attestation decision.
- [ ] Streaming/chunking decision.

### JP-T12.5. Physics feature extensions

Subtasks:

- [ ] Terrain/height field.
- [ ] Materials catalog.
- [ ] Collision layers/filters.
- [ ] Dynamic prefab shape/body definitions.
- [ ] Constraint recipes.
- [ ] Optional Netick integration package.

Epic acceptance:

- [ ] По каждому spike есть решение, измерения и отдельный production implementation backlog.

## 4. Definition of Ready для Task

Task готова к взятию в работу, если:

- [ ] понятен target repository;
- [ ] выполнены указанные dependencies;
- [ ] известны входные APIs/data;
- [ ] acceptance criteria проверяемы;
- [ ] не требуется незафиксированное изменение существующего EFT Jitter/server build;
- [ ] назначен reviewer для public API/artifact format, если они затрагиваются.

## 5. Definition of Done для Task

Task завершена, если:

- [ ] код/артефакты реализованы;
- [ ] релевантные unit/integration tests добавлены и зелёные;
- [ ] error/failure path покрыт;
- [ ] public API/format/workflow документирован;
- [ ] нет неявных mutations и unrelated changes;
- [ ] exact commands/results приложены к тикету;
- [ ] acceptance criteria отмечены доказательствами, а не только словами.

## 6. Рекомендуемая параллелизация

После JP-E00 и JP-E01 можно вести параллельно:

- Track A: JP-E02 — snapshot/installer/lock;
- Track B: JP-E03 — contracts/codec/token;
- Track C: JP-E04 — authoring/characterization после стабилизации DTO;
- Track D: CI/dev-project skeleton.

После JP-E03 и JP-E05:

- Track A: JP-E06 — Editor UX;
- Track B: JP-E07 — server delivery;
- Track C: JP-E08 — samples/docs/fixtures;
- Track D: JP-E09 — CI hardening.

JP-E10 начинается только после Gate B. JP-E11 начинается после полного integration slice JP-E10, кроме подготовки test harness/fixtures, которую можно начать заранее.

## 7. Итоговая сводка backlog-а

| Epic | Название | Gate | Приоритет |
|---|---|---|---|
| JP-E00 | Baseline и spikes | A | P0 |
| JP-E01 | Repository/package bootstrap | A | P0 |
| JP-E02 | Dormant Jitter, lock, installer | A | P0 |
| JP-E03 | Artifact core и token | A | P0 |
| JP-E04 | Authoring/conversion/bake | B | P0 |
| JP-E05 | Jitter world builder | B | P0 |
| JP-E06 | Editor UX/export | B | P1 |
| JP-E07 | Server delivery/.NET | B | P0 |
| JP-E08 | Dev project/samples/docs | B | P1 |
| JP-E09 | CI/release | B | P0/P1 |
| JP-E10 | EFT integration | C | P0 |
| JP-E11 | EFT verification | C | P0 |
| JP-E12 | Production discovery | Post-v1 | P2 |

Итого: 13 Epics. Jira/Linear может автоматически назначить финальные Task/Subtask IDs при импорте; логические `JP-*` ID рекомендуется сохранить в заголовках или labels для трассировки к этому документу.
