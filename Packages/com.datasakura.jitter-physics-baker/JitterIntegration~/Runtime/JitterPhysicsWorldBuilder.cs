using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;
using Jitter2;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;
using NativeArtifact = DataSakura.JitterPhysics.JitterNative.PhysicsArtifact;
using NativeBody = DataSakura.JitterPhysics.JitterNative.PhysicsBodyRecord;
using NativeCanonicalization = DataSakura.JitterPhysics.JitterNative.PhysicsCanonicalization;
using NativeCodec = DataSakura.JitterPhysics.JitterNative.Codec.PhysicsArtifactCodec;
using NativeSettings = DataSakura.JitterPhysics.JitterNative.PhysicsWorldSettings;
using NativeShape = DataSakura.JitterPhysics.JitterNative.PhysicsShapeRecord;
#if !DATASAKURA_SERVER_GLOBAL_REAL
using Real = System.Single;
#endif

namespace DataSakura.JitterPhysics.Integration
{
    internal enum JitterPhysicsWorldBuildFailurePoint
    {
        None = 0,
        AfterFirstBody,
        ForceIncompleteRollback,
    }

    /// <summary>Outcome of applying an artifact to a world.</summary>
    public sealed class PhysicsWorldBuildResult
    {
        /// <summary>Failure description; only meaningful when <see cref="Succeeded"/> is false.</summary>
        public PhysicsArtifactError Error { get; }

        /// <summary>Number of static bodies created.</summary>
        public int BodyCount { get; }

        /// <summary>Number of collision shapes created, counting one per mesh triangle.</summary>
        public int ShapeCount { get; }

        /// <summary>Milliseconds spent building the world.</summary>
        public double ElapsedMilliseconds { get; }

        /// <summary>
        /// Whether cleanup could not prove restoration of the input world. The caller must
        /// dispose this world and create a new one before continuing.
        /// </summary>
        public bool RequiresWorldDiscard { get; }

        /// <summary>
        /// Hash over the created topology in creation order. Two runtimes that build the same
        /// world from the same artifact produce the same value; it is the practical way to
        /// prove the client and the server agree about static geometry, which is a stronger
        /// statement than "both loaded the same file".
        /// </summary>
        public string TopologyFingerprint { get; }

        internal PhysicsWorldBuildResult(
            PhysicsArtifactError error,
            int bodyCount,
            int shapeCount,
            double elapsedMilliseconds,
            string topologyFingerprint,
            bool requiresWorldDiscard = false)
        {
            Error = error;
            BodyCount = bodyCount;
            ShapeCount = shapeCount;
            ElapsedMilliseconds = elapsedMilliseconds;
            TopologyFingerprint = topologyFingerprint;
            RequiresWorldDiscard = requiresWorldDiscard;
        }

        /// <summary>True when the world was built.</summary>
        public bool Succeeded => !Error.IsError;
    }

    /// <summary>
    /// Rebuilds the static half of a Jitter world from a baked artifact.
    /// <para>
    /// This is the one loader. The Unity client and the dedicated server both call it, which
    /// is the entire point: two implementations of "turn these records into shapes" would
    /// drift, and the drift would appear as a player walking through a wall on one side only.
    /// </para>
    /// <para>
    /// The builder creates bodies through Jitter's public API and never restores engine
    /// internals. It also does not own the simulation: <c>World.Step</c> stays with the
    /// consumer, because the tick loop belongs to the game, not to the level format.
    /// </para>
    /// </summary>
    public static class JitterPhysicsWorldBuilder
    {
        /// <summary>
        /// Worlds that already carry a static artifact. Applying a second one would silently
        /// double the level's geometry, so it is refused rather than merged.
        /// </summary>
        private static readonly ConditionalWeakTable<World, AppliedArtifact> Applied =
            new ConditionalWeakTable<World, AppliedArtifact>();

        private sealed class AppliedArtifact
        {
            internal AppliedArtifact(string levelId)
            {
                LevelId = levelId;
            }

            internal string LevelId { get; }
        }

        /// <summary>
        /// Applies world settings and static geometry to <paramref name="world"/>.
        /// <para>
        /// On failure nothing is left behind: every body created during the attempt is
        /// removed again. A partially built level is worse than none, because it looks like
        /// it worked.
        /// </para>
        /// </summary>
        public static PhysicsWorldBuildResult Apply(World world, NativeArtifact artifact)
        {
            return ApplyCore(world, artifact, JitterPhysicsWorldBuildFailurePoint.None);
        }

        internal static PhysicsWorldBuildResult ApplyWithFailureForTests(
            World world,
            NativeArtifact artifact,
            JitterPhysicsWorldBuildFailurePoint failurePoint)
        {
            return ApplyCore(world, artifact, failurePoint);
        }

