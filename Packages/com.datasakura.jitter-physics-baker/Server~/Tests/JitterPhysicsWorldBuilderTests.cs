using System.Collections.Generic;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Integration;
using Jitter2;
using Jitter2.Dynamics;
using Jitter2.LinearMath;
using NUnit.Framework;
using NativeArtifact = DataSakura.JitterPhysics.JitterNative.PhysicsArtifact;
using NativeBody = DataSakura.JitterPhysics.JitterNative.PhysicsBodyRecord;
using NativeCodec = DataSakura.JitterPhysics.JitterNative.Codec.PhysicsArtifactCodec;
using NativeReadResult = DataSakura.JitterPhysics.JitterNative.Codec.PhysicsArtifactResult;
using NativeSettings = DataSakura.JitterPhysics.JitterNative.PhysicsWorldSettings;
using NativeShape = DataSakura.JitterPhysics.JitterNative.PhysicsShapeRecord;

namespace DataSakura.JitterPhysics.Server.Tests
{
    /// <summary>
    /// The shared loader: artifact records in, a Jitter world out.
    /// <para>
    /// These tests run under plain .NET because that is where the dedicated server lives.
    /// The same code is compiled by Unity for the client, so what is asserted here — record
    /// order becomes creation order, the topology fingerprint is reproducible, a failed
    /// build leaves nothing behind — is what makes it safe for both sides to trust one file.
    /// </para>
    /// </summary>
    public sealed class JitterPhysicsWorldBuilderTests
    {
        [Test]
        public void ArtifactBecomesStaticGeometry()
        {
            var world = new World();
            NativeArtifact artifact = CreateArenaArtifact();

            PhysicsWorldBuildResult result = JitterPhysicsWorldBuilder.Apply(world, artifact);

            Assert.That(result.Succeeded, Is.True, result.Error.ToString());
            Assert.That(result.BodyCount, Is.EqualTo(artifact.Bodies.Count));
            Assert.That(result.ShapeCount, Is.GreaterThanOrEqualTo(artifact.ShapeCount));
            Assert.That(result.TopologyFingerprint, Has.Length.EqualTo(64));

            foreach (RigidBody body in world.RigidBodies)
            {
                Assert.That(body.MotionType, Is.EqualTo(MotionType.Static));
            }
        }

        [Test]
        public void TopologyFingerprintIsReproducible()
        {
            NativeArtifact artifact = CreateArenaArtifact();

            string first = Build(artifact).TopologyFingerprint;
            string second = Build(artifact).TopologyFingerprint;

            // Two worlds built from one artifact must be indistinguishable. This is the check
            // a client and a server compare in practice, so it has to be exact.
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void DecodedArtifactBuildsTheSameTopologyAsTheOriginal()
        {
            NativeArtifact original = CreateArenaArtifact();
            byte[] payload = NativeCodec.Write(original);

            NativeReadResult decoded = NativeCodec.Read(payload);
            Assert.That(decoded.Succeeded, Is.True, decoded.Error.ToString());

            // A round trip through the binary format must not change the world that comes out
            // of it, otherwise the file would mean something different on the receiving side.
            Assert.That(
                Build(decoded.Artifact).TopologyFingerprint,
                Is.EqualTo(Build(original).TopologyFingerprint));
        }

        [Test]
        public void WorldSettingsFromTheArtifactAreApplied()
        {
            var world = new World();
            NativeArtifact artifact = CreateArenaArtifact();

            JitterPhysicsWorldBuilder.Apply(world, artifact);

            Assert.That(world.SolveMode, Is.EqualTo(SolveMode.Deterministic));
            Assert.That(world.Gravity.Y, Is.EqualTo(artifact.WorldSettings.Gravity.Y).Within(1e-5f));
            Assert.That(world.SolverIterations.solver, Is.EqualTo(artifact.WorldSettings.SolverIterations));
        }

        [Test]
        public void ApplyingASecondArtifactToTheSameWorldIsRefused()
        {
            var world = new World();
            NativeArtifact artifact = CreateArenaArtifact();

            Assert.That(JitterPhysicsWorldBuilder.Apply(world, artifact).Succeeded, Is.True);
            Assert.That(JitterPhysicsWorldBuilder.HasArtifact(world), Is.True);

            PhysicsWorldBuildResult second = JitterPhysicsWorldBuilder.Apply(world, artifact);

            // Merging would silently double every wall in the level.
            Assert.That(second.Succeeded, Is.False);
            Assert.That(second.Error.Code, Is.EqualTo(PhysicsArtifactErrorCode.InvalidValue));
        }

        [Test]
        public void FailedApplyRestoresBodiesAndWorldSettings()
        {
            var world = new World
            {
                Gravity = new JVector(1f, 2f, 3f),
                SolveMode = SolveMode.Regular,
                SolverIterations = (2, 3),
                AllowDeactivation = false,
            };
            int before = world.RigidBodies.Count;

            PhysicsWorldBuildResult result = JitterPhysicsWorldBuilder.ApplyWithFailureForTests(
                world,
                CreateArenaArtifact(),
                JitterPhysicsWorldBuildFailurePoint.AfterFirstBody);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.RequiresWorldDiscard, Is.False);
            Assert.That(result.Error.Message, Does.Contain("rolled back"));
            Assert.That(world.RigidBodies.Count, Is.EqualTo(before));
            Assert.That(world.Gravity, Is.EqualTo(new JVector(1f, 2f, 3f)));
            Assert.That(world.SolveMode, Is.EqualTo(SolveMode.Regular));
            Assert.That(world.SolverIterations, Is.EqualTo((2, 3)));
            Assert.That(world.AllowDeactivation, Is.False);
            Assert.That(JitterPhysicsWorldBuilder.HasArtifact(world), Is.False);
        }

