# Обновление Jitter Physics Baker после Math Precision Migration

Дата подготовки: 2026-09-02.

Этот документ предназначен для разработчика Unity-клиента, владельца dedicated server и
инженера, который доставляет baked physics content. Он описывает согласованное обновление с
опубликованного `0.0.12` на подготовленный `0.7.0`.

> Важно: Git tag `v0.7.0` считается доступным только после фактической публикации и проверки
> remote. До этого используйте commit ветки миграции только как development dependency.

| Параметр | Значение |
| --- | --- |
| Предыдущая версия | `0.0.12`, tag `v0.0.12` |
| Целевая версия | `0.7.0`, будущий tag `v0.7.0` |
| Artifact schema | `1`, binary layout сохранён |
| Старый runtime compatibility ID | `ca8283611d3221120e69e23c4c028720537de4867f1de53df3752db85cd32006` |
| Новый runtime compatibility ID | `71e9d01f4006a8e1d097beb047efa8b8aabbe24895cb8d50531c764031c9aa4b` |
| Jitter2 source hash | `sha256:ca940ca6483ffcedf65854719396cec2d9e038cc43c01e7d35d147cd70766940` |
| Compile profile ID | `a2925211b983330117414426be9bf8a2798ce9169c1206e1e55178f708cfa72e` |
| `Jitter2.Core.dll` SHA-256 | `1e0aea7a6da1e3887ce90eabe6b508341870b62992b2c79d09382586db3e0321` |
| Требуется re-bake | Да, для каждого уровня |
| Требуется обновить integration | Да, явной командой |
| Требуется обновить server projection | Да, явной командой |

## Что изменилось

- Package-owned Jitter2 приведён к одному canonical source/profile и reproducible prebuilt DLL.
- `Jitter2.LinearMath.StableMath` стал публичным supported deterministic f32 API.
- Authoritative runtime records используют `JVector`, `JQuaternion` и canonical `Real` profile.
- Unity math остаётся в одном установленном Unity-to-Jitter boundary.
- Unity runtime и dedicated server строят static topology одним
  `JitterPhysicsWorldBuilder` из native records.
- File и embedded providers передают server startup точные проверенные payload bytes.
- Failed world apply восстанавливает созданные bodies и прежние settings. Если полное
  восстановление нельзя доказать, результат требует discard world.
- Source audit запрещает новые непроверенные math/precision usages и stale allowlist entries.
- Schema 1 bytes сохранены, но Jitter source/profile identity изменилась. Поэтому runtime ID
  изменился и старые артефакты нельзя запускать с новым runtime.

## Что не изменилось

Интеграция Jitter остаётся отдельной и явной:

1. Базовый UPM package импортируется без `Jitter2.Core`.
2. Импорт package ничего автоматически не записывает в `Assets`.
3. В consumer должен быть ровно один compatible `Jitter2.Core`.
4. Package-owned Jitter ставится отдельной кнопкой **Install Jitter2** только при статусе
   `Missing`.
5. Compatible external Jitter остаётся consumer-owned; package не копирует, не обновляет и не
   удаляет его.
6. Integration ставится или обновляется отдельной кнопкой **Install/update integration**.
7. Server projection обновляется отдельной командой **Install server runtime sources...**.
8. Package не владеет `World.Step`, dynamic bodies, networking, prediction или gameplay.
9. Runtime baking не добавлен: static artifact по-прежнему создаётся в Editor.

## Почему schema 1 всё равно требует re-bake

Artifact schema отвечает только за layout bytes. Runtime compatibility ID дополнительно включает
Jitter source hash, precision/compile profile и версии simulation semantics. Новый reader способен
разобрать старый schema 1 payload, но это не разрешение симулировать его новым runtime.

Compatibility credential:

```text
artifactHash + runtimeCompatibilityId
```

Нельзя заменять его package SemVer или `TopologyFingerprint`. Нельзя вручную менять runtime ID в
manifest и нельзя собирать `.physics.asset`, `.physics.bytes` и `.physics.manifest.json` от разных
bake-операций.

## Подготовка consumer project

Перед обновлением:

1. Закоммитьте или сделайте recoverable backup consumer project.
2. Сохраните `Packages/manifest.json` и `Packages/packages-lock.json`.
3. Сохраните scenes, prefabs, `JitterPhysicsWorldProfile` и generated Level/Source IDs.
4. Сохраните текущую artifact trio каждого уровня.
5. Сохраните
   `Assets/DataSakura/JitterPhysicsBaker/InstallationReceipt.json`.
6. Сохраните server projection и `JitterPhysics.projection.json`.
7. Запишите текущий package tag, compatibility report и runtime ID.
8. Определите ownership Jitter2: `external` или `package-owned`.
9. Найдите локально изменённые receipt-owned integration/projection files.
10. Отдельно сохраните изменения в импортированных samples: после импорта они принадлежат
    consumer project.

