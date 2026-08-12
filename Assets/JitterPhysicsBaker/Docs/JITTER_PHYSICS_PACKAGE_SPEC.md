# Jitter Physics Baker — самостоятельный Unity package (консолидированное ТЗ)

Статус: единое техническое задание. Заменяет предыдущий комплект документов (01–05); все их принятые решения сведены сюда.

Один документ описывает: зачем пакет нужен, архитектурную модель, формат артефакта, authoring, installer, серверную доставку, runtime на клиенте и сервере, интеграцию в EFT, тесты, этапы и Definition of Done. Подходит как ТЗ для инженера и как основа задачи для coding agent.

Рабочие имена (можно изменить до `v0.1.0`; после публикации UPM name, assembly names и receipt format — стабильный API):

```text
Repository: jitter-physics-baker
UPM name:   com.datasakura.jitter-physics-baker
Display:    DataSakura Jitter Physics Baker
Namespace:  DataSakura.JitterPhysics
```

---

## 1. Назначение и ожидаемый результат

Независимый Git UPM package, который можно подключить в EFT и в любой другой проект. Он:

- устанавливается через Unity Package Manager по Git URL + SemVer tag;
- даёт level designer-у explicit authoring и одну команду `Validate + Bake` для статической collision-геометрии уровня;
- запекает детерминированный, версионированный, content-addressed бинарный артефакт (SHA-256), одинаковый для клиента и сервера;
- загружает артефакт в Jitter `World` **в runtime и на Unity-клиенте (Mono/IL2CPP), и на .NET dedicated-сервере** одним общим loader-ом, без повторного runtime bake;
- физика Jitter продолжает крутиться в runtime на обеих сторонах: сервер шагает авторитетный мир, клиент — предиктивный; пакет создаёт одинаковую статическую топологию, tick loop остаётся у потребителя;
- контролирует совместимость внешнего Jitter2 через `jitter2.lock.json`, editor-валидацию и `runtimeCompatibilityId`;
- не зависит от EFT, Netick или конкретного игрового сервера в основном коде;
- имеет отдельный Unity development/QA-проект, samples, документацию, changelog, лицензии.

## 2. Контекст и факты (проверить перед началом работ)

- Референс пайплайна — `com.datasakura.custom-navigation` 0.6.4: explicit authoring, editor-only bake, deterministic binary + manifest + SHA-256, strict fail-fast loader, `Server~`/`Samples~`, installer-паттерн.
- EFT: Unity `6000.3.19f1` (`EFT.Unity`), dedicated server `.NET 10` (`EFT.Server/EFT.Runtime`, UDP 4045, tick rate 30), Netick server-authoritative prediction/rollback.
- Jitter2 (fork, single precision `Real = float`, `SolveMode.Deterministic`, single-threaded step) вендорен в `EFT.Unity/Assets/_Project/EFT/Transport/Jitter2/` (asmdef `Jitter2.Core`) и компилируется сервером через `EFT.Server/Jitter2.Unity/Jitter2.Unity.csproj` (`<Compile Include>` тех же исходников, net10.0, аппаратный SIMD, opt-in `USE_DOUBLE_PRECISION`; Unity-сборка использует define `JITTER_UNITY` c software polyfills).
- Netick-адаптер: `Assets/_Project/EFT/Transport/Jitter.Netick.Adapter/` — `JitterWorldComponent` (создаёт/шагает клиентский `World` в `NetworkFixedUpdate`, хуки `Engine.PreRollback`/`Engine.PostResimulation`, гейт `Sandbox.PhysicsPrediction`), `NetworkJitterBody` (реплицирует pose/velocity/motion type), `JitterBody` (runtime-конвертация Unity Collider → Jitter shape; семантику конвертации пакет обязан повторить, не меняя её).
- Устраняемая проблема: статическая геометрия захардкожена в `ShooterSpec` и строится вручную `ShooterMotor.BuildStaticWorld(World)` на клиенте (`ShooterWorldSetup`) и сервере (`ShooterSimulation`); пайплайна экспорта нет.
- Деплой: корневой `Dockerfile` (two-stage, publish `EFT.Runtime.dll`, повторный Netick weave) и `docker-compose.yml`.

## 3. Архитектурная модель

### 3.1 Физика остаётся внутри match server; отдельного physics HTTP-сервиса нет

Pathfinding у custom-navigation — stateless запрос, поэтому там уместен HTTP-сервис. Физика Jitter — stateful-симуляция матча: результат каждого `World.Step` зависит от input-ов всех игроков в каждом tick, spawn/despawn и lag compensation. Переносится не «query service», а **артефакт**: запечённая статика + общий loader. Роль серверного вычисления играет авторитетный Netick tick, роль предикшена — существующий rollback/resimulation Netick, роль сверки — SHA-256 handshake артефакта при подключении.

### 3.2 Bake = descriptors, не состояние Jitter

Артефакт хранит DTO: world settings, ordered body records, shape records. Loader воссоздаёт мир публичными API (`CreateRigidBody`, конструкторы shape-ов, `AddShape`, `MotionType.Static`). Запрещено сериализовать `RigidBody.Handle`, `ShapeId`, `DynamicTree`, contacts, islands и пресимулировать физику.

### 3.3 Jitter2: external runtime + dormant reference snapshot (зафиксированное решение)

