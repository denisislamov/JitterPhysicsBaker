# JMP-E04 — evidence Jitter-native contracts и codec

Дата фиксации: 2026-09-01.

Ветка: `d.islamov/jmp-e00-baseline-adrs`.

Baseline эпика: `1b397dd` (`JMP-E03`).

## Реализованный контракт

В скрытом и устанавливаемом `JitterIntegration~/Runtime` добавлен authoritative graph
`DataSakura.JitterPhysics.JitterNative`:

- позиции, размеры и вершины представлены `JVector`/`JVector[]`;
- ориентации представлены `JQuaternion`;
- simulation scalar fields представлены f32 `Real`;
- semantic meaning, порядок body/shape и schema 1 сохранены.

Always-imported bootstrap assemblies по-прежнему не имеют ссылки на Jitter. Новые типы становятся
доступны Unity consumer только после существующей explicit Setup-команды; способ установки Jitter
не менялся.

## Codec и validation

Новый native codec записывает `Real`, `JVector` и `JQuaternion` явными little-endian f32
компонентами. Runtime struct memory и padding не сериализуются. Golden fixture совпадает с legacy
schema 1 byte-for-byte: 165 bytes и SHA-256
`b53cf221453ce313ae3e2d9ff3e94b665b65a674a0f1f5e9863acb5b33835479`.

Native canonicalization использует canonical public `StableMath`: finite checks, square root и
absolute difference. Проверены quaternion sign, normalization, `-0`, NaN, positive/negative
Infinity и degenerate quaternion. Ошибки внешнего payload остаются typed
`PhysicsArtifactError`; invalid producer graph отклоняется до выдачи bytes.

## Временная совместимость и срок удаления

Старые DTO остаются временным Jitter-free bootstrap/source-compatibility surface. В период
E04-E07 native reader использует зрелый hostile-input schema 1 parser и один exact f32 bridge,
чтобы не дублировать limits/order/mesh policy во время миграции. Bridge internal; нового формата
или второго wire contract нет.

Инвентаризированы consumers: authoring profile, artifact builder, collider converter, bake
overlay/comparer, legacy codec, world builder, server/tests и samples. E05 переводит Unity
boundaries, E06 фиксирует artifact compatibility, E07 переводит runtime/server/samples и является
жёстким сроком удаления per-record legacy conversions. Новые public overloads со старыми math DTO
добавлять запрещено.

## Regression status

| Gate | Результат |
| --- | --- |
| `git diff --check` | PASS |
| Package metadata/LFS | PASS |
| Jitter source/profile/binary lock | PASS: 96 files, 1 canonical patch, 3 artifacts |
| Lock invariant/negative tests | PASS, включая tampered server DLL |
| Portable/server suite | PASS: 107/107 |
| Editor, Editor.Tests, Runtime.Tests compile | PASS: 0 warnings, 0 errors |
| Unity EditMode | PASS: 100/100 |
| Unity PlayMode | PASS: 57/57 |
| IL2CPP | NOT RUN; не является per-epic regression gate |

Unity results получены свежим `tools/run-unity-tests.sh all`; старые XML не переиспользовались.
