# JMP-P00 — как самостоятельно сделать исполняемый source audit и classifier

Статус: **подробная инструкция для самостоятельной реализации; код не реализован**.

Обновлено: 2026-09-01.

Связанный backlog:
[JITTER_MATH_PRECISION_MIGRATION_DECOMPOSITION.md](JITTER_MATH_PRECISION_MIGRATION_DECOMPOSITION.md),
раздел `JMP-P00`.

Этот документ объясняет:

1. какой именно audit нужен программе миграции Jitter math/precision;
2. как собрать полный inventory и не потерять совпадения в комментариях, aliases и разных
   assembly boundaries;
3. как классифицировать каждое использование по влиянию на simulation/artifact;
4. как отделить migration debt от действительно разрешённого allowlist;
5. как реализовать детерминированный CLI без Unity и внешней сети;
6. как проверить positive, negative, stale-policy и false-positive scenarios;
7. как доказать, что audit ничего не изменяет в проекте и не ломает существующие проверки.

Документ не является реализацией и не доказывает прохождение описанных ниже gates. Все пути
с пометкой «предлагаемый» сейчас могут отсутствовать. Не создавайте их в основном worktree,
пока не снят baseline и не подготовлен disposable worktree.

## 1. Короткое решение

Для `JMP-P00` нужен двухслойный audit:

```text
Raw inventory (полный текстовый superset)
    ↓ reconciliation: ни один raw candidate не потерян
Code-aware scanner (comments/strings masked, aliases учтены)
    ↓
Classifier (category + impact + owner + action)
    ↓
Policy validation
    ├── migration debt: известные места, которые обязаны быть мигрированы
    ├── allowlist: только обоснованные boundaries/telemetry/tests/vendor
    └── ambiguous/unclassified: всегда ошибка
    ↓
Deterministic JSON + human-readable summary + exit code
```

Рекомендуемая реализация Proto — Python 3 standard library tool. Это соответствует текущему
tooling пакета, не требует Unity, NuGet или сети и может запускаться до компиляции. Scanner
должен маскировать C# comments/string/char literals с сохранением line/column, а затем
сверять code-aware findings с raw textual candidates. Если собственный lexer не покрывает
используемый C# syntax, Proto останавливается: нельзя компенсировать неизвестную дыру
широким ignore. Production-вариант в `JMP-E08` при необходимости можно заменить Roslyn-based
scanner-ом, сохранив CLI, policy schema и finding IDs.

Главное правило:

> Baseline не является разрешением. Новое нарушение должно падать даже в файле, где уже
> есть старые migration findings.

## 2. Что именно входит и не входит в JMP-P00

### В scope Proto

- executable read-only scanner;
- точный список scan roots и exclusions;
- полный raw inventory;
- code-aware findings;
- classification taxonomy;
- machine-readable policy draft;
- machine-readable JSON report;
- human-readable summary;
- deterministic finding IDs;
- migration debt register;
- строгий allowlist с owner/reason;
- ambiguous/stale/unused entry detection;
- synthetic unit fixtures;
- disposable-worktree negative integration fixture;
- baseline counts по rule/category/impact/action;
- evidence report для `JMP-E00`.

### Не входит в Proto

- замена `PhysicsVector3` на `JVector`;
- замена `float` на `Real`;
- изменение `StableMath`;
- изменение Jitter sources, lock или DLL;
- изменение asmdef/csproj;
- изменение artifact schema;
- исправление найденных violations;
- установка Jitter или integration;
- CI enforcement для production branch;
- публикация package/tag;
- автоматическое редактирование source файлов.

Audit Proto обязан только находить, классифицировать и доказывать полноту. Исправление
находок выполняется последующими эпиками.

## 3. Неподлежащие нарушению ограничения

1. Tool по умолчанию read-only.
2. Tool не запускает Unity и не меняет Unity assets.
3. Tool не создаёт `.meta` рядом с inspected source.
4. Tool не переписывает policy автоматически.
5. Tool не добавляет inline suppressions в C#.
6. Tool не изменяет `jitter2.lock.json`.
7. Tool не сканирует external Jitter как owned migration scope.
8. Tool не следует symlink за пределы repository root.
9. Tool не зависит от текущего working directory.
10. Output paths задаются явно; без них tool пишет только summary в stdout.
11. Все paths в report repository-relative, с `/` и ordinal ordering.
12. В JSON нет timestamps, absolute paths, usernames и machine-specific values.
13. Повторный запуск на неизменном checkout даёт byte-identical JSON.
14. Unclassified и ambiguous findings нельзя разрешить count baseline-ом.
15. Allowlist entry без owner/reason считается invalid policy.
16. Stale/unused allowlist entry считается ошибкой, а не молча игнорируется.
17. External/unrelated dirty files не изменяются.
18. Запрещены `git add -A`, `git reset` и destructive cleanup.

## 4. Подтверждённая исходная поверхность

На момент подготовки инструкции текущий owned scope содержит raw textual matches:

| Candidate | Raw matches | Что это означает |
|---|---:|---|
| `PhysicsVector3` | 137 | declarations, contracts, codec, editor, tests и comments |
| `PhysicsQuaternion` | 84 | declarations, contracts, codec, editor, tests и comments |
| explicit `float` token | 156 | simulation, serialization, Unity boundary и tests смешаны |
| explicit `double` token | 15 | quaternion normalization, telemetry и comments смешаны |
| `Mathf` | 42 | bake-affecting Editor и presentation code смешаны |
| `MathF` | 0 | отсутствие сейчас; будущая строка всё равно должна падать |
| fully-qualified `System.Math` | 0 | вызовы сейчас обычно идут как `Math.*` |
| selected `Math.Sin/Cos/Sqrt/Round/Abs/...` | 7 | есть deterministic и non-simulation usages |
| `StableMath` outside Jitter source | 0 | consumer-local duplicate сейчас не найден |

Эти counts — только предварительный raw superset, а не готовый audit result. Они могут
включать комментарии/XML-doc и не учитывают semantic aliases. Перед реализацией их нужно
снять заново и сохранить command/output в baseline report.

Подтверждённые примеры, которые classifier обязан различать:

- `PhysicsCanonicalization` использует `double` и `Math.Sqrt` для quaternion normalization:
  это simulation/artifact-affecting migration target;
- `PhysicsArtifactValidator` использует `Math.Abs` над physics coordinates: это
  deterministic validation, а не telemetry;
- `EmbeddedArtifactSourceGenerator` использует `Math.Min` для base64 chunk length: это
  serialization/tooling utility, не simulation math;
