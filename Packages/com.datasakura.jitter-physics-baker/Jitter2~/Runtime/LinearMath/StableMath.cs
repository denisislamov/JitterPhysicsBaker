/*
 * Jitter2 Physics Library
 * (c) Thorben Linneweber and contributors
 * SPDX-License-Identifier: MIT
 */

using System;
using System.Runtime.CompilerServices;

namespace Jitter2.LinearMath;

/// <summary>
/// Deterministic scalar helpers owned by the canonical Jitter runtime.
/// </summary>
/// <remarks>
/// Until scalar operations in the BCL are both deterministic and fixed by managed source across
/// targets, this type keeps the supported simulation-critical behavior in one implementation.
/// The distributed profile is single precision; double-precision compatibility is not claimed.
/// </remarks>
public static class StableMath
{
    /// <summary>The nearest representable <c>Real</c> value to pi.</summary>
    public const Real Pi = (Real)3.141592653589793238462643383279502884;

    /// <summary>The nearest representable <c>Real</c> value to pi divided by two.</summary>
    public const Real HalfPi = (Real)1.570796326794896619231321691639751442;

    /// <summary>The nearest representable <c>Real</c> value to pi divided by four.</summary>
    public const Real QuarterPi = (Real)0.785398163397448309615660845819875721;

    /// <summary>The nearest representable <c>Real</c> value to two times pi.</summary>
    public const Real TwoPi = (Real)6.283185307179586476925286766559005768;