        private static PhysicsWorldBuildResult ApplyCore(
            World world,
            NativeArtifact artifact,
            JitterPhysicsWorldBuildFailurePoint failurePoint)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (artifact == null)
            {
                throw new ArgumentNullException(nameof(artifact));
            }

            JitterRuntimeProfileResult profile = JitterRuntimeProfile.VerifyCanonicalF32();
            if (!profile.Succeeded)
            {
                return new PhysicsWorldBuildResult(profile.Error, 0, 0, 0d, null);
            }

            if (Applied.TryGetValue(world, out AppliedArtifact existing))
            {
                return Failure(
                    PhysicsArtifactErrorCode.InvalidValue,
                    $"This world already has the static artifact of level '{existing.LevelId}' applied. "
                    + "A new artifact needs a new world; hot reloading a running match world is not supported.",
                    artifact);
            }

            PhysicsArtifactError validationError = NativeCodec.Validate(artifact);
            if (validationError.IsError)
            {
                return new PhysicsWorldBuildResult(validationError, 0, 0, 0d, null);
            }

            var stopwatch = Stopwatch.StartNew();
            var created = new List<RigidBody>(artifact.Bodies.Count);
            var fingerprint = new FingerprintBuilder();
            int shapeCount = 0;
            JVector previousGravity = world.Gravity;
            SolveMode previousSolveMode = world.SolveMode;
            (int solver, int relaxation) previousIterations = world.SolverIterations;
            bool previousAllowDeactivation = world.AllowDeactivation;

            try
            {
                ApplyWorldSettings(world, artifact.WorldSettings);

                for (int i = 0; i < artifact.Bodies.Count; i++)
                {
                    NativeBody record = artifact.Bodies[i];
                    RigidBody body = world.CreateRigidBody();
                    created.Add(body);

                    shapeCount += AddShapes(body, record, fingerprint);

                    body.Position = record.Position;
                    body.Orientation = record.Orientation;
                    body.Friction = record.Friction;
                    body.Restitution = record.Restitution;

                    // Set last: switching to static zeroes velocities and deactivates the
                    // body, and Jitter expects the pose to be in place by then.
                    body.MotionType = MotionType.Static;

                    fingerprint.Body(record);

                    if (i == 0 && failurePoint != JitterPhysicsWorldBuildFailurePoint.None)
                    {
                        throw new InvalidOperationException("Injected world-build failure.");
                    }
                }
            }
            catch (Exception exception)
            {
                bool restored = Rollback(world, created);
                try
                {
                    world.Gravity = previousGravity;
                    world.SolveMode = previousSolveMode;
                    world.SolverIterations = previousIterations;
                    world.AllowDeactivation = previousAllowDeactivation;
                }
                catch (Exception)
                {
                    restored = false;
                }

                if (failurePoint == JitterPhysicsWorldBuildFailurePoint.ForceIncompleteRollback)
                {
                    // The production path never fabricates a cleanup result. This hook makes
                    // the caller contract executable without corrupting Jitter internals.
                    restored = false;
                }

                string cleanup = restored
                    ? "The attempted bodies and settings were rolled back."
                    : "Rollback was incomplete; discard this World and create a new one.";
                return Failure(
                    PhysicsArtifactErrorCode.InvalidValue,
                    "Building the world failed. " + cleanup + " Cause: " + exception.Message,
                    artifact,
                    requiresWorldDiscard: !restored);
            }

            stopwatch.Stop();
            Applied.Add(world, new AppliedArtifact(artifact.LevelId));

            return new PhysicsWorldBuildResult(
                default,
                created.Count,
                shapeCount,
                stopwatch.Elapsed.TotalMilliseconds,
                fingerprint.Build());
        }

        /// <summary>
        /// True when a static artifact has already been applied to <paramref name="world"/>.
        /// </summary>
        public static bool HasArtifact(World world)
        {
            return world != null && Applied.TryGetValue(world, out _);
        }

        private static void ApplyWorldSettings(World world, NativeSettings settings)
        {
            world.Gravity = settings.Gravity;

            // Both are invariants of prediction rather than preferences: a client that solves
            // differently from the server diverges in a way no reconciliation can explain.
            world.SolveMode = SolveMode.Deterministic;
            world.SolverIterations = (settings.SolverIterations, settings.RelaxationIterations);
            world.AllowDeactivation = settings.AllowDeactivation;
        }

        private static int AddShapes(
            RigidBody body,
            NativeBody record,
            FingerprintBuilder fingerprint)
        {
            int count = 0;

            for (int i = 0; i < record.Shapes.Count; i++)
            {
                NativeShape shape = record.Shapes[i];
                fingerprint.Shape(shape);

                if (shape.ShapeType == PhysicsShapeType.Mesh)
                {
                    count += AddMeshShapes(body, shape);
                    continue;
                }

                RigidBodyShape primitive = CreatePrimitive(shape);

                // Static bodies never need a mass tensor, and computing one for a large
                // level is pure cost, so the existing values are preserved instead.
                body.AddShape(Transform(primitive, shape), MassInertiaUpdateMode.Preserve);
                count++;
            }

            return count;
        }