- `JitterPhysicsWorldBuilder.ElapsedMilliseconds` и `Stopwatch.Elapsed.TotalMilliseconds`:
  это telemetry и допустимый `double` при доказанном non-flow в artifact/state;
- `JitterPhysicsColliderConverter` использует Unity `Vector3`, `Quaternion` и `Mathf`:
  типы находятся на Unity boundary, но часть вычислений bake-affecting и не должна
  автоматически разрешаться целиком по path;
- `JitterPhysicsBakeGeometryOverlay` использует Unity math для Scene View: это presentation,
  но тот же файл содержит conversions и comparisons, поэтому broad file allowlist опасен;
- tests используют legacy DTO для fixtures: это test category, но production compilation
  references внутри tests всё равно должны быть мигрированы вместе с API.

## 5. Предлагаемые будущие артефакты

В рамках самостоятельной реализации предлагается создать:

```text
Packages/com.datasakura.jitter-physics-baker/
└── tools~/
    ├── audit-jitter-math.py                 # executable scanner/classifier CLI
    ├── jitter-math-audit-policy.json        # scope, categories, migration debt, allowlist
    ├── test-jitter-math-audit.py            # standard-library unit/fixture tests
    └── fixtures/
        └── JitterMathAudit/                  # synthetic source trees, не Unity assets
            ├── positive/
            ├── negative/
            ├── lexer/
            └── policy/

Assets/JitterPhysicsBaker/Docs/
└── JMP_P00_SOURCE_AUDIT_BASELINE.md         # reviewed human-readable result

Logs/JitterMathAudit/                         # generated, не source of truth
├── source-audit.json
└── source-audit.md
```

Названия предлагаемые. До создания проверьте existing naming и решите их в review. JSON
policy и test fixtures входят в package/repository. `Logs` reports являются run evidence и
не должны автоматически попадать в package publication.

Unity `.meta` нужны только для новых tracked файлов внутри Unity-visible `Assets` или
`Packages`. Создавайте их штатным `tools/dev-make-meta.py` после того, как список файлов
стабилен, и проверяйте `tools/verify-package-meta.py`. Не создавайте `.meta` в `Logs`.

## 6. Точный scan scope

### 6.1 Owned roots

Scanner должен перечислять `.cs` файлы рекурсивно и детерминированно в:

```text
Packages/com.datasakura.jitter-physics-baker/Runtime/Contracts
Packages/com.datasakura.jitter-physics-baker/Runtime/ArtifactCodec
Packages/com.datasakura.jitter-physics-baker/Runtime/UnityArtifact
Packages/com.datasakura.jitter-physics-baker/Authoring
Packages/com.datasakura.jitter-physics-baker/Editor
Packages/com.datasakura.jitter-physics-baker/JitterIntegration~
Packages/com.datasakura.jitter-physics-baker/Server~
Packages/com.datasakura.jitter-physics-baker/Samples~
Packages/com.datasakura.jitter-physics-baker/Tests
Packages/com.datasakura.jitter-physics-baker/tools~/fixtures
```

`tools~/fixtures` включается только для `.cs` consumer/test fixtures. Сам Python audit tool
не сканируется как C# source.

### 6.2 Vendor reference scope

```text
Packages/com.datasakura.jitter-physics-baker/Jitter2~
```

не является owned migration scope для правил `PhysicsVector3`, `float`, Unity types и
consumer math. Это canonical third-party/fork source с отдельным lock и patch process.

Однако audit report должен записывать:

- что root существует;
- что он исключён как `third_party_vendor`;
- его source identity берётся из `jitter2.lock.json`;
- `StableMath` declaration и `Precision` проверяются отдельными правилами/эпиками;
- broad exclusion не распространяется на `JitterIntegration~`.

### 6.3 Безусловные exclusions

Исключаются только точные generated/build roots:

```text
**/bin/**
**/obj/**
Library/**
Temp/**
Logs/**
.git/**
```

Не используйте exclusions вида `**/Runtime/**`, `**/Editor/**`, `**/Tests/**` или
`**/*Generated*`. Generated source может участвовать в compilation; если такой файл есть,
его ownership и generation source решаются отдельно.

### 6.4 Consumer scope

Installed copies в другом Unity consumer не входят в repository Proto. Для `JMP-E08/E09`
тот же tool должен уметь принимать explicit consumer root и policy profile. Не сканируйте
случайный `Library/PackageCache`; source of truth — resolved package revision и explicit
project-owned installed files.

## 7. Категории classifier-а

Каждый code-aware finding получает ровно одну основную `category`:

| Category | Значение | По умолчанию |
|---|---|---|
| `simulation` | влияет на world construction, physics semantics или runtime state | migration required |
| `serialization` | определяет artifact bytes, manifest, hash, validation или wire layout | migration/schema review |
| `telemetry` | timing/logging/metrics, не влияет на control flow и persisted state | allow only with proof |
| `unity_boundary` | Unity authoring/input adapter/presentation | conditional allow |
| `test_fixture` | тест или намеренно invalid fixture | exact scoped allow |
| `third_party_vendor` | pinned external/fork source | excluded with identity |
| `non_code` | comment/string/XML-doc raw candidate | not code, retained in reconciliation |
| `ambiguous` | невозможно уверенно классифицировать | hard failure |

Основной category недостаточен. Finding также получает:

- `impact`: `deterministic`, `bake_affecting`, `runtime_affecting`, `non_affecting`,
  `unknown`;
- `boundaryKind` для Unity: `authoring_input`, `unity_to_jitter_adapter`, `inspector`,
  `scene_view`, `presentation`, `none`;
- `disposition`: `must_migrate`, `allowed`, `investigate`, `legacy_fixture`, `vendor`;
- `targetAction`: конкретное действие будущего эпика;
- `owner`: команда/роль, ответственная за решение;
- `reason`: human-readable объяснение;
- `plannedEpic`: `JMP-E02`…`JMP-E08` либо `none` для truly allowed use.

### 7.1 `simulation`

Сюда относятся:

- gravity, position, orientation, size, radius, length;
- friction/restitution и world settings;
- normalization/canonicalization/quantization;
- world builder values;
- topology/fingerprint inputs;
- simulation-affecting validation thresholds;
- math, меняющая значение, которое попадёт в artifact или Jitter world.

Допустимое действие почти всегда `must_migrate`.

### 7.2 `serialization`

Сюда относятся:

- reader/writer scalar layout;
- endianness;
- artifact schema;
- hash/manifest canonicalization;
- embedded source/base64 chunking;
- payload limits.