Не удаляйте receipt, чтобы скрыть конфликт. Без receipt installer не может отличить package-owned
файлы от consumer-authored. Не начинайте с удаления всего `Library`.

## Обновление UPM package

После публикации `v0.7.0` измените dependency:

```json
"com.datasakura.jitter-physics-baker":
  "https://github.com/denisislamov/jitter-physics-baker.git#v0.7.0"
```

Затем:

1. Дождитесь окончания Package Manager resolution и compilation.
2. Проверьте `Packages/packages-lock.json`: resolved revision должен соответствовать `v0.7.0`.
3. Проверьте в Package Manager версию **DataSakura Jitter Physics Baker 0.7.0**.
4. До Setup убедитесь, что base package импортируется без Jitter compile dependency.
5. Откройте **Tools > DataSakura > Jitter Physics Baker Window**.
6. Выберите существующий `JitterPhysicsLevel`; в чистой сцене сначала нажмите **Create Level** и
   сохраните scene.
7. Откройте **Settings > Advanced installation and maintenance > Open installation details**.
8. Нажмите **Validate installation**. Эта операция должна быть read-only.

## Выбор и обновление Jitter2

### Статус `Missing`

1. Нажмите **Install Jitter2**.
2. Дождитесь Unity refresh/compilation.
3. Повторите validation.
4. Продолжайте только при `Compatible` и ровно одном provider.

### Package-owned Jitter

1. Проверьте receipt и отсутствие локальных изменений package-owned DLL/dependencies.
2. Сравните source hash, compile profile и DLL hash с таблицей выше.
3. При несовместимости удаляйте только неизменённую package-owned установку через Advanced UI.
4. Снова выполните **Install Jitter2**, дождитесь compilation и повторите validation.

### External Jitter

1. Не нажимайте **Install Jitter2** поверх existing provider.
2. Не позволяйте package удалять или перезаписывать external source/plugin.
3. Если статус `Compatible`, оставьте Jitter без изменений.
4. Если статус `Incompatible`, обновите его через workflow владельца consumer project.
5. Продолжайте только после совпадения canonical source/profile identity.

### `Duplicate` или `UnsupportedPlugin`

Остановите миграцию. Инвентаризируйте все source asmdef и precompiled candidates, определите
владельца каждого и оставьте ровно один проверяемый provider. Package не должен угадывать нужную
physics implementation и не должен автоматически удалять consumer-owned copy.

## Обновление Unity integration

После статуса Jitter `Compatible`:

1. Нажмите **Install/update integration**.
2. Не копируйте `JitterIntegration~` вручную.
3. Если updater сообщает locally modified receipt-owned file, остановитесь и сохраните изменения.
4. Перенесите custom code в consumer-owned extension или осознанно решите ownership conflict.
5. Дождитесь Unity compilation.
6. Снова выполните **Validate installation**.
7. Проверьте direct Jitter reference installed asmdef и ровно один `Jitter2.Core` candidate.
8. Проверьте, что unrelated assets/scenes не изменились.

Custom code должен загружать asset через installed native boundary:

```csharp
NativeReadResult loaded =
    JitterNativeUnityArtifactLoader.Load(asset, runtimeCompatibilityId);

if (!loaded.Succeeded)
    return loaded.Error;

var candidate = new Jitter2.World();
PhysicsWorldBuildResult built =
    JitterPhysicsWorldBuilder.Apply(candidate, loaded.Artifact);

if (!built.Succeeded)
{
    candidate.Dispose();
    return built.Error;
}
```

Tick loop остаётся в consumer. Step выполняется с artifact tick rate и
`multiThread: false`.

## Обновление custom providers

Успешный `IPhysicsArtifactProvider` обязан вернуть exact validated payload bytes:

```csharp
return PhysicsArtifactLoadResult.Success(
    portableArtifact,
    manifest,
    artifactHash,
    validatedPayloadBytes,
    Description);
```

Старый четырёхпараметрический overload остаётся source-compatible для inspection tooling. Server
startup отклоняет такой success result как `SourceUnavailable`, потому что native simulation не
должна строиться из повторно сконвертированных portable DTO.

## Обновление dedicated server

1. В installation details откройте Advanced.
2. Выполните **Install server runtime sources...** в прежний projection root.
3. Не перезаписывайте locally modified projected files.
4. Проверьте новый `JitterPhysics.projection.json`.
5. Сравните `jitterAssemblySha256` с canonical DLL hash из таблицы.
6. Соберите и протестируйте consumer server.
7. До provider load проверьте exact loaded `Jitter2.Core.dll` SHA-256.
8. Доставьте новую `.physics.bytes + .physics.manifest.json` пару.
9. Создайте `JitterPhysicsServerOptions` с новым runtime ID, expected Level ID, tick rate и DLL
   hash.