        private static RigidBodyShape CreatePrimitive(NativeShape shape)
        {
            switch (shape.ShapeType)
            {
                case PhysicsShapeType.Box:
                    return new BoxShape(shape.Size);

                case PhysicsShapeType.Sphere:
                    return new SphereShape(shape.Radius);

                case PhysicsShapeType.Capsule:
                    return new CapsuleShape(shape.Radius, shape.Length);

                default:
                    throw new InvalidOperationException(
                        $"Shape '{shape.ShapeKey}' has unsupported type {shape.ShapeType}.");
            }
        }

        private static RigidBodyShape Transform(RigidBodyShape shape, NativeShape record)
        {
            bool hasTranslation = record.LocalPosition.X != 0f
                || record.LocalPosition.Y != 0f
                || record.LocalPosition.Z != 0f;
            bool hasRotation = record.LocalRotation.X != 0f
                || record.LocalRotation.Y != 0f
                || record.LocalRotation.Z != 0f
                || record.LocalRotation.W != 1f;

            if (!hasTranslation && !hasRotation)
            {
                // An identity pose is the common case; wrapping it would add an indirection
                // to every collision query for nothing.
                return shape;
            }

            JMatrix rotation = JMatrix.CreateFromQuaternion(record.LocalRotation);
            return new TransformedShape(shape, record.LocalPosition, rotation);
        }

        private static int AddMeshShapes(RigidBody body, NativeShape record)
        {
            // Vertices are already expressed in the body's local space by the baker, so no
            // transform is applied here: the artifact is the single description of where the
            // geometry is.
            var mesh = new TriangleMesh(record.Vertices, record.Indices);

            var shapes = new List<RigidBodyShape>(mesh.Indices.Length);
            for (int i = 0; i < mesh.Indices.Length; i++)
            {
                shapes.Add(new TriangleShape(mesh, i));
            }

            body.AddShapes(shapes, MassInertiaUpdateMode.Preserve);
            return shapes.Count;
        }

        private static bool Rollback(World world, List<RigidBody> created)
        {
            bool succeeded = true;
            for (int i = created.Count - 1; i >= 0; i--)
            {
                try
                {
                    world.Remove(created[i]);
                }
                catch (Exception)
                {
                    succeeded = false;
                }
            }

            return succeeded;
        }

        private static PhysicsWorldBuildResult Failure(
            PhysicsArtifactErrorCode code,
            string message,
            NativeArtifact artifact,
            bool requiresWorldDiscard = false)
        {
            return new PhysicsWorldBuildResult(
                new PhysicsArtifactError(code, message, artifact.LevelId),
                0, 0, 0d, null, requiresWorldDiscard);
        }

        /// <summary>
        /// Accumulates a deterministic description of what was created, in creation order.
        /// </summary>
        private sealed class FingerprintBuilder
        {
            private readonly System.Text.StringBuilder builder = new System.Text.StringBuilder(1024);

            internal void Body(NativeBody record)
            {
                builder.Append("b:").Append(record.SourceId)
                    .Append('|').Append(Format(record.Position))
                    .Append('|').Append(Format(record.Orientation))
                    .Append('\n');
            }

            internal void Shape(NativeShape record)
            {
                builder.Append("s:").Append(record.ShapeKey)
                    .Append('|').Append((int)record.ShapeType)
                    .Append('|').Append(Format(record.LocalPosition))
                    .Append('|').Append(Format(record.LocalRotation))
                    .Append('|').Append(Format(record.Size))
                    .Append('|').Append(NativeCanonicalization.Format(record.Radius))
                    .Append('|').Append(NativeCanonicalization.Format(record.Length))
                    .Append('|').Append(record.Vertices.Length)
                    .Append('|').Append(record.Indices.Length)
                    .Append('\n');
            }

            internal string Build()
            {
                return JitterPhysicsHash.Sha256HexUtf8(builder.ToString());
            }

            private static string Format(in JVector value)
            {
                return NativeCanonicalization.Format(value.X) + ","
                    + NativeCanonicalization.Format(value.Y) + ","
                    + NativeCanonicalization.Format(value.Z);
            }

            private static string Format(in JQuaternion value)
            {
                return NativeCanonicalization.Format(value.X) + ","
                    + NativeCanonicalization.Format(value.Y) + ","
                    + NativeCanonicalization.Format(value.Z) + ","
                    + NativeCanonicalization.Format(value.W);
            }
        }
    }
}