- **Jitter2 в EFT остаётся ровно как сейчас**: `EFT.Unity/Assets/_Project/EFT/Transport/Jitter2/`, серверный `EFT.Server/Jitter2.Unity/Jitter2.Unity.csproj`, Dockerfile — без изменений.
- **Пакет ссылается на `Jitter2.Core` по имени asmdef** (`"useGUIDs": false`) — резолвится на любую копию потребителя, независимо от её расположения.
- **Внутри пакета Jitter2 лежит в `Jitter2~/`** (Unity не компилирует папки с `~`) — референсная спящая копия + installer «Install Jitter2 into Project» для новых проектов. Два компилируемых Jitter2 одновременно невозможны (дубли типов/asmdef), installer это проверяет и блокирует.
- **Рассинхрон версий контролируется `jitter2.lock.json`** (SHA-256 канонического списка исходников): editor-валидация сравнивает Jitter2 проекта с ожидаемым; `runtimeCompatibilityId`-handshake остаётся последней защитой в runtime.
- **`Server~/Tests` компилируют `Jitter2~` напрямую** — копия в пакете всегда проверяется CI, хотя Unity её не собирает.
- **Миграция EFT не трогает ни Jitter2, ни серверную сборку.**

Отклонённые альтернативы: package-owned Jitter2 (требует миграции EFT-сервера и Dockerfile); отдельный пакет `com.datasakura.jitter2` (UPM не резолвит git-зависимости транзитивно); precompiled DLL как DotRecast (серверу нужны исходники под net10.0/SIMD/double, Unity — define `JITTER_UNITY`).

### 3.4 Runtime-физика на клиенте и сервере

Пакет — не «офлайн-инструмент»: его runtime-часть работает в обоих рантаймах.

- Один и тот же portable-код (Contracts + codec + world builder) компилируется Unity (Mono и IL2CPP) и обычным .NET SDK.
- Клиент: артефакт применяется при создании Jitter `World` (в EFT — внутри инициализации `JitterWorldComponent`), до `AdoptUnboundBodies()` и первого `World.Step`; дальше мир шагает в predicted-тиках Netick c rollback/resimulation как сейчас.
- Сервер: артефакт валидируется и применяется к авторитетному `World` до того, как Netick принимает игроков; мир шагает в серверном tick loop (в EFT — `ShooterSimulation`).
- Динамические/кинематические тела продолжают создаваться игровым кодом и синхронизироваться существующим адаптером — пакет их не подменяет.
- Пакет **не владеет tick loop**: `World.Step(...)` вызывает потребитель. Повторное применение одного static-артефакта к тому же миру блокируется guard-ом.

### 3.5 Детерминизм: топология — да, bit-exact шаг — нет

Гарантируется byte-exact артефакт и идентичная упорядоченная статическая топология на обеих сторонах. **Не** гарантируется bit-exact результат `World.Step` между Unity и .NET (software polyfills vs аппаратный SIMD, ~1 ULP в тригонометрии). Сервер авторитетен; дрейф поглощает Netick reconciliation. Acceptance предикшена формулируется поведенчески, а не сравнением хэшей состояний.

### 3.6 Fail-fast вместо silent fallback

Отсутствующий/битый/несовместимый артефакт — это остановка (клиент не начинает connect, сервер не принимает игроков), а не тихое переключение на legacy-геометрию. Никакого hot reload мира активного матча: новый артефакт = новый match world или рестарт процесса.

### 3.7 Сравнение с Custom Navigation

| Аспект | Custom Navigation | Jitter Physics |
| --- | --- | --- |
| Authoring | `NavigationLevel`, sources, profiles | `JitterPhysicsLevel`, `JitterStaticBodySource`, `JitterPhysicsWorldProfile` |
| Bake | editor-only, deterministic, SHA-256 | так же |
| Артефакт | navmesh binary + manifest + asset | `.jphys.bytes` + manifest + asset |
| Loader | strict, fail-fast | так же, один для клиента/сервера |
| 3rd-party ядро | DotRecast precompiled DLL | Jitter2 потребителя + dormant snapshot `Jitter2~/` |
| Server delivery | HTTP upload / export folder | source projection в match server (§10) |
| Runtime «запрос» | `/path` HTTP или локальный scheduler | обычный Netick predicted tick |
| Сверка клиент/сервер | path fingerprint per-query | SHA-256 handshake per-connection + Netick reconciliation |
| Hot reload | да | нет |
| Standalone server | да (`Server~`) | нет; `Server~` = source delivery + .NET tests |

## 4. Границы ответственности

**Пакет отвечает за:** portable contracts/codec/limits/hashing; deterministic-представление статической геометрии; validation совместимости внешнего Jitter2; Unity authoring-компоненты; конвертацию Collider → canonical shape descriptor; Editor window (validate/bake/inspect/export); Unity artifact asset; Jitter-зависимый адаптер «артефакт → shapes/world»; installer Jitter2 и integration; server source export; dev-проект, samples, тесты; cross-runtime parity артефакта/топологии; документацию обновления snapshot-а.

**Пакет не отвечает за:** Netick startup/connection approval/prediction/rollback; prefab/scene/network state layout потребителя; match lifecycle; gameplay (motor, projectiles, lag compensation); доставку артефакта в production-инфраструктуру; динамические сетевые объекты; anti-cheat; отдельный HTTP physics server.

Netick-адаптер остаётся в integration layer потребителя (в EFT — как сейчас) либо позже выделяется в optional package `com.datasakura.jitter-netick`.

**Скоуп v1 (static baking):** `BoxCollider`, `SphereCollider`, `CapsuleCollider`, статический `MeshCollider`. Вне скоупа: dynamic/kinematic baking, joints/constraints, triggers, Terrain/height fields, collision layers/filters, runtime rebake, hot reload, стриминг. Точки расширения для dynamic-описаний (shape/body definitions, factory, constraint recipes) резервируются, но пустой dynamic API в `v0.1.0` не публикуется.

## 5. Репозиторий и структура

```text
Work/
├── jitter-physics-baker/                 # отдельный Git repo; package.json в корне
└── jitter-physics-baker-unity-project/   # отдельный Unity dev/QA проект (подключает "file:../../jitter-physics-baker")
```