    // Used by atan's angle-addition identity: atan(x) = pi/4 + atan((x - 1) / (x + 1)).
    private const Real TanPiOver8 = (Real)0.414213562373095048801688724209698079;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FloorToInt(Real value)
    {
        // Casting truncates toward zero. The reducers need mathematical floor so that negative
        // angles land in the same buckets on every platform and quadrant boundaries stay symmetric.
        int integer = (int)value;
        return value < integer ? integer - 1 : integer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Real ReduceAngle(Real angle)
    {
        // This is intentionally the "naive" reducer: one modulo by 2*pi into [-pi, pi] in the
        // current Real precision. It is not a Payne-Hanek/Cody-Waite style high-precision reducer.
        // That means enormous inputs can lose low bits before the polynomial ever runs, but for the
        // angle magnitudes seen in the solver this is a good tradeoff: tiny code, deterministic data
        // flow, and no dependency on platform libm internals.
        int periods = FloorToInt((angle + Pi) / TwoPi);
        angle -= periods * TwoPi;

        if (angle > Pi) angle -= TwoPi;
        else if (angle <= -Pi) angle += TwoPi;

        return angle;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ReduceToQuadrant(Real angle, out int quadrant, out Real reduced)
    {
        angle = ReduceAngle(angle);

        // Fold once more into [-pi/4, pi/4]. The returned quadrant stores the swaps/sign flips needed
        // to reconstruct the original sine/cosine pair after the low-order polynomial is evaluated.
        int nearestQuarterTurn = FloorToInt((angle + QuarterPi) / HalfPi);

        reduced = angle - nearestQuarterTurn * HalfPi;
        quadrant = nearestQuarterTurn & 3;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Real SinPolynomial(Real x)
    {
        // 13th-order Maclaurin approximation of sin(x), evaluated in Horner form.
        //
        // The reduction above is what makes this usable. In exact arithmetic the truncation remainder
        // on [-pi/4, pi/4] is bounded by |x|^15 / 15!, which is about 2.04e-14 at the endpoint.
        // If we skip reduction and run the same polynomial directly on [-pi, pi], a dense float scan
        // lands around 2.14e-5 max absolute error. The quadrant fold is therefore not optional detail;
        // it is the reason this low-order Taylor polynomial works for the engine.
        Real x2 = x * x;
        Real poly = -(Real)(1.0 / 6227020800.0);
        poly = poly * x2 + (Real)(1.0 / 39916800.0);
        poly = poly * x2 - (Real)(1.0 / 362880.0);
        poly = poly * x2 + (Real)(1.0 / 5040.0);
        poly = poly * x2 - (Real)(1.0 / 120.0);
        poly = poly * x2 + (Real)(1.0 / 6.0);
        poly = poly * x2 - (Real)1.0;
        return -x * poly;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Real CosPolynomial(Real x)
    {
        // 12th-order Maclaurin approximation of cos(x), also in Horner form.
        //
        // The exact-series truncation remainder on [-pi/4, pi/4] is bounded by |x|^14 / 14!, about
        // 3.90e-13 at the endpoint. Without reduction the same coefficients are much less acceptable:
        // a dense float scan over [-pi, pi] lands around 1.01e-4 max absolute error.
        Real x2 = x * x;
        Real poly = -(Real)(1.0 / 479001600.0);
        poly = poly * x2 + (Real)(1.0 / 3628800.0);
        poly = poly * x2 - (Real)(1.0 / 40320.0);
        poly = poly * x2 + (Real)(1.0 / 720.0);
        poly = poly * x2 - (Real)(1.0 / 24.0);
        poly = poly * x2 + (Real)(1.0 / 2.0);
        poly = poly * x2 - (Real)1.0;
        return -poly;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (Real sin, Real cos) ApplyQuadrant(int quadrant, Real sin, Real cos)
    {
        // Undo the octant/quadrant fold from ReduceToQuadrant.
        return quadrant switch
        {
            0 => (sin, cos),
            1 => (cos, -sin),
            2 => (-sin, -cos),
            _ => (-cos, sin)
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Real ApplyQuadrantSin(int quadrant, Real sin, Real cos)
    {
        return quadrant switch
        {
            0 => sin,
            1 => cos,
            2 => -sin,
            _ => -cos
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Real ApplyQuadrantCos(int quadrant, Real sin, Real cos)
    {
        return quadrant switch
        {
            0 => cos,
            1 => -sin,
            2 => -cos,
            _ => sin
        };
    }

    /// <summary>
    /// Returns deterministic sine and cosine approximations for a finite angle in radians.
    /// Non-finite inputs return two canonical quiet NaNs.
    /// </summary>
    public static (Real sin, Real cos) SinCos(Real angle)
    {
        if (!IsFinite(angle)) return (CanonicalNaN(), CanonicalNaN());

        if (angle >= -QuarterPi && angle <= QuarterPi)
        {
            return (SinPolynomial(angle), CosPolynomial(angle));
        }

        // Everything outside the minimal polynomial interval goes through the normal range reducer.
        ReduceToQuadrant(angle, out int quadrant, out Real reduced);

        Real sin = SinPolynomial(reduced);
        Real cos = CosPolynomial(reduced);

        return ApplyQuadrant(quadrant, sin, cos);
    }

    /// <summary>
    /// Returns a deterministic sine approximation for a finite angle in radians.
    /// A non-finite input returns the canonical quiet NaN.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Real Sin(Real angle)
    {
        if (!IsFinite(angle)) return CanonicalNaN();

        if (angle >= -QuarterPi && angle <= QuarterPi)
        {
            return SinPolynomial(angle);
        }

        ReduceToQuadrant(angle, out int quadrant, out Real reduced);

        // The single-output paths keep the same reduction logic but avoid computing the polynomial
        // that is not needed for the selected quadrant.
        return quadrant switch
        {
            0 => SinPolynomial(reduced),
            1 => CosPolynomial(reduced),
            2 => -SinPolynomial(reduced),
            _ => -CosPolynomial(reduced)
        };
    }

    /// <summary>
    /// Returns a deterministic cosine approximation for a finite angle in radians.
    /// A non-finite input returns the canonical quiet NaN.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Real Cos(Real angle)
    {
        if (!IsFinite(angle)) return CanonicalNaN();

        if (angle >= -QuarterPi && angle <= QuarterPi)
        {
            return CosPolynomial(angle);
        }

        ReduceToQuadrant(angle, out int quadrant, out Real reduced);

        return quadrant switch
        {
            0 => CosPolynomial(reduced),
            1 => -SinPolynomial(reduced),
            2 => -CosPolynomial(reduced),
            _ => SinPolynomial(reduced)
        };
    }

    private static Real AtanTaylor(Real value)
    {
        // 17th-order odd Taylor series for atan(x) in Horner form. The caller keeps |x| small
        // enough that the notoriously slow convergence near x = 1 does not dominate the error.
        Real x2 = value * value;
        Real poly = (Real)(1.0 / 17.0);
        poly = poly * x2 - (Real)(1.0 / 15.0);
        poly = poly * x2 + (Real)(1.0 / 13.0);
        poly = poly * x2 - (Real)(1.0 / 11.0);
        poly = poly * x2 + (Real)(1.0 / 9.0);
        poly = poly * x2 - (Real)(1.0 / 7.0);
        poly = poly * x2 + (Real)(1.0 / 5.0);
        poly = poly * x2 - (Real)(1.0 / 3.0);
        poly = poly * x2 + (Real)1.0;
        return value * poly;
    }

    private static Real Atan(Real value)
    {
        if (value < (Real)0.0) return -Atan(-value);

        if (value > (Real)1.0)
        {
            // atan(x) = pi/2 - atan(1/x)
            return HalfPi - Atan((Real)1.0 / value);
        }

        if (value > TanPiOver8)
        {
            // atan(x) = pi/4 + atan((x - 1) / (x + 1)). After this transform the Taylor series only
            // sees values up to tan(pi/8) ~= 0.4142 instead of fighting the slow x ~= 1 case directly.
            Real reduced = (value - (Real)1.0) / (value + (Real)1.0);
            return QuarterPi + AtanTaylor(reduced);
        }

        return AtanTaylor(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Real AsinTaylor(Real value)
    {
        // 15th-order Maclaurin approximation of asin(x). We only feed it |x| <= 0.5 directly; near
        // the endpoints Asin/Acos switch to a half-angle form so the polynomial still sees a small x.
        Real x2 = value * value;
        Real poly = (Real)(143.0 / 10240.0);
        poly = poly * x2 + (Real)(231.0 / 13312.0);
        poly = poly * x2 + (Real)(63.0 / 2816.0);
        poly = poly * x2 + (Real)(35.0 / 1152.0);
        poly = poly * x2 + (Real)(5.0 / 112.0);
        poly = poly * x2 + (Real)(3.0 / 40.0);
        poly = poly * x2 + (Real)(1.0 / 6.0);
        poly = poly * x2 + (Real)1.0;
        return value * poly;
    }

    /// <summary>
    /// Returns the deterministic four-quadrant arctangent approximation for finite inputs.
    /// A non-finite operand returns the canonical quiet NaN; two zero operands return positive zero.
    /// </summary>
    public static Real Atan2(Real y, Real x)
    {
        if (!IsFinite(y) || !IsFinite(x)) return CanonicalNaN();

        // Classic quadrant reconstruction around the scalar atan approximation above.
        if (x > (Real)0.0)
        {
            return Atan(y / x);
        }

        if (x < (Real)0.0)
        {
            return y >= (Real)0.0
                ? Atan(y / x) + Pi
                : Atan(y / x) - Pi;
        }

        if (y > (Real)0.0) return HalfPi;
        if (y < (Real)0.0) return -HalfPi;

        return (Real)0.0;
    }

    /// <summary>
    /// Returns the deterministic arccosine approximation after clamping finite inputs to
    /// <c>[-1, 1]</c>. A non-finite input returns the canonical quiet NaN.
    /// </summary>
    public static Real Acos(Real value)
    {
        if (!IsFinite(value)) return CanonicalNaN();
        value = Clamp(value, (Real)(-1.0), (Real)1.0);

        if (value > (Real)0.5)
        {
            // acos(x) = 2 * asin(sqrt((1 - x) / 2))
            Real reduced = Sqrt(Max((Real)0.0, ((Real)1.0 - value) * (Real)0.5));
            return (Real)2.0 * AsinTaylor(reduced);
        }

        if (value < (Real)(-0.5))
        {
            // acos(x) = pi - 2 * asin(sqrt((1 + x) / 2))
            Real reduced = Sqrt(Max((Real)0.0, ((Real)1.0 + value) * (Real)0.5));
            return Pi - (Real)2.0 * AsinTaylor(reduced);
        }

        return HalfPi - AsinTaylor(value);
    }

    /// <summary>
    /// Returns the deterministic arcsine approximation after clamping finite inputs to
    /// <c>[-1, 1]</c>. A non-finite input returns the canonical quiet NaN.
    /// </summary>
    public static Real Asin(Real value)
    {
        if (!IsFinite(value)) return CanonicalNaN();
        value = Clamp(value, (Real)(-1.0), (Real)1.0);
        Real absValue = Abs(value);

        if (absValue <= (Real)0.5)
        {
            return AsinTaylor(value);
        }

        // asin(x) = pi/2 - 2 * asin(sqrt((1 - |x|) / 2))
        Real reduced = Sqrt(Max((Real)0.0, ((Real)1.0 - absValue) * (Real)0.5));
        Real angle = HalfPi - (Real)2.0 * AsinTaylor(reduced);

        return value < (Real)0.0 ? -angle : angle;
    }

    /// <summary>Returns whether a value is neither NaN nor infinity.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFinite(Real value)
    {
        return !Real.IsNaN(value) && !Real.IsInfinity(value);
    }

    /// <summary>
    /// Returns the absolute value. Either signed zero becomes positive zero and any NaN becomes
    /// the canonical quiet NaN.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Real Abs(Real value)
    {
        if (Real.IsNaN(value)) return CanonicalNaN();
        if (value == (Real)0.0) return (Real)0.0;
        return value < (Real)0.0 ? -value : value;
    }

    /// <summary>
    /// Returns the smaller operand. NaN produces the canonical quiet NaN; when both operands are
    /// zero, negative zero wins.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Real Min(Real first, Real second)
    {
        if (Real.IsNaN(first) || Real.IsNaN(second)) return CanonicalNaN();
        if (first < second) return first;
        if (second < first) return second;
        if (first == (Real)0.0 && (IsNegative(first) || IsNegative(second))) return -((Real)0.0);
        return first;
    }

    /// <summary>
    /// Returns the larger operand. NaN produces the canonical quiet NaN; when both operands are
    /// zero, positive zero wins.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Real Max(Real first, Real second)
    {
        if (Real.IsNaN(first) || Real.IsNaN(second)) return CanonicalNaN();
        if (first > second) return first;
        if (second > first) return second;
        if (first == (Real)0.0 && (!IsNegative(first) || !IsNegative(second))) return (Real)0.0;
        return first;
    }

    /// <summary>Clamps a value to the inclusive ordered bounds.</summary>
    /// <exception cref="ArgumentException">
    /// A bound is NaN or <paramref name="minimum"/> exceeds <paramref name="maximum"/>.
    /// </exception>
    public static Real Clamp(Real value, Real minimum, Real maximum)
    {
        if (Real.IsNaN(minimum) || Real.IsNaN(maximum) || minimum > maximum)
        {
            throw new ArgumentException("Clamp bounds must be ordered non-NaN numbers.");
        }

        if (Real.IsNaN(value)) return CanonicalNaN();
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    /// <summary>Clamps a value to the inclusive range from zero to one.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Real Clamp01(Real value)
    {
        return Clamp(value, (Real)0.0, (Real)1.0);
    }

    /// <summary>
    /// Linearly interpolates finite values without clamping <paramref name="amount"/>. A
    /// non-finite operand returns the canonical quiet NaN.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Real Lerp(Real from, Real to, Real amount)
    {
        if (!IsFinite(from) || !IsFinite(to) || !IsFinite(amount)) return CanonicalNaN();
        Real scaled = MultiplyWithoutFusedAdd(to - from, amount);
        return from + scaled;
    }

    /// <summary>
    /// Returns the correctly rounded square root in the canonical f32 profile. Positive infinity
    /// and either signed zero are returned unchanged; negative and NaN inputs return the canonical
    /// quiet NaN.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// A finite positive value is evaluated under the unsupported double-precision profile.
    /// </exception>
    public static Real Sqrt(Real value)
    {
        if (Real.IsNaN(value) || value < (Real)0.0) return CanonicalNaN();
        if (value == (Real)0.0 || Real.IsPositiveInfinity(value)) return value;

#if USE_DOUBLE_PRECISION
        throw new NotSupportedException("StableMath.Sqrt is supported only by the canonical f32 profile.");
#else
        uint bits = unchecked((uint)BitConverter.SingleToInt32Bits(value));
        int biasedExponent = (int)((bits >> 23) & 0xffu);
        uint significand = bits & 0x007fffffu;
        int exponent;

        if (biasedExponent == 0)
        {
            exponent = -126;
            while ((significand & 0x00800000u) == 0)
            {
                significand <<= 1;
                exponent--;
            }
        }
        else
        {
            significand |= 0x00800000u;
            exponent = biasedExponent - 127;
        }

        if ((exponent & 1) != 0)
        {
            significand <<= 1;
            exponent--;
        }

        ulong radicand = (ulong)significand << 23;
        ulong root = IntegerSquareRoot(radicand);
        ulong remainder = radicand - root * root;
        if (remainder > root) root++;

        int resultExponent = exponent / 2;
        if (root == 0x01000000u)
        {
            root >>= 1;
            resultExponent++;
        }

        uint resultBits = (uint)(resultExponent + 127) << 23;
        resultBits |= (uint)root & 0x007fffffu;
        return BitConverter.Int32BitsToSingle(unchecked((int)resultBits));
#endif
    }

    /// <summary>
    /// Rounds a finite value to an integral <c>Real</c>, resolving exact half-way cases away from
    /// zero. Signed zero is preserved; infinities are returned unchanged; NaN becomes canonical.
    /// </summary>
    public static Real RoundAwayFromZero(Real value)
    {
        if (Real.IsNaN(value)) return CanonicalNaN();
        if (Real.IsInfinity(value) || value == (Real)0.0) return value;

#if USE_DOUBLE_PRECISION
        ulong bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
        ulong sign = bits & 0x8000000000000000UL;
        ulong magnitude = bits & 0x7fffffffffffffffUL;
        int biasedExponent = (int)(magnitude >> 52);
        if (biasedExponent < 1022) return BitConverter.Int64BitsToDouble(unchecked((long)sign));
        if (biasedExponent == 1022)
        {
            return BitConverter.Int64BitsToDouble(unchecked((long)(sign | 0x3ff0000000000000UL)));
        }

        if (biasedExponent >= 1075) return value;
        int fractionalBits = 52 - (biasedExponent - 1023);
        ulong fractionalMask = (1UL << fractionalBits) - 1UL;
        ulong roundedMagnitude = magnitude & ~fractionalMask;
        if ((magnitude & fractionalMask) >= (1UL << (fractionalBits - 1)))
        {
            roundedMagnitude += 1UL << fractionalBits;
        }

        return BitConverter.Int64BitsToDouble(unchecked((long)(sign | roundedMagnitude)));
#else
        uint bits = unchecked((uint)BitConverter.SingleToInt32Bits(value));
        uint sign = bits & 0x80000000u;
        uint magnitude = bits & 0x7fffffffu;
        int biasedExponent = (int)(magnitude >> 23);
        if (biasedExponent < 126) return BitConverter.Int32BitsToSingle(unchecked((int)sign));
        if (biasedExponent == 126)
        {
            return BitConverter.Int32BitsToSingle(unchecked((int)(sign | 0x3f800000u)));
        }

        if (biasedExponent >= 150) return value;
        int fractionalBits = 23 - (biasedExponent - 127);
        uint fractionalMask = (1u << fractionalBits) - 1u;
        uint roundedMagnitude = magnitude & ~fractionalMask;
        if ((magnitude & fractionalMask) >= (1u << (fractionalBits - 1)))
        {
            roundedMagnitude += 1u << fractionalBits;
        }

        return BitConverter.Int32BitsToSingle(unchecked((int)(sign | roundedMagnitude)));
#endif
    }

    /// <summary>Rounds a finite Int64-range value with half-way cases away from zero.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value is non-finite or outside the supported Int64 range.
    /// </exception>
    public static long RoundToInt64AwayFromZero(Real value)
    {
        if (!IsFinite(value) || value >= (Real)long.MaxValue || value < (Real)long.MinValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value, "A finite Int64-range value is required.");
        }

        return checked((long)RoundAwayFromZero(value));
    }

    /// <summary>
    /// Multiplies by a positive finite scale and rounds to Int64 with half-way cases away from
    /// zero. This is the canonical deterministic quantization primitive.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The value or scale is invalid, or the scaled result is outside the Int64 range.
    /// </exception>
    public static long QuantizeToInt64(Real value, Real scale)
    {
        if (!IsFinite(value) || !IsFinite(scale) || scale <= (Real)0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scale), scale, "A finite value and positive finite scale are required.");
        }

        return RoundToInt64AwayFromZero(value * scale);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsNegative(Real value)
    {
#if USE_DOUBLE_PRECISION
        return BitConverter.DoubleToInt64Bits(value) < 0;
#else
        return BitConverter.SingleToInt32Bits(value) < 0;
#endif
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Real MultiplyWithoutFusedAdd(Real first, Real second)
    {
        return first * second;
    }

    private static ulong IntegerSquareRoot(ulong value)
    {
        ulong remainder = value;
        ulong root = 0;
        ulong bit = 1UL << 62;
        while (bit > remainder) bit >>= 2;

        while (bit != 0)
        {
            if (remainder >= root + bit)
            {
                remainder -= root + bit;
                root = (root >> 1) + bit;
            }
            else
            {
                root >>= 1;
            }

            bit >>= 2;
        }

        return root;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Real CanonicalNaN()
    {
#if USE_DOUBLE_PRECISION
        return BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000000UL));
#else
        return BitConverter.Int32BitsToSingle(unchecked((int)0x7fc00000u));
#endif
    }
}
