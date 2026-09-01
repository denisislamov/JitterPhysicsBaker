# JMP-E02 — evidence публичного StableMath

Дата фиксации: 2026-09-01.

Ветка: `d.islamov/jmp-e00-baseline-adrs` — общая migration-ветка для всех эпиков.

Baseline эпика: commit `23555f28f13e3f5353a39c8e9586f501318d61d3` (`JMP-E01`).

## Результат эпика

`Jitter2.LinearMath.StableMath` стал публичным supported API canonical Jitter distribution.
Изменение не переносит Jitter в основной UPM dependency graph: исходник и prebuilt по-прежнему
находятся в скрытом `Jitter2~/`, а установка `Jitter2.Core` и integration выполняется прежней
отдельной Setup-командой.

В API включены:

- константы `Pi`, `HalfPi`, `QuarterPi`, `TwoPi`;
- `Sin`, `Cos`, `SinCos`, `Atan2`, `Asin`, `Acos`;
- `IsFinite`, `Abs`, `Min`, `Max`, `Clamp`, `Clamp01`;
- правильно округлённый f32 `Sqrt`, реализованный целочисленным алгоритмом без platform sqrt;
- `Lerp` с явно разделёнными multiply и add;
- `RoundAwayFromZero`, `RoundToInt64AwayFromZero`, `QuantizeToInt64` с bit-defined halfway policy.

Полный contract — `Packages/com.datasakura.jitter-physics-baker/Jitter2~/STABLE_MATH.md`. Для
каждого члена там записаны domain, exceptional inputs, signed zero, error bound и граница
determinism evidence.

## Canonical ownership и lock

- Единственная декларация находится в
  `Jitter2~/Runtime/LinearMath/StableMath.cs`.
- Portable test сканирует package sources и запрещает вторую consumer-local декларацию.
- Поскольку pinned upstream `c15bc6a` не содержит этот файл, lock schema 3 хранит его в
  `canonicalPatches`, а не в прежнем `consumerPatches`.
- `patchSetId`: `unity-netstandard21-stablemath-public-v3`.
- `integrationApiVersion`: `2`.
- Source sync сохраняет только hash-verified canonical patch и не меняет внешний Jitter checkout.
- Два clean build должны дать byte-identical DLL/XML/Unsafe artifacts до staging в `Prebuilt`.

Финальные identities и hashes:

| Поле/artifact | SHA-256 |
| --- | --- |
| `StableMath.cs` canonical patch | `ee224074a29593f450fccc7ba1d35a73002f97d32fff6db80166133a7248b3b7` |
| `sourceContentHash` | `ca940ca6483ffcedf65854719396cec2d9e038cc43c01e7d35d147cd70766940` |
| `buildInputHash` | `4cb0fa885c351e799f3d9acc3aab0d903b90f0f27e6bfe8cb73d43b13c537006` |
| `Jitter2.Core.dll` | `1e0aea7a6da1e3887ce90eabe6b508341870b62992b2c79d09382586db3e0321` |
| `Jitter2.Core.xml` | `7472a88c36e6239ad8b2be9cb0d076bd4fb3228080184a40ad7df03c6fc16612` |

Authoritative значения находятся в `jitter2.lock.json` этого же коммита.

## Тестовый контракт

`StableMathContractTests` проверяет:

- точный public surface и биты четырёх констант;
- `+0`/`-0`, значения непосредственно вокруг `0.5`, положительные и отрицательные halfway cases;
- smallest subnormal, границы normal/subnormal и 100 000 воспроизводимых f32 значений для `Sqrt`;
- quadrant boundaries, gameplay input `10000`, NaN, infinity и finite out-of-domain policy;
- canonical NaN `7fc00000` и типизированные исключения для нарушений caller contract;
- отсутствие второй декларации `StableMath` в package sources;
- error bounds на плотной сетке: `Sin/Cos <= 0.001` в `[-10000,10000]`, `Atan2 <= 0.000001`
  на integer grid `[-100,100]`, `Asin/Acos <= 0.000001` в `[-1,1]`.

Измеренные максимумы .NET/prebuilt прогона: `Sin 0.0007548444`, `Cos 0.0007548472`,
`Atan2 2.3841858e-7`, `Asin 4.172325e-7`, `Acos 4.7683716e-7`.

## Evidence gates

| Gate | Результат |
| --- | --- |
| Canonical source/API inventory | PASS: exact reflection surface и duplicate-source scan |
| Golden bits и numerical bounds на .NET/prebuilt | PASS: входит в полный portable suite |
| Reproducible Jitter build | PASS: два clean build byte-identical |
| Package metadata | PASS: complete `.meta`, no LFS pointers |
| Source/profile/binary lock | PASS: 96 files, 1 canonical patch, 3 artifacts |
| Lock negative/invariant tests | PASS: все проверки, включая tampered server DLL |
| Portable/server .NET | PASS: 93/93 |
| Editor, Editor.Tests, Runtime.Tests compile | PASS: 0 warnings, 0 errors во всех трёх проектах |
| P00 audit classifier regression | PASS: 9/9 |
| Unity Editor EditMode/PlayMode | BLOCKED: project открыт в Editor, fresh XML не создан |
| Player/IL2CPP | NOT RUN: отдельный consumer build/run gate не настроен этим эпиком |

Epic acceptance «required API находится в canonical source» закрывается source, reflection и
lock evidence. Cross-runtime часть acceptance может считаться выполненной только для .NET; Unity
Editor и IL2CPP остаются независимыми воротами и не получают PASS по результатам portable suite.

Первый portable test запуск в sandbox был aborted из-за запрета VSTest loopback socket; повтор
того же обязательного script с разрешённым локальным socket прошёл 93/93. Первая Editor compile
попытка зависла из-за двух одновременно запущенных agent build; были остановлены только эти два
PID, после чего последовательный build с `--disable-build-servers -m:1` прошёл за несколько секунд.
