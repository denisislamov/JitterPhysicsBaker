# JMP-E08 — автоматический enforcement

Дата фиксации: 2026-09-02.

Ветка: `d.islamov/jmp-e00-baseline-adrs`.

Baseline эпика: `65647d1` (`JMP-E07`).

## Source audit

`tools~/audit-jitter-math.py check` сканирует owned C# scope после lexical masking и запрещает
unreviewed `PhysicsVector3`, `PhysicsQuaternion`, Unity math вне boundary, `Mathf`, `MathF`,
simulation `System.Math`, прямые `float`/`double`, scalar aliases, local `StableMath`, math imports,
inline suppressions и explicit-precision literals.

Policy schema 2 содержит repository-relative exact path/prefix, rule IDs, owner и reason для
каждого класса разрешений. Vendor `Jitter2~` указан отдельным точным root и не входит в owned scan.
Новый finding меняет reviewed hash и делает check красным; entry без единого finding также делает
policy красной. Inline suppression специально запрещена.

При нарушении stderr содержит `path:line:column`, rule, category и remediation. Self-test создаёт
намеренное `double + Math.Sqrt` использование и проверяет non-zero exit и actionable output.

## Runtime identity

Существующие lock, installer, package-layout и portable fixtures остаются частью общего gate:

- source/precompiled provider inventory и duplicate/incompatible states;
- exactly one loaded `Jitter2.Core` в portable consumer;
- canonical source hash, compile profile ID и staged binary hashes;
- direct source/precompiled references в integration template;
- exact server/prebuilt DLL equality;
- tampered server DLL и f64 runtime profile rejection до world mutation.

## CI и developer workflow

`.github/workflows/package.yml` запускает audit self-tests и enforcement до portable .NET suite.
`tools~/README.md` содержит те же exact commands. Audit ничего не пишет без явного report path.
Фактический удалённый CI run будет проверен только после push; наличие YAML не считается PASS CI.

## Результат

| Gate | Результат |
| --- | --- |
| Audit self-tests | PASS: 12/12 |
| Production enforcement | PASS: 110 files, 1810 findings, 0 unapproved |
| Allowlist validation | PASS: 0 stale/unused entries |
| Intentional forbidden-use fixture | PASS: exit 2 + path/category/remediation |
| Baseline-change fixture | PASS |
| Missing/duplicate/tampered/f64 fixtures | PASS в lock/portable suites |
| Remote CI workflow run | NOT RUN до push |
| Unity engine tests | BLOCKED текущим Licensing Client; E07 evidence содержит exact error |

Enforcement не меняет Setup: базовый package остаётся Jitter-free, Jitter и integration ставятся
отдельными явными действиями, внешний compatible Jitter не перезаписывается.
