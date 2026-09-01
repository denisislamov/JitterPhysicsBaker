using System;
using System.IO;
using System.Linq;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Integration;
using NUnit.Framework;

namespace DataSakura.JitterPhysics.Server.Tests
{
    /// <summary>Pre-mutation checks for the only supported Jitter scalar/layout profile.</summary>
    public sealed class JitterRuntimeProfileTests
    {
        [Test]
        public void LoadedCanonicalDistributionPassesF32Preflight()
        {
            JitterRuntimeProfileResult result = JitterRuntimeProfile.VerifyCanonicalF32();

            Assert.That(result.Succeeded, Is.True, result.Error.ToString());
            Assert.That(JitterRuntimeProfile.PrecisionMode, Is.EqualTo("f32"));
        }

        [TestCase(true, typeof(double), typeof(double), typeof(double), 24, 32)]
        [TestCase(false, typeof(float), typeof(double), typeof(float), 12, 16)]
        [TestCase(false, typeof(float), typeof(float), typeof(float), 16, 16)]
        [TestCase(false, typeof(float), typeof(float), typeof(float), 12, 32)]
        public void UnsupportedPrecisionOrLayoutReturnsTypedFailure(
            bool isDouble,
            Type declaredReal,
            Type vectorScalar,
            Type quaternionScalar,
            int vectorSize,
            int quaternionSize)
        {
            JitterRuntimeProfileResult result = JitterRuntimeProfile.VerifyLayout(
                isDouble,
                declaredReal,
                vectorScalar,
                quaternionScalar,
                vectorSize,
                quaternionSize);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Error.Code, Is.EqualTo(PhysicsArtifactErrorCode.IncompatibleRuntime));
            Assert.That(result.Error.Message, Does.Contain("before artifact loading or world mutation"));
        }

        [Test]
        public void OwnedUnityAndServerSourcesDeclareOneExactRealPolicy()
        {
            string packageRoot = PackageRoot();
            string integrationRoot = Path.Combine(packageRoot, "JitterIntegration~");
            string[] aliasLines = Directory
                .GetFiles(integrationRoot, "*.cs", SearchOption.AllDirectories)
                .SelectMany(File.ReadAllLines)
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("using Real =", StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            Assert.That(aliasLines, Is.EqualTo(new[] { "using Real = System.Single;" }));

            string project = File.ReadAllText(Path.Combine(
                packageRoot, "Server~", "Tests", "DataSakura.JitterPhysics.Server.Tests.csproj"));
            Assert.That(project, Does.Contain("<Using Include=\"System.Single\" Alias=\"Real\" />"));
            Assert.That(project, Does.Contain("DATASAKURA_SERVER_GLOBAL_REAL"));
            Assert.That(project, Does.Not.Contain("USE_DOUBLE_PRECISION"));
        }

        private static string PackageRoot()
        {
            return Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", ".."));
        }
    }
}
