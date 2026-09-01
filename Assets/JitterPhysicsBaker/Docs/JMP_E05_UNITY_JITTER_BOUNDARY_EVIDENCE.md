# JMP-E05 — evidence Unity authoring, baking и runtime boundaries

Дата фиксации: 2026-09-01.

Ветка: `d.islamov/jmp-e00-baseline-adrs`.

Baseline эпика: `d9f9835` (`JMP-E04`).

## Один boundary

Единственный supported Unity-to-Jitter adapter находится в explicit-Setup integration:
`UnityJitterMathAdapter`. Зафиксированы component-preserving axes, X/Y/Z/W quaternion layout,
world body pose, body-local primitive pose, one-time primitive scale и one-time full mesh matrix.
Negative mesh determinant меняет winding ровно один раз.

`JitterNativeColliderConverter` создаёт native Box/Sphere/Capsule/Mesh records, а
`JitterNativeUnityArtifactBuilder` собирает native artifact, сохраняет Source ID, structural
collider key и ordinal ordering и валидирует полный graph до результата. Native diagnostics
comparer работает на Jitter values.

## Bootstrap и explicit Setup

Always-imported assemblies не получили Jitter reference. Existing Setup по-прежнему сначала
устанавливает/проверяет Jitter отдельно, затем копирует integration. Integration asmdef теперь
также имеет direct Authoring edge; source Jitter и precompiled DLL tailoring сохранены.

Editor вызывает installed native builder late-bound только по явной Validate/Bake операции.
Production Bake при compatible Jitter, но без integration, возвращает actionable error. Никаких
записей, conversion/hash work из InitializeOnLoad, import callback или Scene View Repaint нет.

## Проверка установленного состояния

Для executable probe была создана точная временная папка
`Assets/__JMP_E05_InstalledProbe` с lock-verified `Jitter2.Core.dll`, integration sources и тем же
precompiled-reference asmdef, который формирует Setup. Existing Editor suite автоматически пошёл
через native bridge:

- первый прогон дал 100/101 и обнаружил только сокращённый duplicate Source ID UX message;
- после сохранения полного actionable текста: EditMode 101/101;
- installed-state PlayMode: 57/57.

Probe-папка и сгенерированные для неё `.meta` удалены после прогона. Затем выполняется отдельная
clean/no-Jitter regression, доказывающая, что package import contract не зависит от probe.

Imported UPM sample copy проверена отдельно: Runtime и Editor assemblies скомпилировались вместе
с installed integration. Scene-dependent sample PlayMode fixture был обнаружен и ожидаемо сообщил,
что `SampleBouncingBall` ещё не сгенерирована/не добавлена в build profile; после исключения только
этой временной Tests-копии базовый PlayMode дал 57/57. Это compile/import evidence, не утверждение о
полном runnable sample scenario; генерация scene, bake и sample PlayMode остаются gate E07/E09.

## Artifact/preview policy

Unity asset хранит bytes/manifest metadata, а не Unity/Jitter math structs. Staged pair write,
hash/manifest verification, import/reimport и moved/removed diagnostics не менялись. Известная
late-failure граница третьего `.physics.asset` остаётся явно документированной: failed bake нельзя
доставлять, trio нужно re-bake и проверить полностью.

## Финальная regression

| Gate | Результат |
| --- | --- |
| `git diff --check` | PASS |
| Package metadata/LFS | PASS |
| Jitter source/profile/binary lock | PASS: 96 files, 1 canonical patch, 3 artifacts |
| Lock invariant/negative tests | PASS, включая tampered server DLL |
| Portable/server suite | PASS: 107/107 |
| Installed native Unity EditMode | PASS: 101/101 |
| Installed native Unity PlayMode | PASS: 57/57 |
| Imported sample runtime/editor compile probe | PASS; generated-scene scenario deferred to E07/E09 |
| Clean/no-Jitter Unity EditMode | PASS: 101/101 |
| Clean/no-Jitter Unity PlayMode | PASS: 57/57 |
| Editor, Editor.Tests, Runtime.Tests compile after clean refresh | PASS: 0 warnings, 0 errors |
| IL2CPP | NOT RUN; не является per-epic regression gate |
