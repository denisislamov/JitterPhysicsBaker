using System.Collections.Generic;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Editor.Api;
using DataSakura.JitterPhysics.Editor.Baking;
using DataSakura.JitterPhysics.UnityArtifact;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor.Tests
{
    /// <summary>Consumer-facing editor contract used by an NPI adapter or standalone tooling.</summary>
    public sealed class JitterPhysicsEditorApiTests
    {
        private const string TestFolder = "Assets/JitterPhysicsEditorApiTests";
        private readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < spawned.Count; i++)
            {
                if (spawned[i] != null) Object.DestroyImmediate(spawned[i]);
            }

            spawned.Clear();
            if (AssetDatabase.IsValidFolder(TestFolder)) AssetDatabase.DeleteAsset(TestFolder);
            AssetDatabase.Refresh();
        }

        [Test]
        public void StandaloneValidationOwnsAndAssignsItsLevelId()
        {
            JitterPhysicsLevel level = CreateLevel("", "standalone_source");

            JitterPhysicsEditorResult result = JitterPhysicsEditorApi.Validate(level);

            Assert.That(result.Ownership, Is.EqualTo(JitterPhysicsLevelIdOwnership.Standalone));
            Assert.That(JitterPhysicsIdUtility.IsCanonical(level.LevelId), Is.True);
            Assert.That(result.LevelId, Is.EqualTo(level.LevelId));
            Assert.That(result.Issues.Format(), Does.Not.Contain("Level ID"));
        }

        [Test]
        public void ExternalManagedIdIsExplicitAndDoesNotMutateStandaloneId()
        {
            JitterPhysicsLevel level = CreateLevel("standalone_level", "managed_source");

            JitterPhysicsEditorResult result = JitterPhysicsEditorApi.Validate(
                level,
                JitterPhysicsLevelIdBinding.External("NPI", "npi_managed_level"));

            Assert.That(result.LevelId, Is.EqualTo("npi_managed_level"));
            Assert.That(result.Owner, Is.EqualTo("NPI"));
            Assert.That(result.Ownership, Is.EqualTo(JitterPhysicsLevelIdOwnership.ExternalManaged));
            Assert.That(level.LevelId, Is.EqualTo("standalone_level"));
            Assert.That(result.Issues.Format(), Does.Not.Contain("Level ID"));
        }

        [TestCase("", "managed_level")]
        [TestCase("NPI", "Managed Level")]
        public void InvalidExternalBindingIsRejected(string owner, string managedLevelId)
        {
            JitterPhysicsLevel level = CreateLevel("standalone_level", "invalid_source");

            JitterPhysicsEditorResult result = JitterPhysicsEditorApi.Validate(
                level,
                JitterPhysicsLevelIdBinding.External(owner, managedLevelId));

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Status, Is.EqualTo(JitterPhysicsEditorResultStatus.Failed));
            Assert.That(result.Issues.HasErrors, Is.True);
        }

        [Test]
        public void ConflictingManagedIdIsRejectedBeforeBake()
        {
            JitterPhysicsLevel level = CreateLevel("standalone_level", "first_source");
            JitterPhysicsLevel other = CreateLevel("shared_level", "second_source");

            JitterPhysicsEditorResult result = JitterPhysicsEditorApi.Validate(
                level,
                JitterPhysicsLevelIdBinding.External("NPI", "shared_level"));

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Issues.Format(), Does.Contain("Level ID conflict"));
            Assert.That(result.Issues.Issues[0].Context, Is.SameAs(other));
        }

        [Test]
        public void BakeAndReadOnlySummaryExposeTheSameVerifiedDelivery()
        {
            const string runtimeId =
                "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";
            JitterPhysicsLevel level = CreateLevel("npi_delivery_level", "baked_source");
            JitterPhysicsLevelIdBinding binding =
                JitterPhysicsLevelIdBinding.External("NPI", "npi_delivery_level");

            JitterPhysicsBakeResult baked = JitterPhysicsBaker.Bake(level, runtimeId);
            SetLevelId(level, "standalone_level");
            JitterPhysicsEditorResult summary = JitterPhysicsEditorApi.ReadSummary(level, binding);

            Assert.That(baked.Succeeded, Is.True, baked.Issues.Format());
            Assert.That(summary.Succeeded, Is.True, summary.Issues.Format());
            Assert.That(summary.Status, Is.EqualTo(JitterPhysicsEditorResultStatus.Ready));
            Assert.That(summary.LevelId, Is.EqualTo("npi_delivery_level"));
            Assert.That(summary.ArtifactPath, Does.EndWith("npi_delivery_level.physics.asset"));
            Assert.That(summary.PayloadPath, Does.EndWith("npi_delivery_level.physics.bytes"));
            Assert.That(summary.ManifestPath, Does.EndWith("npi_delivery_level.physics.manifest.json"));
            Assert.That(summary.Digest, Is.EqualTo(baked.Output.ArtifactHash));
            Assert.That(summary.PayloadSize, Is.EqualTo(baked.Output.PayloadSize));
            Assert.That(summary.BodyCount, Is.EqualTo(baked.Output.Manifest.BodyCount));
            Assert.That(summary.ShapeCount, Is.EqualTo(baked.Output.Manifest.ShapeCount));
            Assert.That(level.LevelId, Is.EqualTo("standalone_level"));

            var asset = AssetDatabase.LoadAssetAtPath<JitterPhysicsArtifactAsset>(summary.ArtifactPath);
            PhysicsArtifactResult unityLoaded = JitterPhysicsArtifactLoader.Load(asset, runtimeId);
            PhysicsArtifactLoadResult dotNetLoaded = new FilePhysicsArtifactProvider(summary.ManifestPath)
                .Load(runtimeId);
            Assert.That(unityLoaded.Succeeded, Is.True, unityLoaded.Error.ToString());
            Assert.That(dotNetLoaded.Succeeded, Is.True, dotNetLoaded.Error.ToString());
            Assert.That(
                PhysicsArtifactWriter.Write(dotNetLoaded.Artifact),
                Is.EqualTo(PhysicsArtifactWriter.Write(unityLoaded.Artifact)));
            Assert.That(dotNetLoaded.ArtifactHash, Is.EqualTo(summary.Digest));
        }

        [Test]
        public void PublicBakeReportsMissingProjectOwnedJitterWithoutWriting()
        {
            JitterPhysicsLevel level = CreateLevel("standalone_level", "no_jitter_source");
            JitterPhysicsEditorResult result = JitterPhysicsEditorApi.Bake(level);

            if (JitterPhysicsBakeCommand.CanBake)
            {
                Assert.That(result.Succeeded, Is.True, result.Issues.Format());
            }
            else
            {
                Assert.That(result.Status, Is.EqualTo(JitterPhysicsEditorResultStatus.Failed));
                Assert.That(result.Issues.HasErrors, Is.True);
                Assert.That(AssetDatabase.IsValidFolder(TestFolder), Is.False);
            }
        }

        private JitterPhysicsLevel CreateLevel(string levelId, string sourceId)
        {
            var levelObject = new GameObject("Level " + sourceId);
            spawned.Add(levelObject);
            var level = levelObject.AddComponent<JitterPhysicsLevel>();

            var root = new GameObject("Geometry " + sourceId);
            spawned.Add(root);

            var profile = ScriptableObject.CreateInstance<JitterPhysicsWorldProfile>();
            spawned.Add(profile);

            var serialized = new SerializedObject(level);
            serialized.FindProperty("levelId").stringValue = levelId;
            serialized.FindProperty("geometryRoot").objectReferenceValue = root.transform;
            serialized.FindProperty("worldProfile").objectReferenceValue = profile;
            serialized.FindProperty("generatedFolder").stringValue = TestFolder;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var body = new GameObject("Body " + sourceId);
            spawned.Add(body);
            body.transform.SetParent(root.transform);
            body.AddComponent<BoxCollider>();
            body.AddComponent<JitterStaticBodySource>().SetSourceId(sourceId);
            return level;
        }

        private static void SetLevelId(JitterPhysicsLevel level, string value)
        {
            var serialized = new SerializedObject(level);
            serialized.FindProperty("levelId").stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
