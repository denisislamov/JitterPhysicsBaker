using System;
using System.Collections.Generic;
using System.IO;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.WebViewer;

namespace DataSakura.JitterPhysics.Tools.SampleArtifact
{
    /// <summary>
    /// Writes a valid demo artifact without Unity.
    /// <para>
    /// It builds the same kind of arena the Unity demo scene does — a floor, walls, a rotated
    /// ramp, a platform, capsule pillars, a sphere, a multi-shape crate stack and a triangle
    /// mesh hill — using the package's own writer, so the file it produces is byte-for-byte a
    /// thing the loader accepts. The Unity bake remains the source of truth; this only seeds a
    /// clean checkout so the server and its smoke test have an artifact to load.
    /// </para>
    /// </summary>
    public static class Program
    {
        private const string LevelId = "demo_arena";

        public static int Main(string[] args)
        {
            string outputFolder = args.Length > 0
                ? args[0]
                : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts");

            outputFolder = Path.GetFullPath(outputFolder);
            Directory.CreateDirectory(outputFolder);

            JitterLock jitterLock = JitterLock.Load(
                Path.Combine(AppContext.BaseDirectory, "jitter2.lock.json"));

            // The arena carries the mesh shape, so it stays a hand-built definition here. The other
            // levels are read from the same JSON the scene author uses, so a scene and its seed
            // cannot drift.
            var levels = new List<DemoLevels.Level>
            {
                new DemoLevels.Level(LevelId, BuildArtifact(jitterLock.RuntimeCompatibilityId)),
            };

            string definition = DemoLevels.FindDefinitionFile();
            if (definition != null)
            {
                levels.AddRange(DemoLevels.Load(definition, jitterLock.RuntimeCompatibilityId));
            }
            else
            {
                Console.Error.WriteLine("warning: demo-levels.json not found; only the arena was written.");
            }

            foreach (DemoLevels.Level level in levels)
            {
                Write(level.LevelId, level.Artifact, outputFolder);
            }

            Console.WriteLine($"Wrote {levels.Count} level(s) into {outputFolder}");
            Console.WriteLine("  runtime id " + jitterLock.RuntimeCompatibilityId);
            return 0;
        }

        private static void Write(string levelId, PhysicsArtifact artifact, string outputFolder)
        {
            PhysicsArtifactPayload payload = PhysicsArtifactWriter.WriteWithManifest(
                artifact, JitterPhysicsPackage.PackageVersion);

            string payloadName = JitterPhysicsArtifactNaming.BinaryFileName(levelId);
            string manifestName = JitterPhysicsArtifactNaming.ManifestFileName(levelId);

            PhysicsArtifactPairWriter.Write(
                Path.Combine(outputFolder, payloadName),
                payload.Bytes,
                Path.Combine(outputFolder, manifestName),
                PhysicsArtifactManifestCodec.Write(payload.Manifest));

            Console.WriteLine(
                $"  {levelId}: {payload.Bytes.Length} bytes, {artifact.Bodies.Count} bodies, "
                + $"{artifact.ShapeCount} shapes, {artifact.TriangleCount} triangles, hash {payload.ArtifactHash[..12]}");
        }

        private static PhysicsArtifact BuildArtifact(string runtimeCompatibilityId)
        {
            // Bodies must be in strictly ascending ordinal order of their source id; they are
            // collected in any order here and sorted once at the end.
            var bodies = new List<PhysicsBodyRecord>();

            bodies.Add(BoxBody("floor", V(0f, -0.5f, 0f), Quat.Identity, V(60f, 1f, 60f), friction: 0.6f));
            bodies.Add(BoxBody("wall_east", V(30f, 1.5f, 0f), Quat.Identity, V(1f, 3f, 60f), friction: 0.3f));
            bodies.Add(BoxBody("wall_north", V(0f, 1.5f, 30f), Quat.Identity, V(60f, 3f, 1f), friction: 0.3f));
            bodies.Add(BoxBody("wall_south", V(0f, 1.5f, -30f), Quat.Identity, V(60f, 3f, 1f), friction: 0.3f));
            bodies.Add(BoxBody("wall_west", V(-30f, 1.5f, 0f), Quat.Identity, V(1f, 3f, 60f), friction: 0.3f));

            bodies.Add(SphereBody("boulder", V(-6f, 2f, -10f), radius: 2f, friction: 0.2f, restitution: 0.35f));

            bodies.Add(CrateStack("crate_stack", V(-14f, 0f, -6f)));

            bodies.Add(MeshBody("hill", V(12f, 0f, 14f)));

            bodies.Add(BoxBody("platform", V(10f, 3f, -8f), Quat.Identity, V(10f, 0.5f, 10f), friction: 0.5f));

            // A ramp tilted about Z by -18 degrees: the artifact has to carry orientation, and
            // a slope is where a wrong quaternion is immediately visible.
            bodies.Add(BoxBody("ramp", V(-10f, 1.6f, 8f), Quat.AxisZ(-18f), V(12f, 0.5f, 8f), friction: 0.4f));

            for (int i = 0; i < 4; i++)
            {
                float x = 6f + ((i % 2) * 8f);
                float z = -4f - ((i / 2) * 8f);
                bodies.Add(CapsuleBody("pillar_" + i, V(x, 1.5f, z), radius: 0.5f, length: 2f, friction: 0.3f));
            }

            bodies.Sort((a, b) => string.CompareOrdinal(a.SourceId, b.SourceId));

            var settings = new PhysicsWorldSettings(
                V(0f, -9.81f, 0f).Canonical(),
                tickRate: 60,
                substepCount: 1,
                solverIterations: 6,
                relaxationIterations: 4,
                allowDeactivation: true);

            return new PhysicsArtifact(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                runtimeCompatibilityId,
                LevelId,
                settings,
                bodies);
        }

