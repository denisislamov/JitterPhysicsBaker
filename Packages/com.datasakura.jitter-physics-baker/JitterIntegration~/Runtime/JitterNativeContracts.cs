using System;
using System.Collections.Generic;
using System.Globalization;
using DataSakura.JitterPhysics.Contracts;
using Jitter2.LinearMath;
#if !DATASAKURA_SERVER_GLOBAL_REAL
using Real = System.Single;
#endif

namespace DataSakura.JitterPhysics.JitterNative
{
    /// <summary>Canonical Jitter-native world settings stored by an artifact.</summary>
    public sealed class PhysicsWorldSettings
    {
        /// <summary>World gravity.</summary>
        public JVector Gravity { get; }
        /// <summary>Simulation ticks per second.</summary>
        public int TickRate { get; }
        /// <summary>Substeps per tick.</summary>
        public int SubstepCount { get; }
        /// <summary>Solver iterations per substep.</summary>
        public int SolverIterations { get; }
        /// <summary>Relaxation iterations per substep.</summary>
        public int RelaxationIterations { get; }
        /// <summary>Whether bodies may deactivate.</summary>
        public bool AllowDeactivation { get; }
        /// <summary>The only supported solve-mode marker.</summary>
        public const byte DeterministicSolveMode = 1;

        /// <summary>Creates an immutable set of simulation-affecting world settings.</summary>
        public PhysicsWorldSettings(
            JVector gravity,
            int tickRate,
            int substepCount,
            int solverIterations,
            int relaxationIterations,
            bool allowDeactivation)
        {
            Gravity = gravity;
            TickRate = tickRate;
            SubstepCount = substepCount;
            SolverIterations = solverIterations;
            RelaxationIterations = relaxationIterations;
            AllowDeactivation = allowDeactivation;
        }
    }

    /// <summary>One ordered static body expressed entirely in Jitter math types.</summary>
    public sealed class PhysicsBodyRecord
    {
        /// <summary>Stable authoring identity and creation-order key.</summary>
        public string SourceId { get; }
        /// <summary>World position.</summary>
        public JVector Position { get; }
        /// <summary>Canonical normalized world orientation.</summary>
        public JQuaternion Orientation { get; }
        /// <summary>Jitter friction value.</summary>
        public Real Friction { get; }
        /// <summary>Jitter restitution value.</summary>
        public Real Restitution { get; }
        /// <summary>Shapes ordered by key.</summary>
        public IReadOnlyList<PhysicsShapeRecord> Shapes { get; }

        /// <summary>Creates one ordered static-body record.</summary>
        public PhysicsBodyRecord(
            string sourceId,
            JVector position,
            JQuaternion orientation,
            Real friction,
            Real restitution,
            IReadOnlyList<PhysicsShapeRecord> shapes)
        {
            SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
            Position = position;
            Orientation = orientation;
            Friction = friction;
            Restitution = restitution;
            Shapes = shapes ?? throw new ArgumentNullException(nameof(shapes));
        }
    }

    /// <summary>One immutable Jitter-native collision-shape record.</summary>
    public sealed class PhysicsShapeRecord
    {
        /// <summary>Stable diagnostic and ordering key.</summary>
        public string ShapeKey { get; }
        /// <summary>Schema-defined shape kind.</summary>
        public PhysicsShapeType ShapeType { get; }
        /// <summary>Body-local origin.</summary>
        public JVector LocalPosition { get; }
        /// <summary>Body-local canonical rotation.</summary>
        public JQuaternion LocalRotation { get; }
        /// <summary>Full box size.</summary>
        public JVector Size { get; }
        /// <summary>Sphere or capsule radius.</summary>
        public Real Radius { get; }
        /// <summary>Capsule cylinder length.</summary>
        public Real Length { get; }
        /// <summary>Body-local mesh vertices.</summary>
        public JVector[] Vertices { get; }
        /// <summary>Triangle indices.</summary>
        public int[] Indices { get; }
        /// <summary>Number of mesh triangles.</summary>
        public int TriangleCount => Indices.Length / 3;

