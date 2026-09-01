using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jitter2.LinearMath;
using NUnit.Framework;

namespace DataSakura.JitterPhysics.Server.Tests
{
    /// <summary>The public and bit-level contract of the canonical f32 StableMath API.</summary>
    public sealed class StableMathContractTests
    {
        [Test]
        public void PublicSurfaceIsExactAndCallableFromAnExternalAssembly()
        {
            Type type = typeof(StableMath);
            Assert.That(type.IsPublic, Is.True);

            string[] methods = type
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .Select(method => method.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.That(methods, Is.EqualTo(new[]
            {
                "Abs",
                "Acos",
                "Asin",
                "Atan2",
                "Clamp",
                "Clamp01",
                "Cos",
                "IsFinite",
                "Lerp",
                "Max",
                "Min",
                "QuantizeToInt64",
                "RoundAwayFromZero",
                "RoundToInt64AwayFromZero",
                "Sin",
                "SinCos",
                "Sqrt",
            }));

            string[] constants = type
                .GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(field => field.IsLiteral)
                .Select(field => field.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.That(constants, Is.EqualTo(new[] { "HalfPi", "Pi", "QuarterPi", "TwoPi" }));
            Assert.That(Bits(StableMath.Pi), Is.EqualTo("40490fdb"));
            Assert.That(Bits(StableMath.HalfPi), Is.EqualTo("3fc90fdb"));
            Assert.That(Bits(StableMath.QuarterPi), Is.EqualTo("3f490fdb"));
            Assert.That(Bits(StableMath.TwoPi), Is.EqualTo("40c90fdb"));
        }

        [Test]
        public void SignedZeroAndHalfwayPolicyIsBitDefined()
        {
            AssertBits(StableMath.Sin(-0f), "80000000");
            AssertBits(StableMath.Abs(-0f), "00000000");
            AssertBits(StableMath.Min(-0f, 0f), "80000000");
            AssertBits(StableMath.Max(-0f, 0f), "00000000");
            AssertBits(StableMath.Clamp01(-0f), "80000000");
            AssertBits(StableMath.Sqrt(-0f), "80000000");
            AssertBits(StableMath.RoundAwayFromZero(-0f), "80000000");

            Assert.That(StableMath.RoundAwayFromZero(0.5f), Is.EqualTo(1f));
            Assert.That(StableMath.RoundAwayFromZero(-0.5f), Is.EqualTo(-1f));
            Assert.That(StableMath.RoundAwayFromZero(1.5f), Is.EqualTo(2f));
            Assert.That(StableMath.RoundAwayFromZero(-1.5f), Is.EqualTo(-2f));
            Assert.That(StableMath.RoundAwayFromZero(2.5f), Is.EqualTo(3f));
            Assert.That(StableMath.RoundAwayFromZero(-2.5f), Is.EqualTo(-3f));
            Assert.That(StableMath.RoundAwayFromZero(FromBits(0x3effffff)), Is.EqualTo(0f));
            Assert.That(StableMath.RoundAwayFromZero(FromBits(0x3f000001)), Is.EqualTo(1f));
            Assert.That(StableMath.QuantizeToInt64(-0.25f, 2f), Is.EqualTo(-1));
            Assert.That(StableMath.QuantizeToInt64(0.25f, 2f), Is.EqualTo(1));
        }

        [Test]
        public void SqrtIsCorrectlyRoundedForBoundariesAndStratifiedF32Inputs()
        {
            AssertBits(StableMath.Sqrt(FromBits(0x00000001)), "1a3504f3");
            AssertBits(StableMath.Sqrt(FromBits(0x007fffff)), "1fffffff");
            AssertBits(StableMath.Sqrt(FromBits(0x00800000)), "20000000");
            AssertBits(StableMath.Sqrt(0.5f), "3f3504f3");
            AssertBits(StableMath.Sqrt(1f), "3f800000");
            AssertBits(StableMath.Sqrt(2f), "3fb504f3");
            AssertBits(StableMath.Sqrt(float.MaxValue), "5f7fffff");

            uint state = 0x6d2b79f5u;
            for (int index = 0; index < 100_000; index++)
            {
                state = unchecked(state * 1664525u + 1013904223u);
                uint inputBits = state & 0x7fffffffu;
                if (inputBits >= 0x7f800000u) inputBits &= 0x7f7fffffu;

                float input = FromBits(inputBits);
                string actual = Bits(StableMath.Sqrt(input));
                string expected = Bits(MathF.Sqrt(input));
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    Assert.Fail(
                        $"sqrt input 0x{inputBits:x8}: expected 0x{expected}, actual 0x{actual}");
                }
            }
        }

        [Test]
        public void TrigonometryKeepsFrozenQuadrantSubnormalAndGameplayBits()
        {
            AssertBits(StableMath.Sin(0f), "00000000");
            AssertBits(StableMath.Sin(float.Epsilon), "00000001");
            AssertBits(StableMath.Sin(StableMath.HalfPi), "3f800000");
            AssertBits(StableMath.Sin(StableMath.Pi), "80000000");
            AssertBits(StableMath.Sin(10000f), "be9c73cd");
            AssertBits(StableMath.Cos(0f), "3f800000");
            AssertBits(StableMath.Cos(StableMath.HalfPi), "80000000");
            AssertBits(StableMath.Atan2(0f, 0f), "00000000");
            AssertBits(StableMath.Atan2(1f, 0f), "3fc90fdb");
            AssertBits(StableMath.Atan2(-1f, -1f), "c016cbe4");
            AssertBits(StableMath.Asin(-1f), "bfc90fdb");
            AssertBits(StableMath.Asin(0f), "00000000");
            AssertBits(StableMath.Asin(1f), "3fc90fdb");
            AssertBits(StableMath.Acos(-1f), "40490fdb");
            AssertBits(StableMath.Acos(0f), "3fc90fdb");
            AssertBits(StableMath.Acos(1f), "00000000");

            (float sin, float cos) = StableMath.SinCos(StableMath.QuarterPi);
            Assert.That(sin, Is.GreaterThan(0f));
            Assert.That(cos, Is.GreaterThan(0f));
            Assert.That(StableMath.Lerp(1000f, 2000f, 0.25f), Is.EqualTo(1250f));
            Assert.That(StableMath.QuantizeToInt64(-123.456f, 1000f), Is.EqualTo(-123456));
        }

        [Test]
        public void ApproximationErrorStaysWithinTheDocumentedF32Bounds()
        {
            float maximumSinError = 0f;
            float maximumCosError = 0f;
            for (int index = -100_000; index <= 100_000; index++)
            {
                float angle = index * 0.1f;
                maximumSinError = MathF.Max(
                    maximumSinError, MathF.Abs(StableMath.Sin(angle) - MathF.Sin(angle)));
                maximumCosError = MathF.Max(
                    maximumCosError, MathF.Abs(StableMath.Cos(angle) - MathF.Cos(angle)));
            }

            float maximumAtan2Error = 0f;
            for (int y = -100; y <= 100; y++)
            {
                for (int x = -100; x <= 100; x++)
                {
                    if (x == 0 && y == 0) continue;
                    maximumAtan2Error = MathF.Max(
                        maximumAtan2Error,
                        MathF.Abs(StableMath.Atan2(y, x) - MathF.Atan2(y, x)));
                }
            }

            float maximumAsinError = 0f;
            float maximumAcosError = 0f;
            for (int index = -100_000; index <= 100_000; index++)
            {
                float value = index / 100_000f;
                maximumAsinError = MathF.Max(
                    maximumAsinError, MathF.Abs(StableMath.Asin(value) - MathF.Asin(value)));
                maximumAcosError = MathF.Max(
                    maximumAcosError, MathF.Abs(StableMath.Acos(value) - MathF.Acos(value)));
            }

            TestContext.Progress.WriteLine(
                $"max errors: sin={maximumSinError:R}, cos={maximumCosError:R}, " +
                $"atan2={maximumAtan2Error:R}, asin={maximumAsinError:R}, acos={maximumAcosError:R}");
            Assert.That(maximumSinError, Is.LessThanOrEqualTo(0.001f));
            Assert.That(maximumCosError, Is.LessThanOrEqualTo(0.001f));
            Assert.That(maximumAtan2Error, Is.LessThanOrEqualTo(0.001f));
            Assert.That(maximumAsinError, Is.LessThanOrEqualTo(0.001f));
            Assert.That(maximumAcosError, Is.LessThanOrEqualTo(0.001f));
        }

        [Test]
        public void ExceptionalInputsHaveCanonicalResultsOrTypedFailures()
        {
            Assert.That(StableMath.IsFinite(0f), Is.True);
            Assert.That(StableMath.IsFinite(float.NaN), Is.False);
            Assert.That(StableMath.IsFinite(float.PositiveInfinity), Is.False);

            AssertCanonicalNaN(StableMath.Sin(float.NaN));
            AssertCanonicalNaN(StableMath.Cos(float.PositiveInfinity));
            AssertCanonicalNaN(StableMath.Atan2(1f, float.NegativeInfinity));
            AssertCanonicalNaN(StableMath.Asin(float.NaN));
            AssertCanonicalNaN(StableMath.Acos(float.PositiveInfinity));
            AssertCanonicalNaN(StableMath.Abs(float.NaN));
            AssertCanonicalNaN(StableMath.Min(float.NaN, 1f));
            AssertCanonicalNaN(StableMath.Max(1f, float.NaN));
            AssertCanonicalNaN(StableMath.Clamp(float.NaN, 0f, 1f));
            AssertCanonicalNaN(StableMath.Lerp(0f, float.PositiveInfinity, 0.5f));
            AssertCanonicalNaN(StableMath.Sqrt(-1f));
            AssertCanonicalNaN(StableMath.Sqrt(float.NegativeInfinity));
            AssertBits(StableMath.Sqrt(float.PositiveInfinity), "7f800000");
            AssertBits(StableMath.RoundAwayFromZero(float.PositiveInfinity), "7f800000");

            Assert.That(StableMath.Asin(2f), Is.EqualTo(StableMath.HalfPi));
            Assert.That(StableMath.Asin(-2f), Is.EqualTo(-StableMath.HalfPi));
            Assert.That(StableMath.Acos(2f), Is.EqualTo(0f));
            Assert.That(StableMath.Acos(-2f), Is.EqualTo(StableMath.Pi));

            Assert.Throws<ArgumentException>(() => StableMath.Clamp(0f, 2f, 1f));
            Assert.Throws<ArgumentException>(() => StableMath.Clamp(0f, float.NaN, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StableMath.RoundToInt64AwayFromZero(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StableMath.QuantizeToInt64(1f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => StableMath.QuantizeToInt64(float.PositiveInfinity, 1f));
        }

        [Test]
        public void NoConsumerLocalStableMathDeclarationExists()
        {
            string packageRoot = Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", ".."));
            string canonical = Path.GetFullPath(Path.Combine(
                packageRoot, "Jitter2~", "Runtime", "LinearMath", "StableMath.cs"));
            var declaration = new Regex(@"\b(?:class|struct)\s+StableMath\b", RegexOptions.CultureInvariant);

            string[] duplicates = Directory
                .GetFiles(packageRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !string.Equals(Path.GetFullPath(path), canonical, StringComparison.Ordinal))
                .Where(path => declaration.IsMatch(File.ReadAllText(path)))
                .Select(path => path.Substring(packageRoot.Length).TrimStart(Path.DirectorySeparatorChar))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            Assert.That(duplicates, Is.Empty, "Consumers must reference canonical Jitter2.LinearMath.StableMath.");
        }

        private static float FromBits(uint bits)
        {
            return BitConverter.Int32BitsToSingle(unchecked((int)bits));
        }

        private static string Bits(float value)
        {
            return unchecked((uint)BitConverter.SingleToInt32Bits(value)).ToString("x8");
        }

        private static void AssertBits(float value, string expected)
        {
            Assert.That(Bits(value), Is.EqualTo(expected));
        }

        private static void AssertCanonicalNaN(float value)
        {
            AssertBits(value, "7fc00000");
        }
    }
}