Не всякий `Math.Min` здесь требует `StableMath`. Если операция работает только с integer
buffer length и не затрагивает simulation value, её можно оставить, но finding обязан
иметь exact reason. Float/vector serialization является migration/schema target.

### 7.3 `telemetry`

Допустимый пример:

```text
Stopwatch.Elapsed.TotalMilliseconds → double ElapsedMilliseconds
```

Telemetry разрешается только если значение:

- не записывается в artifact/manifest/fingerprint;
- не участвует в ветвлении simulation/bake result;
- не передаётся в network state;
- не меняет retries/timeouts, влияющие на authoritative result;
- используется только для log/report/UI timing.

Если хотя бы одно условие неизвестно, category — `ambiguous`, не `telemetry`.

### 7.4 `unity_boundary`

Unity type допустим в:

- serialized authoring field;
- Inspector/Scene View;
- presentation drawing;
- одном явном Unity-to-Jitter adapter.

Но path `Editor/**` или `Authoring/**` сам по себе не является разрешением. Collider
conversion и pre-bake normalization могут быть `bake_affecting`. Для них category может
остаться `unity_boundary`, но `impact=bake_affecting` и `disposition=must_migrate` для
math operations ниже момента conversion.

### 7.5 `test_fixture`

Разрешайте только:

- exact fixture file/symbol;
- конкретный rule;
- объяснение, что тест проверяет legacy/negative behavior;
- срок/эпик удаления, если fixture относится к migration debt.

Нельзя разрешать весь `Tests/**`: тесты компилируют production API и способны скрыть
устаревшие usages.

### 7.6 `third_party_vendor`

Vendor category требует exact root и identity. Нельзя классифицировать так файл только
потому, что исправлять его неудобно. `JitterIntegration~` package-owned и не vendor.

## 8. Каталог audit rules

Предлагаемые stable rule IDs:

| Rule | Находит | Default severity | Allowed categories |
|---|---|---|---|
| `JMP001` | declaration/reference `PhysicsVector3` | error | legacy test only |
| `JMP002` | declaration/reference `PhysicsQuaternion` | error | legacy test only |
| `JMP003` | Unity `Vector3`/`Quaternion`/`Bounds`/`Matrix4x4` | error | exact Unity boundary |
| `JMP004` | `Mathf` reference/invocation | error | presentation/authoring exceptions |
| `JMP005` | `MathF` reference/invocation | error | no default allow |
| `JMP006` | `System.Math` or `Math.*` simulation methods | error | integer/telemetry exact exception |
| `JMP007` | explicit scalar type `float`/`System.Single` | error | serialization/boundary/test exception |
| `JMP008` | explicit scalar type `double`/`System.Double` | error | telemetry/exact fixture exception |
| `JMP009` | alias to scalar other than canonical `Real` | error | canonical alias file only |
| `JMP010` | local `StableMath` declaration/duplicate | error | canonical Jitter source only |
| `JMP011` | `using` alias/static import hiding Math/Unity types | error | no implicit allow |
| `JMP012` | inline suppression marker for audit | error | none |
| `JMP013` | f/d numeric literal in deterministic context | review | exact classification |
| `JMP014` | raw candidate missing from code/non-code reconciliation | internal error | none |

### 8.1 Что считать scalar type finding

Нужно находить:

- `float` и `double` в fields, properties, parameters, returns, locals, arrays, generics;
- `System.Single`, `System.Double`, `Single`, `Double` при соответствующем namespace/import;
- casts `(float)`/`(double)`;
- `typeof(float)`, `sizeof(float)`, `default(float)`;
- aliases `using Scalar = System.Single`;
- `global using Real = ...` и любые конкурирующие aliases.

`var` сам по себе не нарушение: его фактический type без semantic model неизвестен. Но
initializer всё равно может дать finding `Mathf`, `Math.*`, custom DTO или scalar cast.
Если критичную связь нельзя вывести lexical scanner-ом, finding/файл помечается
`requiresSemanticReview`, а не молча считается чистым.

### 8.2 Numeric literals

Не делайте каждый `0f` немедленным hard error: это создаст шум на Unity boundary и в
existing f32 wire fixtures. Rule `JMP013` сначала `review`:

- literal в deterministic member получает `must_migrate/review`;
- literal в presentation/test может быть allowed exact scope;
- `d` literal в simulation path — error;
- отсутствие suffix не доказывает правильный `Real` type.

Production enforcement policy уточняется после `Real` assembly design в `JMP-E03`.

### 8.3 Math aliases и static imports

Scanner обязан распознавать:

```csharp
using MathAlias = System.Math;
using static System.Math;
using UMath = UnityEngine.Mathf;
```

Иначе `MathAlias.Sqrt`, unqualified `Sqrt` или `UMath.Clamp` обойдут простую regex.
Каждый alias/import записывается отдельным `JMP011` finding и связывается с последующими
invocations в пределах compilation unit.

## 9. Raw inventory и code-aware reconciliation

### 9.1 Зачем нужен raw inventory

Собственный C# masker может ошибиться на raw/interpolated strings, preprocessor branches
или незавершённом source. Поэтому сначала сохраняется полный textual superset.

Raw inventory включает candidate occurrences в:

- code;
- comments/XML-doc;
- string/char literals;
- disabled preprocessor branches;
- malformed source.

Затем code-aware scanner обязан классифицировать каждый raw candidate как `code`,
`non_code`, `preprocessor_disabled` либо `parse_error`. Ни один candidate не исчезает.

### 9.2 Минимальные candidate patterns

Raw layer ищет identifiers/qualified names, а не только вызовы:

```text
PhysicsVector3
PhysicsQuaternion
Vector3
Quaternion
Bounds
Matrix4x4
float
double
System.Single
System.Double
Mathf
MathF
System.Math
Math.
StableMath
USE_DOUBLE_PRECISION
global using Real
```

Report хранит rule candidate, path, line, column, matched text и lexical region.

### 9.3 Masker requirements

Masker должен сохранять длину строки и newline positions, заменяя содержимое пробелами.
Нужно покрыть fixtures для:

- `//` comment;
- `/* ... */` multi-line comment;
- XML-doc `///`;
- regular strings с escapes;
- verbatim strings `@"..."`;
- interpolated strings `$"..."`;
- interpolated verbatim strings `$@"..."`/`@$"..."`;
- raw strings `"""..."""`;
- interpolated raw strings;
- char literals;
- escaped quotes/backslashes;
- comment markers внутри strings;
- string markers внутри comments;
- preprocessor lines;
- malformed/unclosed token.

