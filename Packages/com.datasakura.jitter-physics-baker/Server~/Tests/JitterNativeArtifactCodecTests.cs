using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;
using Jitter2.LinearMath;
using NUnit.Framework;
using LegacyArtifact = DataSakura.JitterPhysics.Contracts.PhysicsArtifact;
using LegacyBody = DataSakura.JitterPhysics.Contracts.PhysicsBodyRecord;
using LegacyQuaternion = DataSakura.JitterPhysics.Contracts.PhysicsQuaternion;
using LegacySettings = DataSakura.JitterPhysics.Contracts.PhysicsWorldSettings;
using LegacyShape = DataSakura.JitterPhysics.Contracts.PhysicsShapeRecord;
using LegacyVector = DataSakura.JitterPhysics.Contracts.PhysicsVector3;
using NativeArtifact = DataSakura.JitterPhysics.JitterNative.PhysicsArtifact;
using NativeBody = DataSakura.JitterPhysics.JitterNative.PhysicsBodyRecord;
using NativeCanonicalization = DataSakura.JitterPhysics.JitterNative.PhysicsCanonicalization;
using NativeCodec = DataSakura.JitterPhysics.JitterNative.Codec.PhysicsArtifactCodec;
using NativeSettings = DataSakura.JitterPhysics.JitterNative.PhysicsWorldSettings;
using NativeShape = DataSakura.JitterPhysics.JitterNative.PhysicsShapeRecord;

namespace DataSakura.JitterPhysics.Server.Tests
{
    /// <summary>Executable contract for the JMP-E04 Jitter-native record and codec boundary.</summary>
    public sealed class JitterNativeArtifactCodecTests
    {
        private const string RuntimeId =
            "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

        [Test]
        public void NativeWriterPreservesTheSchemaOneGoldenBytes()
        {
            byte[] legacy = PhysicsArtifactWriter.Write(CreateLegacyArtifact());
            byte[] native = NativeCodec.Write(CreateNativeArtifact());

            Assert.That(native, Is.EqualTo(legacy));
            Assert.That(native.Length, Is.EqualTo(165));
            Assert.That(
                JitterPhysicsHash.Sha256Hex(native),
                Is.EqualTo("b53cf221453ce313ae3e2d9ff3e94b665b65a674a0f1f5e9863acb5b33835479"));
        }

        [Test]
        public void NativeReaderReturnsOnlyJitterMathValues()
        {
            byte[] payload = PhysicsArtifactWriter.Write(CreateLegacyArtifact());
            DataSakura.JitterPhysics.JitterNative.Codec.PhysicsArtifactResult result =
                NativeCodec.Read(payload);

            Assert.That(result.Succeeded, Is.True, result.Error.ToString());
            Assert.That(result.Artifact.WorldSettings.Gravity, Is.TypeOf<JVector>());
            Assert.That(result.Artifact.Bodies[0].Position, Is.TypeOf<JVector>());
            Assert.That(result.Artifact.Bodies[0].Orientation, Is.TypeOf<JQuaternion>());
            Assert.That(result.Artifact.Bodies[0].Shapes[0].Size, Is.TypeOf<JVector>());
            Assert.That(result.Artifact.Bodies[0].Shapes[0].Size.X, Is.EqualTo(1f));
        }

        [Test]
        public void AuthoritativeRecordPropertiesDoNotExposeLegacyMathDtos()
        {
            Type[] authoritative =
            {
                typeof(NativeArtifact),
                typeof(NativeSettings),
                typeof(NativeBody),
                typeof(NativeShape),
            };
            Type[] exposed = authoritative
                .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                .Select(property => property.PropertyType)
                .ToArray();

            Assert.That(exposed, Does.Contain(typeof(JVector)));
            Assert.That(exposed, Does.Contain(typeof(JQuaternion)));
            Assert.That(exposed, Does.Contain(typeof(JVector[])));
            Assert.That(exposed, Does.Not.Contain(typeof(LegacyVector)));
            Assert.That(exposed, Does.Not.Contain(typeof(LegacyQuaternion)));
        }

        [Test]
        public void CanonicalizationUsesStableF32RulesForSignAndSignedZero()
        {
            JQuaternion positive = NativeCanonicalization.CanonicalQuaternion(
                new JQuaternion(0f, 0f, 0f, 2f));
            JQuaternion negative = NativeCanonicalization.CanonicalQuaternion(
                new JQuaternion(0f, 0f, 0f, -2f));

            Assert.That(negative.X, Is.EqualTo(positive.X));
            Assert.That(negative.Y, Is.EqualTo(positive.Y));
            Assert.That(negative.Z, Is.EqualTo(positive.Z));
            Assert.That(negative.W, Is.EqualTo(positive.W));
            Assert.That(NativeCanonicalization.IsCanonicalQuaternion(positive), Is.True);
            Assert.That(
                BitConverter.SingleToInt32Bits(NativeCanonicalization.CanonicalReal(-0f)),
                Is.EqualTo(0));

            NativeArtifact signedZero = CreateNativeArtifact(new JVector(-0f, 0f, 0f));
            Assert.That(NativeCodec.Write(signedZero), Is.EqualTo(NativeCodec.Write(CreateNativeArtifact())));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void InvalidNativeScalarsProduceTypedFailures(float value)
        {
            NativeArtifact artifact = CreateNativeArtifact(new JVector(value, 0f, 0f));
            PhysicsArtifactError error = NativeCodec.Validate(artifact);

            Assert.That(error.Code, Is.EqualTo(PhysicsArtifactErrorCode.InvalidValue));
            Assert.That(() => NativeCodec.Write(artifact), Throws.ArgumentException);
        }

        [Test]
        public void DegenerateQuaternionFailsBeforeAnyPayloadIsReturned()
        {
            NativeArtifact artifact = CreateNativeArtifact(
                JVector.Zero,
                new JQuaternion(0f, 0f, 0f, 0f));

            PhysicsArtifactError error = NativeCodec.Validate(artifact);
            Assert.That(error.Code, Is.EqualTo(PhysicsArtifactErrorCode.InvalidValue));
            Assert.That(() => NativeCodec.Write(artifact), Throws.ArgumentException);
        }

        private static LegacyArtifact CreateLegacyArtifact()
        {
            var shape = LegacyShape.Box(
                "s",
                LegacyVector.Zero,
                LegacyQuaternion.Identity,
                new LegacyVector(1f, 2f, 3f));
            var body = new LegacyBody(
                "b",
                LegacyVector.Zero,
                LegacyQuaternion.Identity,
                0.2f,
                0f,
                new List<LegacyShape> { shape });
            return new LegacyArtifact(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                RuntimeId,
                "l",
                LegacySettings.Default,
                new List<LegacyBody> { body });
        }

        private static NativeArtifact CreateNativeArtifact(
            JVector? position = null,
            JQuaternion? orientation = null)
        {
            var shape = NativeShape.Box(
                "s",
                JVector.Zero,
                JQuaternion.Identity,
                new JVector(1f, 2f, 3f));
            var body = new NativeBody(
                "b",
                position ?? JVector.Zero,
                orientation ?? JQuaternion.Identity,
                0.2f,
                0f,
                new List<NativeShape> { shape });
            var settings = new NativeSettings(
                new JVector(0f, -9.81f, 0f),
                30,
                1,
                6,
                4,
                true);
            return new NativeArtifact(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                RuntimeId,
                "l",
                settings,
                new List<NativeBody> { body });
        }
    }
}