        private PhysicsShapeRecord(
            string shapeKey,
            PhysicsShapeType shapeType,
            JVector localPosition,
            JQuaternion localRotation,
            JVector size,
            Real radius,
            Real length,
            JVector[] vertices,
            int[] indices)
        {
            ShapeKey = shapeKey ?? throw new ArgumentNullException(nameof(shapeKey));
            ShapeType = shapeType;
            LocalPosition = localPosition;
            LocalRotation = localRotation;
            Size = size;
            Radius = radius;
            Length = length;
            Vertices = vertices ?? Array.Empty<JVector>();
            Indices = indices ?? Array.Empty<int>();
        }

        /// <summary>Creates a box record.</summary>
        public static PhysicsShapeRecord Box(
            string key, JVector position, JQuaternion rotation, JVector size) =>
            new PhysicsShapeRecord(
                key, PhysicsShapeType.Box, position, rotation, size, 0, 0, null, null);

        /// <summary>Creates a sphere record.</summary>
        public static PhysicsShapeRecord Sphere(
            string key, JVector position, JQuaternion rotation, Real radius) =>
            new PhysicsShapeRecord(
                key, PhysicsShapeType.Sphere, position, rotation, JVector.Zero, radius, 0, null, null);

        /// <summary>Creates a capsule record.</summary>
        public static PhysicsShapeRecord Capsule(
            string key, JVector position, JQuaternion rotation, Real radius, Real length) =>
            new PhysicsShapeRecord(
                key, PhysicsShapeType.Capsule, position, rotation, JVector.Zero, radius, length, null, null);

        /// <summary>Creates a mesh record without copying its arrays.</summary>
        public static PhysicsShapeRecord Mesh(
            string key,
            JVector position,
            JQuaternion rotation,
            JVector[] vertices,
            int[] indices) =>
            new PhysicsShapeRecord(
                key,
                PhysicsShapeType.Mesh,
                position,
                rotation,
                JVector.Zero,
                0,
                0,
                vertices ?? throw new ArgumentNullException(nameof(vertices)),
                indices ?? throw new ArgumentNullException(nameof(indices)));
    }

    /// <summary>The authoritative schema record graph using Jitter math and f32 Real.</summary>
    public sealed class PhysicsArtifact
    {
        /// <summary>Binary schema version.</summary>
        public int SchemaVersion { get; }
        /// <summary>Runtime semantic identity.</summary>
        public string RuntimeCompatibilityId { get; }
        /// <summary>Canonical level identity.</summary>
        public string LevelId { get; }
        /// <summary>World-affecting settings.</summary>
        public PhysicsWorldSettings WorldSettings { get; }
        /// <summary>Ordered static bodies.</summary>
        public IReadOnlyList<PhysicsBodyRecord> Bodies { get; }

        /// <summary>Creates one immutable Jitter-native artifact graph.</summary>
        public PhysicsArtifact(
            int schemaVersion,
            string runtimeCompatibilityId,
            string levelId,
            PhysicsWorldSettings worldSettings,
            IReadOnlyList<PhysicsBodyRecord> bodies)
        {
            SchemaVersion = schemaVersion;
            RuntimeCompatibilityId = runtimeCompatibilityId
                ?? throw new ArgumentNullException(nameof(runtimeCompatibilityId));
            LevelId = levelId ?? throw new ArgumentNullException(nameof(levelId));
            WorldSettings = worldSettings ?? throw new ArgumentNullException(nameof(worldSettings));
            Bodies = bodies ?? throw new ArgumentNullException(nameof(bodies));
        }

        /// <summary>Total record shape count.</summary>
        public int ShapeCount => Count(shape => 1);
        /// <summary>Total mesh vertex count.</summary>
        public int VertexCount => Count(shape => shape.Vertices.Length);
        /// <summary>Total mesh triangle count.</summary>
        public int TriangleCount => Count(shape => shape.TriangleCount);