Malformed source не должен silently scan-иться частично. Выход — parse/mask error с path и
position; exit code non-zero.

### 9.4 Preprocessor policy

Proto не должен пытаться угадать весь Unity define set. Рекомендуемый policy:

- scan code во всех branches как source inventory;
- записывать nearest `#if/#elif/#else` condition;
- отдельно отмечать current production branch, если define profile передан явно;
- не исключать `USE_DOUBLE_PRECISION` branch только потому, что production сейчас f32;
- f64 branch остаётся предметом audit/preflight, а не dead code ignore.

## 10. Stable finding identity

Line number нельзя использовать как единственный key: вставка комментария сдвинет весь
allowlist. Предлагаемый finding ID строится из:

```text
ruleId
repository-relative path
declaring type/member identity, если найден
normalized matched token
normalized local code context hash
lexical region
```

Пример human-readable ID:

```text
JMP008:Runtime/Contracts/PhysicsCanonicalization.cs:
DataSakura.JitterPhysics.PhysicsCanonicalization.CanonicalQuaternion:
double:sha256-12chars
```

JSON может хранить полный SHA-256. Line/column остаются display fields, но не permission
identity.

Требования:

- одинаковый source даёт одинаковый ID на macOS/Linux/Windows;
- LF/CRLF не меняет ID;
- absolute checkout path не влияет;
- переименование member/path создаёт новый finding и stale old policy entry;
- изменение local code context требует повторного review;
- duplicate IDs являются internal error.

## 11. Policy schema

### 11.1 Общая структура

Предлагаемый `jitter-math-audit-policy.json`:

```json
{
  "schemaVersion": 1,
  "repositoryRootMarker": "Packages/com.datasakura.jitter-physics-baker/package.json",
  "ownedRoots": [],
  "vendorRoots": [],
  "excludedRoots": [],
  "rules": [],
  "classificationRules": [],
  "migrationDebt": [],
  "allowlist": []
}
```

Tool обязан reject unknown `schemaVersion`, unknown fields при strict mode, duplicate IDs,
absolute paths, `..`, backslashes и broad unsafe globs.

### 11.2 Migration debt entry

Migration debt — не suppress и не PASS. Entry означает: finding подтверждён, его текущее
состояние известно и назначено будущему эпику.

```json
{
  "findingId": "JMP008:...",
  "category": "simulation",
  "impact": "deterministic",
  "disposition": "must_migrate",
  "plannedEpic": "JMP-E04",
  "owner": "Jitter Physics Baker",
  "reason": "Quaternion normalization currently widens to double.",
  "targetAction": "Replace with canonical Real and StableMath path."
}
```

В inventory mode это reviewed debt. В enforcement mode после migration milestone такое
entry делает audit красным, пока finding существует.

### 11.3 Allowlist entry

Allowlist только для реально допустимого boundary:

```json
{
  "findingId": "JMP008:...",
  "category": "telemetry",
  "impact": "non_affecting",
  "disposition": "allowed",
  "owner": "Jitter Physics Baker",
  "reason": "Stopwatch duration is emitted only in diagnostics.",
  "proof": "No artifact, topology, network state or readiness branching consumer.",
  "reviewByVersion": "0.1.0"
}
```

Обязательные поля: exact `findingId`, category, impact, owner, reason, proof и review
boundary. Нельзя разрешать только по line, count или broad directory.

### 11.4 Classification rules

Для повторяющихся очевидных случаев допустимы rules, но они должны быть узкими:

- exact path plus declaring namespace/type;
- exact audit rule;
- expected occurrence count или symbol set;
- owner/reason;
- no recursive wildcard over all `Editor`/`Tests`.

Auto-classification rule, совпавший с нулём findings, является stale и падает. Rule,
совпавший с большим числом, чем expected maximum, падает как scope expansion.

## 12. JSON report contract

### 12.1 Top-level fields

```json
{
  "schemaVersion": 1,
  "toolVersion": "0.1.0-proto",
  "mode": "inventory",
  "repositoryRevision": "<git commit or working-tree marker>",
  "policyHash": "sha256:<hex>",
  "scanRoots": [],
  "filesScanned": 0,
  "rawCandidates": 0,
  "codeFindings": 0,
  "nonCodeCandidates": 0,
  "migrationDebtCount": 0,
  "allowedCount": 0,
  "ambiguousCount": 0,
  "unclassifiedCount": 0,
  "stalePolicyCount": 0,
  "findings": [],
  "summary": {}
}
```

Не включайте current time. Для dirty tree `repositoryRevision` может быть
`<HEAD>+working-tree`, а отдельный deterministic `scannedContentHash` строится из exact
relative paths и bytes. Git status сохраняется в human report/evidence, но не должен делать
JSON nondeterministic.

### 12.2 Finding fields

Каждый finding:

```json
{
  "id": "...",
  "ruleId": "JMP008",
  "path": "Packages/.../File.cs",
  "line": 27,
  "column": 16,
  "symbol": "Namespace.Type.Member",
  "matchedText": "double",
  "lexicalRegion": "code",
  "preprocessorCondition": null,
  "category": "simulation",
  "impact": "deterministic",
  "disposition": "must_migrate",
  "owner": "Jitter Physics Baker",
  "reason": "...",
  "targetAction": "...",
  "plannedEpic": "JMP-E04",
  "classificationSource": "exact-policy-entry"
}
```

Findings сортируются по path ordinal, line, column, ruleId, id. JSON пишется UTF-8 LF,
stable key ordering/indent и завершается одним newline.

### 12.3 Summary dimensions

Сохраните cross-tab counts:

- by rule;
- by category;
- by impact;
- by disposition;
- by planned epic;
- by assembly/root;
- code versus non-code;
- allowed versus debt versus ambiguous/unclassified;
- files with mixed categories;
- top files by finding count.

Counts никогда не заменяют finding list.

## 13. CLI contract

Предлагаемые команды:

```sh
python3 "Packages/com.datasakura.jitter-physics-baker/tools~/audit-jitter-math.py" \
  inventory \
  --policy "Packages/com.datasakura.jitter-physics-baker/tools~/jitter-math-audit-policy.json"

python3 "Packages/com.datasakura.jitter-physics-baker/tools~/audit-jitter-math.py" \
  inventory \
  --policy "Packages/com.datasakura.jitter-physics-baker/tools~/jitter-math-audit-policy.json" \
  --json-report "Logs/JitterMathAudit/source-audit.json" \
  --markdown-report "Logs/JitterMathAudit/source-audit.md"

python3 "Packages/com.datasakura.jitter-physics-baker/tools~/audit-jitter-math.py" \
  check \
  --policy "Packages/com.datasakura.jitter-physics-baker/tools~/jitter-math-audit-policy.json"

python3 "Packages/com.datasakura.jitter-physics-baker/tools~/audit-jitter-math.py" \
  validate-policy \
  --policy "Packages/com.datasakura.jitter-physics-baker/tools~/jitter-math-audit-policy.json"
```

