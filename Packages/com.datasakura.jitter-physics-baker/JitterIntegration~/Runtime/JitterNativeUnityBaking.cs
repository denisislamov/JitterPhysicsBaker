#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.JitterNative.Codec;
using Jitter2.LinearMath;
using UnityEngine;
using NativeArtifact = DataSakura.JitterPhysics.JitterNative.PhysicsArtifact;
using NativeBody = DataSakura.JitterPhysics.JitterNative.PhysicsBodyRecord;
using NativeCanonicalization = DataSakura.JitterPhysics.JitterNative.PhysicsCanonicalization;
using NativeSettings = DataSakura.JitterPhysics.JitterNative.PhysicsWorldSettings;
using NativeShape = DataSakura.JitterPhysics.JitterNative.PhysicsShapeRecord;
#if !DATASAKURA_SERVER_GLOBAL_REAL
using Real = System.Single;
#endif

namespace DataSakura.JitterPhysics.JitterNative.UnityBoundary
{
    /// <summary>The only supported conversion boundary from Unity math to Jitter math.</summary>
    public static class UnityJitterMathAdapter
    {
        /// <summary>
        /// Converts one Unity vector component-for-component. Both engines use a right-handed
        /// mathematical cross product and the package preserves Unity world axes without swapping.
        /// </summary>
        public static JVector ToJVector(Vector3 value)
        {
            if (!IsFinite(value)) throw new ArgumentException("Unity vector is not finite.", nameof(value));
            return new JVector(
                NativeCanonicalization.CanonicalReal(value.x),
                NativeCanonicalization.CanonicalReal(value.y),
                NativeCanonicalization.CanonicalReal(value.z));
        }

        /// <summary>Converts and canonicalizes a Unity quaternion in X,Y,Z,W order.</summary>
        public static JQuaternion ToJQuaternion(Quaternion value)
        {
            return NativeCanonicalization.CanonicalQuaternion(
                new JQuaternion(value.x, value.y, value.z, value.w));
        }

        /// <summary>
        /// Computes a collider pose relative to its body root. Scale is deliberately excluded;
        /// primitive dimensions consume absolute lossy scale and mesh vertices consume the full
        /// local-to-world-to-body matrix exactly once.
        /// </summary>
        public static void GetLocalPose(
            Transform bodyRoot,
            Transform colliderTransform,
            Vector3 center,
            out JVector position,
            out JQuaternion rotation,
            Quaternion? axisCorrection = null)
        {
            Vector3 worldCenter = colliderTransform.TransformPoint(center);
            Quaternion inverseBody = Quaternion.Inverse(bodyRoot.rotation);
            Vector3 localPosition = inverseBody * (worldCenter - bodyRoot.position);
            Quaternion localRotation = inverseBody * colliderTransform.rotation;
            if (axisCorrection.HasValue) localRotation *= axisCorrection.Value;

            position = ToJVector(localPosition);
            rotation = ToJQuaternion(localRotation);
        }

        /// <summary>Transforms one mesh vertex into body-local space exactly once.</summary>
        public static JVector TransformPoint(Matrix4x4 toBodyLocal, Vector3 vertex)
        {
            return ToJVector(toBodyLocal.MultiplyPoint3x4(vertex));
        }

        /// <summary>Whether all Unity vector components are finite.</summary>
        public static bool IsFinite(Vector3 value)
        {
            return StableMath.IsFinite(value.x)
                && StableMath.IsFinite(value.y)
                && StableMath.IsFinite(value.z);
        }
    }

    /// <summary>Why a Unity collider could not become a Jitter-native shape.</summary>
    public enum NativeUnityConversionStatus
    {
        /// <summary>A complete native shape was produced.</summary>
        Converted = 0,
        /// <summary>The collider kind is outside schema one.</summary>
        UnsupportedType,
        /// <summary>The collider is a gameplay trigger.</summary>
        Trigger,
        /// <summary>A transform, dimension, or vertex is NaN or infinity.</summary>
        NotFinite,
        /// <summary>A primitive has no usable volume.</summary>
        DegenerateShape,
        /// <summary>A mesh is absent or cannot be read.</summary>
        UnreadableMesh,
        /// <summary>A mesh has no complete triangle data.</summary>
        InvalidMesh,
    }