Потребители пинят tag/commit: `"com.datasakura.jitter-physics-baker": "https://<git-host>/<org>/jitter-physics-baker.git#v0.1.0"`. Плавающая ветка — только для dev-проекта. Dev-проект обязан проверять оба режима: `External Jitter` (Jitter уже в проекте) и `Installed Fallback` (Jitter ставится из `Jitter2~/`).

```text
jitter-physics-baker/
├── package.json  README.md  CHANGELOG.md  LICENSE.md  Third Party Notices.md
├── jitter2.lock.json
├── Documentation~/            # artifact-format-v1, authoring-guide, installing-jitter2,
│                              # runtime-integration, server-source-integration, upgrading-jitter2
├── Runtime/                   # всегда компилируется; БЕЗ Jitter dependency
│   ├── Contracts/  ArtifactCodec/  Validation/  UnityArtifact/
├── Authoring/                 # Unity authoring; без Jitter dependency
├── Editor/                    # Bootstrap (discovery/installer), Baking, Inspectors, Export
├── Jitter2~/                  # спящий Jitter reference snapshot
│   ├── Runtime/               # canonical .cs sources
│   ├── StandaloneUnity/       # standalone asmdef template (без ссылок на EFT.*)
│   ├── LICENSE.md  PATCHES.md
├── JitterIntegration~/        # source adapter, зависит от Jitter2; ставится installer-ом
│   ├── Runtime/  EditorDiagnostics/  UnityAssemblyTemplate/  install-manifest.json
├── Server~/
│   ├── RuntimeSources/        # projection recipe для consumer server
│   ├── Tests/                 # .NET 10 tests, компилируют Jitter2~ напрямую
│   └── README.md
├── Tests/                     # Editor/ Runtime/ (UTF)
├── Samples~/                  # Static World Baking, Runtime Loading, Cross Runtime Parity,
│                              # Netick Integration Reference (только пример glue/handshake)
└── tools~/                    # sync-jitter2, verify-jitter2-lock, validate-package, test-dotnet
```

Правила: компилируемые `Runtime`/`Authoring`/bootstrap-`Editor` не ссылаются на Jitter; Jitter-зависимый код живёт в `JitterIntegration~/` до явной установки; `Server~/Tests` включает исходники ссылками, без ручных копий; `Library/PackageCache` никогда не используется как server build dependency.

## 6. Assemblies и bootstrap-проблема

Если package assembly сразу ссылается на отсутствующий `Jitter2.Core`, чистый проект получит compile error до запуска installer-а. Поэтому ядро пакета компилируется без Jitter.

### 6.1 Всегда доступные assemblies

| Assembly | Зависимости | Назначение |
| --- | --- | --- |
| `DataSakura.JitterPhysics.Contracts` | BCL | Artifact DTO, IDs, manifest; `noEngineReferences: true` |
| `DataSakura.JitterPhysics.ArtifactCodec` | Contracts | Binary codec, limits, SHA-256; `noEngineReferences: true` |
| `DataSakura.JitterPhysics.UnityArtifact` | Contracts, UnityEngine | Artifact asset / TextAsset bridge |
| `DataSakura.JitterPhysics.Authoring` | UnityEngine | Level/source/profile authoring |
| `DataSakura.JitterPhysics.Editor` | всё выше, UnityEditor | Bake, validation, installer/export UX |

### 6.2 Устанавливаемая integration assembly

Installer создаёт consumer-owned assembly из `JitterIntegration~/`:

```text
Assets/DataSakura/JitterPhysics/Integration/DataSakura.JitterPhysics.JitterIntegration.asmdef
  references (по именам): Contracts, ArtifactCodec, Jitter2.Core
```

Важно для EFT: текущий `Jitter2.Core.asmdef` ссылается на `EFT.Runtime`, поэтому `EFT.Runtime` не должен ссылаться на integration assembly (assembly cycle). Integration вызывается из более верхнего слоя (`EFT.Shared`/`EFT.Shooter`/отдельная integration assembly).

### 6.3 Standalone asmdef для fallback Jitter

Для новых проектов installer использует шаблон из `Jitter2~/StandaloneUnity`: assembly с тем же именем `Jitter2.Core`, без ссылок на `EFT.*`/Netick, `allowUnsafeCode`, поддерживаемый compile profile, без глобальных правок `ProjectSettings`. Consumer-specific asmdef не входит в canonical source hash.

## 7. Installer и lifecycle

Меню: `Tools > DataSakura > Jitter Physics > Setup` с actions: `Validate Installation`, `Install Jitter2 into Project`, `Install/Update Jitter Integration`, `Install Server Runtime Sources...`, `Show Compatibility Report`, `Remove Package-Owned Installation`.

**Discovery** — через compilation/assembly metadata и AssetDatabase, не по известному пути: существует ли `Jitter2.Core`; source или precompiled plugin; путь; число кандидатов; source hash и compile profile; установлен ли integration adapter; ownership по receipt.

**Алгоритм:**

```text
Package imported
  -> bootstrap assemblies компилируются без Jitter
  -> Validate / Install (явная команда пользователя или CI)
      -> Jitter2.Core нет         -> скопировать Jitter2~ (standalone template),
                                     проверить hash копии, установить JitterIntegration~
      -> один совместимый         -> Jitter НЕ копировать; установить/обновить только JitterIntegration~
      -> дубликат/несовместимый   -> стоп; показать пути, actual/expected версии, remediation
```

Копирование через staging-папку + перемещение, затем `AssetDatabase.Refresh`.