### 13.1 Modes

`inventory`:

- используется в P00 до migration;
- разрешает reviewed `migrationDebt` существовать;
- падает на new/unclassified/ambiguous findings;
- падает на invalid/stale/overbroad policy;
- печатает debt count, но не называет его clean.

`check`:

- production enforcement mode;
- падает на любые forbidden findings;
- после milestone падает на remaining migration debt;
- допускает только exact valid allowlist;
- будет подключён в `JMP-E08`, не объявляется готовым в P00 без migration.

`validate-policy`:

- проверяет schema/path/duplicates/stale entries;
- не переписывает policy;
- требует scan, чтобы доказать used entries.

### 13.2 Exit codes

Зафиксируйте и протестируйте:

| Exit | Значение |
|---:|---|
| 0 | mode завершён и его критерии выполнены |
| 2 | violations, unclassified или ambiguous findings |
| 3 | invalid/stale/overbroad policy |
| 4 | IO, encoding, parse/mask или repository-root error |
| 5 | internal invariant: lost raw candidate, duplicate ID, nondeterministic state |

Не возвращайте 0 только потому, что report удалось записать.

### 13.3 Stdout/stderr

Успех:

```text
JMP_P00_AUDIT_OK mode=inventory files=... findings=... debt=... allowed=...
```

Ошибка:

```text
JMP_P00_AUDIT_FAILED exit=2 unclassified=1 ambiguous=0 stale=0
```

Detailed findings идут в stderr/report. Summary line должна быть пригодна для CI grep, но
CI обязан также проверять exit code и JSON, а не только marker.

## 14. Пошаговая самостоятельная реализация

### Шаг 0. Подготовить безопасную рабочую область

Из корня repository зафиксируйте:

```sh
git status --short
git branch --show-current
git rev-parse HEAD
git diff --check
```

Сохраните список unrelated dirty/untracked files. Не продолжайте в основном worktree с
negative fixture. Создайте disposable worktree от exact baseline commit. Используйте
task-specific variable, не `$HOME`:

```sh
JMP_P00_WORKTREE="$(mktemp -d)/jitter-physics-baker-jmp-p00"
git worktree add -b d.islamov/jmp-p00-audit "$JMP_P00_WORKTREE" HEAD
cd "$JMP_P00_WORKTREE"
```

Если branch уже существует, не удаляйте и не переиспользуйте её вслепую: выберите новое
точное имя или проверьте existing branch. Cleanup выполняйте только после проверки, что
disposable worktree clean и ничего нужного не осталось.

### Шаг 1. Снять baseline проекта

Запишите:

- branch/HEAD/status;
- package version;
- `JitterPhysicsPackage.PackageVersion`;
- Jitter lock assembly/precision/source hash;
- список assemblies;
- список scan files;
- текущие четыре mandatory checks;
- Unity test state и existing blockers, если они релевантны.

Команды source inventory выполняйте из repository root и сохраняйте exact patterns/version
tooling в report. Не редактируйте findings на этом этапе.

### Шаг 2. Создать policy skeleton

Сначала запишите:

- exact owned roots;
- exact vendor root;
- exact generated exclusions;
- rule catalog;
- empty migration debt/allowlist;
- strict schema version.

Запустите `validate-policy`: skeleton должен быть valid, но `inventory` ожидаемо красный из-за
unclassified current findings.

### Шаг 3. Реализовать deterministic file enumeration

Требования:

- найти repository root по explicit argument или root marker;
- reject root, где marker отсутствует;
- enumerate only configured roots;
- не следовать symlinks;
- reject path outside root after resolution;
- читать bytes, нормализовать только для scanner, но hash исходных bytes записывать отдельно;
- сортировать relative POSIX paths ordinal;
- reject duplicate normalized paths/case collisions;
- печатать files scanned count.

Тестируйте repository invocation и invocation из вложенного directory: report paths/IDs
должны совпадать.

### Шаг 4. Реализовать raw candidate scanner

Для каждого файла:

1. прочитать UTF-8 bytes;
2. определить LF/CRLF без изменения файла;
3. найти все raw candidate patterns;
4. сохранить path/offset/line/column/matched text;
5. отсортировать;
6. вычислить raw inventory hash.

Invalid UTF-8 — exit 4, а не replacement characters.

### Шаг 5. Реализовать C# lexical masker

Начните только после fixtures. State machine должна различать code/comment/string/char и
сохранять positions. На каждом raw candidate запросите lexical region.

Если syntax незнаком:

- не продолжать как code;
- создать `parse_error` finding;
- завершить exit 4;
- добавить fixture перед исправлением.

Не используйте regex, которая удаляет comments/strings вместе с newline: line evidence станет
ложным.

### Шаг 6. Реализовать rule matchers

Rule matcher получает masked code, raw tokens и per-file imports/aliases. Реализуйте rules
по одному с fixtures:

1. custom DTO declarations/references;
2. scalar type tokens/casts;
3. Unity type imports/references;
4. `Mathf`/`MathF`;
5. `Math` qualified/unqualified aliases;
6. local `StableMath` declaration;
7. canonical/competing Real aliases;
8. inline suppression markers;
9. informational numeric literal review.

После каждого matcher проверьте raw reconciliation. Не переходите дальше при lost candidate.

### Шаг 7. Извлечь symbol/context

Proto может использовать conservative lexical symbol extraction:

- namespace;
- containing type;
- nearest method/property/field declaration;
- local context lines;
- preprocessor condition stack.

Если member не определён уверенно, используйте `symbol=null` и
`requiresSemanticReview=true`; finding остаётся unclassified до ручного решения. Не
присваивайте ошибочный symbol ради стабильного ID.

### Шаг 8. Сформировать findings и stable IDs

Нормализуйте context:

- LF;
- trim trailing whitespace только в identity copy;
- не включать line number;
- не включать absolute path;
- включить rule/path/symbol/token/region.

Проверьте uniqueness и cross-platform fixtures.

### Шаг 9. Выполнить ручную классификацию

Работайте file-by-file, member-by-member:

1. прочитайте control/data flow;
2. определите, может ли value изменить artifact/world/network output;
3. назначьте category/impact;
4. назначьте disposition;
5. укажите owner/reason/target action/epic;
6. проверьте соседние findings в том же member;
7. отметьте mixed-category files;
8. не добавляйте allowlist до peer review.

Для каждого `telemetry` выполните обратный search всех consumers. Для каждого
`unity_boundary` найдите точку conversion и докажите, что ниже неё Unity type не живёт.

### Шаг 10. Разделить debt и allowlist

Current custom DTO/simulation usages идут в `migrationDebt`, не allowlist. В allowlist
попадают только:

- подтверждённая telemetry;
- presentation-only Unity math;
- exact authoring boundary;
- exact legacy/negative test fixture;
- exact vendor exclusion.

Запустите policy validation и убедитесь, что нет:

- unused entries;
- duplicate finding IDs;
- missing owner/reason/proof;
- broad path scopes;
- count-only permissions;
- unknown categories/epics.

### Шаг 11. Сгенерировать baseline reports

Сгенерируйте JSON дважды в разные temp output files и сравните:

```sh
cmp first-source-audit.json second-source-audit.json
shasum -a 256 first-source-audit.json second-source-audit.json
```

Оба SHA-256 должны совпасть. Затем создайте human report из того же in-memory result, а не
вторым независимым scanner pass.

### Шаг 12. Выполнить negative fixture

Сначала используйте synthetic temp source tree. Затем в disposable worktree добавьте один
точный запрещённый C# файл в owned deterministic root, например с новым explicit `double`
и `Math.Sqrt` в несуществовавшем member. Не добавляйте fixture в основной worktree и не
коммитьте её.

Ожидается:

- raw candidate count увеличился;
- появились конкретные rule IDs;
- finding имеет repository-relative path/line/context;
- finding не совпал со старым migration debt;
- `inventory` завершился exit 2 как unclassified/new finding;
- `check` завершился exit 2 как violation;
- report не изменил source/policy;
- после удаления только fixture исходный JSON SHA-256 восстановился.

Не используйте уже существующую строку и не меняйте count baseline вручную: это не докажет
new-finding detection.

### Шаг 13. Проверить false positives

Добавьте synthetic fixtures, где forbidden words находятся в:

- comments/XML-doc;
- normal/verbatim/raw/interpolated strings;
- char literal;
- test expected error text;
- JSON string embedded в C#;
- disabled preprocessor branch.

Raw layer обязан их увидеть. Code matcher не должен выдавать production code finding для
comment/string, но reconciliation должен сохранить `non_code` candidate. Disabled branch
сохраняется как source finding с condition, согласно policy.

### Шаг 14. Проверить policy drift

Для exact allowlist entry:

1. запустите green inventory;
2. измените synthetic source context;
3. убедитесь, что old entry stale;
4. убедитесь, что new finding unclassified;
5. верните source;
6. убедитесь, что original result восстановился.

Добавьте лишний allowlist entry: `validate-policy` должен упасть. Расширьте classification
rule так, чтобы он захватил больше findings, чем expected: audit должен упасть как scope
expansion.

### Шаг 15. Прогнать repository checks

После удаления negative fixture и до commit:

```sh
git diff --check
python3 tools/verify-package-meta.py
python3 "Packages/com.datasakura.jitter-physics-baker/tools~/verify-jitter2-lock.py"
python3 "Packages/com.datasakura.jitter-physics-baker/tools~/test-jitter2-lock.py"
bash "Packages/com.datasakura.jitter-physics-baker/tools~/test-dotnet.sh"
```

Дополнительно при новых test C# files:

```sh
python3 tools/dev-refresh-csproj.py
dotnet build DataSakura.JitterPhysics.Editor.csproj -v q --nologo
dotnet build DataSakura.JitterPhysics.Editor.Tests.csproj -v q --nologo
dotnet build DataSakura.JitterPhysics.Tests.csproj -v q --nologo
```

Source audit tooling не должен менять runtime behavior. Unity EditMode/PlayMode можно
оставить `NOT RUN` для чистого Python-only Proto только при явном объяснении evidence
boundary. Перед release/после assembly или Tests changes они обязательны и требуют fresh XML.

### Шаг 16. Проверить original worktree

Вернитесь в original checkout и сравните:

```sh
git status --short
git diff --check
```

Unrelated files должны совпасть с baseline. Никакой negative fixture, generated report,
`bin/obj` или temp policy не должен появиться в original worktree.

## 15. Unit test matrix

### 15.1 File enumeration

- [ ] Roots перечисляются детерминированно.
- [ ] Invocation cwd не влияет.
- [ ] POSIX path normalization стабильно.
- [ ] LF/CRLF source даёт одинаковые finding IDs.
- [ ] Symlink наружу не обходится.
- [ ] Missing root даёт exit 4.
- [ ] Invalid UTF-8 даёт exit 4.
- [ ] Case-collision/duplicate normalized path даёт internal error.
- [ ] Generated exact roots исключены.
- [ ] Broad unknown exclusion rejected policy validator-ом.

### 15.2 Lexical masker

- [ ] Line/block/XML comments.
- [ ] Escaped regular strings.
- [ ] Verbatim strings.
- [ ] Interpolated strings.
- [ ] Raw/interpolated raw strings.
- [ ] Char literals.
- [ ] Comment markers inside strings.
- [ ] Quotes inside comments.
- [ ] Multiline constructs.
- [ ] Preprocessor directives/branches.
- [ ] Unterminated string/comment produces error.
- [ ] Line/column preserved after masking.

### 15.3 Rules

- [ ] `PhysicsVector3` declaration and usage.
- [ ] `PhysicsQuaternion` declaration and usage.
- [ ] Qualified/unqualified Unity types.
- [ ] `Mathf` invocation/reference.
- [ ] `MathF` invocation/reference.
- [ ] `System.Math`, `Math`, alias and static import.
- [ ] `float/double` fields/parameters/returns/locals/arrays/casts.
- [ ] `System.Single/System.Double` and aliases.
- [ ] Canonical `Real` alias recognised.
- [ ] Competing scalar alias rejected.
- [ ] Local `StableMath` declaration rejected.
- [ ] Inline suppression marker rejected.
- [ ] Numeric literals reported in review mode.

### 15.4 Classifier/policy