    /// <summary>Result of one Unity-to-Jitter collider conversion.</summary>
    public readonly struct NativeUnityConversionResult
    {
        private NativeUnityConversionResult(
            NativeUnityConversionStatus status,
            NativeShape shape,
            string message,
            string warning)
        {
            Status = status;
            Shape = shape;
            Message = message;
            Warning = warning;
        }

        /// <summary>Conversion status.</summary>
        public NativeUnityConversionStatus Status { get; }
        /// <summary>Native shape, or null on failure.</summary>
        public NativeShape Shape { get; }
        /// <summary>Failure explanation.</summary>
        public string Message { get; }
        /// <summary>Non-fatal approximation explanation.</summary>
        public string Warning { get; }
        /// <summary>Whether a native shape was produced.</summary>
        public bool Succeeded => Status == NativeUnityConversionStatus.Converted;

        internal static NativeUnityConversionResult Success(NativeShape shape, string warning = null) =>
            new NativeUnityConversionResult(NativeUnityConversionStatus.Converted, shape, null, warning);

        internal static NativeUnityConversionResult Failure(
            NativeUnityConversionStatus status,
            string message) => new NativeUnityConversionResult(status, null, message, null);
    }

    /// <summary>Converts supported Unity colliders into authoritative Jitter-native records.</summary>
    public static class JitterNativeColliderConverter
    {
        /// <summary>Smallest supported primitive extent.</summary>
        public const Real MinimumExtent = (Real)1e-5f;

        /// <summary>Converts one collider in the local space of its static-body root.</summary>
        public static NativeUnityConversionResult Convert(
            Transform bodyRoot,
            Collider collider,
            string shapeKey)
        {
            if (bodyRoot == null) throw new ArgumentNullException(nameof(bodyRoot));
            if (collider == null) throw new ArgumentNullException(nameof(collider));
            if (collider.isTrigger)
            {
                return NativeUnityConversionResult.Failure(
                    NativeUnityConversionStatus.Trigger,
                    "Triggers describe gameplay volumes, not collision geometry.");
            }

            Vector3 scale = collider.transform.lossyScale;
            if (!UnityJitterMathAdapter.IsFinite(scale)
                || !UnityJitterMathAdapter.IsFinite(collider.transform.position))
            {
                return NativeUnityConversionResult.Failure(
                    NativeUnityConversionStatus.NotFinite,
                    "The transform contains NaN or infinity.");
            }

            var absoluteScale = new Vector3(
                Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            switch (collider)
            {
                case BoxCollider box: return ConvertBox(bodyRoot, box, absoluteScale, shapeKey);
                case SphereCollider sphere: return ConvertSphere(bodyRoot, sphere, absoluteScale, shapeKey);
                case CapsuleCollider capsule: return ConvertCapsule(bodyRoot, capsule, absoluteScale, shapeKey);
                case MeshCollider mesh: return ConvertMesh(bodyRoot, mesh, shapeKey);
                default:
                    return NativeUnityConversionResult.Failure(
                        NativeUnityConversionStatus.UnsupportedType,
                        collider.GetType().Name + " is not supported by artifact schema 1.");
            }
        }

        private static NativeUnityConversionResult ConvertBox(
            Transform root, BoxCollider box, Vector3 scale, string key)
        {
            Vector3 size = Vector3.Scale(box.size, scale);
            size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));
            if (size.x < MinimumExtent || size.y < MinimumExtent || size.z < MinimumExtent)
            {
                return NativeUnityConversionResult.Failure(
                    NativeUnityConversionStatus.DegenerateShape, "The scaled box has a zero extent.");
            }

            UnityJitterMathAdapter.GetLocalPose(
                root, box.transform, box.center, out JVector position, out JQuaternion rotation);
            return NativeUnityConversionResult.Success(
                NativeShape.Box(key, position, rotation, UnityJitterMathAdapter.ToJVector(size)));
        }

        private static NativeUnityConversionResult ConvertSphere(
            Transform root, SphereCollider sphere, Vector3 scale, string key)
        {
            Real radius = sphere.radius * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
            if (radius < MinimumExtent)
            {
                return NativeUnityConversionResult.Failure(
                    NativeUnityConversionStatus.DegenerateShape, "The scaled sphere has a zero radius.");
            }

            UnityJitterMathAdapter.GetLocalPose(
                root, sphere.transform, sphere.center, out JVector position, out JQuaternion rotation);
            string warning = IsUniform(scale)
                ? null
                : "Non-uniform sphere scale was conservatively represented by its largest axis.";
            return NativeUnityConversionResult.Success(
                NativeShape.Sphere(key, position, rotation, radius), warning);
        }