**Безопасность:** installer не перезаписывает и не удаляет внешние файлы; перед update проверяет хэши ранее установленного; блокирует update, если package-owned файлы изменены вручную; пишет installation receipt (пути, хэши, версия пакета, ownership); при uninstall удаляет только неизменённые файлы из receipt; не пишет вне Unity-проекта без явного выбора; не меняет scripting defines / глобальный `csc.rsp`. Никаких мутаций из `[InitializeOnLoad]`/import — только явные команды.

Default paths: `Assets/DataSakura/ThirdParty/Jitter2/`, `Assets/DataSakura/JitterPhysics/Integration/`, `Assets/DataSakura/JitterPhysics/InstallationReceipt.json`. Для EFT ownership Jitter-а в receipt = `external`; installer не трогает `Assets/_Project/EFT/Transport/Jitter2`.

## 8. `jitter2.lock.json` и runtimeCompatibilityId

Lock описывает поддерживаемую семантическую версию Jitter source set (не расположение):

```json
{
  "schemaVersion": 1,
  "assemblyName": "Jitter2.Core",
  "upstreamRepository": "https://github.com/notgiven688/jitterphysics2",
  "upstreamCommit": "<commit>",
  "patchSetId": "datasakura-jitter2-<revision>",
  "sourceContentHash": "sha256:<hex>",
  "integrationApiVersion": 1,
  "compileProfile": {
    "precision": "f32",
    "allowUnsafe": true,
    "unityDefine": "JITTER_UNITY",
    "polyfillProfile": "unity-supported-version",
    "intrinsicsProfile": "unity-supported-version"
  },
  "includedFiles": ["Runtime/**/*.cs", "Runtime/**/csc.rsp"],
  "excludedFiles": ["**/*.meta", "**/*.asmdef", "**/bin/**", "**/obj/**", "**/Tests/**"]
}
```

**Source hash (детерминированный):** выбрать файлы по include/exclude → relative paths к `/` + ordinal-сортировка → нормализовать encoding/line endings (или гарантировать `.gitattributes` и валидировать) → в digest: relative path, byte length, normalized bytes каждого файла + canonical compile profile → SHA-256. Не хешировать timestamps, absolute paths, `.meta`, consumer asmdef, README, build output.

**Source of truth и sync:** для POC истина — Jitter2 в EFT; `Jitter2~/Runtime` — автоматически синхронизируемый release snapshot (ручные правки в нём запрещены). Обновление: зафиксировать revision в EFT → `tools~/sync-jitter2 --source <path>` → обновить patch manifest/compile profile → пересчитать lock → package self-tests → EFT consumer CI → атомарный release + migration note. Правка Jitter только в EFT без обновления lock = несовместимое состояние, блокирует bake/release. Смена source-направления (выделение Jitter2 в свой repo) — отдельный ADR, формат артефакта не меняется.

**`runtimeCompatibilityId`** вычисляется автоматически (не руками):

```text
runtimeCompatibilityId = SHA-256(artifactSchemaVersion + sourceContentHash + precisionMode
  + compileProfileId + colliderConversionSemanticsVersion
  + shapeConstructionSemanticsVersion + physicsWorldBuilderVersion
  + worldAffectingDefaultsVersion)
```

Правила: baker пишет ID в manifest и client asset; server integration знает ожидаемый ID той же release; bake запрещён при несовпадении внешнего Jitter и lock; сервер не грузит артефакт с чужим ID; handshake отклоняет несовместимые пары; изменение runtime-семантики меняет ID даже без смены схемы артефакта. UPM SemVer, artifact schema и compatibility ID — три независимые версии. Handshake — последняя защита, он не заменяет build/CI-валидацию реально компилируемых исходников.

## 9. Authoring, детерминизм и artifact v1

### 9.1 Authoring model

- `JitterPhysicsLevel` — один на сцену (validator: error при 0 или >1): `levelId` (sanitized), `geometryRoot`, `worldProfile`, ссылка на последний артефакт.
- `JitterStaticBodySource` — explicit-маркер корня одного статического тела: стабильный сериализованный `sourceId`, `includeChildren`, `includeInactiveChildren=false`, friction (0.2), restitution (0.0). Только Collider-ы под explicit source попадают в bake; глобальный сбор Collider-ов сцены запрещён.
- `JitterPhysicsWorldProfile` (ScriptableObject): gravity; `SolveMode.Deterministic` (единственный допустимый); `multiThread=false` (инвариант предикшена); substeps; solver/relaxation iterations; allow deactivation; expected tick rate (30). Все runtime-affecting настройки — в артефакте или проверяются против него; скрытых разных defaults на клиенте/сервере быть не может.

### 9.2 Конвертация Collider-ов (семантика = текущему `JitterBody`)

- `BoxCollider` → `BoxShape` (полный размер; center, rotation, абсолютный lossy scale).
- `SphereCollider` → `SphereShape`; non-uniform scale → max abs axis + warning о консервативном приближении.
- `CapsuleCollider` → `CapsuleShape`; radius по поперечным осям, cylinder length `max(0, scaledHeight - 2*radius)`, коррекция оси для X/Z direction.
- Статический `MeshCollider` → `TriangleMesh` (в артефакте — вершины+индексы, не готовые shape-ы); полная transform matrix в local space body; при отрицательном determinant — исправление winding; индексы валидируются, кратность 3; degenerate triangles → reject с диагностикой.
- Общие правила: trigger — validation error; disabled/inactive — не включаются; NaN/Infinity — error; нулевая геометрия — error с hierarchy path; shear у primitive — error; negative scale — только если конвертер гарантирует корректность, иначе reject; порядок не зависит от `FindObjectsByType`/instance ID/hash map enumeration.

### 9.3 Канонизация (обязательна для byte-exact bake)

