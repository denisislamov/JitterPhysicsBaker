using System.Collections.Generic;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Editor.Diagnostics;
using DataSakura.JitterPhysics.Editor.Api;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;

namespace DataSakura.JitterPhysics.Editor.Tests
{
    /// <summary>Exact stale-geometry decisions used by the Scene View overlay.</summary>
    public sealed class JitterPhysicsGeometryOverlayTests
    {
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
        }

        [Test]
        public void UnchangedBodyPoseMatchesTheBakedRecord()
        {
            Transform transform = CreateTransform();
            transform.position = new Vector3(2f, 3f, 4f);
            transform.rotation = Quaternion.Euler(10f, 20f, 30f);

            PhysicsBodyRecord baked = BodyAt(transform);

            Assert.That(
                JitterPhysicsGeometryComparer.BodyPoseMatches(baked, transform),
                Is.True);
        }

        [Test]
        public void MovingABodyMarksItsGeometryAsChanged()
        {
            Transform transform = CreateTransform();
            PhysicsBodyRecord baked = BodyAt(transform);

            transform.position = new Vector3(0f, 0.125f, 0f);

            Assert.That(
                JitterPhysicsGeometryComparer.BodyPoseMatches(baked, transform),
                Is.False);
        }

        [Test]
        public void IdenticalPrimitiveShapesMatch()
        {
            PhysicsShapeRecord baked = PhysicsShapeRecord.Box(
                "/box#0",
                new PhysicsVector3(1f, 2f, 3f),
                PhysicsQuaternion.Identity,
                new PhysicsVector3(4f, 5f, 6f));
            PhysicsShapeRecord current = PhysicsShapeRecord.Box(
                "/box#0",
                new PhysicsVector3(1f, 2f, 3f),
                PhysicsQuaternion.Identity,
                new PhysicsVector3(4f, 5f, 6f));

            Assert.That(JitterPhysicsGeometryComparer.ShapesMatch(baked, current), Is.True);
        }

        [Test]
        public void ResizingAPrimitiveMarksItAsChanged()
        {
            PhysicsShapeRecord baked = PhysicsShapeRecord.Sphere(
                "/sphere#0", PhysicsVector3.Zero, PhysicsQuaternion.Identity, 1f);
            PhysicsShapeRecord current = PhysicsShapeRecord.Sphere(
                "/sphere#0", PhysicsVector3.Zero, PhysicsQuaternion.Identity, 1.001f);

            Assert.That(JitterPhysicsGeometryComparer.ShapesMatch(baked, current), Is.False);
        }

        [Test]
        public void EditingOneMeshVertexMarksItAsChanged()
        {
            PhysicsShapeRecord baked = Triangle(new PhysicsVector3(0f, 1f, 0f));
            PhysicsShapeRecord current = Triangle(new PhysicsVector3(0f, 1.01f, 0f));

            Assert.That(JitterPhysicsGeometryComparer.ShapesMatch(baked, current), Is.False);
        }

        [Test]
        public void ReorderingMeshIndicesMarksItAsChanged()
        {
            PhysicsVector3[] vertices =
            {
                PhysicsVector3.Zero,
                new PhysicsVector3(1f, 0f, 0f),
                new PhysicsVector3(0f, 1f, 0f),
            };

            PhysicsShapeRecord baked = PhysicsShapeRecord.Mesh(
                "/mesh#0", PhysicsVector3.Zero, PhysicsQuaternion.Identity, vertices, new[] { 0, 1, 2 });
            PhysicsShapeRecord current = PhysicsShapeRecord.Mesh(
                "/mesh#0", PhysicsVector3.Zero, PhysicsQuaternion.Identity, vertices, new[] { 0, 2, 1 });

            Assert.That(JitterPhysicsGeometryComparer.ShapesMatch(baked, current), Is.False);
        }

        [Test]
        public void PreviewUsesTheSpecifiedMutedPalette()
        {
            AssertColor(JitterPhysicsBakeGeometryOverlay.SourcesColor, "C5AC83");
            AssertColor(JitterPhysicsBakeGeometryOverlay.BakedColor, "BD984F");
            AssertColor(JitterPhysicsBakeGeometryOverlay.RuntimeColor, "D5B975");
            AssertColor(JitterPhysicsBakeGeometryOverlay.ChangedColor, "A87945");
            AssertColor(JitterPhysicsBakeGeometryOverlay.MovedColor, "A66B5B");
            AssertColor(JitterPhysicsBakeGeometryOverlay.RemovedColor, "8F4F4A");
            AssertColor(JitterPhysicsBakeGeometryOverlay.ErrorColor, "684779");
            AssertColor(JitterPhysicsBakeGeometryOverlay.ErrorBackdropColor, "F0DEB8");
        }