        [Test]
        public void IncompleteRollbackRequiresCallerToDiscardWorld()
        {
            var world = new World();

            PhysicsWorldBuildResult result = JitterPhysicsWorldBuilder.ApplyWithFailureForTests(
                world,
                CreateArenaArtifact(),
                JitterPhysicsWorldBuildFailurePoint.ForceIncompleteRollback);

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.RequiresWorldDiscard, Is.True);
            Assert.That(result.Error.Message, Does.Contain("discard this World"));
            Assert.That(JitterPhysicsWorldBuilder.HasArtifact(world), Is.False);
        }

        [Test]
        public void BakedGeometryActuallyCollides()
        {
            var world = new World();
            Assert.That(JitterPhysicsWorldBuilder.Apply(world, CreateArenaArtifact()).Succeeded, Is.True);

            RigidBody falling = world.CreateRigidBody();
            falling.AddShape(new Jitter2.Collision.Shapes.BoxShape(new JVector(1f, 1f, 1f)));
            falling.Position = new JVector(0f, 5f, 0f);

            for (int i = 0; i < 240; i++)
            {
                world.Step(1f / 30f, multiThread: false);
            }

            // The ground of the fixture spans y in [-1, 0], so a unit box rests at y = 0.5.
            // Without this the tests would only prove that objects were created, not that the
            // level they describe can be stood on.
            Assert.That(falling.Position.Y, Is.GreaterThan(0f), "The body fell through the baked ground.");
            Assert.That(falling.Position.Y, Is.EqualTo(0.5f).Within(0.15f));
        }

        [Test]
        public void MeshGeometryBecomesTriangles()
        {
            NativeArtifact artifact = CreateMeshArtifact();

            PhysicsWorldBuildResult result = Build(artifact);

            Assert.That(result.Succeeded, Is.True, result.Error.ToString());

            // One Jitter shape per triangle, which is how Jitter represents a mesh.
            Assert.That(result.ShapeCount, Is.EqualTo(artifact.TriangleCount));
        }

