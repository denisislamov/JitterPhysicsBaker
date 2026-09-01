using System;
using System.IO;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.UnityArtifact;
using UnityEditor;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor.Baking
{
    internal enum JitterPhysicsArtifactTrioFailurePoint
    {
        None = 0,
        AfterPairImport,
        AfterAssetSave,
    }

    /// <summary>What a successful bake produced.</summary>
    public sealed class JitterPhysicsBakeOutput
    {
        /// <summary>Asset path of the artifact ScriptableObject.</summary>
        public string AssetPath { get; }

        /// <summary>Asset path of the binary payload.</summary>
        public string PayloadPath { get; }

        /// <summary>Asset path of the manifest.</summary>
        public string ManifestPath { get; }

        /// <summary>SHA-256 of the payload.</summary>
        public string ArtifactHash { get; }

        /// <summary>Size of the payload in bytes.</summary>
        public int PayloadSize { get; }

        /// <summary>The manifest describing the payload.</summary>
        public PhysicsArtifactManifest Manifest { get; }

        internal JitterPhysicsBakeOutput(
            string assetPath,
            string payloadPath,
            string manifestPath,
            string artifactHash,
            int payloadSize,
            PhysicsArtifactManifest manifest)
        {
            AssetPath = assetPath;
            PayloadPath = payloadPath;
            ManifestPath = manifestPath;
            ArtifactHash = artifactHash;
            PayloadSize = payloadSize;
            Manifest = manifest;
        }
    }

    /// <summary>Result of a full bake: build plus persistence.</summary>
    public sealed class JitterPhysicsBakeResult
    {
        /// <summary>What was written, or <c>null</c> when the bake did not complete.</summary>
        public JitterPhysicsBakeOutput Output { get; }

        /// <summary>Everything found while validating, converting and writing.</summary>
        public JitterPhysicsIssueLog Issues { get; }

        internal JitterPhysicsBakeResult(JitterPhysicsBakeOutput output, JitterPhysicsIssueLog issues)
        {
            Output = output;
            Issues = issues;
        }

        /// <summary>True when an artifact was written to the project.</summary>
        public bool Succeeded => Output != null;
    }

    /// <summary>
    /// Persists a built artifact into the project.
    /// <para>
    /// Writing is separated from building because the two fail for unrelated reasons and have
    /// unrelated consequences. A build failure means the scene is wrong; a write failure means
    /// the disk or the asset database is. Keeping them apart also lets validation run without
    /// touching the project at all.
    /// </para>
    /// <para>
    /// The write is staged: files are produced in a temporary folder, verified by decoding
    /// them back, and only then moved into place. If anything fails, the previously baked
    /// artifact is still there — a level that used to work must not stop working because
    /// somebody pressed Bake with a broken scene.
    /// </para>
    /// </summary>
    public static class JitterPhysicsBaker
    {
        /// <summary>Builds and writes the artifact for a level.</summary>
        public static JitterPhysicsBakeResult Bake(
            Authoring.JitterPhysicsLevel level,
            string runtimeCompatibilityId)
        {
            return BakeCore(
                level, runtimeCompatibilityId, null, JitterPhysicsArtifactTrioFailurePoint.None);
        }

        internal static JitterPhysicsBakeResult Bake(
            Authoring.JitterPhysicsLevel level,
            string runtimeCompatibilityId,
            string managedLevelId)
        {
            return BakeCore(
                level, runtimeCompatibilityId, managedLevelId,
                JitterPhysicsArtifactTrioFailurePoint.None);
        }

        internal static JitterPhysicsBakeResult BakeWithFailureForTests(
            Authoring.JitterPhysicsLevel level,
            string runtimeCompatibilityId,
            JitterPhysicsArtifactTrioFailurePoint failurePoint)
        {
            return BakeCore(level, runtimeCompatibilityId, null, failurePoint);
        }

        private static JitterPhysicsBakeResult BakeCore(
            Authoring.JitterPhysicsLevel level,
            string runtimeCompatibilityId,
            string managedLevelId,
            JitterPhysicsArtifactTrioFailurePoint failurePoint)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                var playModeIssues = new JitterPhysicsIssueLog();

                // Scene state in Play Mode is the simulation's, not the author's: colliders
                // have moved, objects have spawned. Baking it would produce an artifact of a
                // level that never existed in the project.
                playModeIssues.Error(
                    "Baking is not available in Play Mode. Exit Play Mode and bake from the "
                    + "authored scene state.",
                    level);
                return new JitterPhysicsBakeResult(null, playModeIssues);
            }

            string targetLevelId = managedLevelId ?? level?.LevelId;
            if (level != null
                && JitterPhysicsIdUtility.IsCanonical(targetLevelId)
                && JitterPhysicsArtifactMigration.IsRequired(
                    level.GeneratedFolder, targetLevelId, level.LastArtifactHash))
            {
                var migrationIssues = new JitterPhysicsIssueLog();
                migrationIssues.Error(
                    "This level still uses legacy bake file names. Run 'Migrate Legacy Bake Files' "
                    + "from the Bake tab before baking so Unity references and payload bytes are preserved.",
                    level);
                return new JitterPhysicsBakeResult(null, migrationIssues);
            }

            JitterPhysicsBuildResult build = JitterPhysicsArtifactBuilder.Build(
                level, runtimeCompatibilityId, managedLevelId);
            if (!build.Succeeded)
            {
                return new JitterPhysicsBakeResult(null, build.Issues);
            }

            try
            {
                JitterPhysicsBakeOutput output = Write(
                    build.Artifact, level.GeneratedFolder, build.Issues, failurePoint);
                if (output == null)
                {
                    return new JitterPhysicsBakeResult(null, build.Issues);
                }

                level.SetLastArtifactHash(output.ArtifactHash);
                EditorUtility.SetDirty(level);

                return new JitterPhysicsBakeResult(output, build.Issues);
            }
            catch (Exception exception)
            {
                build.Issues.Error(
                    "Writing the artifact failed; the previously baked artifact was left in place. "
                    + exception.Message,
                    level);
                return new JitterPhysicsBakeResult(null, build.Issues);
            }
        }

        private static JitterPhysicsBakeOutput Write(
            PhysicsArtifact artifact,
            string generatedFolder,
            JitterPhysicsIssueLog issues,
            JitterPhysicsArtifactTrioFailurePoint failurePoint)
        {
            PhysicsArtifactPayload payload = PhysicsArtifactWriter.WriteWithManifest(
                artifact, JitterPhysicsPackage.PackageVersion);

            // Decode the bytes that are about to be written, not the records they came from.
            // Only this proves the file on disk is loadable; verifying the in-memory artifact
            // would just re-check the object we already have.
            PhysicsArtifactResult verification = PhysicsArtifactReader.Read(
                payload.Bytes, payload.ArtifactHash, payload.Manifest);

            if (!verification.Succeeded)
            {
                issues.Error(
                    "The produced artifact does not decode: " + verification.Error
                    + ". Nothing was written; this is a bug in the baker.");
                return null;
            }

            string folder = string.IsNullOrEmpty(generatedFolder)
                ? JitterPhysicsArtifactPaths.DefaultGeneratedFolder
                : generatedFolder.TrimEnd('/', '\\');

            EnsureFolder(folder);

            string payloadPath = JitterPhysicsArtifactPaths.BinaryAssetPath(folder, artifact.LevelId);
            string manifestPath = JitterPhysicsArtifactPaths.ManifestAssetPath(folder, artifact.LevelId);
            string assetPath = JitterPhysicsArtifactPaths.ArtifactAssetPath(folder, artifact.LevelId);
            TrioSnapshot previous = TrioSnapshot.Capture(payloadPath, manifestPath, assetPath);

            string staging = FileUtil.GetUniqueTempPathInProject();
            Directory.CreateDirectory(staging);

            try
            {
                string stagedPayload = Path.Combine(staging, Path.GetFileName(payloadPath));
                string stagedManifest = Path.Combine(staging, Path.GetFileName(manifestPath));

                File.WriteAllBytes(stagedPayload, payload.Bytes);
                File.WriteAllText(stagedManifest, PhysicsArtifactManifestCodec.Write(payload.Manifest));

                if (!VerifyOnDisk(stagedPayload, payload, issues))
                {
                    return null;
                }

                PhysicsArtifactPairWriter.Write(
                    ToAbsolutePath(payloadPath),
                    File.ReadAllBytes(stagedPayload),
                    ToAbsolutePath(manifestPath),
                    File.ReadAllText(stagedManifest));

                AssetDatabase.ImportAsset(payloadPath, ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(manifestPath, ImportAssetOptions.ForceSynchronousImport);
                ThrowAt(failurePoint, JitterPhysicsArtifactTrioFailurePoint.AfterPairImport);

                var payloadAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(payloadPath);
                if (payloadAsset == null)
                {
                    throw new IOException($"Unity did not import the payload at '{payloadPath}'.");
                }

                UpdateArtifactAsset(assetPath, payload.Manifest, payloadAsset);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                ThrowAt(failurePoint, JitterPhysicsArtifactTrioFailurePoint.AfterAssetSave);

                string trioError = VerifyArtifactTrio(
                    payloadPath, manifestPath, assetPath, payload);
                if (trioError != null)
                {
                    throw new IOException("Artifact trio verification failed: " + trioError);
                }

                return new JitterPhysicsBakeOutput(
                    assetPath,
                    payloadPath,
                    manifestPath,
                    payload.ArtifactHash,
                    payload.Bytes.Length,
                    payload.Manifest);
            }
            catch
            {
                previous.Restore();
                throw;
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, true);
            }
        }

        private static void ThrowAt(
            JitterPhysicsArtifactTrioFailurePoint configured,
            JitterPhysicsArtifactTrioFailurePoint current)
        {
            if (configured == current)
            {
                throw new IOException("Injected artifact trio failure at " + current + ".");
            }
        }

        private static string VerifyArtifactTrio(
            string payloadPath,
            string manifestPath,
            string assetPath,
            PhysicsArtifactPayload expected)
        {
            byte[] bytes = File.ReadAllBytes(payloadPath);
            PhysicsArtifactManifest manifest = PhysicsArtifactManifestCodec.Read(
                File.ReadAllText(manifestPath), out string manifestError);
            if (manifest == null) return "Manifest is invalid: " + manifestError;

            PhysicsArtifactResult decoded = PhysicsArtifactReader.Read(
                bytes, expected.ArtifactHash, manifest);
            if (!decoded.Succeeded) return decoded.Error.ToString();

            var asset = AssetDatabase.LoadAssetAtPath<JitterPhysicsArtifactAsset>(assetPath);
            if (asset == null) return "Unity artifact asset is missing.";
            if (!ReferenceEquals(asset.Payload, AssetDatabase.LoadAssetAtPath<TextAsset>(payloadPath)))
                return "Unity artifact asset points at a different payload.";
            if (!JitterPhysicsHash.HexEquals(asset.ArtifactHash, expected.ArtifactHash))
                return "Unity artifact asset records a different hash.";

            PhysicsArtifactResult loaded = JitterPhysicsArtifactLoader.Load(
                asset, expected.Manifest.RuntimeCompatibilityId);
            return loaded.Succeeded ? null : loaded.Error.ToString();
        }

        private sealed class TrioSnapshot
        {
            private readonly FileSnapshot[] files;

            private TrioSnapshot(FileSnapshot[] files) => this.files = files;

            internal static TrioSnapshot Capture(params string[] paths)
            {
                var files = new FileSnapshot[paths.Length];
                for (int index = 0; index < paths.Length; index++)
                    files[index] = FileSnapshot.Capture(paths[index]);
                return new TrioSnapshot(files);
            }

            internal void Restore()
            {
                for (int index = 0; index < files.Length; index++) files[index].Restore();
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        private sealed class FileSnapshot
        {
            private readonly string assetPath;
            private readonly string absolutePath;
            private readonly byte[] bytes;

            private FileSnapshot(string assetPath, string absolutePath, byte[] bytes)
            {
                this.assetPath = assetPath;
                this.absolutePath = absolutePath;
                this.bytes = bytes;
            }

            internal static FileSnapshot Capture(string assetPath)
            {
                string absolute = ToAbsolutePath(assetPath);
                return new FileSnapshot(
                    assetPath,
                    absolute,
                    File.Exists(absolute) ? File.ReadAllBytes(absolute) : null);
            }

            internal void Restore()
            {
                if (bytes != null)
                {
                    File.WriteAllBytes(absolutePath, bytes);
                    return;
                }

                if (!AssetDatabase.DeleteAsset(assetPath))
                {
                    if (File.Exists(absolutePath)) File.Delete(absolutePath);
                    if (File.Exists(absolutePath + ".meta")) File.Delete(absolutePath + ".meta");
                }
            }
        }

        /// <summary>
        /// Re-reads the staged file from disk and re-hashes it. This is not paranoia about
        /// SHA-256; it catches a truncated write, a full disk and an antivirus that decided to
        /// modify the file on the way in.
        /// </summary>
        private static bool VerifyOnDisk(
            string stagedPayload,
            PhysicsArtifactPayload payload,
            JitterPhysicsIssueLog issues)
        {
            byte[] written = File.ReadAllBytes(stagedPayload);

            if (written.Length != payload.Bytes.Length)
            {
                issues.Error(
                    $"The staged payload is {written.Length} bytes, expected {payload.Bytes.Length}. "
                    + "Nothing was written.");
                return false;
            }

            string writtenHash = JitterPhysicsHash.Sha256Hex(written);
            if (!JitterPhysicsHash.HexEquals(writtenHash, payload.ArtifactHash))
            {
                issues.Error(
                    "The staged payload hashes differently from the artifact it was produced "
                    + "from. Nothing was written.");
                return false;
            }

            return true;
        }

        private static void UpdateArtifactAsset(
            string assetPath,
            PhysicsArtifactManifest manifest,
            TextAsset payloadAsset)
        {
            var asset = AssetDatabase.LoadAssetAtPath<JitterPhysicsArtifactAsset>(assetPath);
            bool isNew = asset == null;

            if (isNew)
            {
                asset = ScriptableObject.CreateInstance<JitterPhysicsArtifactAsset>();
            }

            asset.Initialize(manifest, payloadAsset);

            if (isNew)
            {
                AssetDatabase.CreateAsset(asset, assetPath);
            }
            else
            {
                // Updated in place so that every scene reference to this level survives a
                // re-bake. Recreating the asset would break them silently.
                EditorUtility.SetDirty(asset);
            }
        }

        private static string ToAbsolutePath(string projectRelativePath)
        {
            if (Path.IsPathRooted(projectRelativePath))
            {
                return projectRelativePath;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? string.Empty;
            return Path.Combine(projectRoot, projectRelativePath);
        }

        /// <summary>Creates every missing folder of an <c>Assets/...</c> path.</summary>
        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
            {
                return;
            }

            string[] parts = assetFolder.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}
