using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;
using NUnit.Framework;

namespace DataSakura.JitterPhysics.Server.Tests
{
    /// <summary>Executable schema and runtime-identity decision for JMP-E06.</summary>
    public sealed class JMPE06ArtifactCompatibilityTests
    {
        private const string E00RuntimeId =
            "ca8283611d3221120e69e23c4c028720537de4867f1de53df3752db85cd32006";
        private const string E06RuntimeId =
            "71e9d01f4006a8e1d097beb047efa8b8aabbe24895cb8d50531c764031c9aa4b";

        [Test]
        public void SchemaStaysOneWhileCurrentRuntimeIdentityChanges()
        {
            const string sourceHash =
                "sha256:ca940ca6483ffcedf65854719396cec2d9e038cc43c01e7d35d147cd70766940";
            const string compileProfileId =
                "a2925211b983330117414426be9bf8a2798ce9169c1206e1e55178f708cfa72e";

            string actual = RuntimeCompatibilityId.Compute(
                RuntimeCompatibilityInputs.ForCurrentBuild(sourceHash, compileProfileId));

            Assert.That(JitterPhysicsPackage.ArtifactSchemaVersion, Is.EqualTo(1));
            Assert.That(actual, Is.EqualTo(E06RuntimeId));
            Assert.That(actual, Is.Not.EqualTo(E00RuntimeId));
        }

        [Test]
        public void OldRuntimeIdentityIsRejectedEvenThoughSchemaOneStillParses()
        {
            var artifact = new PhysicsArtifact(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                E00RuntimeId,
                "runtime-migration",
                PhysicsWorldSettings.Default,
                System.Array.Empty<PhysicsBodyRecord>());

            PhysicsArtifactError mismatch = PhysicsArtifactReader.CheckRuntimeCompatibility(
                artifact, E06RuntimeId);
            Assert.That(mismatch.Code, Is.EqualTo(PhysicsArtifactErrorCode.IncompatibleRuntime));
        }
    }
}
