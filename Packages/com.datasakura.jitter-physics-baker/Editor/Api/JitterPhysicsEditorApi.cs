using System;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Editor.Baking;
using DataSakura.JitterPhysics.Editor.Bootstrap;
using DataSakura.JitterPhysics.Editor.Export;
using DataSakura.JitterPhysics.UnityArtifact;
using UnityEditor;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor.Api
{
    /// <summary>Who supplies the level identity used by an editor operation.</summary>
    public enum JitterPhysicsLevelIdOwnership
    {
        /// <summary>The standalone Jitter Physics level owns and persists its own id.</summary>
        Standalone = 0,

        /// <summary>An external editor tool supplies the id explicitly for this operation.</summary>
        ExternalManaged,
    }

    /// <summary>Explicit identity binding for validation, bake and summary reads.</summary>
    public sealed class JitterPhysicsLevelIdBinding
    {
        private static readonly JitterPhysicsLevelIdBinding StandaloneValue =
            new JitterPhysicsLevelIdBinding(JitterPhysicsLevelIdOwnership.Standalone, null, null);

        private JitterPhysicsLevelIdBinding(
            JitterPhysicsLevelIdOwnership ownership,
            string levelId,
            string owner)
        {
            Ownership = ownership;
            LevelId = levelId;
            Owner = owner;
        }

        /// <summary>Binding that uses the id stored by <see cref="JitterPhysicsLevel"/>.</summary>
        public static JitterPhysicsLevelIdBinding Standalone => StandaloneValue;

        /// <summary>Creates an explicit external binding without referencing the owner assembly.</summary>
        public static JitterPhysicsLevelIdBinding External(string owner, string levelId)
        {
            return new JitterPhysicsLevelIdBinding(
                JitterPhysicsLevelIdOwnership.ExternalManaged, levelId, owner);
        }

        /// <summary>Identity ownership mode.</summary>
        public JitterPhysicsLevelIdOwnership Ownership { get; }

        /// <summary>Externally supplied canonical id, or <c>null</c> for standalone ownership.</summary>
        public string LevelId { get; }

        /// <summary>Diagnostic owner label such as <c>NPI</c>; never resolved as an assembly type.</summary>
        public string Owner { get; }
    }

    /// <summary>State of an editor API result.</summary>
    public enum JitterPhysicsEditorResultStatus
    {
        /// <summary>No current bake exists at the resolved paths.</summary>
        Missing = 0,

        /// <summary>Validation completed and the level can be baked.</summary>
        Valid,

        /// <summary>A verified current bake exists or was produced.</summary>
        Ready,

        /// <summary>Identity, validation, bake or load failed.</summary>
        Failed,
    }

    /// <summary>Read-only result shared by standalone tools and external editor callers.</summary>
    public sealed class JitterPhysicsEditorResult
    {
        internal JitterPhysicsEditorResult(
            JitterPhysicsEditorResultStatus status,
            JitterPhysicsLevelIdOwnership ownership,
            string owner,
            string levelId,
            string artifactPath,
            string payloadPath,
            string manifestPath,
            string digest,
            int payloadSize,
            int bodyCount,
            int shapeCount,
            int vertexCount,
            int triangleCount,
            JitterPhysicsIssueLog issues)
        {
            Status = status;
            Ownership = ownership;
            Owner = owner ?? string.Empty;
            LevelId = levelId ?? string.Empty;
            ArtifactPath = artifactPath ?? string.Empty;
            PayloadPath = payloadPath ?? string.Empty;
            ManifestPath = manifestPath ?? string.Empty;
            Digest = digest ?? string.Empty;
            PayloadSize = payloadSize;
            BodyCount = bodyCount;
            ShapeCount = shapeCount;
            VertexCount = vertexCount;
            TriangleCount = triangleCount;
            Issues = issues ?? new JitterPhysicsIssueLog();
        }

        /// <summary>Outcome state.</summary>
        public JitterPhysicsEditorResultStatus Status { get; }

        /// <summary>Who supplied <see cref="LevelId"/>.</summary>
        public JitterPhysicsLevelIdOwnership Ownership { get; }

        /// <summary>External owner label, empty for standalone levels.</summary>
        public string Owner { get; }

        /// <summary>Resolved canonical level id.</summary>
        public string LevelId { get; }

        /// <summary>Project-relative ScriptableObject path.</summary>
        public string ArtifactPath { get; }

        /// <summary>Project-relative deterministic binary path.</summary>
        public string PayloadPath { get; }

        /// <summary>Project-relative manifest path.</summary>
        public string ManifestPath { get; }

        /// <summary>Full lowercase SHA-256 of the exact payload, when available.</summary>
        public string Digest { get; }

        /// <summary>Payload size in bytes, or -1 when unavailable.</summary>
        public int PayloadSize { get; }

        /// <summary>Body count, or -1 when unavailable.</summary>
        public int BodyCount { get; }

        /// <summary>Shape count, or -1 when unavailable.</summary>
        public int ShapeCount { get; }

        /// <summary>Vertex count, or -1 when unavailable.</summary>
        public int VertexCount { get; }

        /// <summary>Triangle count, or -1 when unavailable.</summary>
        public int TriangleCount { get; }

        /// <summary>Validation, bake or load findings.</summary>
        public JitterPhysicsIssueLog Issues { get; }

        /// <summary>True for a valid dry run or a verified bake.</summary>
        public bool Succeeded => Status == JitterPhysicsEditorResultStatus.Valid
                                 || Status == JitterPhysicsEditorResultStatus.Ready;

        /// <summary>True when body and shape counts are available.</summary>
        public bool HasCounts => BodyCount >= 0 && ShapeCount >= 0;
    }

    /// <summary>
    /// Minimal editor-only integration API. It owns no runtime loop and references no consumer
    /// assembly; NPI or another tool can call it from its own editor adapter.
    /// </summary>
    public static class JitterPhysicsEditorApi
    {
        /// <summary>Validates without writing files.</summary>
        public static JitterPhysicsEditorResult Validate(
            JitterPhysicsLevel level,
            JitterPhysicsLevelIdBinding binding = null)
        {
            binding ??= JitterPhysicsLevelIdBinding.Standalone;
            string levelId = ResolveLevelId(level, binding, true, out JitterPhysicsIssueLog identityIssues);
            if (identityIssues.HasErrors)
            {
                return Empty(JitterPhysicsEditorResultStatus.Failed, level, binding, levelId, identityIssues);
            }

            JitterPhysicsCompatibilityReport report = JitterPhysicsCompatibilityReport.Create();
            JitterPhysicsBuildResult build = JitterPhysicsArtifactBuilder.Build(
                level,
                report.RuntimeCompatibilityId,
                binding.Ownership == JitterPhysicsLevelIdOwnership.ExternalManaged ? levelId : null);
            if (!report.CanBake)
            {
                build.Issues.Error(JitterPhysicsBakeCommand.DescribeBlockedSetup(report), level);
            }

            if (!build.Succeeded || build.Issues.HasErrors)
            {
                return Empty(JitterPhysicsEditorResultStatus.Failed, level, binding, levelId, build.Issues);
            }

            byte[] bytes = PhysicsArtifactWriter.Write(build.Artifact);
            return FromArtifact(
                JitterPhysicsEditorResultStatus.Valid,
                level,
                binding,
                build.Artifact,
                JitterPhysicsHash.Sha256Hex(bytes),
                bytes.Length,
                build.Issues);
        }

        /// <summary>Runs the separate physics bake and returns its verified delivery summary.</summary>
        public static JitterPhysicsEditorResult Bake(
            JitterPhysicsLevel level,
            JitterPhysicsLevelIdBinding binding = null)
        {
            binding ??= JitterPhysicsLevelIdBinding.Standalone;
            string levelId = ResolveLevelId(level, binding, true, out JitterPhysicsIssueLog identityIssues);
            if (identityIssues.HasErrors)
            {
                return Empty(JitterPhysicsEditorResultStatus.Failed, level, binding, levelId, identityIssues);
            }

            JitterPhysicsCompatibilityReport report = JitterPhysicsCompatibilityReport.Create();
            if (!report.CanBake)
            {
                identityIssues.Error(JitterPhysicsBakeCommand.DescribeBlockedSetup(report), level);
                return Empty(JitterPhysicsEditorResultStatus.Failed, level, binding, levelId, identityIssues);
            }

            JitterPhysicsBakeResult baked = JitterPhysicsBaker.Bake(
                level,
                report.RuntimeCompatibilityId,
                binding.Ownership == JitterPhysicsLevelIdOwnership.ExternalManaged ? levelId : null);
            if (!baked.Succeeded)
            {
                return Empty(JitterPhysicsEditorResultStatus.Failed, level, binding, levelId, baked.Issues);
            }

            JitterPhysicsBakeOutput output = baked.Output;
            PhysicsArtifactManifest manifest = output.Manifest;
            return new JitterPhysicsEditorResult(
                JitterPhysicsEditorResultStatus.Ready,
                binding.Ownership,
                binding.Owner,
                manifest.LevelId,
                output.AssetPath,
                output.PayloadPath,
                output.ManifestPath,
                output.ArtifactHash,
                output.PayloadSize,
                manifest.BodyCount,
                manifest.ShapeCount,
                manifest.VertexCount,
                manifest.TriangleCount,
                baked.Issues);
        }

        /// <summary>
        /// Reads and verifies the current bake. This method never assigns ids, imports assets,
        /// changes preview settings or writes files.
        /// </summary>
        public static JitterPhysicsEditorResult ReadSummary(
            JitterPhysicsLevel level,
            JitterPhysicsLevelIdBinding binding = null)
        {
            binding ??= JitterPhysicsLevelIdBinding.Standalone;
            string levelId = ResolveLevelId(level, binding, false, out JitterPhysicsIssueLog issues);
            if (issues.HasErrors)
            {
                return Empty(JitterPhysicsEditorResultStatus.Failed, level, binding, levelId, issues);
            }

            string artifactPath = JitterPhysicsArtifactPaths.ArtifactAssetPath(level.GeneratedFolder, levelId);
            var asset = AssetDatabase.LoadAssetAtPath<JitterPhysicsArtifactAsset>(artifactPath);
            if (asset == null)
            {
                return Empty(JitterPhysicsEditorResultStatus.Missing, level, binding, levelId, issues);
            }

            JitterPhysicsArtifactDelivery delivery = JitterPhysicsArtifactExporter.ReadDelivery(asset);
            if (!delivery.Succeeded)
            {
                return Empty(JitterPhysicsEditorResultStatus.Failed, level, binding, levelId, delivery.Issues);
            }

            PhysicsArtifactManifest manifest = delivery.Manifest;
            string payloadPath = AssetDatabase.GetAssetPath(asset.Payload);
            string manifestPath = JitterPhysicsArtifactPaths.ManifestAssetPath(level.GeneratedFolder, levelId);
            return new JitterPhysicsEditorResult(
                JitterPhysicsEditorResultStatus.Ready,
                binding.Ownership,
                binding.Owner,
                manifest.LevelId,
                artifactPath,
                payloadPath,
                manifestPath,
                JitterPhysicsHash.Sha256Hex(delivery.Payload),
                delivery.Payload.Length,
                manifest.BodyCount,
                manifest.ShapeCount,
                manifest.VertexCount,
                manifest.TriangleCount,
                delivery.Issues);
        }

        private static string ResolveLevelId(
            JitterPhysicsLevel level,
            JitterPhysicsLevelIdBinding binding,
            bool allowStandaloneAssignment,
            out JitterPhysicsIssueLog issues)
        {
            issues = new JitterPhysicsIssueLog();
            if (level == null)
            {
                issues.Error("No JitterPhysicsLevel was supplied.");
                return string.Empty;
            }

            string levelId;
            if (binding.Ownership == JitterPhysicsLevelIdOwnership.Standalone)
            {
                levelId = level.LevelId;
                if (!JitterPhysicsIdUtility.IsCanonical(levelId) && allowStandaloneAssignment)
                {
                    levelId = level.EnsureLevelId();
                    EditorUtility.SetDirty(level);
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(binding.Owner))
                {
                    issues.Error("External-managed Level ID requires a non-empty owner label.", level);
                }

                levelId = binding.LevelId;
            }

            if (!JitterPhysicsIdUtility.IsCanonical(levelId))
            {
                issues.Error($"Level ID '{levelId}' is not canonical.", level);
                return levelId ?? string.Empty;
            }

            JitterPhysicsLevel[] levels = UnityEngine.Object.FindObjectsByType<JitterPhysicsLevel>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < levels.Length; i++)
            {
                JitterPhysicsLevel other = levels[i];
                if (other == level || !string.Equals(other.LevelId, levelId, StringComparison.Ordinal))
                {
                    continue;
                }

                issues.Error(
                    $"Level ID conflict: '{levelId}' is already owned by '{other.name}'.",
                    other);
                break;
            }

            return levelId;
        }

        private static JitterPhysicsEditorResult FromArtifact(
            JitterPhysicsEditorResultStatus status,
            JitterPhysicsLevel level,
            JitterPhysicsLevelIdBinding binding,
            PhysicsArtifact artifact,
            string digest,
            int payloadSize,
            JitterPhysicsIssueLog issues)
        {
            return new JitterPhysicsEditorResult(
                status,
                binding.Ownership,
                binding.Owner,
                artifact.LevelId,
                JitterPhysicsArtifactPaths.ArtifactAssetPath(level.GeneratedFolder, artifact.LevelId),
                JitterPhysicsArtifactPaths.BinaryAssetPath(level.GeneratedFolder, artifact.LevelId),
                JitterPhysicsArtifactPaths.ManifestAssetPath(level.GeneratedFolder, artifact.LevelId),
                digest,
                payloadSize,
                artifact.Bodies.Count,
                artifact.ShapeCount,
                artifact.VertexCount,
                artifact.TriangleCount,
                issues);
        }

        private static JitterPhysicsEditorResult Empty(
            JitterPhysicsEditorResultStatus status,
            JitterPhysicsLevel level,
            JitterPhysicsLevelIdBinding binding,
            string levelId,
            JitterPhysicsIssueLog issues)
        {
            string folder = level != null ? level.GeneratedFolder : string.Empty;
            bool canonical = JitterPhysicsIdUtility.IsCanonical(levelId);
            return new JitterPhysicsEditorResult(
                status,
                binding.Ownership,
                binding.Owner,
                levelId,
                canonical ? JitterPhysicsArtifactPaths.ArtifactAssetPath(folder, levelId) : null,
                canonical ? JitterPhysicsArtifactPaths.BinaryAssetPath(folder, levelId) : null,
                canonical ? JitterPhysicsArtifactPaths.ManifestAssetPath(folder, levelId) : null,
                null,
                -1,
                -1,
                -1,
                -1,
                -1,
                issues);
        }
    }
}
