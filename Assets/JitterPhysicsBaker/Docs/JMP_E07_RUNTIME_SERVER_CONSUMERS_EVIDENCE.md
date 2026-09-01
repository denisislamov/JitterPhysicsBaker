# JMP-E07 — runtime, server и consumers

Дата фиксации: 2026-09-02.

Ветка: `d.islamov/jmp-e00-baseline-adrs`.

Baseline эпика: `69928ea` (`JMP-E06`).

## Shared runtime

`JitterPhysicsWorldBuilder.Apply` принимает authoritative `JitterNative.PhysicsArtifact` и
передаёт `JVector`, `JQuaternion` и `Real` в Jitter2 без промежуточных custom vector DTO.
Порядок bodies/shapes остаётся порядком canonical artifact graph. Package runtime не вызывает
`World.Step`; этот вызов остаётся только в consumer-owned sample tick loop.

Failure path восстанавливает gravity, solve mode, solver iterations и deactivation, удаляет
созданные bodies и не помечает world как applied. Исполняемые failure fixtures отдельно проверяют
полный rollback и `RequiresWorldDiscard == true`, когда полноту cleanup доказать нельзя.

## Dedicated server

File и embedded providers возвращают exact validated payload bytes. Server startup до изменения
мира проверяет f32 runtime profile, exact `Jitter2.Core.dll` SHA-256, provider validation,
artifact hash, runtime ID, level и tick rate, затем декодирует payload прямо в native graph.
Provider старого вида без payload получает typed `SourceUnavailable`; readiness не открывается.

Server и Unity используют один файл `JitterPhysicsWorldBuilder.cs`. Projection продолжает
доставлять integration sources отдельно и exact lock-verified DLL отдельной explicit-командой.
Базовый package не получил Jitter dependency.

## Consumers и samples

Portable fixture компилирует shared integration с exact prebuilt DLL, проверяет публичную native
signature, один загруженный `Jitter2.Core` и отсутствие package-owned tick loop. Runtime sample
загружает `.physics.asset` через `JitterNativeUnityArtifactLoader`, строит native graph общей
реализацией и проецирует legacy records только по явному Scene View presentation request.

Combined Baker/Custom Navigation consumer и player/IL2CPP не запускались: это остаётся отдельным
release gate `JMP-E09`, consumer-owned networking/gameplay не изменялись.

## Regression

| Gate | Результат |
| --- | --- |
| Portable/server suite | PASS: 119/119 |
| World rollback/discard fixtures | PASS |
| Exact-payload/provider readiness fixtures | PASS |
| Native consumer/exactly-one-Jitter source fixtures | PASS |
| Editor, Editor.Tests, Runtime.Tests compile | PASS: 0 warnings, 0 errors |
| Installed integration + imported sample Unity run | BLOCKED before compile result: Licensing Client protocol `1.18.1`, response 505 |
| Fresh Unity XML | NOT CREATED; предыдущие XML не засчитаны |
| Combined Custom Navigation consumer | NOT RUN; release gate E09 |
| Player/IL2CPP | NOT RUN; release gate E09 |

Unity blocker является внешним licensing blocker, а не доказательством compile success или
failure. Временные `Assets/__JMP_E07_*` probe-папки удалены после остановки запуска.

## Setup contract

Existing UX не менялся в коде установки: сначала пользователь отдельно предоставляет или ставит
ровно один compatible Jitter2, затем отдельно нажимает **Install/update integration**, а server
projection обновляет своей явной командой. External Jitter не копируется и не перезаписывается.