10. Вызовите `JitterPhysicsServerStartup.Start` на новом world.
11. Не открывайте connection approval до `state.IsReady == true`.
12. Логируйте `SelfCheck`; в handshake сравнивайте полный artifact hash и runtime ID.
13. Серверный tick loop вызывает `World.Step`; package этого не делает.

## Re-bake и atomic delivery

Для каждого уровня:

1. Откройте правильную scene и выберите правильный `JitterPhysicsLevel`.
2. Проверьте Level ID, Geometry Root и World Profile.
3. Выполните **Validate** и исправьте все errors.
4. Выполните **Build for Client**.
5. Сразу повторите bake без изменений и сравните полные bytes/SHA-256.
6. Проверьте одну согласованную trio:
   - `<level-id>.physics.asset`;
   - `<level-id>.physics.bytes`;
   - `<level-id>.physics.manifest.json`.
7. Проверьте schema `1`, новый runtime ID, Level ID, tick rate, counts и artifact hash.
8. Выполните export и доставьте всю trio как одну versioned content unit.
9. Обновите server payload/manifest одновременно с client content и runtime.

Negative checks должны отвергать старый runtime ID, tampered payload/manifest, отсутствующий файл,
mixed-generation trio, неправильный Level ID и неправильный tick rate до readiness.

## Imported samples

Старый sample остаётся consumer-owned здесь:

```text
Assets/Samples/DataSakura Jitter Physics Baker/0.0.12/Physics Baking Demos/
```

После Setup импортируйте `0.7.0` sample рядом, сравните версии, вручную перенесите local changes и
только затем решайте, удалять ли старую копию. Импорт sample не устанавливает и не заменяет Jitter.

## Проверка package repository

Из корня package development repository:

```sh
git diff --check
python3 tools/verify-package-meta.py
python3 "Packages/com.datasakura.jitter-physics-baker/tools~/verify-jitter2-lock.py"
python3 "Packages/com.datasakura.jitter-physics-baker/tools~/test-jitter2-lock.py"
python3 "Packages/com.datasakura.jitter-physics-baker/tools~/test-jitter-math-audit.py"
python3 "Packages/com.datasakura.jitter-physics-baker/tools~/audit-jitter-math.py" check \
  --policy "Packages/com.datasakura.jitter-physics-baker/tools~/jitter-math-audit-policy.json"
bash "Packages/com.datasakura.jitter-physics-baker/tools~/test-dotnet.sh"
```

Editor compile gates:

```sh
python3 tools/dev-refresh-csproj.py
dotnet build DataSakura.JitterPhysics.Editor.csproj -v q --nologo
dotnet build DataSakura.JitterPhysics.Editor.Tests.csproj -v q --nologo
dotnet build DataSakura.JitterPhysics.Tests.csproj -v q --nologo
```

Unity gates при закрытом Editor:

```sh
bash tools/run-unity-tests.sh editmode
bash tools/run-unity-tests.sh playmode
```

Результат принимается только по свежим `Logs/TestResults/*.xml`: записывайте path, timestamp,
total, passed, failed, skipped и command exit code. Успешный .NET run не заменяет Unity XML,
player/IL2CPP, clean consumer или manual Editor scenarios.

## Критерии успешного rollout

- Package Manager разрешил exact `v0.7.0` commit.
- Base package компилируется до Setup без Jitter.
- После Setup существует ровно один compatible Jitter provider.
- Unity/server используют exact canonical DLL bytes и один runtime ID.
- Integration и server projection обновлены явными командами.
- Все уровни re-baked; две неизменные bake-операции дают равные bytes/hash.
- Каждая artifact trio полностью согласована.
- Старый runtime ID и tampered/mixed content fail-fast отвергаются.
- Server не открывает connection approval до readiness.
- Static/package, portable/server, Unity, clean consumer и нужные player gates записаны отдельно.
- Unrelated consumer-owned files не изменены.

## Безопасный rollback

Rollback выполняется только согласованным набором:

1. Остановите deployment и connection approval.
2. Верните UPM dependency и package lock к `v0.0.12`.
3. Через ownership-aware UI верните соответствующий package-owned Jitter/integration; external
   Jitter не удаляйте.
4. Верните server projection той же версии.
5. Верните сохранённые `0.0.12` artifact trios.
6. Пересоберите Unity client и server.
7. Повторите validation, hash/runtime checks и startup smoke.
8. Не откатывайте unrelated consumer work.

Нельзя откатить только payload, только manifest, только integration или только server DLL. Package,
Jitter runtime, integration, server projection и baked content должны образовывать один совместимый
набор.

## Текущий evidence boundary RC

На момент подготовки документа static/meta/lock/audit и portable/server gates зелёные. Свежий Unity
run заблокирован Licensing Client protocol mismatch; combined Custom Navigation consumer и
player/IL2CPP не пройдены. Эти gates нельзя считать PASS по документации или старым XML, и release
tag нельзя объявлять проверенным до их фактического завершения.
