using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DataSakura.JitterPhysics.Integration;
using Jitter2;
using NUnit.Framework;
using NativeArtifact = DataSakura.JitterPhysics.JitterNative.PhysicsArtifact;

namespace DataSakura.JitterPhysics.Server.Tests
{
    /// <summary>Executable boundaries shared by standalone and combined consumers.</summary>
    public sealed class JMPE07ConsumerContractTests
    {
        [Test]
        public void WorldBuilderPublicContractConsumesNativeRecordsDirectly()
        {
            MethodInfo apply = typeof(JitterPhysicsWorldBuilder).GetMethod(
                nameof(JitterPhysicsWorldBuilder.Apply),
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(World), typeof(NativeArtifact) },
                null);

            Assert.That(apply, Is.Not.Null);
            Assert.That(apply.ReturnType, Is.EqualTo(typeof(PhysicsWorldBuildResult)));
        }

        [Test]
        public void ProcessLoadsExactlyOneJitterCoreAssembly()
        {
            Assembly[] candidates = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => assembly.GetName().Name == "Jitter2.Core")
                .ToArray();

            Assert.That(candidates, Has.Length.EqualTo(1));
            Assert.That(candidates[0], Is.SameAs(typeof(World).Assembly));
        }

        [Test]
        public void PackageRuntimeDoesNotOwnWorldStepButSampleDoes()
        {
            string packageRoot = PackageRoot();
            string builder = File.ReadAllText(Path.Combine(
                packageRoot, "JitterIntegration~", "Runtime", "JitterPhysicsWorldBuilder.cs"));
            string startup = File.ReadAllText(Path.Combine(
                packageRoot, "JitterIntegration~", "Runtime", "JitterPhysicsServerStartup.cs"));
            string sample = File.ReadAllText(Path.Combine(
                packageRoot, "Samples~", "Demos", "Runtime", "JitterPhysicsSampleWorld.cs"));

            Assert.That(builder, Does.Not.Contain("world.Step("));
            Assert.That(startup, Does.Not.Contain("world.Step("));
            Assert.That(sample, Does.Contain("World.Step("));
        }

        [Test]
        public void RuntimeAndSampleDoNotConvertNativeRecordsBackBeforeSimulation()
        {
            string packageRoot = PackageRoot();
            string builder = File.ReadAllText(Path.Combine(
                packageRoot, "JitterIntegration~", "Runtime", "JitterPhysicsWorldBuilder.cs"));
            string sample = File.ReadAllText(Path.Combine(
                packageRoot, "Samples~", "Demos", "Runtime", "JitterPhysicsSampleWorld.cs"));

            Assert.That(builder, Does.Not.Contain("PhysicsVector3"));
            Assert.That(builder, Does.Not.Contain("PhysicsQuaternion"));
            Assert.That(builder, Does.Not.Contain("ToJVector"));
            Assert.That(builder, Does.Not.Contain("ToJQuaternion"));
            Assert.That(sample, Does.Contain("NativeArtifact loadedArtifact"));
            Assert.That(sample, Does.Contain("JitterNativeUnityArtifactLoader.Load"));
        }

        private static string PackageRoot()
        {
            return Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", ".."));
        }
    }
}