- [ ] Every code finding has one category.
- [ ] Ambiguous is hard failure.
- [ ] Missing owner/reason/proof rejected.
- [ ] Migration debt is not counted as allowed.
- [ ] Inventory permits reviewed debt but reports it explicitly.
- [ ] Check mode rejects remaining debt after configured milestone.
- [ ] Stale/unused entry rejected.
- [ ] Duplicate entry rejected.
- [ ] Unknown finding ID rejected.
- [ ] Broad scope expansion rejected.
- [ ] New finding in already indebted file is not hidden.
- [ ] Mixed-category file retains per-finding classification.

### 15.5 Report determinism

- [ ] Two runs produce byte-identical JSON.
- [ ] JSON key/finding ordering stable.
- [ ] No timestamp/absolute path/username.
- [ ] Same content in different checkout path produces same content hash/findings.
- [ ] Human summary counts match JSON.
- [ ] Raw candidate reconciliation count closes exactly.
- [ ] Duplicate finding ID is internal error.
- [ ] Report write failure returns non-zero.

## 16. Integration test matrix

| Scenario | Expected result |
|---|---|
| Current checkout + empty classifications | FAIL, all code findings unclassified |
| Current checkout + reviewed policy | PASS inventory, debt count > 0 |
| Current checkout in check mode before migration | FAIL, migration debt remains |
| New forbidden scalar in deterministic source | FAIL with `JMP007/JMP008` |
| New `MathF.Sqrt` | FAIL with `JMP005` |
| New `using static System.Math` + `Sqrt` | FAIL with `JMP011/JMP006` |
| Forbidden word only in comment | raw candidate, no code violation |
| `Stopwatch` telemetry exact entry | allowed only with proof |
| New telemetry-like `double` outside exact entry | FAIL unclassified |
| Unity `Vector3` in Scene View exact member | allowed boundary |
| Unity `Vector3` in Runtime/Contracts | FAIL |
| New finding in allowlisted Editor file | FAIL unless exact ID reviewed |
| Deleted allowlisted source | FAIL stale entry |
| Vendor Jitter source | excluded with recorded vendor identity |
| Second vendor-like root | FAIL unknown/exclusion expansion |
| Run from repository root/nested cwd | identical report |
| Run in copied checkout | identical relative findings |
| Report output omitted | no filesystem writes |

## 17. Как вручную классифицировать текущие сложные места

### 17.1 `PhysicsCanonicalization`

Проверьте:

- `double lengthSquared`;
- casts каждого quaternion component;
- `Math.Sqrt`;
- final cast to `float`;
- negative zero canonicalization;
- formatting-only helpers.

Normalization findings: `simulation`, `deterministic`, `must_migrate`, `JMP-E02/E04`.
Formatting может быть serialization/diagnostics, но требует отдельного finding reason.

### 17.2 `CanonicalBinaryReader/Writer`

Разделите:

- explicit `float` wire layout: `serialization`, schema review;
- custom vector/quaternion codec: `serialization`, must migrate;
- comments с word `float`: `non_code` raw candidate;
- integer buffer operations: non-simulation, exact reason.

Не разрешайте весь codec directory: это один из самых критичных deterministic scopes.

### 17.3 `PhysicsArtifactValidator`

`Math.Abs` над coordinates/ranges влияет на acceptance artifact-а и должен быть
`serialization/deterministic` либо `simulation/deterministic`, не generic utility.
Проверьте, может ли изменение math semantics изменить accepted bytes.

### 17.4 `JitterPhysicsColliderConverter`

Разделите member-level:

- Unity collider/transform reads: `unity_boundary`;
- conversion point Unity → authoritative values: boundary adapter;
- scale/radius/length calculations: `bake_affecting`, migration review;
- error bounds/tolerances: deterministic;
- comments/Inspector messages: non-code/presentation.

Не создавайте path-wide allowlist для файла.

### 17.5 `JitterPhysicsBakeGeometryOverlay`

Scene drawing types обычно `unity_boundary/presentation`. Но `ToPhysics`, geometry compare,
bounds reconstruction или runtime-data mapping могут пересекать authoritative values.
Каждый member классифицируется отдельно.

### 17.6 `JitterPhysicsWorldBuilder` и server startup

`ElapsedMilliseconds` — telemetry candidate. Shape/world values — simulation. Startup
compatibility values — runtime-affecting validation. Докажите data flow, прежде чем разрешать
любой `double`.

### 17.7 Tests и samples

Для каждого legacy DTO usage решите:

- оно обязано мигрировать вместе с API;
- это golden legacy reader fixture;
- это negative compilation/source fixture;
- это presentation sample.

Только второй/третий вариант могут получить exact temporary allowance с removal milestone.

## 18. Проверка полноты классификации

Перед exit Proto выполните четыре reconciliation:

### Reconciliation A — raw → lexical regions

```text
rawCandidates == codeCandidates + nonCodeCandidates + disabledCandidates + parseErrors
```

### Reconciliation B — code → category

```text
codeFindings == categorizedFindings + ambiguousFindings + unclassifiedFindings
```

Для PASS inventory:

```text
ambiguousFindings == 0
unclassifiedFindings == 0
```

### Reconciliation C — disposition

```text
categorizedFindings == migrationDebt + allowed + vendor + legacyFixture
```

Никакой `ignored` bucket не допускается.

### Reconciliation D — policy use

```text
policyEntries == usedEntries
staleEntries == 0
scopeExpansion == 0
```

Каждое равенство проверяется самим tool и покрывается unit test. Human reviewer не должен
складывать counts вручную.

## 19. Проверка отсутствия регрессий

JMP-P00 не меняет runtime code, но проверка «ничего не сломалось» всё равно разделяется:

### Gate A — audit correctness

- unit fixtures green;
- negative integration fixture detected;
- false positives classified correctly;
- raw reconciliation exact;
- deterministic reports identical;
- stale/overbroad policy rejected.

### Gate B — repository integrity

- `git diff --check` green;
- metadata/LFS verification green;
- Jitter lock verification unchanged;
- lock invariant tests unchanged;
- portable/server `78` baseline tests либо актуальный count green;
- no unexpected tracked/generated files.

### Gate C — Unity evidence boundary

Если добавлены только Python/JSON/docs/tool fixtures и ни один asmdef/C# source не изменён,
зафиксируйте Unity EditMode/PlayMode как `NOT RUN — no compiled Unity code changed`, но не
как PASS. Если добавлен C# test/fixture в compiled assembly, refresh csproj, build tests и
запустите Unity suites с fresh XML.

### Gate D — workspace preservation

- original untracked files совпадают с baseline;
- negative fixture существовала только в disposable worktree;
- reports находятся только в explicit output/temp paths;
- no `git add -A`;
- final status содержит только task-owned files плюс исходные unrelated files.