        private int Count(Func<PhysicsShapeRecord, int> selector)
        {
            int count = 0;
            for (int body = 0; body < Bodies.Count; body++)
            {
                for (int shape = 0; shape < Bodies[body].Shapes.Count; shape++)
                {
                    count += selector(Bodies[body].Shapes[shape]);
                }
            }

            return count;
        }
    }

    /// <summary>Stable f32 canonicalization for Jitter-native values.</summary>
    public static class PhysicsCanonicalization
    {
        /// <summary>Accepted squared-length distance from one for stored quaternions.</summary>
        public const Real QuaternionLengthTolerance = (Real)1e-4f;

        /// <summary>Canonicalizes signed zero and rejects no other finite value.</summary>
        public static Real CanonicalReal(Real value) => value == (Real)0 ? (Real)0 : value;

        /// <summary>Whether all vector components are finite.</summary>
        public static bool IsFinite(in JVector value) =>
            StableMath.IsFinite(value.X) && StableMath.IsFinite(value.Y) && StableMath.IsFinite(value.Z);

        /// <summary>Whether all quaternion components are finite.</summary>
        public static bool IsFinite(in JQuaternion value) =>
            StableMath.IsFinite(value.X) && StableMath.IsFinite(value.Y)
            && StableMath.IsFinite(value.Z) && StableMath.IsFinite(value.W);

        /// <summary>Normalizes a quaternion and chooses one deterministic sign.</summary>
        public static JQuaternion CanonicalQuaternion(JQuaternion value)
        {
            if (!IsFinite(value))
            {
                throw new ArgumentException("Quaternion contains NaN or infinity.", nameof(value));
            }

            Real lengthSquared = value.X * value.X + value.Y * value.Y
                + value.Z * value.Z + value.W * value.W;
            if (!StableMath.IsFinite(lengthSquared) || lengthSquared <= (Real)0)
            {
                throw new ArgumentException("Quaternion has invalid length.", nameof(value));
            }

            Real inverseLength = (Real)1 / StableMath.Sqrt(lengthSquared);
            Real x = value.X * inverseLength;
            Real y = value.Y * inverseLength;
            Real z = value.Z * inverseLength;
            Real w = value.W * inverseLength;
            if (ShouldNegate(w, x, y, z))
            {
                x = -x;
                y = -y;
                z = -z;
                w = -w;
            }

            return new JQuaternion(
                CanonicalReal(x), CanonicalReal(y), CanonicalReal(z), CanonicalReal(w));
        }

        /// <summary>Whether a quaternion is finite, normalized, sign-canonical and contains no -0.</summary>
        public static bool IsCanonicalQuaternion(in JQuaternion value)
        {
            if (!IsFinite(value)) return false;
            Real lengthSquared = value.X * value.X + value.Y * value.Y
                + value.Z * value.Z + value.W * value.W;
            return StableMath.Abs(lengthSquared - (Real)1) <= QuaternionLengthTolerance
                && !ShouldNegate(value.W, value.X, value.Y, value.Z)
                && !IsNegativeZero(value.X) && !IsNegativeZero(value.Y)
                && !IsNegativeZero(value.Z) && !IsNegativeZero(value.W);
        }

        /// <summary>Whether a scalar has the IEEE negative-zero representation.</summary>
        public static bool IsNegativeZero(Real value) =>
            value == (Real)0 && BitConverter.SingleToInt32Bits(value) < 0;

        /// <summary>Invariant round-trip formatting for diagnostics only.</summary>
        public static string Format(Real value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static bool ShouldNegate(Real w, Real x, Real y, Real z)
        {
            if (w != (Real)0) return w < (Real)0;
            if (x != (Real)0) return x < (Real)0;
            if (y != (Real)0) return y < (Real)0;
            return z < (Real)0;
        }
    }
}
