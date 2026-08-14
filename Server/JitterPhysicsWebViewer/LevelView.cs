using System.Collections.Generic;
using DataSakura.JitterPhysics.Contracts;

namespace DataSakura.JitterPhysics.WebViewer
{
    /// <summary>One collision shape, in the form the browser draws it.</summary>
    public sealed class ShapeView
    {
        /// <summary>Stable key of the shape inside its body.</summary>
        public string Key { get; set; }

        /// <summary>"box", "sphere", "capsule" or "mesh".</summary>
        public string Type { get; set; }

        /// <summary>Shape pose in the body's local space.</summary>
        public float[] LocalPosition { get; set; }

        /// <summary>Shape rotation in the body's local space, as x, y, z, w.</summary>
        public float[] LocalRotation { get; set; }

        /// <summary>Full extents of a box.</summary>
        public float[] Size { get; set; }

        /// <summary>Radius of a sphere or capsule.</summary>
        public float Radius { get; set; }

        /// <summary>Cylinder length of a capsule, excluding the caps.</summary>
        public float Length { get; set; }

        /// <summary>Flat x, y, z triples of a mesh, in body-local space.</summary>
        public float[] Vertices { get; set; }

        /// <summary>Triangle indices of a mesh.</summary>
        public int[] Indices { get; set; }
    }

    /// <summary>One static body of the level.</summary>
    public sealed class BodyView
    {
        /// <summary>Stable identifier authored in the Unity scene.</summary>
        public string SourceId { get; set; }

        /// <summary>World position.</summary>
        public float[] Position { get; set; }

        /// <summary>World orientation, as x, y, z, w.</summary>
        public float[] Orientation { get; set; }

        /// <summary>Friction baked for this body.</summary>
        public float Friction { get; set; }

        /// <summary>Restitution baked for this body.</summary>
        public float Restitution { get; set; }

        /// <summary>Shapes attached to this body.</summary>
        public List<ShapeView> Shapes { get; set; }
    }

    /// <summary>
    /// The static level as the browser receives it.
    /// <para>
    /// This is a projection of the artifact for rendering only. It is deliberately built from
    /// the decoded artifact rather than from the Jitter world: what the page draws must be
    /// what the file says, so that a wrong artifact looks wrong on screen instead of being
    /// hidden behind whatever the physics engine happened to accept.
    /// </para>
    /// </summary>
    public sealed class LevelView
    {
        /// <summary>Level identifier baked into the artifact.</summary>
        public string LevelId { get; set; }

        /// <summary>SHA-256 of the payload the world was built from.</summary>
        public string ArtifactHash { get; set; }

        /// <summary>Hash of the rebuilt static topology.</summary>
        public string TopologyFingerprint { get; set; }

        /// <summary>Fixed tick rate the level was authored for.</summary>
        public int TickRate { get; set; }

        /// <summary>Gravity baked into the artifact.</summary>
        public float[] Gravity { get; set; }

        /// <summary>Static bodies, in artifact order.</summary>
        public List<BodyView> Bodies { get; set; }

        /// <summary>Projects a decoded artifact into the render form.</summary>
        public static LevelView From(PhysicsArtifact artifact, string artifactHash, string topologyFingerprint)
        {
            var bodies = new List<BodyView>(artifact.Bodies.Count);

            for (int i = 0; i < artifact.Bodies.Count; i++)
            {
                PhysicsBodyRecord record = artifact.Bodies[i];
                var shapes = new List<ShapeView>(record.Shapes.Count);

                for (int j = 0; j < record.Shapes.Count; j++)
                {
                    shapes.Add(Project(record.Shapes[j]));
                }

                bodies.Add(new BodyView
                {
                    SourceId = record.SourceId,
                    Position = Vector(record.Position),
                    Orientation = Quaternion(record.Orientation),
                    Friction = record.Friction,
                    Restitution = record.Restitution,
                    Shapes = shapes,
                });
            }

            return new LevelView
            {
                LevelId = artifact.LevelId,
                ArtifactHash = artifactHash,
                TopologyFingerprint = topologyFingerprint,
                TickRate = artifact.WorldSettings.TickRate,
                Gravity = Vector(artifact.WorldSettings.Gravity),
                Bodies = bodies,
            };
        }

        private static ShapeView Project(PhysicsShapeRecord record)
        {
            var view = new ShapeView
            {
                Key = record.ShapeKey,
                Type = Name(record.ShapeType),
                LocalPosition = Vector(record.LocalPosition),
                LocalRotation = Quaternion(record.LocalRotation),
                Size = Vector(record.Size),
                Radius = record.Radius,
                Length = record.Length,
            };

            if (record.ShapeType != PhysicsShapeType.Mesh)
            {
                return view;
            }

            // Flattened into one array: a mesh of a few hundred triangles becomes an order of
            // magnitude more JSON when every vertex is its own object, and the browser feeds
            // a flat array straight into a buffer attribute anyway.
            var vertices = new float[record.Vertices.Length * 3];
            for (int i = 0; i < record.Vertices.Length; i++)
            {
                vertices[(i * 3) + 0] = record.Vertices[i].X;
                vertices[(i * 3) + 1] = record.Vertices[i].Y;
                vertices[(i * 3) + 2] = record.Vertices[i].Z;
            }

            view.Vertices = vertices;
            view.Indices = record.Indices;
            return view;
        }

        private static string Name(PhysicsShapeType type)
        {
            switch (type)
            {
                case PhysicsShapeType.Box:
                    return "box";
                case PhysicsShapeType.Sphere:
                    return "sphere";
                case PhysicsShapeType.Capsule:
                    return "capsule";
                case PhysicsShapeType.Mesh:
                    return "mesh";
                default:
                    return "unknown";
            }
        }

        private static float[] Vector(PhysicsVector3 value)
        {
            return new[] { value.X, value.Y, value.Z };
        }

        private static float[] Quaternion(PhysicsQuaternion value)
        {
            return new[] { value.X, value.Y, value.Z, value.W };
        }
    }
}

