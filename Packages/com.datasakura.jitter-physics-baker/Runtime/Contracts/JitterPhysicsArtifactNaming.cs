using System;

namespace DataSakura.JitterPhysics.Contracts
{
    /// <summary>
    /// File naming rules for a baked artifact. Client, server projection and editor export
    /// all derive names here so that a file copied between them stays recognisable.
    /// </summary>
    public static class JitterPhysicsArtifactNaming
    {
        /// <summary>Extension of the deterministic binary payload.</summary>
        public const string BinaryExtension = ".physics.bytes";

        /// <summary>Extension of the JSON manifest that travels next to the payload.</summary>
        public const string ManifestExtension = ".physics.manifest.json";

        /// <summary>Extension used by packages before human-readable artifact names.</summary>
        public const string LegacyBinaryExtension = ".jphys.bytes";

        /// <summary>Manifest extension used by packages before human-readable artifact names.</summary>
        public const string LegacyManifestExtension = ".manifest.json";

        /// <summary>Number of leading hash characters embedded into file names.</summary>
        public const int ShortHashLength = 12;

        /// <summary>Length of a full lowercase hex SHA-256.</summary>
        public const int FullHashLength = 64;

        /// <summary><c>&lt;levelId&gt;.physics.bytes</c></summary>
        public static string BinaryFileName(string levelId)
        {
            ValidateLevelId(levelId);
            return levelId + BinaryExtension;
        }

        /// <summary><c>&lt;levelId&gt;.physics.manifest.json</c></summary>
        public static string ManifestFileName(string levelId)
        {
            ValidateLevelId(levelId);
            return levelId + ManifestExtension;
        }

        /// <summary>Legacy <c>&lt;levelId&gt;.&lt;hash12&gt;.jphys.bytes</c> name.</summary>
        public static string LegacyBinaryFileName(string levelId, string artifactHash)
        {
            return LegacyPrefix(levelId, artifactHash) + LegacyBinaryExtension;
        }

        /// <summary>Legacy <c>&lt;levelId&gt;.&lt;hash12&gt;.manifest.json</c> name.</summary>
        public static string LegacyManifestFileName(string levelId, string artifactHash)
        {
            return LegacyPrefix(levelId, artifactHash) + LegacyManifestExtension;
        }

        /// <summary>Whether a manifest names the current or exact legacy payload for its identity.</summary>
        public static bool IsSupportedBinaryFileName(string levelId, string artifactHash, string fileName)
        {
            return string.Equals(fileName, BinaryFileName(levelId), StringComparison.Ordinal)
                   || string.Equals(fileName, LegacyBinaryFileName(levelId, artifactHash), StringComparison.Ordinal);
        }

        /// <summary>
        /// First <see cref="ShortHashLength"/> characters of the hash. Runtime logs use the
        /// short form; the editor prints the full hash so that mismatches stay diagnosable.
        /// </summary>
        public static string ShortHash(string artifactHash)
        {
            if (artifactHash == null)
            {
                throw new ArgumentNullException(nameof(artifactHash));
            }

            if (artifactHash.Length != FullHashLength)
            {
                throw new ArgumentException(
                    $"Artifact hash must be {FullHashLength} lowercase hex characters, got {artifactHash.Length}.",
                    nameof(artifactHash));
            }

            return artifactHash.Substring(0, ShortHashLength);
        }

        private static string LegacyPrefix(string levelId, string artifactHash)
        {
            ValidateLevelId(levelId);
            return levelId + "." + ShortHash(artifactHash);
        }

        private static void ValidateLevelId(string levelId)
        {
            if (!JitterPhysicsIdUtility.IsCanonical(levelId))
            {
                throw new ArgumentException(
                    $"Level id '{levelId}' is not canonical; artifact names must not depend on authoring spelling.",
                    nameof(levelId));
            }
        }
    }
}