        [Test]
        public void LocalShapePosesArePreserved()
        {
            var world = new World();

            var shapes = new List<NativeShape>
            {
                NativeShape.Box(
                    "offset",
                    new JVector(0f, 2f, 0f),
                    JQuaternion.Identity,
                    new JVector(1f, 1f, 1f)),
            };

            var artifact = new NativeArtifact(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                RuntimeId,
                "poses",
                DefaultSettings(),
                new List<NativeBody>
                {
                    new NativeBody(
                        "body",
                        new JVector(10f, 0f, 0f),
                        JQuaternion.Identity,
                        0.2f,
                        0f,
                        shapes),
                });

            Assert.That(JitterPhysicsWorldBuilder.Apply(world, artifact).Succeeded, Is.True);

            RigidBody body = null;
            foreach (RigidBody candidate in world.RigidBodies)
            {
                body = candidate;
            }

            Assert.That(body, Is.Not.Null);

            // The shape sits two units above a body that stands ten units along X, so the
            // world-space bounds of the shape have to reflect both the body pose and the
            // local one. Checking the shape rather than the body is what proves the local
            // pose survived: a body pose alone would place the geometry at y = 0.
            Assert.That(body.Position.X, Is.EqualTo(10f).Within(1e-4f));

            Jitter2.Collision.Shapes.RigidBodyShape shape = body.Shapes[0];
            JVector center = (shape.WorldBoundingBox.Min + shape.WorldBoundingBox.Max) * (Real)0.5;

            Assert.That(center.Y, Is.EqualTo(2f).Within(1e-3f));
            Assert.That(center.X, Is.EqualTo(10f).Within(1e-3f));
        }

        private const string RuntimeId =
            "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

        private static PhysicsWorldBuildResult Build(NativeArtifact artifact)
        {
            return JitterPhysicsWorldBuilder.Apply(new World(), artifact);
        }

        /// <summary>Ground plus two covers, the shape of the smallest realistic level.</summary>
        private static NativeArtifact CreateArenaArtifact()
        {
            var bodies = new List<NativeBody>
            {
                new NativeBody(
                    "cover_a",
                    new JVector(-3f, 0.5f, 2f),
                    JQuaternion.Identity,
                    0.2f,
                    0f,
                    new List<NativeShape>
                    {
                        NativeShape.Box(
                            "s_box",
                            JVector.Zero,
                            JQuaternion.Identity,
                            new JVector(1f, 1f, 1f)),
                    }),
                new NativeBody(
                    "cover_b",
                    new JVector(3f, 0.5f, 2f),
                    JQuaternion.Identity,
                    0.2f,
                    0f,
                    new List<NativeShape>
                    {
                        NativeShape.Capsule(
                            "s_capsule",
                            JVector.Zero,
                            JQuaternion.Identity,
                            0.5f,
                            1f),
                    }),
                new NativeBody(
                    "ground",
                    new JVector(0f, -0.5f, 0f),
                    JQuaternion.Identity,
                    0.2f,
                    0f,
                    new List<NativeShape>
                    {
                        NativeShape.Box(
                            "s_ground",
                            JVector.Zero,
                            JQuaternion.Identity,
                            new JVector(40f, 1f, 40f)),
                    }),
            };

            return new NativeArtifact(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                RuntimeId,
                "arena",
                DefaultSettings(),
                bodies);
        }

        private static NativeArtifact CreateMeshArtifact()
        {
            var vertices = new[]
            {
                new JVector(-5f, 0f, -5f),
                new JVector(5f, 0f, -5f),
                new JVector(5f, 0f, 5f),
                new JVector(-5f, 0f, 5f),
            };

            var indices = new[] { 0, 1, 2, 0, 2, 3 };

            return new NativeArtifact(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                RuntimeId,
                "mesh_level",
                DefaultSettings(),
                new List<NativeBody>
                {
                    new NativeBody(
                        "terrain",
                        JVector.Zero,
                        JQuaternion.Identity,
                        0.2f,
                        0f,
                        new List<NativeShape>
                        {
                            NativeShape.Mesh(
                                "s_mesh",
                                JVector.Zero,
                                JQuaternion.Identity,
                                vertices,
                                indices),
                        }),
                });
        }

        private static NativeSettings DefaultSettings()
        {
            return new NativeSettings(new JVector(0f, -9.81f, 0f), 30, 1, 6, 4, true);
        }
    }
}