1. Sources сортируются ordinal по `sourceId`; Collider-ы внутри — по каноническому key (relative hierarchy path с sibling index + component index + тип).
2. Quaternion нормализуется, `q`/`-q` приводятся к одной форме; `-0.0f` → `+0.0f`.
3. Строки — UTF-8 без BOM, ordinal.
4. Timestamp, absolute paths, instance ID, machine/user, случайные GUID в бинарь не попадают.

Два bake неизменённой сцены обязаны дать byte-for-byte одинаковый файл и SHA-256.

### 9.4 Artifact v1

```text
<levelId>.<hash12>.jphys.bytes
<levelId>.<hash12>.manifest.json
<levelId>.artifact.asset          # ScriptableObject + TextAsset; runtime повторно проверяет hash
```

Default client folder: `Assets/Generated/JitterPhysics/`.

Manifest (минимум): `schemaVersion "1"`, `runtimeCompatibilityId`, `generatorVersion`, `levelId`, `artifactHash` (64 lower hex), `bodyCount/shapeCount/vertexCount/triangleCount`, `tickRate`, `fileName`. Nondeterministic-поля (createdAt и т.п.) не участвуют в identity.

Binary: fixed magic; schema version; little-endian; IEEE-754 float32; runtimeCompatibilityId; levelId; world settings; ordered body records (sourceId, position/orientation, friction/restitution, shape count); ordered shape records (stable key, тип, local pose, payload: Box=size, Sphere=radius, Capsule=radius+length, Mesh=vertices+indices); length-prefixed bounded UTF-8 strings; explicit counts. Layout фиксируется golden-bytes-тестом; изменение после merge = bump schema version.

**Loader safety (strict, fail-fast):** magic/schema/compatibility/endianness; SHA-256 до parse; manifest counts == binary counts; лимиты на размер/counts/strings; finite floats; нормализованные quaternion-ы; уникальные IDs; валидные mesh-индексы; отсутствие trailing bytes. Ошибка → typed result с levelId/hash/причиной; partial world не продолжает симуляцию (тела удаляются или мир dispose-ится целиком).

### 9.5 Portable API

```csharp
// без Jitter-типов (Contracts/Codec):
PhysicsArtifact artifact = PhysicsArtifactReader.Read(stream, manifest);
PhysicsArtifactValidationResult result = PhysicsArtifactValidator.Validate(artifact);

// Jitter-зависимый adapter (JitterIntegration):
World world = new World();
PhysicsWorldBuildResult build = JitterPhysicsWorldBuilder.Apply(world, artifact);
// build: metrics (counts, elapsed), typed errors; повторный Apply к тому же миру — guard
```

`World.Step` — у потребителя (Netick tick на клиенте и сервере).

## 10. Server delivery model

Пакет не поставляет physics server — он поставляет source-compatible runtime, встраиваемый в match server и компилируемый против Jitter-копии потребителя.

Причина source delivery (а не DLL): Unity использует assembly `Jitter2.Core`, EFT-сервер собирает те же исходники как `Jitter2.Unity` — один precompiled Jitter-зависимый бинарь не совместим с обеими identity без правок серверного проекта.

`Server~` содержит: .NET 10 test project; Contracts/codec включены ссылками из package source; adapter — из `JitterIntegration~`; self-tests компилируют `Jitter2~/Runtime` напрямую; source export manifest/tooling; инструкцию интеграции.

**Интеграция в EFT-сервер (без правок Jitter2.Unity.csproj и Dockerfile):**

- `Install Server Runtime Sources...` экспортирует versioned generated projection в `EFT.Server/EFT.Runtime/JitterPhysics/`; SDK-style csproj компилирует `.cs` своей папки автоматически.
- Exported-файлы — generated/managed: receipt с версией и хэшами, ручные правки запрещены, EFT CI валидирует projection против установленного UPM-пакета.
- Для EFT POC Editor action `Export Embedded Server Artifact` превращает **точные уже испечённые bytes** и canonical manifest в generated `.g.cs` provider внутри `EFT.Server/EFT.Runtime/JitterPhysics/Generated/`. Provider разбивает payload на безопасные chunks, восстанавливает exact bytes один раз при startup и повторно проверяет SHA-256. Благодаря SDK default compile glob не меняются ни `EFT.Runtime.csproj`, ни `Jitter2.Unity.csproj`, ни Dockerfile.
- Для других проектов и больших production-карт package также предоставляет `FilePhysicsArtifactProvider`: consumer сам доставляет immutable `.jphys.bytes`/manifest через publish content, mount или artifact registry. Добавление `Content`-rule/volume относится к deploy-интеграции конкретного consumer-а и не требуется EFT POC.
- Порядок старта сервера: resolve configured provider (`embedded` либо CLI `--physics-manifest <path>`) → получить exact bytes/manifest → проверка schema/ID/hash/counts/tick rate → построение мира → self-check лог (levelId, short hash, counts, elapsed) → только затем Netick connection approval. Smoke-check Docker обязан искать self-check строку.

Embedded provider допустим для POC и малых fixtures; для него задаётся жёсткий size cap. Он не считается production delivery strategy для большой карты, но сохраняет тот же artifact format и exact binary identity.

После стабилизации identity/API source export можно заменить NuGet-пакетом (не условие v1).

## 11. Netick-интеграция и handshake (consumer layer / sample)

Пакет не ссылается на Netick. Sample `Netick Integration` (или integration layer потребителя) содержит:

- перенос/пример адаптера (`JitterWorldComponent`, `NetworkJitterBody`, `JitterBody`);
- handshake-кодек: `magic(4) + protocolVersion(1) + levelIdLen(1) + levelId + artifactSha256(32) + runtimeCompatibilityId(32)`; ограниченные длины; без BinaryFormatter/reflection.
- Клиент: формирует payload из валидированного артефакта, передаёт через `NetworkSandbox.Connect(..., connectionData, ...)`, показывает причину refuse.
- Сервер: парсит в `OnConnectRequest`, сравнивает version/levelId/artifact hash/runtime ID (constant-time где практично), принимает только exact match, логирует expected/actual short IDs + endpoint без полного payload.

