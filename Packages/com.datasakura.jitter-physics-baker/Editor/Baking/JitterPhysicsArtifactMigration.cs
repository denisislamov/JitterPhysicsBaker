using System;
using System.IO;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.UnityArtifact;
using UnityEditor;

namespace DataSakura.JitterPhysics.Editor.Baking
{
    /// <summary>Explicit, GUID-preserving migration from legacy hash-addressed bake files.</summary>
    public static class JitterPhysicsArtifactMigration
    {
        /// <summary>Whether a legacy artifact asset exists for this level.</summary>
        public static bool IsRequired(string generatedFolder, string levelId, string artifactHash = null)
        {
            if (File.Exists(JitterPhysicsArtifactPaths.LegacyArtifactAssetPath(generatedFolder, levelId)))
            {
                return true;
            }

            return !string.IsNullOrEmpty(artifactHash)
                   && (File.Exists(JitterPhysicsArtifactPaths.LegacyBinaryAssetPath(
                           generatedFolder, levelId, artifactHash))
                       || File.Exists(JitterPhysicsArtifactPaths.LegacyManifestAssetPath(
                           generatedFolder, levelId, artifactHash)));
        }

        /// <summary>
        /// Moves the asset, payload and manifest through <see cref="AssetDatabase.MoveAsset"/>,
        /// preserving their meta files and Unity references. The payload bytes are never rewritten.
        /// </summary>
        public static JitterPhysicsIssueLog Migrate(string generatedFolder, string levelId, string artifactHash)
        {
            var issues = new JitterPhysicsIssueLog();
            string oldAsset = JitterPhysicsArtifactPaths.LegacyArtifactAssetPath(generatedFolder, levelId);
            string newAsset = JitterPhysicsArtifactPaths.ArtifactAssetPath(generatedFolder, levelId);
            string oldPayload = JitterPhysicsArtifactPaths.LegacyBinaryAssetPath(generatedFolder, levelId, artifactHash);
            string newPayload = JitterPhysicsArtifactPaths.BinaryAssetPath(generatedFolder, levelId);
            string oldManifest = JitterPhysicsArtifactPaths.LegacyManifestAssetPath(generatedFolder, levelId, artifactHash);
            string newManifest = JitterPhysicsArtifactPaths.ManifestAssetPath(generatedFolder, levelId);

            bool anyLegacy = File.Exists(oldAsset) || File.Exists(oldPayload) || File.Exists(oldManifest);
            if (!anyLegacy)
            {
                if (File.Exists(newAsset) && File.Exists(newPayload) && File.Exists(newManifest))
                {
                    return issues;
                }

                issues.Error("No complete legacy or current artifact pair was found for migration.");
                return issues;
            }

            if (!File.Exists(oldAsset) || !File.Exists(oldPayload) || !File.Exists(oldManifest))
            {
                issues.Error("Legacy migration refused because the asset, payload and manifest are not all present.");
                return issues;
            }

            if (File.Exists(newAsset) || File.Exists(newPayload) || File.Exists(newManifest))
            {
                issues.Error("Legacy migration refused because a current-name destination already exists.");
                return issues;
            }

            byte[] payload = File.ReadAllBytes(oldPayload);
            string manifestJson = File.ReadAllText(oldManifest);
            PhysicsArtifactManifest manifest = PhysicsArtifactManifestCodec.Read(manifestJson, out string parseError);
            if (manifest == null)
            {
                issues.Error("Legacy manifest is invalid: " + parseError);
                return issues;
            }

            PhysicsArtifactResult verified = PhysicsArtifactReader.Read(payload, manifest.ArtifactHash, manifest);
            if (!verified.Succeeded)
            {
                issues.Error("Legacy migration refused because the artifact pair is invalid: " + verified.Error);
                return issues;
            }

            if (!string.Equals(levelId, manifest.LevelId, StringComparison.Ordinal)
                || !JitterPhysicsHash.HexEquals(artifactHash, manifest.ArtifactHash))
            {
                issues.Error("Legacy migration refused because the selected asset identity disagrees with its manifest.");
                return issues;
            }

            string assetGuid = AssetDatabase.AssetPathToGUID(oldAsset);
            string payloadGuid = AssetDatabase.AssetPathToGUID(oldPayload);
            string manifestGuid = AssetDatabase.AssetPathToGUID(oldManifest);
            bool payloadMoved = false;
            bool manifestMoved = false;
            bool assetMoved = false;

            try
            {
                Move(oldPayload, newPayload);
                payloadMoved = true;
                Move(oldManifest, newManifest);
                manifestMoved = true;
                Move(oldAsset, newAsset);
                assetMoved = true;

                File.WriteAllText(
                    newManifest,
                    PhysicsArtifactManifestCodec.Write(manifest.WithCurrentFileName()),
                    new System.Text.UTF8Encoding(false));
                AssetDatabase.ImportAsset(newManifest, ImportAssetOptions.ForceSynchronousImport);

                PhysicsArtifactManifest migratedManifest = PhysicsArtifactManifestCodec.Read(
                    File.ReadAllText(newManifest), out string migratedManifestError);
                PhysicsArtifactResult migratedPair = migratedManifest == null
                    ? PhysicsArtifactResult.Failure(
                        PhysicsArtifactErrorCode.ManifestMismatch, migratedManifestError)
                    : PhysicsArtifactReader.Read(payload, migratedManifest.ArtifactHash, migratedManifest);
                var migratedAsset = AssetDatabase.LoadAssetAtPath<JitterPhysicsArtifactAsset>(newAsset);

                if (!BytesEqual(payload, File.ReadAllBytes(newPayload))
                    || assetGuid != AssetDatabase.AssetPathToGUID(newAsset)
                    || payloadGuid != AssetDatabase.AssetPathToGUID(newPayload)
                    || manifestGuid != AssetDatabase.AssetPathToGUID(newManifest)
                    || !migratedPair.Succeeded
                    || migratedAsset == null
                    || AssetDatabase.GetAssetPath(migratedAsset.Payload) != newPayload)
                {
                    throw new IOException(
                        "Post-migration GUID, reference, payload-byte or manifest verification failed.");
                }
            }
            catch (Exception exception)
            {
                if (assetMoved) TryMove(newAsset, oldAsset);
                if (manifestMoved)
                {
                    TryMove(newManifest, oldManifest);
                    File.WriteAllText(oldManifest, manifestJson, new System.Text.UTF8Encoding(false));
                    AssetDatabase.ImportAsset(oldManifest, ImportAssetOptions.ForceSynchronousImport);
                }
                if (payloadMoved) TryMove(newPayload, oldPayload);
                issues.Error("Legacy migration failed and was rolled back: " + exception.Message);
            }

            AssetDatabase.Refresh();
            return issues;
        }

        private static void Move(string source, string destination)
        {
            string error = AssetDatabase.MoveAsset(source, destination);
            if (!string.IsNullOrEmpty(error)) throw new IOException(error);
        }

        private static void TryMove(string source, string destination)
        {
            if (File.Exists(source) && !File.Exists(destination)) AssetDatabase.MoveAsset(source, destination);
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