        [Test]
        public void OldOverlayPreferenceIsTheOnlyBakedLayerState()
        {
            bool existed = EditorPrefs.HasKey(JitterPhysicsBakeGeometryOverlay.PreferenceKey);
            bool previous = JitterPhysicsBakeGeometryOverlay.Enabled;
            try
            {
                JitterPhysicsBakeGeometryOverlay.SetEnabled(!previous);
                Assert.That(JitterPhysicsPreviewPreferences.Baked, Is.EqualTo(!previous));
                Assert.That(JitterPhysicsBakeGeometryOverlay.PreferenceKey,
                    Is.EqualTo("DataSakura.JitterPhysics.Editor.ShowBakedGeometryOverlay"));
            }
            finally
            {
                if (existed) JitterPhysicsBakeGeometryOverlay.SetEnabled(previous);
                else JitterPhysicsBakeGeometryOverlay.ResetPreference();
            }
        }

        [Test]
        public void PublicPreviewApiReadsTheOverlayStateWithoutCreatingPreferences()
        {
            JitterPhysicsPreviewPreferences.ResetToDefaults();

            JitterPhysicsPreviewState read = JitterPhysicsPreviewApi.Current;

            Assert.That(read.Sources, Is.False);
            Assert.That(read.Baked, Is.False);
            Assert.That(read.Runtime, Is.False);
            Assert.That(EditorPrefs.HasKey(JitterPhysicsPreviewPreferences.SourcesKey), Is.False);
            Assert.That(EditorPrefs.HasKey(JitterPhysicsPreviewPreferences.BakedKey), Is.False);
            Assert.That(EditorPrefs.HasKey(JitterPhysicsPreviewPreferences.RuntimeKey), Is.False);
            Assert.That(EditorPrefs.HasKey(JitterPhysicsPreviewPreferences.ScopeKey), Is.False);
            Assert.That(EditorPrefs.HasKey(JitterPhysicsPreviewPreferences.OcclusionKey), Is.False);
        }

        [Test]
        public void PublicPreviewApiAndPackageOverlayShareOneState()
        {
            JitterPhysicsPreviewState previous = JitterPhysicsPreviewApi.Current;
            try
            {
                var requested = new JitterPhysicsPreviewState(
                    true,
                    !previous.Baked,
                    true,
                    JitterPhysicsPreviewScope.AllLoadedLevels,
                    JitterPhysicsPreviewOcclusion.XRay);

                JitterPhysicsPreviewApi.Apply(requested);

                Assert.That(JitterPhysicsPreviewPreferences.Sources, Is.True);
                Assert.That(JitterPhysicsBakeGeometryOverlay.Enabled, Is.EqualTo(requested.Baked));
                Assert.That(JitterPhysicsPreviewPreferences.Runtime, Is.True);
                Assert.That(JitterPhysicsPreviewPreferences.Scope, Is.EqualTo(requested.Scope));
                Assert.That(JitterPhysicsPreviewPreferences.Occlusion, Is.EqualTo(requested.Occlusion));
            }
            finally
            {
                JitterPhysicsPreviewApi.Apply(previous);
            }
        }

        [Test]
        public void RuntimePreviewContractStaysIndependentOfJitter2()
        {
            Assert.That(typeof(IJitterPhysicsRuntimePreviewSource).Assembly,
                Is.EqualTo(typeof(PhysicsArtifact).Assembly));
            foreach (System.Reflection.AssemblyName assembly in
                     typeof(IJitterPhysicsRuntimePreviewSource).Assembly.GetReferencedAssemblies())
            {
                Assert.That(assembly.Name.StartsWith("Jitter2", System.StringComparison.Ordinal),
                    Is.False);
            }
        }

        private static void AssertColor(Color color, string expected)
        {
            Assert.That(ColorUtility.ToHtmlStringRGB(color), Is.EqualTo(expected));
        }

        private Transform CreateTransform()
        {
            var gameObject = new GameObject("Geometry");
            spawned.Add(gameObject);
            return gameObject.transform;
        }

        private static PhysicsBodyRecord BodyAt(Transform transform)
        {
            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;

            return new PhysicsBodyRecord(
                "body",
                new PhysicsVector3(position.x, position.y, position.z).Canonical(),
                new PhysicsQuaternion(rotation.x, rotation.y, rotation.z, rotation.w).Canonical(),
                0.2f,
                0f,
                new[]
                {
                    PhysicsShapeRecord.Box(
                        "/box#0",
                        PhysicsVector3.Zero,
                        PhysicsQuaternion.Identity,
                        new PhysicsVector3(1f, 1f, 1f)),
                });
        }

        private static PhysicsShapeRecord Triangle(PhysicsVector3 top)
        {
            return PhysicsShapeRecord.Mesh(
                "/mesh#0",
                PhysicsVector3.Zero,
                PhysicsQuaternion.Identity,
                new[]
                {
                    PhysicsVector3.Zero,
                    new PhysicsVector3(1f, 0f, 0f),
                    top,
                },
                new[] { 0, 1, 2 });
        }
    }
}