Handshake ловит «другую карту у клиента» до spawn; anti-cheat-защитой не является.

## 12. Editor UX

Окно `Tools > DataSakura > Jitter Physics > Physics Baker`, вкладки:

1. `Setup` — активный Jitter (путь, ownership, hash, совместимость), integration status.
2. `Level & Sources` — level/profile/sources, validation issues c ping/select объекта.
3. `Bake` — Validate, Bake for Client, counts/timings/hash, client asset.
4. `Artifacts` — inspect/export/delete; `Export Artifact to Folder` копирует уже испечённый бинарь (silent rebuild запрещён).
5. `Diagnostics` — roundtrip, topology fingerprint, determinism check.
6. `About` — версии package/Jitter/schema/runtime ID, licenses.

Запись файлов — temp + atomic replace; неуспешный bake не портит предыдущий артефакт. Bake не выполняется в Play Mode.

**Наблюдаемость:** bake start/success/failure; levelId + full hash (editor) / short hash (runtime); counts; compatibility ID; «client world initialized before first step»; «server world initialized before accept»; handshake accept/refuse reason; отсутствие legacy static world в artifact mode. Не логировать бинарные payload-ы, полные connection data, пользовательские абсолютные пути.

## 13. Тестовая матрица

### 13.1 Package bootstrap

- import в чистый Unity-проект без Jitter: bootstrap assemblies компилируются, ошибок нет;
- installer ставит fallback Jitter + integration; повторный запуск idempotent;
- uninstall удаляет только package-owned неизменённые файлы; изменённые — оставляет с отчётом;
- пакет не меняет global defines/csc.rsp/ProjectSettings.

### 13.2 External Jitter

- совместимая внешняя `Jitter2.Core` находится независимо от пути; Jitter не копируется, ставится только integration;
- несовместимый source hash блокирует bake; дубликат assembly блокирует установку;
- precompiled plugin без подтверждённого профиля — блок или explicit supported profile.

### 13.3 Package self-tests

- codec: golden bytes; roundtrip; determinism repeat; канонизация (`-0.0`, `q/-q`); corrupt-матрица (magic/schema/ID/hash/усечение/trailing/counts/strings/NaN/дубликаты/индексы) — typed error, мир не тронут; safety caps; manifest cross-check;
- world builder: descriptor → мир (counts/порядок/static/friction); raycast-проверки по каждому типу shape (ε ≤ 1e-4); повторная загрузка → идентичная топология; ошибка на середине → чистый rollback; metrics заполнены;
- конвертеры соответствуют characterization-тестам текущего `JitterBody` (снять ДО изменений);
- `Server~/Tests` компилируют `Jitter2~/Runtime` напрямую (.NET 10); Unity Mono EditMode/PlayMode; IL2CPP build для целевой mobile-платформы;
- cross-runtime: Unity и .NET читают один артефакт → exact decode parity и exact topology fingerprint parity; physics ticks сравниваются с tolerance (bit-exact не объявляется).
- embedded server provider восстанавливает byte-for-byte тот же artifact и hash; oversized artifact отклоняется до генерации/компиляции.

### 13.4 Consumer (EFT) tests

- hash фактического `Assets/_Project/EFT/Transport/Jitter2` совпадает с lock установленного пакета; `Jitter2.Unity.csproj` собирает те же исходники;
- server projection совпадает с receipt релиза;
- один артефакт загружается Unity-клиентом и EFT-сервером; topology fingerprint совпадает;
- несовпадающий `runtimeCompatibilityId`/hash → refuse до spawn; пустой/oversized payload → refuse;
- клиент без валидного артефакта не начинает connect; сервер без артефакта не принимает игроков; порядок инициализации подтверждён self-check логами;
- Shooter в artifact mode: grounded, движение, прыжок, projectile-vs-static, hit по игроку; legacy `BuildStaticWorld` не вызывается (нет двойной статики);
- prediction: при искусственной задержке/потерях 60 с движения вдоль статики — отзывчиво, без систематического проваливания/отталкивания/телепортов;
- offline comparison fixture выполняет не менее 300 ticks одинакового input sequence в Unity и .NET, фиксирует max/mean position/rotation/velocity error; unbounded drift или разный obstacle outcome отклоняет POC;
- регрессия существующей dynamic physics/Netick — smoke проходит.

Self-tests проверяют спящую копию, consumer-tests — реально компилируемую внешнюю; одно не заменяет другое.

### 13.5 Performance smoke (v1, защита от неработоспособности)

- bake только в editor, не блокирует runtime; load артефакта — один раз на мир; после загрузки статика не создаёт per-tick аллокаций;
- POC-сцена: load < 100 ms, binary < 1 MiB; loader имеет hard safety caps;
- для больших карт — сперва измерения, мобильные бюджеты не обещаются до representative map.

## 14. CI/CD и release

Package pipeline: layout/manifests/licenses → verify `jitter2.lock.json` против `Jitter2~/Runtime` → clean-import без Jitter (bootstrap) → fallback install + Unity tests → external-Jitter fixture tests → IL2CPP test player → .NET 10 `Server~/Tests` → cross-runtime golden artifact/topology → source export/receipt validation → SemVer/changelog/tag → release; determinism job: bake golden-сцены на двух ОС, сравнение SHA-256; изменение golden bytes без bump schemaVersion блокирует merge.