        private static NativeUnityConversionResult ConvertCapsule(
            Transform root, CapsuleCollider capsule, Vector3 scale, string key)
        {
            Real heightScale;
            Real radiusScale;
            Quaternion correction;
            switch (capsule.direction)
            {
                case 0:
                    heightScale = scale.x;
                    radiusScale = Mathf.Max(scale.y, scale.z);
                    correction = Quaternion.Euler(0f, 0f, -90f);
                    break;
                case 2:
                    heightScale = scale.z;
                    radiusScale = Mathf.Max(scale.x, scale.y);
                    correction = Quaternion.Euler(90f, 0f, 0f);
                    break;
                default:
                    heightScale = scale.y;
                    radiusScale = Mathf.Max(scale.x, scale.z);
                    correction = Quaternion.identity;
                    break;
            }

            Real radius = capsule.radius * radiusScale;
            if (radius < MinimumExtent)
            {
                return NativeUnityConversionResult.Failure(
                    NativeUnityConversionStatus.DegenerateShape, "The scaled capsule has a zero radius.");
            }

            Real length = StableMath.Max((Real)0, capsule.height * heightScale - (Real)2 * radius);
            UnityJitterMathAdapter.GetLocalPose(
                root, capsule.transform, capsule.center,
                out JVector position, out JQuaternion rotation, correction);
            return NativeUnityConversionResult.Success(
                NativeShape.Capsule(key, position, rotation, radius, length));
        }

        private static NativeUnityConversionResult ConvertMesh(
            Transform root, MeshCollider collider, string key)
        {
            Mesh mesh = collider.sharedMesh;
            if (mesh == null || !mesh.isReadable)
            {
                return NativeUnityConversionResult.Failure(
                    NativeUnityConversionStatus.UnreadableMesh,
                    mesh == null ? "The mesh collider has no mesh." : "The mesh is not readable.");
            }

            Vector3[] sourceVertices = mesh.vertices;
            int[] sourceIndices = mesh.triangles;
            if (sourceVertices.Length == 0 || sourceIndices.Length == 0 || sourceIndices.Length % 3 != 0)
            {
                return NativeUnityConversionResult.Failure(
                    NativeUnityConversionStatus.InvalidMesh, "The mesh has no complete triangles.");
            }

            Matrix4x4 toBodyLocal = root.worldToLocalMatrix * collider.transform.localToWorldMatrix;
            var vertices = new JVector[sourceVertices.Length];
            try
            {
                for (int index = 0; index < vertices.Length; index++)
                {
                    vertices[index] = UnityJitterMathAdapter.TransformPoint(toBodyLocal, sourceVertices[index]);
                }
            }
            catch (ArgumentException)
            {
                return NativeUnityConversionResult.Failure(
                    NativeUnityConversionStatus.NotFinite, "A transformed mesh vertex is not finite.");
            }

            var indices = new int[sourceIndices.Length];
            Array.Copy(sourceIndices, indices, indices.Length);
            if (toBodyLocal.determinant < 0f)
            {
                for (int index = 0; index < indices.Length; index += 3)
                {
                    int temporary = indices[index + 1];
                    indices[index + 1] = indices[index + 2];
                    indices[index + 2] = temporary;
                }
            }

            return NativeUnityConversionResult.Success(
                NativeShape.Mesh(key, JVector.Zero, JQuaternion.Identity, vertices, indices));
        }

