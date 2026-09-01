# Canonical Jitter RC: P00-E01 evidence

Дата проверки: 2026-09-01, Asia/Makassar.

## Итог

Canonical Jitter release candidate подготовлен и технически готов к отдельному approval/publish
циклу. Custom Navigation не менялся. Существующий explicit Setup flow Jitter Physics Baker сохранён:
Jitter устанавливается отдельным действием до integration и до компиляции consumer assemblies.

`P00-E01` пока имеет verdict **BLOCKED**, а не PASS: предложенный immutable tag и GitHub release
ещё не опубликованы. Push/tag/publish выполняются только после отдельной команды пользователя.

## Предлагаемые immutable coordinates

| Поле | Значение |
|---|---|
| Repository | `https://github.com/denisislamov/jitter-physics-baker` |
| Intended tag | `jitter-v2.8.9-datasakura.1-rc.1` |
| Distribution version | `2.8.9-datasakura.1-rc.1` |
| Asset | `DataSakura.Jitter2.Core-2.8.9-datasakura.1-rc.1.zip` |
| Asset SHA-256 | `61896c9d63e6262c113c9c353773b36b1825b10a3630d1f9b4eb05af07977bab` |
| AssemblyName | `Jitter2.Core` |
| Jitter2.Core.dll SHA-256 | `944666bbe73dfce5ffc5bfb18569fb0004f50e767dcbb8b471dde15242023ca6` |
| Jitter2.Core.xml SHA-256 | `be7115c897ac58357cd8155dd1cb91bb6d35f5b31b72841102b43eec83877fa0` |
| Unsafe DLL SHA-256 | `01748200f2400c742aa689f1f5101bd6298efdfd92c00c18f4fa473847235ba9` |
| Upstream commit | `c15bc6abfdda90a936975979a42f7a54a211084e` |
| Patch set | `unity-netstandard21-stablemath-v2` |
| Source content hash | `sha256:749c79e40c4965cd455ca80a2d1d1c80a24eb580eb7b721e07adc78b41c82762` |
| Compile profile ID | `a2925211b983330117414426be9bf8a2798ce9169c1206e1e55178f708cfa72e` |
| Precision | `f32` |
| StableMath compatibility ID | `54b456c04074909605d2ba138e5001d39a90a338885eafcb32265483b35054b0` |
| Baker runtime compatibility ID | `4d83760322e8e89365d6721126b243584b4369e66d052c679a8a12cc34c8212b` |

Full release commit SHA intentionally is not self-declared inside the commit. После публикации он
должен быть получен из remote tag ref и совпасть с commit, на который указывает immutable tag.

## Public StableMath contract

`Jitter2.LinearMath.StableMath` является public type. Supported surface:

- constants: `Pi`, `HalfPi`, `QuarterPi`, `TwoPi`;
- trigonometry: `Sin`, `Cos`, `SinCos`, `Atan2`, `Asin`, `Acos`;
- scalar helpers: `IsFinite`, `Abs`, `Min`, `Max`, `Clamp`, `Clamp01`, `Lerp`;
- owned f32 square root: `Sqrt` без `Math`, `MathF`, `MathR` или platform libm;
- deterministic conversion: `RoundAwayFromZero`, `RoundToInt64AwayFromZero`,
  `QuantizeToInt64`.

Public exceptional behavior canonicalizes NaN where a numeric result is returned. `Abs(-0)` and
`Sqrt(-0)` return `+0`. Invalid integer conversion/quantization inputs throw before deterministic
work. Unsupported f64 is rejected by the release probe.

## Separate-install contract

- Unity: explicit **Install Jitter2** остаётся отдельным шагом; затем consumer напрямую компилирует
  `Jitter2.Core` types. В isolated project доказана ровно одна `Jitter2.Core.dll`.
- .NET: consumer использует explicit `Reference`/`HintPath` на DLL из того же archive и copy-local.
- Unity и .NET используют одинаковые bytes/hash. Server не пересобирает Jitter sources.
- Custom Navigation не содержит Jitter source/DLL и не получает Jitter транзитивно из Baker.
- Automatic UPM dependency не добавлена.

## Regression evidence

| Gate | Status | Evidence |
|---|---|---|
| Clean tracked build inputs | PASS | оба required `.csproj` tracked; clean worktree build работает |
| Source lock | PASS | 96 inputs; exact source content hash совпал |
| Package metadata/LFS | PASS | complete `.meta`; no LFS pointers in package validation |
| Deterministic DLL rebuild | PASS | две forced non-incremental сборки дали одинаковые DLL/XML SHA-256 |
| Deterministic release archive | PASS | две сборки ZIP дали одинаковые bytes/SHA-256 |
| Public API external compile (.NET) | PASS | clean net10 consumer; `StableMath` public; f32 |
| Portable/server regression | PASS | 89/89, failed 0, skipped 0 |
| Sqrt f32 oracle | PASS | 100,000 stratified finite inputs совпали bit-for-bit с correctly rounded f32 oracle |
| Missing Jitter negative | PASS | verifier rejected archive without Jitter2.Core |
| Duplicate Jitter negative | PASS | verifier rejected duplicate ZIP assembly member |
| Hash mismatch negative | PASS | verifier rejected one-bit DLL tamper |
| f64 negative | PASS | clean consumer rejected foreign Jitter2.Core with double precision |
| Isolated Unity separate install | PASS | explicit Jitter then integration; exactly one Jitter2.Core |
| Public API external compile (Unity) | PASS | external Editor assembly emitted canonical success marker |
| Isolated editor API | PASS | 7/7 |
| Isolated sample PlayMode | PASS | 1/1 |
| Full Unity EditMode | PASS | 97/97 |
| Full Unity PlayMode | PASS | 57/57 |
| Player/IL2CPP | NOT RUN | не входит в P00 DoD; остаётся отдельным release/runtime gate |
| Published immutable remote tag/asset | BLOCKED | требуется отдельное user approval для tag/push/release upload |

## Approval/publish checklist

1. Review scoped diff and approve this candidate.
2. Commit the RC implementation branch.
3. Create signed/annotated tag `jitter-v2.8.9-datasakura.1-rc.1` at the approved commit.
4. Push branch and tag only after explicit permission.
5. Upload ZIP, detached manifest and `.sha256` as one GitHub release.
6. Resolve tag through `git ls-remote`, download the published asset into a clean directory,
   verify its SHA and rerun the release verifier.
7. Run the P00 read-only audit against published coordinates. Только после этого verdict может
   измениться с BLOCKED на PASS и разблокировать Custom Navigation implementation prompts.