Consumer (EFT) pipeline: pin tag → validate EFT Jitter hash против lock → validate Unity/server projections → build Unity + `EFT.Server` → cross-runtime artifact/handshake tests → dynamic/Netick smoke. Release пакета и обновление EFT lock/projections — атомарное integration change.

## 15. Этапы реализации

- **Phase 0. Repository bootstrap** — repo + dev-проект; manifests/licenses/readme/changelog; Jitter-free assembly graph; CI skeleton; clean UPM import test.
- **Phase 1. Dormant snapshot и installer** — импорт snapshot в `Jitter2~/`; standalone asmdef template; lock + deterministic hash tool; discovery/installer/receipt/uninstall; тесты external/fallback/duplicate/incompatible.
- **Phase 2. Contracts, codec, integration** — DTO/codec/hash/limits; canonical shape descriptors; world builder в `JitterIntegration~/`; `.NET Server~/Tests` против snapshot; golden bytes/topology parity.
- **Phase 3. Authoring и Editor** — level/source/profile; конвертеры; validator; deterministic bake; artifact asset/export; окно + diagnostics.
- **Phase 4. Server source delivery** — projection manifest/tool; configurable target; receipt/update validation; .NET consumer fixture; artifact publish docs.
- **Phase 5. Samples, QA, release** — dev-сцены (генерируются editor-скриптами, без хрупких GUID); `Samples~`; determinism/roundtrip/perf smoke; tag `v0.1.0`; installation/migration docs.
- **Phase 6. EFT consumer POC** — интеграция по §16; Shooter artifact; handshake; artifact mode + dynamic regression.

Каждая фаза оставляет репозитории собираемыми. Перед Phase 2/3 снять characterization-тесты текущего `JitterBody`-маппинга в EFT.

## 16. Интеграция в EFT (после standalone DoD)

1. Pin UPM-пакета на tag/commit.
2. `Validate Installation`: существующий Jitter EFT определяется как `external-compatible`.
3. Установить только `JitterIntegration` (Jitter2 не копировать/не перемещать).
4. `Install Server Runtime Sources...` → `EFT.Server/EFT.Runtime/JitterPhysics/`.
5. `Export Embedded Server Artifact` → generated provider в существующем `EFT.Runtime` project; проверить exact SHA-256 после восстановления bytes.
6. `EFT.Runtime.csproj`, `Jitter2.Unity.csproj` и Dockerfile для POC не менять; проверить physics self-check в текущем container build.
7. EFT-специфика: выбор артефакта по levelId, Netick handshake, клиентский bootstrap до `Network.StartAsClient` (артефакт → верификация → connection data → world initializer для `JitterWorldComponent`).
8. Испечь Shooter static world (ground + 2 cover cubes, эквивалент `ShooterSpec`).
9. Artifact mode отключает legacy `BuildStaticWorld` (обе стороны); legacy fallback — только явный dev mode, вне acceptance.
10. Пройти consumer test matrix (§13.4).

Запрещено в рамках миграции: удалять `Assets/_Project/EFT/Transport/Jitter2`; переключать EFT на fallback-Jitter пакета; менять assembly identity серверного Jitter; одновременно мигрировать dynamic bodies/prediction (отдельная задача).

## 17. Definition of Done

- [ ] Пакет ставится по pinned Git URL в чистый проект без Jitter; до installer-а нет compile errors.
- [ ] Fallback Jitter ставится явной командой; совместимый внешний Jitter не копируется и не модифицируется; дубликат/несовместимый — блокируется.
- [ ] Installer: receipt, idempotent update, безопасный uninstall; никаких неявных мутаций при import.
- [ ] Основные assemblies без ссылок на EFT/Netick; Jitter-зависимый код изолирован в устанавливаемой projection.
- [ ] Lock hash детерминирован; `runtimeCompatibilityId` вычисляется автоматически.
- [ ] `Server~/Tests` компилируют dormant snapshot; EFT CI отдельно валидирует внешнюю копию.
- [ ] Unity Mono, IL2CPP и .NET 10 тесты зелёные; Unity и .NET читают один артефакт и строят идентичную статическую топологию.
- [ ] Bake детерминирован (byte-exact повтор), артефакт v1 с manifest и strict loader-ом; golden bytes зафиксированы.
- [ ] EFT-POC: Shooter на baked-геометрии (движение/прыжок/projectile/prediction), handshake отклоняет чужой hash до spawn, legacy static отключён в artifact mode, `dotnet build EFT.Server.sln -c Release` + Unity compile + Docker smoke зелёные, SHA-256 артефакта стабилен.
- [ ] EFT POC использует generated embedded artifact provider; `EFT.Runtime.csproj`, `Jitter2.Unity.csproj` и Dockerfile не изменены, container physics self-check проходит.
- [ ] Документация: artifact schema, authoring guide, installing/upgrading Jitter2, runtime/server integration, migration guide, CHANGELOG, Third Party Notices; явный disclaimer о cross-runtime детерминизме.

## 18. Анти-паттерны (наличие любого = решение не готово)

- package assembly жёстко ссылается на отсутствующий `Jitter2.Core` и ломает clean import;
- пакет автоматически компилирует `Jitter2~/` или в проекте существуют две `Jitter2.Core`;
- installer перезаписывает/удаляет внешний Jitter; import выполняет неявные мутации;
- совместимость определяется только версией пакета без source/compile hash; `runtimeCompatibilityId` задан вручную;
- self-tests зелёные, но фактический Jitter потребителя не проверяется;
- server build читает исходники из `Library/PackageCache`; projection без receipt/hash validation;
- handshake сравнивает только artifact hash, но не `runtimeCompatibilityId`;
- embedded provider меняет bytes, не проверяет hash или используется без size cap;
- Unity и сервер используют независимые реализации codec/loader;
- сериализация внутренностей Jitter, runtime bake, hot reload активного мира, отдельный HTTP physics server;
- миграция EFT удаляет или заменяет текущий Jitter; артефакт недетерминирован.

