# JMP-E06 — evidence artifact compatibility и migration

Дата фиксации: 2026-09-01.

Ветка: `d.islamov/jmp-e00-baseline-adrs`.

Baseline эпика: `f035b04` (`JMP-E05`).

## Byte/schema decision

v0.0.12 legacy writer и Jitter-native migration writer сравнены по полному 165-byte payload,
SHA-256 и canonical manifest JSON. Все значения совпали:

- schema: `1`;
- payload SHA-256: `b53cf221453ce313ae3e2d9ff3e94b665b65a674a0f1f5e9863acb5b33835479`;
- manifest field order, LF и значения: byte-identical.

По ADR schema остаётся 1. Native reader отдельно проверен на schema 2 fixture и возвращает typed
`UnsupportedSchema`, не переинтерпретируя layout как schema 1. Legacy reader не нужен, потому что
layout не менялся; старые bytes допускаются для inspection, но не для simulation с новым runtime.

## Runtime identity и обязательная миграция

Current canonical inputs:

- source: `sha256:ca940ca6483ffcedf65854719396cec2d9e038cc43c01e7d35d147cd70766940`;
- compile profile: `a2925211b983330117414426be9bf8a2798ce9169c1206e1e55178f708cfa72e`;
- precision: `f32`;
- runtime compatibility ID:
  `71e9d01f4006a8e1d097beb047efa8b8aabbe24895cb8d50531c764031c9aa4b`.

E00 ID `ca8283611d3221120e69e23c4c028720537de4867f1de53df3752db85cd32006`
отличается и отклоняется typed `IncompatibleRuntime`. Нужны coordinated integration/server
update, re-bake каждого уровня и re-export delivery unit; ручная подмена ID запрещена.

## Atomic trio

Bake теперь snapshot-ит существующие payload, manifest и `.physics.asset`, пишет/import-ит новый
pair, обновляет asset, затем cross-check-ит все три. Любое исключение восстанавливает все три bytes
и сохраняет их GUID. Negative fixtures инъецируют сбой:

- после payload/manifest import, до asset update;
- после asset save, до final trio verification.

Оба пути оставляют предыдущий loader/export result valid. Export/upload дополнительно сравнивают
все asset metadata fields с manifest и payload и отказываются выдавать mixed trio.

## Regression

| Gate | Результат |
| --- | --- |
| `git diff --check` | PASS |
| Package metadata/LFS | PASS |
| Jitter source/profile/binary lock | PASS: 96 files, 1 canonical patch, 3 artifacts |
| Lock invariant/negative tests | PASS, включая tampered server DLL |
| Portable/server suite | PASS: 111/111 |
| Editor, Editor.Tests, Runtime.Tests compile | PASS: 0 warnings, 0 errors |
| Unity EditMode | PASS: 104/104 |
| Unity PlayMode | PASS: 57/57 |
| IL2CPP | NOT RUN; не является per-epic regression gate |
