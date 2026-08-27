using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace DataSakura.JitterPhysics.Samples.Tests
{
    /// <summary>Runs after a sample scene is generated to prove its delivered runtime path.</summary>
    public sealed class JitterPhysicsSampleDeliveryTests
    {
        /// <summary>Lets the generated scene enter play and verifies its artifact-backed world.</summary>
        [UnityTest]
        public IEnumerator GeneratedSampleLoadsAndStepsItsBakedArtifact()
        {
            AsyncOperation loaded = SceneManager.LoadSceneAsync("SampleBouncingBall", LoadSceneMode.Single);
            Assert.That(loaded, Is.Not.Null, "The generated sample scene must be enabled in build settings.");
            yield return loaded;
            yield return null;

            JitterPhysicsSampleWorld sample = Object.FindFirstObjectByType<JitterPhysicsSampleWorld>();
            Assert.That(sample, Is.Not.Null, "Generate a sample scene before running this fixture.");

            Assert.That(sample.IsReady, Is.True);
            Assert.That(sample.LevelId, Is.Not.Empty);
            Assert.That(sample.StaticBodyCount, Is.GreaterThan(0));
            Assert.That(sample.StaticShapeCount, Is.GreaterThan(0));

            var verification = sample.GetComponent<JitterPhysicsArtifactVerificationSample>();
            Assert.That(verification, Is.Not.Null);
            Assert.That(verification.Verify(), Is.True);
        }
    }
}