## 19. Риски и обязательные spike-и

| ID | Риск | Митигация |
| --- | --- | --- |
| R-1 | Float-дрейф Unity vs .NET больше ожидаемого: predicted-клиент «дрожит» на статике | Поведенческие тесты §13.4; сервер авторитетен; при провале — `USE_DOUBLE_PRECISION` на обеих сторонах или канонические software-интринсики |
| R-2 | Недетерминированный bake на разных машинах | Канонизация §9.3, golden bytes, determinism CI на двух ОС |
| R-3 | MeshCollider больших карт раздувает артефакт/загрузку | Safety caps, лимиты v1, измерения до обещаний mobile-бюджетов |
| R-4 | Правка Jitter2 в EFT без обновления lock — тихий рассинхрон | Lock-check в editor и EFT CI; bake блокируется; handshake — последняя защита |
| R-5 | Двойная статика при неполной миграции | Fail-fast: artifact mode запрещает legacy path; self-check логи |
| R-6 | Деградация детерминизма после будущих правок Jitter2 | Пост-v1: offline determinism harness (запись input-ов, replay на обоих рантаймах, сравнение траекторий с tolerance) |
| R-7 | Assembly cycle с `EFT.Runtime` | Integration вызывается из верхнего слоя (§6.2); в пакете нет `[Networked]`-типов |
| R-8 | Negative scale/shear дают неверные shape-ы | Validator отклоняет негарантируемые случаи |
| R-9 | Embedded `.g.cs` становится слишком большим для compiler/build | Жёсткий POC size cap; большие карты используют file/mount provider без изменения artifact format |

Spike-вопросы до production: mesh scalability (представительная карта: размер, load, память); процедура bump compatibility ID при апдейте fork-а; multi-world сервер (несколько матчей в процессе); large world coordinates / origin shift; Terrain/height fields; physics materials catalog; collision layers/filters; streaming chunks; хранение production-артефактов (Git/LFS/registry).

## 20. Deliverables

1. Git-репозиторий пакета + отдельный Unity dev/QA-проект.
2. Git-installable `v0.1.0`.
3. `Jitter2~/` snapshot с license/patch manifest; `jitter2.lock.json` + sync/hash tooling.
4. Safe installer + installation receipt.
5. Authoring + baker + artifact v1 + strict loader + Jitter world builder.
6. Server runtime source projection + .NET 10 tests.
7. Unity/.NET/cross-runtime test suite; samples и demo-сцены.
8. Документация (`Documentation~`), CHANGELOG, Third Party Notices, EFT consumer integration guide.
9. Verification report EFT-POC: хэши, counts, тайминги, команды тестов, известные ограничения.

## 21. Execution brief для инженера или coding agent

Этот файл является единственным источником требований для package-а. Не восстанавливать удалённые черновики 01–05 и не создавать параллельную архитектуру.

### До изменений

1. Найти и полностью прочитать repository instructions (`AGENTS.md`), если они есть.
2. Проверить `git status`; существующие изменения принадлежат пользователю.
3. Зафиксировать baseline package/dev-project build и tests.
4. Для EFT integration прочитать минимум:
   - `EFT.Server/EFT.Runtime/EFT.Runtime.csproj`;
   - `EFT.Server/Jitter2.Unity/Jitter2.Unity.csproj`;
   - корневой `Dockerfile`;
   - server world/bootstrap и connection approval;
   - `EFT.Unity/Assets/_Project/EFT/Transport/Jitter2/`;
   - `EFT.Unity/Assets/_Project/EFT/Transport/Jitter.Netick.Adapter/`;
   - client Jitter world/bootstrap;
   - текущий `JitterBody` collider conversion;
   - все call sites `ShooterMotor.BuildStaticWorld`;
   - Custom Navigation package layout, artifact flow и installers.
5. Коротко зафиксировать фактический runtime flow клиента/сервера и места создания static geometry.

### Порядок работы

1. Реализовывать phases из §15 последовательно.
2. После каждой phase оставлять repositories в собираемом состоянии.
3. Сначала зафиксировать characterization текущей Box/Sphere/Capsule semantics, затем переносить её в baker.
4. После Contracts/codec зафиксировать golden bytes до runtime integration.
5. После Jitter installer-а отдельно доказать clean import, external reuse и duplicate rejection.
6. После world builder-а доказать Unity/.NET topology parity.
7. После server projection доказать, что он не зависит от `Library/PackageCache`.
8. После EFT integration доказать runtime stepping, prediction и mismatch refusal end-to-end.

### Ограничения выполнения

- не переносить и не рефакторить существующий EFT Jitter2 без отдельной задачи;
- не удалять и не переносить текущий `Jitter.Netick.Adapter`;
- не создавать второй codec/world builder;
- не сериализовать Jitter internals;
- не использовать silent fallback;
- не менять unrelated files;
- не заявлять bit-exact cross-runtime determinism;
- не commit/push без отдельного запроса.

### Финальный отчёт исполнителя

Отчёт обязан содержать:

- фактическую архитектуру и все отклонения от ТЗ;
- список созданных/изменённых файлов;
- точные build/test commands и результаты;
- package/Jitter source hash/runtimeCompatibilityId;
- artifact level/full SHA-256/size/counts;
- bake/load timings;
- topology parity evidence;
- handshake mismatch evidence;
- подтверждение client/server runtime `World.Step` и dynamic regression;
- известные ограничения и незакрытые production spikes.

Нельзя называть задачу выполненной, если server/client используют разные loader implementations, одна сторона всё ещё строит другую static geometry, mismatch допускает spawn или runtime Jitter simulation была заменена static-only поведением.