        private static PhysicsBodyRecord BoxBody(
            string id, PhysicsVector3 position, PhysicsQuaternion orientation, PhysicsVector3 size,
            float friction, float restitution = 0f)
        {
            return Box(id, position, orientation, size, friction, restitution);
        }

        private static PhysicsBodyRecord Box(
            string id, PhysicsVector3 position, PhysicsQuaternion orientation, PhysicsVector3 size,
            float friction, float restitution)
        {
            var shape = PhysicsShapeRecord.Box(
                "shape_0", PhysicsVector3.Zero, PhysicsQuaternion.Identity, size.Canonical());
            return Body(id, position, orientation, friction, restitution, shape);
        }

        private static PhysicsBodyRecord SphereBody(
            string id, PhysicsVector3 position, float radius, float friction, float restitution)
        {
            var shape = PhysicsShapeRecord.Sphere(
                "shape_0", PhysicsVector3.Zero, PhysicsQuaternion.Identity, radius);
            return Body(id, position, Quat.Identity, friction, restitution, shape);
        }

        private static PhysicsBodyRecord CapsuleBody(
            string id, PhysicsVector3 position, float radius, float length, float friction)
        {
            var shape = PhysicsShapeRecord.Capsule(
                "shape_0", PhysicsVector3.Zero, PhysicsQuaternion.Identity, radius, length);
            return Body(id, position, Quat.Identity, friction, 0f, shape);
        }

        private static PhysicsBodyRecord CrateStack(string id, PhysicsVector3 position)
        {
            // One body, several boxes, shape keys in strictly ascending ordinal order. This is
            // the case where an unstable shape order would silently change the artifact hash.
            var shapes = new List<PhysicsShapeRecord>();
            for (int i = 0; i < 3; i++)
            {
                shapes.Add(PhysicsShapeRecord.Box(
                    "shape_" + i,
                    V(i * 0.35f, 0.6f + (i * 1.2f), 0f).Canonical(),
                    Quat.AxisY(i * 12f),
                    V(1.2f, 1.2f, 1.2f).Canonical()));
            }

            return new PhysicsBodyRecord(
                id, position.Canonical(), Quat.Identity, 0.7f, 0f, shapes);
        }

        private static PhysicsBodyRecord MeshBody(string id, PhysicsVector3 position)
        {
            BuildHillMesh(out PhysicsVector3[] vertices, out int[] indices);
            var shape = PhysicsShapeRecord.Mesh(
                "shape_0", PhysicsVector3.Zero, PhysicsQuaternion.Identity, vertices, indices);
            return new PhysicsBodyRecord(id, position.Canonical(), Quat.Identity, 0.8f, 0f, new[] { shape });
        }

        private static PhysicsBodyRecord Body(
            string id, PhysicsVector3 position, PhysicsQuaternion orientation,
            float friction, float restitution, PhysicsShapeRecord shape)
        {
            return new PhysicsBodyRecord(
                id, position.Canonical(), orientation.Canonical(), friction, restitution, new[] { shape });
        }

        /// <summary>A gentle sine height field, matching the Unity demo mesh in body-local space.</summary>
        private static void BuildHillMesh(out PhysicsVector3[] vertices, out int[] indices)
        {
            const int segments = 16;
            const float size = 20f;
            const float height = 2.5f;

            vertices = new PhysicsVector3[(segments + 1) * (segments + 1)];
            for (int z = 0; z <= segments; z++)
            {
                for (int x = 0; x <= segments; x++)
                {
                    float u = x / (float)segments;
                    float v = z / (float)segments;
                    float y = height
                        * (float)Math.Sin(u * Math.PI)
                        * (float)Math.Sin(v * Math.PI);

                    int index = (z * (segments + 1)) + x;
                    vertices[index] = V((u - 0.5f) * size, y, (v - 0.5f) * size).Canonical();
                }
            }

            var triangleList = new List<int>(segments * segments * 6);
            for (int z = 0; z < segments; z++)
            {
                for (int x = 0; x < segments; x++)
                {
                    int i0 = (z * (segments + 1)) + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + segments + 1;
                    int i3 = i2 + 1;

                    triangleList.Add(i0);
                    triangleList.Add(i2);
                    triangleList.Add(i1);
                    triangleList.Add(i1);
                    triangleList.Add(i2);
                    triangleList.Add(i3);
                }
            }

            indices = triangleList.ToArray();
        }

        private static PhysicsVector3 V(float x, float y, float z) => new PhysicsVector3(x, y, z);

        /// <summary>Small quaternion helpers so the seed does not depend on UnityEngine.</summary>
        private static class Quat
        {
            internal static PhysicsQuaternion Identity => PhysicsQuaternion.Identity;

            internal static PhysicsQuaternion AxisY(float degrees) => Axis(0f, 1f, 0f, degrees);

            internal static PhysicsQuaternion AxisZ(float degrees) => Axis(0f, 0f, 1f, degrees);

            private static PhysicsQuaternion Axis(float x, float y, float z, float degrees)
            {
                double half = degrees * Math.PI / 360d;
                float s = (float)Math.Sin(half);
                float c = (float)Math.Cos(half);
                return new PhysicsQuaternion(x * s, y * s, z * s, c).Canonical();
            }
        }
    }
}

