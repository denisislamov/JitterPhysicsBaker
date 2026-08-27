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
    /// and manifest under content-addressed canonical names.
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

            string canonicalPayload = JitterPhysicsArtifactNaming.BinaryFileName(
                manifest.LevelId, manifest.ArtifactHash);
            if (!string.Equals(manifest.FileName, canonicalPayload, StringComparison.Ordinal))
            {
                return Failure(
                    PhysicsArtifactErrorCode.InvalidValue,
                    $"Manifest payload name '{manifest.FileName}' is not canonical.",
                    manifest);
            }

            if (string.IsNullOrEmpty(targetFolder))
            {
                return Failure(PhysicsArtifactErrorCode.SourceUnavailable, "Artifact folder is not configured.", manifest);
            }

            string payloadPath = Path.Combine(targetFolder, canonicalPayload);
            string manifestPath = Path.Combine(
                targetFolder,
                JitterPhysicsArtifactNaming.ManifestFileName(manifest.LevelId, manifest.ArtifactHash));
            string canonicalManifest = PhysicsArtifactManifestCodec.Write(manifest);

            try
            {
                Directory.CreateDirectory(targetFolder);
                if (!MatchesExisting(payloadPath, payload) || !MatchesExisting(manifestPath, canonicalManifest))
                {
                    return Failure(
                        PhysicsArtifactErrorCode.HashMismatch,
                        "A file already exists at the content-addressed destination with different bytes.",
                        manifest);
                }

                bool payloadExisted = File.Exists(payloadPath);
                WriteIfMissing(payloadPath, payload);
                try
                {
                    WriteIfMissing(manifestPath, canonicalManifest);
                }
                catch
                {
                    if (!payloadExisted && File.Exists(payloadPath) && !File.Exists(manifestPath))
                    {
                        File.Delete(payloadPath);
                    }

                    throw;
                }
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

        private static bool MatchesExisting(string path, byte[] expected)
        {
            return !File.Exists(path) || BytesEqual(File.ReadAllBytes(path), expected);
        }

        private static bool MatchesExisting(string path, string expected)
        {
            return !File.Exists(path)
                   || string.Equals(File.ReadAllText(path), expected, StringComparison.Ordinal);
        }

        private static void WriteIfMissing(string path, byte[] content)
        {
            if (File.Exists(path)) return;
            string temporary = path + ".upload-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, content);
                File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static void WriteIfMissing(string path, string content)
        {
            if (File.Exists(path)) return;
            string temporary = path + ".upload-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, content, new UTF8Encoding(false));
                File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i]) return false;
            }

            return true;
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
