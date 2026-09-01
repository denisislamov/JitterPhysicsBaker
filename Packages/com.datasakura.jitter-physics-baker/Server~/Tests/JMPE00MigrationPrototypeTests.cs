using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;
using Jitter2;
using Jitter2.LinearMath;
using NUnit.Framework;

namespace DataSakura.JitterPhysics.Server.Tests
{
    /// <summary>
    /// Characterizes the migration boundary before production contracts or the vendored Jitter
    /// snapshot are changed. These tests belong to JMP-E00 evidence; later epics must update the
    /// asserted contract deliberately instead of inheriting an accidental layout or math change.
    /// </summary>
    public sealed class JMPE00MigrationPrototypeTests
    {
        private static readonly Type StableMathType = typeof(Precision).Assembly.GetType(
            "Jitter2.LinearMath.StableMath", throwOnError: true);

        [Test]
        public void ProductionJitterAssemblyIsSinglePrecisionWithRecordedLayout()
        {
            Assert.That(Precision.IsDoublePrecision, Is.False);
            Assert.That(sizeof(float), Is.EqualTo(4));
            Assert.That(Marshal.SizeOf<JVector>(), Is.EqualTo(12));
            Assert.That(Marshal.SizeOf<JQuaternion>(), Is.EqualTo(16));
            Assert.That(Marshal.OffsetOf<JVector>(nameof(JVector.X)).ToInt32(), Is.EqualTo(0));
            Assert.That(Marshal.OffsetOf<JVector>(nameof(JVector.Y)).ToInt32(), Is.EqualTo(4));
            Assert.That(Marshal.OffsetOf<JVector>(nameof(JVector.Z)).ToInt32(), Is.EqualTo(8));
            Assert.That(Marshal.OffsetOf<JQuaternion>(nameof(JQuaternion.X)).ToInt32(), Is.EqualTo(0));
            Assert.That(Marshal.OffsetOf<JQuaternion>(nameof(JQuaternion.Y)).ToInt32(), Is.EqualTo(4));
            Assert.That(Marshal.OffsetOf<JQuaternion>(nameof(JQuaternion.Z)).ToInt32(), Is.EqualTo(8));
            Assert.That(Marshal.OffsetOf<JQuaternion>(nameof(JQuaternion.W)).ToInt32(), Is.EqualTo(12));
        }

        [Test]
        public void ServerLoadsTheExactPrebuiltDllBytesAndTamperingChangesIdentity()
        {
            string loaded = typeof(Precision).Assembly.Location;
            string packageRoot = Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", ".."));
            string prebuilt = Path.Combine(packageRoot, "Jitter2~", "Prebuilt", "Jitter2.Core.dll");

            byte[] expected = File.ReadAllBytes(prebuilt);
            byte[] actual = File.ReadAllBytes(loaded);
            Assert.That(Sha256(actual), Is.EqualTo(Sha256(expected)));

            byte[] tampered = (byte[])expected.Clone();
            tampered[tampered.Length / 2] ^= 0x01;
            Assert.That(Sha256(tampered), Is.Not.EqualTo(Sha256(expected)));
        }

        [Test]
        public void CurrentRuntimeIdentityIsFrozenAndF64IsRejectedBeforeWorldConstruction()
        {
            const string sourceHash =
                "sha256:d67ac0c421687ec7308501bf4b8bcba9c33bed7845a0bfe64d4675b2326cce85";
            const string compileProfileId =
                "9e724df81fb24d55e6136d35174c721457231606bd602464dbc35b017da73643";
            string current = RuntimeCompatibilityId.Compute(
                RuntimeCompatibilityInputs.ForCurrentBuild(sourceHash, compileProfileId));
            Assert.That(
                current,
                Is.EqualTo("ca8283611d3221120e69e23c4c028720537de4867f1de53df3752db85cd32006"));

            string f64 = RuntimeCompatibilityId.Compute(new RuntimeCompatibilityInputs(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                sourceHash,
                "f64",
                compileProfileId,
                JitterPhysicsSemantics.ColliderConversionVersion,
                JitterPhysicsSemantics.ShapeConstructionVersion,
                JitterPhysicsSemantics.WorldBuilderVersion,
                JitterPhysicsSemantics.WorldDefaultsVersion));
            Assert.That(f64, Is.Not.EqualTo(current));

            var artifact = new PhysicsArtifact(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                current,
                "precision-fixture",
                PhysicsWorldSettings.Default,
                Array.Empty<PhysicsBodyRecord>());
            PhysicsArtifactError mismatch = PhysicsArtifactReader.CheckRuntimeCompatibility(artifact, f64);
            Assert.That(mismatch.Code, Is.EqualTo(PhysicsArtifactErrorCode.IncompatibleRuntime));
        }