## 20. Baseline report: обязательное содержание

`JMP_P00_SOURCE_AUDIT_BASELINE.md` должен содержать:

1. date и tool version;
2. repository path только для воспроизводимости human report;
3. branch, HEAD, package version;
4. original git status;
5. exact scan roots/exclusions;
6. raw patterns;
7. files scanned count;
8. raw candidates count;
9. code/non-code/disabled/parse counts;
10. counts by rule/category/impact/disposition/root;
11. migration debt count;
12. allowlist count;
13. mixed-category files;
14. ambiguous/unclassified count — обязаны быть zero для Proto exit;
15. stale policy count — zero;
16. policy SHA-256;
17. deterministic JSON SHA-256;
18. negative fixture command/result/exit code;
19. false-positive fixture result;
20. repository check commands/results;
21. Unity tests `PASS/FAIL/BLOCKED/NOT RUN` с evidence boundary;
22. final git status;
23. unrelated-file preservation statement;
24. список спорных архитектурных мест для `JMP-E00`;
25. явное утверждение, что production source migration не выполнялась.

## 21. Что запрещено считать доказательством

- один `rg` command без comments/aliases reconciliation;
- count совпал с прошлым запуском;
- tool завершился 0, но JSON не проверен;
- все current findings добавлены в allowlist;
- whole-directory allowlist для `Editor` или `Tests`;
- Console без ошибок;
- отсутствие `MathF` сегодня;
- timestamp report/tool;
- ручное удаление findings из JSON;
- generated report без policy hash;
- negative fixture, добавленная в основной dirty worktree;
- unit test regex без integration test на actual repository paths;
- успешный portable test как доказательство Unity/IL2CPP;
- наличие `Jitter2.Core.dll` как доказательство canonical identity.

## 22. Типичные ошибки и диагностика

### Audit внезапно не видит comments

Это нормально только для code findings. Raw inventory обязан видеть candidate, а
reconciliation — классифицировать как `non_code`. Если candidate исчез полностью, masker/raw
pipeline нарушен.

### `Math.Abs` не находится

Проверьте `using Math = ...`, `using static System.Math`, namespace imports и rule matcher.
Raw `Math.` должен присутствовать даже если semantic owner неизвестен. Unknown owner —
ambiguous failure.

### Тысячи `float` findings в presentation code

Не добавляйте broad allowlist. Сначала grouping только для review, затем exact member-level
classification. Numeric literal rule может оставаться review severity до E03, explicit type
rule — строгий.

### Новый finding скрылся за existing debt

Policy слишком широк. Permission должна ссылаться на stable finding ID/context, не path/rule
count. Добавьте test «new finding in indebted file».

### Allowlist постоянно становится stale после форматирования

Context identity слишком чувствительна. Не включайте line number/indent-only whitespace, но
оставьте declaring symbol и normalized local tokens. Изменение semantics должно требовать
review; форматирование — нет.

### Report отличается между checkout paths

Ищите absolute paths, cwd, OS separators, locale sorting, timestamps и unordered maps.
Используйте repository-relative POSIX paths, ordinal sort и stable JSON serializer.

### Report отличается LF/CRLF

Finding identity должна строиться на normalized lexical content, а source content hash policy
должен явно решить, хеширует ли canonical LF. Не смешивайте display byte hash и normalized
finding identity.

### Vendor exclusion скрывает package integration

Разрешён только exact `Jitter2~` root. `JitterIntegration~` — owned scope. Добавьте fixture,
доказывающую, что похожее имя/соседняя папка не исключается.

### Tool пишет reports при обычном check

Это нарушение no-write default. Reports создаются только с explicit output arguments. Unit
test должен сравнить filesystem tree до/после invocation без output paths.

## 23. Definition of Done JMP-P00

`JMP-P00` завершён только если:

- [ ] Exact owned/vendor/generated scopes приняты.
- [ ] Executable CLI работает без Unity, сети и внешних packages.
- [ ] Raw textual inventory строится полностью.
- [ ] C# lexical regions покрыты fixtures.
- [ ] Raw/code/non-code reconciliation закрывается точно.
- [ ] Все rules `JMP001`–`JMP014` имеют positive/negative fixtures.
- [ ] Каждый current code finding классифицирован.
- [ ] `ambiguousCount == 0`.
- [ ] `unclassifiedCount == 0`.
- [ ] Migration debt отделён от allowlist.
- [ ] Каждая allowance имеет exact finding ID, owner, reason и proof.
- [ ] Stale/unused/overbroad policy entries fail.
- [ ] New violation в уже indebted file fail.
- [ ] Disposable-worktree negative fixture даёт ожидаемый non-zero exit.
- [ ] После удаления fixture baseline JSON SHA-256 восстанавливается.
- [ ] Два clean runs дают byte-identical JSON.
- [ ] Human summary counts совпадают с JSON.
- [ ] Tool без output arguments ничего не пишет.
- [ ] Existing metadata/lock/portable checks зелёные.
- [ ] Unity gates имеют честный `PASS/FAIL/BLOCKED/NOT RUN`, без подмены Console.
- [ ] Baseline report содержит все обязательные evidence.
- [ ] Original unrelated files не затронуты.
- [ ] Production Jitter/contracts/artifact code не изменялся.
- [ ] Push/tag/publish не выполнялись без отдельного подтверждения.

## 24. Самый безопасный порядок выполнения

1. Снять original status/HEAD/version/lock baseline.
2. Создать disposable worktree.
3. Зафиксировать exact scope и policy schema.
4. Создать lexer fixtures до scanner implementation.
5. Реализовать deterministic file enumeration.
6. Реализовать raw inventory.
7. Реализовать lexical masker и reconciliation.
8. Добавлять rules по одному с tests.
9. Реализовать stable IDs.
10. Получить красный unclassified current inventory.
11. Классифицировать findings file/member-level.
12. Разделить migration debt и allowlist.
13. Проверить stale/overbroad policy.
14. Проверить JSON determinism.
15. Прогнать synthetic negative/false-positive fixtures.
16. Прогнать disposable-worktree negative integration fixture.
17. Удалить только fixture и доказать восстановление baseline hash.
18. Запустить repository checks.
19. Подготовить baseline report.
20. Проверить original worktree status.
21. Отдать Proto на review как input для `JMP-E00`.

Не переходите к исправлению найденных source usages в рамках JMP-P00. Сначала review должен
подтвердить, что classifier не потерял candidates, не скрывает новые нарушения и корректно
различает simulation, artifact, Unity boundary, telemetry, tests и vendor code.
