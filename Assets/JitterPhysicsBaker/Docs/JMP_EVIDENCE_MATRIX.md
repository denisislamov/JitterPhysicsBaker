# JMP evidence matrix

Этот файл задаёт независимые gates всей precision migration. `PASS` одного gate не повышает
статус другого. Итог `PASS` запрещён, если хотя бы один обязательный дочерний gate имеет
`NOT RUN`, `BLOCKED` или `FAIL`.

Статусы: `PASS`, `FAIL`, `BLOCKED`, `NOT RUN`, `NOT APPLICABLE`.

| № | Gate | Команда / действие | Expected output | Evidence path | JMP-E00 status | JMP-E02 status |
|---:|---|---|---|---|---|---|
| 1 | Diff hygiene | `git diff --check`; `git status --short` | exit 0; только task-owned paths | terminal output + scoped diff | PASS; unrelated baseline перечислен отдельно | PASS; unrelated untracked docs исключены из staging |
| 2 | Metadata/LFS/lock | `verify-package-meta.py`; два lock scripts | exit 0 во всех трёх | terminal output | PASS baseline | PASS: schema 3, canonical patch, 3 artifacts |
| 3 | StableMath public API | filtered API/reflection tests | exact supported public surface | test result XML/console | NOT RUN; API ещё internal | PASS .NET/prebuilt: exact public surface |
| 4 | StableMath golden bits | `StableMathContractTests` на каждом runtime | exact f32 bit fixtures | .NET console, Unity XML, IL2CPP run | BLOCKED partial: .NET 10 PASS, Unity/IL2CPP нет | PASS .NET; Unity/IL2CPP pending отдельно |
| 5 | Old/new artifact bytes | golden writer + binary comparer | equal SHA/bytes или approved schema bump и first offset | fixture report | PASS prototype component-level; final full migration NOT RUN | Входит в portable regression; schema артефакта не менялась |
| 6 | Portable .NET | `tools~/test-dotnet.sh` | 0 failed, exact total | console/testhost output | PASS final 85/85 | PASS 93/93 |
| 7 | Dedicated server | отдельный server filter/suite | startup/world tests 0 failed | console/TRX | PASS within full 85/85; separate filtered count remains a release gate | Входит в полный 93/93; отдельный filtered gate не запускался |
| 8 | Unity EditMode | `tools/run-unity-tests.sh editmode` | fresh readable XML, failed=0 | `Logs/TestResults/EditMode.xml` | BLOCKED licensing 505; старый XML не используется | BLOCKED: project открыт в Editor; fresh XML не создан |
| 9 | Unity PlayMode | `tools/run-unity-tests.sh playmode` | fresh readable XML, failed=0 | `Logs/TestResults/PlayMode.xml` | BLOCKED, runner не дошёл после EditMode | BLOCKED: runner остановлен до запуска; fresh XML не создан |
| 10 | Clean isolated consumer | `tools/verify-jp05-delivery.sh` plus pre-Setup compile | compile до/после Setup, filtered XML green | preserved fixture logs/XML | BLOCKED текущей Unity license | NOT RUN: E02 не меняет отдельный Setup flow |
| 11 | Exactly one `Jitter2.Core` | candidate inventory; missing/duplicate/unowned fixtures | 0 до Setup, ровно 1 после; conflicts refused | inventory JSON + fixture logs | PASS static/current tree; Unity negative fixtures NOT RUN | PASS static/lock; фактический Unity Setup pending |
| 12 | Unity/server DLL equality | SHA-256 installed plugin и server reference | exact equal lowercase SHA-256 | projection manifest + test output | PASS prototype server/prebuilt; installed Unity file BLOCKED | PASS prebuilt/projection architecture; installed Unity file pending |
| 13 | Player/IL2CPP smoke | build и запуск exact consumer | build/run exit 0, no load/precision errors | build log + player log | NOT RUN | NOT RUN |
| 14 | Repeat-bake determinism | два bake неизменной сцены | равные byte streams и SHA-256 | two payloads + hashes + XML/log | BLOCKED Unity license | NOT RUN: не подменяется StableMath .NET tests |

## Aggregation rule

Для завершения `JMP-E00` нужны пять завершённых Proto и принятые ADR. На 2026-09-01 ADR
приняты, portable prototypes выполнены, но `JMP-P01` и Unity runtime часть `JMP-P02/P04`
заблокированы licensing protocol mismatch. Поэтому статус эпика — `BLOCKED`, не `PASS`.

Перед любым следующим эпиком необходимо либо получить свежую Unity лицензию и закрыть эти
строки, либо отдельным решением пользователя изменить evidence contract. Старые XML от
2026-08-28 не являются свежим evidence текущей ветки.
