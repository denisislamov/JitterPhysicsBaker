# JMP-ADR-002. Совместимость артефакта при миграции на Jitter-native math

- Статус: принято для реализации в `JMP-E04` и `JMP-E06`.
- Дата: 2026-09-01.
- Исходная версия: package `0.0.12`, artifact schema `1`.

## Решение

### Schema decision

Schema 1 сохраняется только если финальный writer с `Real=f32`, `JVector` и `JQuaternion`
создаёт byte-for-byte тот же payload для полного набора golden fixtures. Prototype подтвердил
эквивалентность component order и f32 bytes:

- legacy vector `X,Y,Z` и `JVector.X,Y,Z` — одинаковые 12 bytes;
- legacy quaternion `X,Y,Z,W` и `JQuaternion.X,Y,Z,W` — одинаковые 16 bytes;
- minimal-box schema-1 payload — 165 bytes,
  SHA-256 `b53cf221453ce313ae3e2d9ff3e94b665b65a674a0f1f5e9863acb5b33835479`.

Это разрешение не позволяет менять writer незаметно. Первый отличающийся byte в любом full
fixture переводит решение в schema bump до merge.

### Runtime semantics

Даже при неизменной schema новый Jitter source hash, compile profile, precision или semantics
version создаёт новый `runtimeCompatibilityId`. Для baseline:

- Jitter source content hash:
  `sha256:d67ac0c421687ec7308501bf4b8bcba9c33bed7845a0bfe64d4675b2326cce85`;
- compile profile id:
  `9e724df81fb24d55e6136d35174c721457231606bd602464dbc35b017da73643`;
- current runtime compatibility id:
  `ca8283611d3221120e69e23c4c028720537de4867f1de53df3752db85cd32006`.

Публичная миграция `StableMath` меняет Jitter source bytes и потому требует нового runtime id,
даже если numeric output и artifact payload bytes остались теми же.

### Legacy artifacts

Автоматический legacy reader не требуется, пока schema bytes равны. Старый payload всё равно
отклоняется typed error `IncompatibleRuntime`, если был создан с другим runtime id. Пользователь
должен re-bake/re-export artifact полным триплетом.

Если финальный codec меняет layout, вводится новая schema. Старый reader допускается только как
явный offline migration path; он не может выдавать старый artifact за текущую runtime semantics.

### Atomic delivery

Payload, manifest и `.physics.asset` являются одной поставкой:

1. сформировать все три во staging;
2. проверить payload hash, manifest agreement и asset metadata;
3. заменить весь триплет;
4. при любой ошибке восстановить предыдущий полный триплет.

Нельзя считать предыдущий artifact валидным, если payload/manifest уже заменены, а Unity asset
нет. Известное окно late-bake failure должно быть закрыто в `JMP-E06`.

## Write/read contract для schema 1

- `WriteReal/ReadReal`: IEEE-754 binary32, little-endian, canonical `-0` policy writer-а.
- `WriteJVector/ReadJVector`: `X`, `Y`, `Z`, каждый через `WriteReal`.
- `WriteJQuaternion/ReadJQuaternion`: `X`, `Y`, `Z`, `W`, каждый через `WriteReal`.
- Layout Jitter struct не сериализуется raw-memory copy; padding/ABI не участвуют в формате.
- `f64` profile не может читать или писать schema-1 f32 artifact под тем же runtime id.

## Evidence

`JMPE00MigrationPrototypeTests` фиксирует f32 component bytes, current runtime id, f64 negative
fixture, tampered DLL identity и golden payload/manifest. Существующий independent
`PhysicsArtifactGoldenBytesTests` остаётся главным layout oracle.

Unity `.physics.asset` fixture и repeat-bake через Editor в текущем запуске не обновлялись:
Unity licensing handshake заблокировал fresh XML. Поэтому ADR принят, но Unity-dependent gates
остаются `BLOCKED/NOT RUN`, пока не появится свежий consumer run.

## Отклонённые варианты

- Сохранить старый runtime id при новом source hash.
- Принять старый artifact с warning.
- Считать равенство schema достаточным доказательством одинаковой world semantics.
- Сериализовать `JVector`/`JQuaternion` через raw struct memory.
- Заменять payload, manifest и asset независимо.