        [Test]
        public void ProposedF32JitterFieldsHaveTheLegacySchemaOneComponentBytes()
        {
            var legacyVector = new PhysicsVector3(1.25f, -2.5f, 0.125f);
            var jitterVector = new JVector(1.25f, -2.5f, 0.125f);
            Assert.That(
                Scalars(legacyVector.X, legacyVector.Y, legacyVector.Z),
                Is.EqualTo(Scalars(jitterVector.X, jitterVector.Y, jitterVector.Z)));

            var legacyQuaternion = new PhysicsQuaternion(0.25f, -0.5f, 0.75f, -1f);
            var jitterQuaternion = new JQuaternion(0.25f, -0.5f, 0.75f, -1f);
            Assert.That(
                Scalars(legacyQuaternion.X, legacyQuaternion.Y, legacyQuaternion.Z, legacyQuaternion.W),
                Is.EqualTo(Scalars(jitterQuaternion.X, jitterQuaternion.Y, jitterQuaternion.Z, jitterQuaternion.W)));
        }

        [Test]
        public void CurrentSchemaOneGoldenIdentityIsEmittedForFreezing()
        {
            var body = new PhysicsBodyRecord(
                "b",
                PhysicsVector3.Zero,
                PhysicsQuaternion.Identity,
                0.2f,
                0f,
                new List<PhysicsShapeRecord>
                {
                    PhysicsShapeRecord.Box(
                        "s",
                        PhysicsVector3.Zero,
                        PhysicsQuaternion.Identity,
                        new PhysicsVector3(1f, 2f, 3f)),
                });
            var artifact = new PhysicsArtifact(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff",
                "l",
                PhysicsWorldSettings.Default,
                new List<PhysicsBodyRecord> { body });

            PhysicsArtifactPayload payload = PhysicsArtifactWriter.WriteWithManifest(artifact, "0.0.12");
            string manifest = PhysicsArtifactManifestCodec.Write(payload.Manifest);
            TestContext.Progress.WriteLine($"JMP_P03_PAYLOAD length={payload.Bytes.Length} sha256={payload.ArtifactHash}");
            TestContext.Progress.WriteLine($"JMP_P03_MANIFEST {manifest}");

            Assert.That(payload.Manifest.SchemaVersion, Is.EqualTo("1"));
            Assert.That(payload.Manifest.RuntimeCompatibilityId, Is.EqualTo(artifact.RuntimeCompatibilityId));
            Assert.That(payload.Manifest.GeneratorVersion, Is.EqualTo("0.0.12"));
            Assert.That(payload.Bytes.Length, Is.EqualTo(165));
            Assert.That(
                payload.ArtifactHash,
                Is.EqualTo("b53cf221453ce313ae3e2d9ff3e94b665b65a674a0f1f5e9863acb5b33835479"));
            Assert.That(manifest, Is.EqualTo(
                "{\n"
                + "  \"schemaVersion\": \"1\",\n"
                + "  \"runtimeCompatibilityId\": \"00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff\",\n"
                + "  \"generatorVersion\": \"0.0.12\",\n"
                + "  \"levelId\": \"l\",\n"
                + "  \"artifactHash\": \"b53cf221453ce313ae3e2d9ff3e94b665b65a674a0f1f5e9863acb5b33835479\",\n"
                + "  \"bodyCount\": 1,\n"
                + "  \"shapeCount\": 1,\n"
                + "  \"vertexCount\": 0,\n"
                + "  \"triangleCount\": 0,\n"
                + "  \"tickRate\": 30,\n"
                + "  \"fileName\": \"l.physics.bytes\"\n"
                + "}\n"));
        }

