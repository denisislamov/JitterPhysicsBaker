using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DataSakura.JitterPhysics.Contracts;

namespace DataSakura.JitterPhysics.Tools.SampleArtifact
{
    /// <summary>
    /// Builds seed artifacts from <c>Server/demo-levels.json</c> — the same file the scene author
    /// reads. One definition drives both the committed <c>.unity</c> scene and the artifact the
    /// web viewer shows, so the two cannot describe different geometry.
    /// </summary>
    /// <remarks>
    /// These are seeds. The authoritative artifact is what Unity bakes from the scene; this exists
    /// so a fresh checkout can run the server without opening the editor. The euler-to-quaternion
    /// composition here matches the scene author's exactly, because Unity bakes the quaternion the
    /// author wrote into the scene rather than re-deriving it, so a baked artifact lands on the
    /// same orientation this seed does.
    /// </remarks>
    public static class DemoLevels
    {
        /// <summary>One built level, ready to write.</summary>
        public sealed class Level
        {
            public Level(string levelId, PhysicsArtifact artifact)
            {
                LevelId = levelId;
                Artifact = artifact;
            }

            public string LevelId { get; }

            public PhysicsArtifact Artifact { get; }
        }

        /// <summary>Finds <c>demo-levels.json</c> by walking up from the binary, or returns null.</summary>
        public static string FindDefinitionFile()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            for (int depth = 0; depth < 10 && directory != null; depth++)
            {
                string candidate = Path.Combine(directory.FullName, "Server", "demo-levels.json");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                candidate = Path.Combine(directory.FullName, "demo-levels.json");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return null;
        }

        /// <summary>Reads the definition file and builds every level in it.</summary>
        public static IReadOnlyList<Level> Load(string path, string runtimeCompatibilityId)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;

            PhysicsWorldSettings settings = ReadWorld(root.GetProperty("world"));

            var levels = new List<Level>();
            foreach (JsonElement levelElement in root.GetProperty("levels").EnumerateArray())
            {
                string levelId = levelElement.GetProperty("levelId").GetString();
                var bodies = new List<PhysicsBodyRecord>();

                foreach (JsonElement bodyElement in levelElement.GetProperty("bodies").EnumerateArray())
                {
                    bodies.Add(ReadBody(bodyElement));
                }

                // The writer refuses records out of order rather than sorting silently, so the one
                // sort happens here.
                bodies.Sort((a, b) => string.CompareOrdinal(a.SourceId, b.SourceId));

                var artifact = new PhysicsArtifact(
                    JitterPhysicsPackage.ArtifactSchemaVersion,
                    runtimeCompatibilityId,
                    levelId,
                    settings,
                    bodies);

                levels.Add(new Level(levelId, artifact));
            }

            return levels;
        }

        private static PhysicsWorldSettings ReadWorld(JsonElement world)
        {
            float[] gravity = ReadVector(world.GetProperty("gravity"));

            return new PhysicsWorldSettings(
                new PhysicsVector3(gravity[0], gravity[1], gravity[2]).Canonical(),
                world.GetProperty("tickRate").GetInt32(),
                world.GetProperty("substepCount").GetInt32(),
                world.GetProperty("solverIterations").GetInt32(),
                world.GetProperty("relaxationIterations").GetInt32(),
                world.GetProperty("allowDeactivation").GetBoolean());
        }

        private static PhysicsBodyRecord ReadBody(JsonElement body)
        {
            string id = body.GetProperty("id").GetString();
            float[] pos = ReadVector(body.GetProperty("pos"));
            PhysicsQuaternion orientation = ReadOrientation(body);
            float friction = ReadFloat(body, "friction", 0.4f);
            float restitution = ReadFloat(body, "restitution", 0f);

            PhysicsShapeRecord shape = ReadShape(body);

            return new PhysicsBodyRecord(
                id,
                new PhysicsVector3(pos[0], pos[1], pos[2]).Canonical(),
                orientation.Canonical(),
                friction,
                restitution,
                new[] { shape });
        }

        private static PhysicsShapeRecord ReadShape(JsonElement body)
        {
            string shape = body.GetProperty("shape").GetString();

            switch (shape)
            {
                case "box":
                {
                    float[] size = ReadVector(body.GetProperty("size"));
                    return PhysicsShapeRecord.Box(
                        "shape_0",
                        PhysicsVector3.Zero,
                        PhysicsQuaternion.Identity,
                        new PhysicsVector3(size[0], size[1], size[2]).Canonical());
                }

                case "sphere":
                {
                    float radius = (float)body.GetProperty("radius").GetDouble();
                    return PhysicsShapeRecord.Sphere(
                        "shape_0", PhysicsVector3.Zero, PhysicsQuaternion.Identity, radius);
                }

                case "capsule":
                {
                    // Uniform scale s in the scene gives Unity radius 0.5s and total height 2s, so
                    // the cylinder length (height minus the two caps) is s.
                    float s = (float)body.GetProperty("scale").GetDouble();
                    return PhysicsShapeRecord.Capsule(
                        "shape_0", PhysicsVector3.Zero, PhysicsQuaternion.Identity, 0.5f * s, s);
                }

                default:
                    throw new InvalidOperationException($"Unknown shape '{shape}' in demo-levels.json.");
            }
        }

        private static PhysicsQuaternion ReadOrientation(JsonElement body)
        {
            if (!body.TryGetProperty("euler", out JsonElement euler))
            {
                return PhysicsQuaternion.Identity;
            }

            float[] e = ReadVector(euler);
            return EulerToQuaternion(e[0], e[1], e[2]);
        }

        /// <summary>Euler degrees to a quaternion, composed y * x * z to match the scene author.</summary>
        private static PhysicsQuaternion EulerToQuaternion(float x, float y, float z)
        {
            (float, float, float, float) Axis(double angle, int ax, int ay, int az)
            {
                double half = angle * Math.PI / 360.0;
                float s = (float)Math.Sin(half);
                float c = (float)Math.Cos(half);
                return (ax * s, ay * s, az * s, c);
            }

            var qx = Axis(x, 1, 0, 0);
            var qy = Axis(y, 0, 1, 0);
            var qz = Axis(z, 0, 0, 1);

            (float, float, float, float) q = Multiply(Multiply(qy, qx), qz);
            return new PhysicsQuaternion(q.Item1, q.Item2, q.Item3, q.Item4);
        }

        private static (float, float, float, float) Multiply(
            (float x, float y, float z, float w) a, (float x, float y, float z, float w) b)
        {
            return (
                a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
                a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
                a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
                a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
        }

        private static float[] ReadVector(JsonElement array)
        {
            var values = new float[3];
            int i = 0;
            foreach (JsonElement element in array.EnumerateArray())
            {
                values[i++] = (float)element.GetDouble();
            }

            return values;
        }

        private static float ReadFloat(JsonElement element, string name, float fallback) =>
            element.TryGetProperty(name, out JsonElement value) ? (float)value.GetDouble() : fallback;
    }
}

