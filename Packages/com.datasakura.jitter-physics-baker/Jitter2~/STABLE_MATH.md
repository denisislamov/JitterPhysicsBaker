# StableMath supported contract

`Jitter2.LinearMath.StableMath` is the canonical deterministic scalar API distributed in the
explicitly installed `Jitter2.Core` assembly. It is not part of the Jitter-free base package and
does not change the separate Setup flow.

The supported distribution profile is `Real = System.Single` (`f32`). The lock file fixes that
profile and the exact source and binary hashes. Public signatures use `Real` in source and appear
as `System.Single` to an assembly consumer. The double-precision source profile is not a supported
distribution target; in particular, its finite positive `Sqrt` path throws `NotSupportedException`.

## Constants

| Member | f32 bits | Contract |
| --- | --- | --- |
| `Pi` | `40490fdb` | Nearest f32 to pi. |
| `HalfPi` | `3fc90fdb` | Nearest f32 to pi/2. |
| `QuarterPi` | `3f490fdb` | Nearest f32 to pi/4. |
| `TwoPi` | `40c90fdb` | Nearest f32 to 2*pi. |

## Methods

All NaN results named below use the canonical quiet-NaN bits `7fc00000`. The error bounds are
absolute error against the corresponding .NET f32 reference function. They are enforced by a
dense deterministic test over the stated supported gameplay domain; golden-bit tests, rather than
the reference functions, define output identity. Bounds outside a stated domain are not claimed.

| Exact source signature | Domain and result | Exceptional inputs and signed zero | Error/determinism contract |
| --- | --- | --- | --- |
| `public static bool IsFinite(Real value)` | Any f32; true except for NaN and either infinity. | `+0` and `-0` are finite. | Exact classification from f32 state. |
| `public static Real Abs(Real value)` | Any f32. | Either zero becomes `+0`; NaN becomes canonical; infinities become `+Infinity`. | Exact bit policy. |
| `public static Real Min(Real first, Real second)` | Any non-NaN f32 pair. | Any NaN becomes canonical; `Min(-0,+0)` is `-0`. | Exact comparison/bit policy. |
| `public static Real Max(Real first, Real second)` | Any non-NaN f32 pair. | Any NaN becomes canonical; `Max(-0,+0)` is `+0`. | Exact comparison/bit policy. |
| `public static Real Clamp(Real value, Real minimum, Real maximum)` | Ordered non-NaN bounds, including infinities. | NaN value becomes canonical. NaN or inverted bounds throw `ArgumentException`. A retained `-0` stays negative. | Exact comparisons; no platform libm. |
| `public static Real Clamp01(Real value)` | Any f32 value. | NaN becomes canonical; retained `-0` stays negative. | `Clamp(value, 0, 1)` contract. |
| `public static Real Lerp(Real from, Real to, Real amount)` | Three finite f32 operands; amount is not clamped. | Any non-finite operand becomes canonical. Signed-zero outcome follows the documented multiply then add sequence. | Exactly one rounded multiply followed by one rounded add; fused multiply-add is excluded. |
| `public static Real Sqrt(Real value)` | Non-negative finite f32 plus `+Infinity`. | `Sqrt(-0)` is `-0`; `+Infinity` is unchanged; negative/NaN input becomes canonical. | Correctly rounded f32 result, produced by integer arithmetic; no platform sqrt. |
| `public static Real RoundAwayFromZero(Real value)` | Any f32. | Signed zero and infinities are unchanged; NaN becomes canonical. | Exact IEEE-bit rounding; halfway cases go away from zero. |
| `public static long RoundToInt64AwayFromZero(Real value)` | Finite values representable by the guarded Int64 conversion. | Non-finite/out-of-range input throws `ArgumentOutOfRangeException`. | Uses the exact `RoundAwayFromZero` policy, then checked conversion. |
| `public static long QuantizeToInt64(Real value, Real scale)` | Finite value and positive finite scale whose rounded product fits Int64. | Invalid value/scale or overflow throws `ArgumentOutOfRangeException`. | One f32 multiplication, then the exact away-from-zero Int64 policy. |
| `public static Real Sin(Real angle)` | Finite radians; accuracy domain `abs(angle) <= 10000`. | `Sin(-0)` is `-0`; non-finite input becomes canonical. | Sampled absolute error <= `0.001`; canonical polynomial and range reduction, no platform sin. |
| `public static Real Cos(Real angle)` | Finite radians; accuracy domain `abs(angle) <= 10000`. | Either zero returns `1`; non-finite input becomes canonical. | Sampled absolute error <= `0.001`; canonical polynomial and range reduction, no platform cos. |
| `public static (Real sin, Real cos) SinCos(Real angle)` | Same finite domain as `Sin`/`Cos`. | Non-finite input returns two canonical NaNs. | Same algorithms and bounds as the single-result methods. |
| `public static Real Atan2(Real y, Real x)` | Finite operands; accuracy test grid is integer pairs in `[-100,100]`. | A non-finite operand becomes canonical; `(0,0)` returns `+0`. | Sampled absolute error <= `0.000001`; canonical polynomial, no platform atan. |
| `public static Real Asin(Real value)` | Finite input is clamped to `[-1,1]`; accuracy domain is that interval. | NaN/infinity becomes canonical; `Asin(-0)` preserves `-0`; finite out-of-domain values return the clamped endpoint. | Sampled absolute error <= `0.000001`; canonical polynomial and canonical `Sqrt`. |
| `public static Real Acos(Real value)` | Finite input is clamped to `[-1,1]`; accuracy domain is that interval. | NaN/infinity becomes canonical; finite out-of-domain values return the clamped endpoint. | Sampled absolute error <= `0.000001`; canonical polynomial and canonical `Sqrt`. |

The accuracy test currently observes maxima below `0.000755` for sine/cosine, below
`0.00000024` for atan2, and below `0.00000048` for asin/acos. The larger public limits above are
intentional stable contract margins, not claims that every mathematically possible f32 input has
been exhaustively compared.

## Ownership and verification

There is one declaration: `Jitter2~/Runtime/LinearMath/StableMath.cs`. Consumers reference the
public type in the installed canonical `Jitter2.Core`; they must not copy or patch it locally.
`jitter2.lock.json` records the file in `canonicalPatches`, because pinned upstream Jitter 2.8.9
does not contain it. The lock verifier checks its exact hash, the source tree hash, compile profile,
and prebuilt assembly hash.

The portable suite freezes the public member inventory, constant and edge-case bits, 100,000
stratified f32 square-root comparisons, numerical bounds, typed failures, and absence of duplicate
declarations. A passing .NET run proves the canonical .NET/prebuilt path only. Unity Editor and
IL2CPP must produce their own fresh evidence before cross-runtime determinism is claimed.
