using System.Collections.Generic;
using System.IO;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Editor.Baking;
using DataSakura.JitterPhysics.UnityArtifact;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor.Tests
{
    /// <summary>
    /// Writing a baked artifact into the project, and reading it back.
    /// <para>
    /// The bake is only useful if what lands on disk is exactly what was hashed, and if a
    /// failed attempt leaves the previous result intact. Both are tested here against the real
    /// asset database rather than a mock, because the failure modes worth catching — a missed
    /// import, a broken reference after re-bake — live in Unity's behaviour, not in ours.
    /// </para>
    /// </summary>
    public sealed class JitterPhysicsArtifactWriteTests
    {
        private const string RuntimeId =
            "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

        private const string TestFolder = "Assets/JitterPhysicsBakeTests";

        private readonly List<Object> spawned = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < spawned.Count; i++)
            {
                if (spawned[i] != null)
                {
                    Object.DestroyImmediate(spawned[i]);
                }
            }

            spawned.Clear();

            if (AssetDatabase.IsValidFolder(TestFolder))
            {
                AssetDatabase.DeleteAsset(TestFolder);
            }

            AssetDatabase.Refresh();
        }

        [Test]
        public void BakeWritesPayloadManifestAndAsset()
        {
            JitterPhysicsLevel level = CreateLevelWithGround();

            JitterPhysicsBakeResult result = JitterPhysicsBaker.Bake(level, RuntimeId);

            Assert.That(result.Succeeded, Is.True, result.Issues.Format());
            Assert.That(File.Exists(result.Output.PayloadPath), Is.True, result.Output.PayloadPath);
            Assert.That(File.Exists(result.Output.ManifestPath), Is.True, result.Output.ManifestPath);

            var asset = AssetDatabase.LoadAssetAtPath<JitterPhysicsArtifactAsset>(result.Output.AssetPath);
            Assert.That(asset, Is.Not.Null);
            Assert.That(asset.HasPayload, Is.True);
            Assert.That(asset.ArtifactHash, Is.EqualTo(result.Output.ArtifactHash));
            Assert.That(asset.LevelId, Is.EqualTo("test_level"));
        }

        [Test]
        public void WrittenBytesHashToTheRecordedArtifactHash()
        {
            JitterPhysicsLevel level = CreateLevelWithGround();
            JitterPhysicsBakeResult result = JitterPhysicsBaker.Bake(level, RuntimeId);
            Assert.That(result.Succeeded, Is.True, result.Issues.Format());

            byte[] onDisk = File.ReadAllBytes(result.Output.PayloadPath);

            // The stable name is for humans; the full content identity remains verified metadata.
            Assert.That(JitterPhysicsHash.Sha256Hex(onDisk), Is.EqualTo(result.Output.ArtifactHash));
            Assert.That(
                result.Output.PayloadPath,
                Does.EndWith("test_level.physics.bytes"));
        }

        [Test]
        public void LoaderDecodesTheWrittenArtifact()
        {
            JitterPhysicsLevel level = CreateLevelWithGround();
            JitterPhysicsBakeResult result = JitterPhysicsBaker.Bake(level, RuntimeId);
            Assert.That(result.Succeeded, Is.True, result.Issues.Format());

            var asset = AssetDatabase.LoadAssetAtPath<JitterPhysicsArtifactAsset>(result.Output.AssetPath);
            PhysicsArtifactResult loaded = JitterPhysicsArtifactLoader.Load(asset, RuntimeId);

            Assert.That(loaded.Succeeded, Is.True, loaded.Error.ToString());
            Assert.That(loaded.Artifact.LevelId, Is.EqualTo("test_level"));
            Assert.That(loaded.Artifact.Bodies.Count, Is.EqualTo(1));
        }

        [Test]
        public void LoaderRefusesAnArtifactBakedForAnotherRuntime()
        {
            JitterPhysicsLevel level = CreateLevelWithGround();
            JitterPhysicsBakeResult result = JitterPhysicsBaker.Bake(level, RuntimeId);
            Assert.That(result.Succeeded, Is.True, result.Issues.Format());

            var asset = AssetDatabase.LoadAssetAtPath<JitterPhysicsArtifactAsset>(result.Output.AssetPath);
            PhysicsArtifactResult loaded = JitterPhysicsArtifactLoader.Load(asset, new string('b', 64));

            Assert.That(loaded.Succeeded, Is.False);
            Assert.That(loaded.Error.Code, Is.EqualTo(PhysicsArtifactErrorCode.IncompatibleRuntime));
        }

        [Test]
        public void LoaderRejectsAPayloadThatNoLongerMatchesItsAsset()
        {
            JitterPhysicsLevel level = CreateLevelWithGround();
            JitterPhysicsBakeResult result = JitterPhysicsBaker.Bake(level, RuntimeId);
            Assert.That(result.Succeeded, Is.True, result.Issues.Format());

            byte[] tampered = File.ReadAllBytes(result.Output.PayloadPath);
            tampered[tampered.Length - 1] ^= 0xFF;
            File.WriteAllBytes(result.Output.PayloadPath, tampered);
            AssetDatabase.ImportAsset(result.Output.PayloadPath, ImportAssetOptions.ForceSynchronousImport);

            var asset = AssetDatabase.LoadAssetAtPath<JitterPhysicsArtifactAsset>(result.Output.AssetPath);
            PhysicsArtifactResult loaded = JitterPhysicsArtifactLoader.Load(asset);

            // The asset still records the old hash, so the edited payload has to be refused.
            Assert.That(loaded.Succeeded, Is.False);
            Assert.That(loaded.Error.Code, Is.EqualTo(PhysicsArtifactErrorCode.HashMismatch));
        }

        [Test]
        public void RebakingAnUnchangedLevelKeepsTheSameFilesAndAssetInstance()
        {
            JitterPhysicsLevel level = CreateLevelWithGround();

            JitterPhysicsBakeResult first = JitterPhysicsBaker.Bake(level, RuntimeId);
            Assert.That(first.Succeeded, Is.True, first.Issues.Format());
            var firstAsset = AssetDatabase.LoadAssetAtPath<JitterPhysicsArtifactAsset>(first.Output.AssetPath);

            JitterPhysicsBakeResult second = JitterPhysicsBaker.Bake(level, RuntimeId);
            Assert.That(second.Succeeded, Is.True, second.Issues.Format());
            var secondAsset = AssetDatabase.LoadAssetAtPath<JitterPhysicsArtifactAsset>(second.Output.AssetPath);

            Assert.That(second.Output.ArtifactHash, Is.EqualTo(first.Output.ArtifactHash));
            Assert.That(second.Output.PayloadPath, Is.EqualTo(first.Output.PayloadPath));

            // The asset is updated in place: recreating it would silently break every scene
            // reference to this level.
            Assert.That(secondAsset, Is.SameAs(firstAsset));
        }

        [Test]
        public void LegacyMigrationPreservesGuidsPayloadBytesAndIsRepeatSafe()
        {
            JitterPhysicsLevel level = CreateLevelWithGround();
            JitterPhysicsBakeResult baked = JitterPhysicsBaker.Bake(level, RuntimeId);
            Assert.That(baked.Succeeded, Is.True, baked.Issues.Format());

            string legacyAsset = JitterPhysicsArtifactPaths.LegacyArtifactAssetPath(
                TestFolder, level.LevelId);
            string legacyPayload = JitterPhysicsArtifactPaths.LegacyBinaryAssetPath(
                TestFolder, level.LevelId, baked.Output.ArtifactHash);
            string legacyManifest = JitterPhysicsArtifactPaths.LegacyManifestAssetPath(
                TestFolder, level.LevelId, baked.Output.ArtifactHash);
            string assetGuid = AssetDatabase.AssetPathToGUID(baked.Output.AssetPath);
            string payloadGuid = AssetDatabase.AssetPathToGUID(baked.Output.PayloadPath);
            string manifestGuid = AssetDatabase.AssetPathToGUID(baked.Output.ManifestPath);
            byte[] payloadBytes = File.ReadAllBytes(baked.Output.PayloadPath);

            PhysicsArtifactManifest current = baked.Output.Manifest;
            var oldManifest = new PhysicsArtifactManifest(
                current.SchemaVersion,
                current.RuntimeCompatibilityId,
                current.GeneratorVersion,
                current.LevelId,
                current.ArtifactHash,
                current.BodyCount,
                current.ShapeCount,
                current.VertexCount,
                current.TriangleCount,
                current.TickRate,
                JitterPhysicsArtifactNaming.LegacyBinaryFileName(current.LevelId, current.ArtifactHash));
            File.WriteAllText(baked.Output.ManifestPath, PhysicsArtifactManifestCodec.Write(oldManifest));
            Assert.That(AssetDatabase.MoveAsset(baked.Output.PayloadPath, legacyPayload), Is.Empty);
            Assert.That(AssetDatabase.MoveAsset(baked.Output.ManifestPath, legacyManifest), Is.Empty);
            Assert.That(AssetDatabase.MoveAsset(baked.Output.AssetPath, legacyAsset), Is.Empty);

            JitterPhysicsIssueLog first = JitterPhysicsArtifactMigration.Migrate(
                TestFolder, level.LevelId, baked.Output.ArtifactHash);
            JitterPhysicsIssueLog second = JitterPhysicsArtifactMigration.Migrate(
                TestFolder, level.LevelId, baked.Output.ArtifactHash);

            Assert.That(first.HasErrors, Is.False, first.Format());
            Assert.That(second.HasErrors, Is.False, second.Format());
            Assert.That(AssetDatabase.AssetPathToGUID(baked.Output.AssetPath), Is.EqualTo(assetGuid));
            Assert.That(AssetDatabase.AssetPathToGUID(baked.Output.PayloadPath), Is.EqualTo(payloadGuid));
            Assert.That(AssetDatabase.AssetPathToGUID(baked.Output.ManifestPath), Is.EqualTo(manifestGuid));
            Assert.That(File.ReadAllBytes(baked.Output.PayloadPath), Is.EqualTo(payloadBytes));

            var migrated = AssetDatabase.LoadAssetAtPath<JitterPhysicsArtifactAsset>(baked.Output.AssetPath);
            Assert.That(AssetDatabase.GetAssetPath(migrated.Payload), Is.EqualTo(baked.Output.PayloadPath));
        }

        [Test]
        public void FailedBakeLeavesThePreviousArtifactInPlace()
        {
            JitterPhysicsLevel level = CreateLevelWithGround();

            JitterPhysicsBakeResult good = JitterPhysicsBaker.Bake(level, RuntimeId);
            Assert.That(good.Succeeded, Is.True, good.Issues.Format());
            byte[] before = File.ReadAllBytes(good.Output.PayloadPath);

            // Break the scene the way an author would: a trigger is not collision geometry.
            JitterStaticBodySource source = level.GeometryRoot.GetComponentInChildren<JitterStaticBodySource>();
            source.GetComponent<BoxCollider>().isTrigger = true;

            JitterPhysicsBakeResult failed = JitterPhysicsBaker.Bake(level, RuntimeId);

            Assert.That(failed.Succeeded, Is.False);
            Assert.That(failed.Issues.HasErrors, Is.True);

            // A level that used to work must not stop working because somebody pressed Bake
            // with a broken scene.
            Assert.That(File.Exists(good.Output.PayloadPath), Is.True);
            Assert.That(File.ReadAllBytes(good.Output.PayloadPath), Is.EqualTo(before));
            Assert.That(
                AssetDatabase.LoadAssetAtPath<JitterPhysicsArtifactAsset>(good.Output.AssetPath),
                Is.Not.Null);
        }

        [Test]
        public void BakeIsRefusedWithoutACompatibleRuntimeId()
        {
            JitterPhysicsLevel level = CreateLevelWithGround();

            JitterPhysicsBakeResult result = JitterPhysicsBaker.Bake(level, null);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Issues.HasErrors, Is.True);
            Assert.That(AssetDatabase.IsValidFolder(TestFolder), Is.False, "Nothing should have been written.");
        }

        [Test]
        public void ChangedGeometrySafelyReplacesTheStableNamedPair()
        {
            JitterPhysicsLevel level = CreateLevelWithGround();

            JitterPhysicsBakeResult first = JitterPhysicsBaker.Bake(level, RuntimeId);
            Assert.That(first.Succeeded, Is.True, first.Issues.Format());

            JitterStaticBodySource source = level.GeometryRoot.GetComponentInChildren<JitterStaticBodySource>();
            source.transform.position += new Vector3(0f, 2f, 0f);

            JitterPhysicsBakeResult second = JitterPhysicsBaker.Bake(level, RuntimeId);
            Assert.That(second.Succeeded, Is.True, second.Issues.Format());

            Assert.That(second.Output.ArtifactHash, Is.Not.EqualTo(first.Output.ArtifactHash));
            Assert.That(second.Output.PayloadPath, Is.EqualTo(first.Output.PayloadPath));

            // The asset always points at the current payload.
            var asset = AssetDatabase.LoadAssetAtPath<JitterPhysicsArtifactAsset>(second.Output.AssetPath);
            Assert.That(asset.ArtifactHash, Is.EqualTo(second.Output.ArtifactHash));
        }

        private JitterPhysicsLevel CreateLevelWithGround()
        {
            var levelObject = new GameObject("Level");
            spawned.Add(levelObject);
            var level = levelObject.AddComponent<JitterPhysicsLevel>();

            var root = new GameObject("GeometryRoot");
            spawned.Add(root);

            var profile = ScriptableObject.CreateInstance<JitterPhysicsWorldProfile>();
            spawned.Add(profile);

            var serialized = new SerializedObject(level);
            serialized.FindProperty("levelId").stringValue = "test_level";
            serialized.FindProperty("geometryRoot").objectReferenceValue = root.transform;
            serialized.FindProperty("worldProfile").objectReferenceValue = profile;
            serialized.FindProperty("generatedFolder").stringValue = TestFolder;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var groundObject = new GameObject("ground");
            spawned.Add(groundObject);
            groundObject.transform.SetParent(root.transform);

            BoxCollider box = groundObject.AddComponent<BoxCollider>();
            box.size = new Vector3(20f, 1f, 20f);

            var source = groundObject.AddComponent<JitterStaticBodySource>();
            source.SetSourceId("ground");

            return level;
        }
    }
}