        [Test]
        public void E02ReplacesTheInternalSurfaceAndPlatformSqrtDebtExplicitly()
        {
            Assert.That(StableMathType.IsPublic, Is.True);
            string[] actualMethods = StableMathType
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName)
                .Select(method => method.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.That(actualMethods, Is.EqualTo(new[]
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

            string packageRoot = Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", ".."));
            string source = File.ReadAllText(Path.Combine(
                packageRoot, "Jitter2~", "Runtime", "LinearMath", "StableMath.cs"));
            Assert.That(source, Does.Not.Contain("MathR.Sqrt"));
            Assert.That(source, Does.Not.Contain("Math.Sqrt("));
            Assert.That(source, Does.Not.Contain("MathF.Sqrt("));
        }

        [Test]
        public void CurrentStableMathBitPatternsAreEmittedForFreezing()
        {
            var evidence = new SortedDictionary<string, string>(StringComparer.Ordinal);
            AddUnary(evidence, "Sin", 0f);
            AddUnary(evidence, "Sin", -0f);
            AddUnary(evidence, "Sin", float.Epsilon);
            AddUnary(evidence, "Sin", BitConverter.Int32BitsToSingle(unchecked((int)0x3fc90fdb)));
            AddUnary(evidence, "Sin", BitConverter.Int32BitsToSingle(unchecked((int)0x40490fdb)));
            AddUnary(evidence, "Sin", 10000f);
            AddUnary(evidence, "Cos", 0f);
            AddUnary(evidence, "Cos", BitConverter.Int32BitsToSingle(unchecked((int)0x3fc90fdb)));
            AddBinary(evidence, "Atan2", 0f, 0f);
            AddBinary(evidence, "Atan2", 1f, 0f);
            AddBinary(evidence, "Atan2", -1f, -1f);
            AddUnary(evidence, "Acos", -1f);
            AddUnary(evidence, "Acos", 0f);
            AddUnary(evidence, "Acos", 1f);
            AddUnary(evidence, "Acos", float.NaN);
            AddUnary(evidence, "Asin", -1f);
            AddUnary(evidence, "Asin", 0f);
            AddUnary(evidence, "Asin", 1f);
            AddUnary(evidence, "Asin", float.NaN);

            foreach (KeyValuePair<string, string> pair in evidence)
            {
                TestContext.Progress.WriteLine($"JMP_P02_BITS {pair.Key}={pair.Value}");
            }

            var expected = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["Acos(00000000)"] = "3fc90fdb",
                ["Acos(3f800000)"] = "00000000",
                ["Acos(bf800000)"] = "40490fdb",
                ["Acos(ffc00000)"] = "7fc00000",
                ["Asin(00000000)"] = "00000000",
                ["Asin(3f800000)"] = "3fc90fdb",
                ["Asin(bf800000)"] = "bfc90fdb",
                ["Asin(ffc00000)"] = "7fc00000",
                ["Atan2(00000000,00000000)"] = "00000000",
                ["Atan2(3f800000,00000000)"] = "3fc90fdb",
                ["Atan2(bf800000,bf800000)"] = "c016cbe4",
                ["Cos(00000000)"] = "3f800000",
                ["Cos(3fc90fdb)"] = "80000000",
                ["Sin(00000000)"] = "00000000",
                ["Sin(00000001)"] = "00000001",
                ["Sin(3fc90fdb)"] = "3f800000",
                ["Sin(40490fdb)"] = "80000000",
                ["Sin(461c4000)"] = "be9c73cd",
                ["Sin(80000000)"] = "80000000",
            };

            Assert.That(evidence.ToArray(), Is.EqualTo(expected.ToArray()));
        }

        private static void AddUnary(IDictionary<string, string> evidence, string method, float value)
        {
            MethodInfo target = StableMathType.GetMethod(
                method,
                BindingFlags.Static | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(float) },
                modifiers: null);
            Assert.That(target, Is.Not.Null, method);
            float result = (float)target.Invoke(null, new object[] { value });
            evidence[$"{method}({Bits(value)})"] = Bits(result);
        }

        private static void AddBinary(
            IDictionary<string, string> evidence,
            string method,
            float first,
            float second)
        {
            MethodInfo target = StableMathType.GetMethod(
                method,
                BindingFlags.Static | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(float), typeof(float) },
                modifiers: null);
            Assert.That(target, Is.Not.Null, method);
            float result = (float)target.Invoke(null, new object[] { first, second });
            evidence[$"{method}({Bits(first)},{Bits(second)})"] = Bits(result);
        }

        private static byte[] Scalars(params float[] values)
        {
            var result = new byte[values.Length * sizeof(float)];
            for (int index = 0; index < values.Length; index++)
            {
                byte[] scalar = BitConverter.GetBytes(values[index]);
                Buffer.BlockCopy(scalar, 0, result, index * sizeof(float), scalar.Length);
            }

            return result;
        }

        private static string Bits(float value)
        {
            return unchecked((uint)BitConverter.SingleToInt32Bits(value)).ToString("x8");
        }

        private static string Sha256(byte[] value)
        {
            using (SHA256 hash = SHA256.Create())
            {
                return Convert.ToHexString(hash.ComputeHash(value)).ToLowerInvariant();
            }
        }
    }
}
