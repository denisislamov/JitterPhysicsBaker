using System;
using System.IO;
using System.Text;
using DataSakura.JitterPhysics.Contracts;

namespace DataSakura.JitterPhysics.ArtifactCodec
{
    /// <summary>Result of validating and storing one remotely delivered artifact.</summary>
    public sealed class PhysicsArtifactUploadResult
    {
        internal PhysicsArtifactUploadResult(
            PhysicsArtifactManifest manifest,
            string payloadPath,
            string manifestPath,
            PhysicsArtifactError error)
        {
            Manifest = manifest;
            PayloadPath = payloadPath;
            ManifestPath = manifestPath;
            Error = error;
        }

        /// <summary>Validated manifest, or <c>null</c> when delivery failed.</summary>
        public PhysicsArtifactManifest Manifest { get; }

        /// <summary>Canonical stored payload path.</summary>
        public string PayloadPath { get; }

        /// <summary>Canonical stored manifest path.</summary>
        public string ManifestPath { get; }

        /// <summary>Typed reason why delivery was rejected.</summary>
        public PhysicsArtifactError Error { get; }

        /// <summary>True when both files are present and verified.</summary>
        public bool Succeeded => !Error.IsError;
    }

    /// <summary>
    /// Validates an artifact received from an untrusted delivery channel and stores its payload
    /// and manifest under stable, human-readable canonical names.
    /// </summary>
    public static class PhysicsArtifactUploadStore
    {
        /// <summary>Checks and atomically stores a payload/manifest pair.</summary>
        public static PhysicsArtifactUploadResult Store(
            byte[] payload,
            string manifestJson,
            string targetFolder,
            string expectedRuntimeCompatibilityId)
        {
            if (string.IsNullOrEmpty(manifestJson))
            {
                return Failure(PhysicsArtifactErrorCode.ManifestMismatch, "Manifest is empty.");
            }

            if (Encoding.UTF8.GetByteCount(manifestJson) > PhysicsArtifactManifestCodec.MaxManifestBytes)
            {
                return Failure(PhysicsArtifactErrorCode.LimitExceeded, "Manifest exceeds the delivery limit.");
            }

            PhysicsArtifactManifest manifest = PhysicsArtifactManifestCodec.Read(manifestJson, out string error);
            if (manifest == null)
            {
                return Failure(PhysicsArtifactErrorCode.ManifestMismatch, error);
            }

            PhysicsArtifactResult decoded = PhysicsArtifactReader.Read(payload, manifest.ArtifactHash, manifest);
            if (!decoded.Succeeded)
            {
                return new PhysicsArtifactUploadResult(null, null, null, decoded.Error);
            }

            PhysicsArtifactError compatibility = PhysicsArtifactReader.CheckRuntimeCompatibility(
                decoded.Artifact, expectedRuntimeCompatibilityId);
            if (compatibility.IsError)
            {
                return new PhysicsArtifactUploadResult(null, null, null, compatibility);
            }

            if (!JitterPhysicsArtifactNaming.IsSupportedBinaryFileName(
                    manifest.LevelId, manifest.ArtifactHash, manifest.FileName))
            {
                return Failure(
                    PhysicsArtifactErrorCode.InvalidValue,
                    $"Manifest payload name '{manifest.FileName}' is not canonical.",
                    manifest);
            }

            manifest = manifest.WithCurrentFileName();
            string canonicalPayload = manifest.FileName;

            if (string.IsNullOrEmpty(targetFolder))
            {
                return Failure(PhysicsArtifactErrorCode.SourceUnavailable, "Artifact folder is not configured.", manifest);
            }

            string payloadPath = Path.Combine(targetFolder, canonicalPayload);
            string manifestPath = Path.Combine(
                targetFolder,
                JitterPhysicsArtifactNaming.ManifestFileName(manifest.LevelId));
            string canonicalManifest = PhysicsArtifactManifestCodec.Write(manifest);

            try
            {
                Directory.CreateDirectory(targetFolder);
                PhysicsArtifactPairWriter.Write(payloadPath, payload, manifestPath, canonicalManifest);
            }
            catch (Exception exception)
            {
                return Failure(
                    PhysicsArtifactErrorCode.SourceUnavailable,
                    "Could not store artifact: " + exception.Message,
                    manifest);
            }

            return new PhysicsArtifactUploadResult(manifest, payloadPath, manifestPath, default);
        }

        private static PhysicsArtifactUploadResult Failure(
            PhysicsArtifactErrorCode code,
            string message,
            PhysicsArtifactManifest manifest = null)
        {
            return new PhysicsArtifactUploadResult(
                null,
                null,
                null,
                new PhysicsArtifactError(code, message, manifest?.LevelId, manifest?.ArtifactHash));
        }
    }
}