        private static bool IsUniform(Vector3 scale)
        {
            const Real tolerance = (Real)1e-4f;
            return StableMath.Abs(scale.x - scale.y) <= tolerance
                && StableMath.Abs(scale.y - scale.z) <= tolerance;
        }
    }

    /// <summary>Exact artifact-geometry comparison after the single Unity boundary conversion.</summary>
    public static class JitterNativeGeometryComparer
    {
        /// <summary>Whether a Unity transform has the exact canonical pose of a native body.</summary>
        public static bool BodyPoseMatches(NativeBody baked, Transform current)
        {
            if (baked == null || current == null) return false;
            JVector position = UnityJitterMathAdapter.ToJVector(current.position);
            JQuaternion orientation = UnityJitterMathAdapter.ToJQuaternion(current.rotation);
            return position.Equals(baked.Position) && orientation.Equals(baked.Orientation);
        }

        /// <summary>Whether two native shapes have identical schema-one geometry.</summary>
        public static bool ShapesMatch(NativeShape baked, NativeShape current)
        {
            if (baked == null || current == null
                || !string.Equals(baked.ShapeKey, current.ShapeKey, StringComparison.Ordinal)
                || baked.ShapeType != current.ShapeType
                || !baked.LocalPosition.Equals(current.LocalPosition)
                || !baked.LocalRotation.Equals(current.LocalRotation)
                || !baked.Size.Equals(current.Size)
                || !baked.Radius.Equals(current.Radius)
                || !baked.Length.Equals(current.Length)
                || baked.Vertices.Length != current.Vertices.Length
                || baked.Indices.Length != current.Indices.Length)
            {
                return false;
            }

            for (int index = 0; index < baked.Vertices.Length; index++)
            {
                if (!baked.Vertices[index].Equals(current.Vertices[index])) return false;
            }

            for (int index = 0; index < baked.Indices.Length; index++)
            {
                if (baked.Indices[index] != current.Indices[index]) return false;
            }

            return true;
        }
    }

    /// <summary>One native bake diagnostic with its Unity context.</summary>
    public sealed class NativeUnityBakeIssue
    {
        /// <summary>Creates a diagnostic.</summary>
        public NativeUnityBakeIssue(string message, UnityEngine.Object context)
        {
            Message = message ?? string.Empty;
            Context = context;
        }

        /// <summary>Human-readable failure.</summary>
        public string Message { get; }
        /// <summary>Object the author should inspect.</summary>
        public UnityEngine.Object Context { get; }
    }

    /// <summary>All-or-nothing native build result.</summary>
    public sealed class NativeUnityArtifactBuildResult
    {
        /// <summary>Creates a native build result.</summary>
        public NativeUnityArtifactBuildResult(
            NativeArtifact artifact,
            IReadOnlyList<NativeUnityBakeIssue> errors,
            IReadOnlyList<string> warnings)
        {
            Artifact = artifact;
            Errors = errors ?? throw new ArgumentNullException(nameof(errors));
            Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
        }

        /// <summary>Native graph, or null if any input failed.</summary>
        public NativeArtifact Artifact { get; }
        /// <summary>Fatal authoring diagnostics.</summary>
        public IReadOnlyList<NativeUnityBakeIssue> Errors { get; }
        /// <summary>Non-fatal approximation diagnostics.</summary>
        public IReadOnlyList<string> Warnings { get; }
        /// <summary>Whether the complete native graph was produced.</summary>
        public bool Succeeded => Artifact != null && Errors.Count == 0;
    }

    /// <summary>Builds the authoritative Jitter-native artifact graph from Unity authoring.</summary>
    public static class JitterNativeUnityArtifactBuilder
    {
        /// <summary>Collects, converts, orders and validates a level without writing files.</summary>
        public static NativeUnityArtifactBuildResult Build(
            JitterPhysicsLevel level,
            string runtimeCompatibilityId,
            string managedLevelId = null)
        {
            var errors = new List<NativeUnityBakeIssue>();
            var warnings = new List<string>();
            if (level == null)
            {
                errors.Add(new NativeUnityBakeIssue("No JitterPhysicsLevel was supplied.", null));
                return Failure(errors, warnings);
            }

            string levelId = managedLevelId ?? level.EnsureLevelId();
            if (runtimeCompatibilityId == null || runtimeCompatibilityId.Length != 64
                || !JitterPhysicsIdUtility.IsCanonical(levelId) || level.WorldProfile == null)
            {
                errors.Add(new NativeUnityBakeIssue(
                    "Runtime identity, level identity, or world profile is invalid.", level));
                return Failure(errors, warnings);
            }

            IReadOnlyList<JitterStaticBodySource> sources = level.CollectSources();
            var bodies = new List<NativeBody>(sources.Count);
            var sourceIds = new Dictionary<string, JitterStaticBodySource>(StringComparer.Ordinal);
            for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
            {
                JitterStaticBodySource source = sources[sourceIndex];
                string sourceId = source.EnsureSourceId();
                if (!JitterPhysicsIdUtility.IsCanonical(sourceId))
                {
                    errors.Add(new NativeUnityBakeIssue("Source id is invalid: " + sourceId, source));
                    continue;
                }

                if (sourceIds.TryGetValue(sourceId, out JitterStaticBodySource previous))
                {
                    errors.Add(new NativeUnityBakeIssue(
                        "Duplicate Source Id '" + sourceId + "': '" + source.name + "' and '"
                        + previous.name + "' both use it. Duplicating a GameObject copies its Source Id. "
                        + "Set Jitter Static Body Source > Source Id to a unique value before baking.",
                        source));
                    continue;
                }

                sourceIds.Add(sourceId, source);

                NativeBody body = BuildBody(source, sourceId, errors, warnings);
                if (body != null) bodies.Add(body);
            }

            bodies.Sort((left, right) => string.CompareOrdinal(left.SourceId, right.SourceId));
            if (errors.Count != 0 || bodies.Count == 0)
            {
                if (bodies.Count == 0) errors.Add(new NativeUnityBakeIssue("The level has no native bodies.", level));
                return Failure(errors, warnings);
            }

            JitterPhysicsWorldProfile profile = level.WorldProfile;
            var settings = new NativeSettings(
                UnityJitterMathAdapter.ToJVector(profile.Gravity),
                profile.TickRate,
                profile.SubstepCount,
                profile.SolverIterations,
                profile.RelaxationIterations,
                profile.AllowDeactivation);
            var artifact = new NativeArtifact(
                JitterPhysicsPackage.ArtifactSchemaVersion,
                runtimeCompatibilityId,
                levelId,
                settings,
                bodies);
            PhysicsArtifactError validation = PhysicsArtifactCodec.Validate(artifact);
            if (validation.IsError)
            {
                errors.Add(new NativeUnityBakeIssue("Native artifact is not canonical: " + validation, level));
                return Failure(errors, warnings);
            }

            return new NativeUnityArtifactBuildResult(artifact, errors, warnings);
        }

        private static NativeBody BuildBody(
            JitterStaticBodySource source,
            string sourceId,
            List<NativeUnityBakeIssue> errors,
            List<string> warnings)
        {
            Transform root = source.transform;
            var colliders = new List<Collider>();
            if (source.IncludeChildren)
            {
                root.GetComponentsInChildren(JitterStaticBodySource.IncludeInactiveChildren, colliders);
            }
            else
            {
                colliders.AddRange(root.GetComponents<Collider>());
            }

            var shapes = new List<NativeShape>(colliders.Count);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < colliders.Count; index++)
            {
                Collider collider = colliders[index];
                if (!collider.enabled || !collider.gameObject.activeInHierarchy) continue;
                string key = JitterPhysicsStableColliderKey.Build(root, collider);
                if (!keys.Add(key))
                {
                    errors.Add(new NativeUnityBakeIssue("Collider key is duplicated: " + key, collider));
                    continue;
                }

                NativeUnityConversionResult converted = JitterNativeColliderConverter.Convert(root, collider, key);
                if (!converted.Succeeded)
                {
                    errors.Add(new NativeUnityBakeIssue(converted.Message, collider));
                    continue;
                }

                if (!string.IsNullOrEmpty(converted.Warning)) warnings.Add(converted.Warning);
                shapes.Add(converted.Shape);
            }

            if (shapes.Count == 0)
            {
                errors.Add(new NativeUnityBakeIssue("The source has no convertible colliders.", source));
                return null;
            }

            shapes.Sort((left, right) => string.CompareOrdinal(left.ShapeKey, right.ShapeKey));
            return new NativeBody(
                sourceId,
                UnityJitterMathAdapter.ToJVector(root.position),
                UnityJitterMathAdapter.ToJQuaternion(root.rotation),
                source.Friction,
                source.Restitution,
                shapes);
        }

        private static NativeUnityArtifactBuildResult Failure(
            IReadOnlyList<NativeUnityBakeIssue> errors,
            IReadOnlyList<string> warnings) => new NativeUnityArtifactBuildResult(null, errors, warnings);
    }
}
#endif
