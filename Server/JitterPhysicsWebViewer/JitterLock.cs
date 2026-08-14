using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using DataSakura.JitterPhysics.ArtifactCodec;

namespace DataSakura.JitterPhysics.WebViewer
{
    /// <summary>
    /// The part of <c>jitter2.lock.json</c> a server needs: which Jitter2 sources this build
    /// compiles and how they were compiled.
    /// <para>
    /// The server derives its runtime compatibility id from these two values instead of
    /// accepting one from configuration. A configured id is a value somebody sets to whatever
    /// silences the startup error, and the failure it hides — a client and a server rebuilding
    /// different worlds from the same file — is invisible until players disagree about where
    /// the walls are.
    /// </para>
    /// </summary>
    public sealed class JitterLock
    {
        private JitterLock(string sourceContentHash, string compileProfileId, string upstreamCommit)
        {
            SourceContentHash = sourceContentHash;
            CompileProfileId = compileProfileId;
            UpstreamCommit = upstreamCommit;
        }

        /// <summary>Canonical source hash of the locked Jitter2 sources.</summary>
        public string SourceContentHash { get; }

        /// <summary>Hash of the canonical compile profile text.</summary>
        public string CompileProfileId { get; }

        /// <summary>Upstream commit the snapshot was taken at; diagnostics only.</summary>
        public string UpstreamCommit { get; }

        /// <summary>Runtime compatibility id implied by this lock and this package version.</summary>
        public string RuntimeCompatibilityId =>
            ArtifactCodec.RuntimeCompatibilityId.Compute(
                RuntimeCompatibilityInputs.ForCurrentBuild(SourceContentHash, CompileProfileId));

        /// <summary>Reads and parses the lock file.</summary>
        public static JitterLock Load(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"'{path}' is missing. It is copied next to the binary by the build; without it "
                    + "the server cannot state which Jitter2 semantics it was built with.",
                    path);
            }

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = document.RootElement;

            string sourceHash = root.GetProperty("sourceContentHash").GetString();
            JsonElement profile = root.GetProperty("compileProfile");
            string profileText = CanonicalProfileText(profile);

            return new JitterLock(
                sourceHash,
                JitterPhysicsHash.Sha256HexUtf8(profileText),
                root.TryGetProperty("upstreamCommit", out JsonElement commit) ? commit.GetString() : null);
        }

        /// <summary>
        /// Serializes the compile profile exactly the way the editor and the Python tooling
        /// do — <c>json.dumps(profile, sort_keys=True, separators=(",", ":"))</c> — because
        /// the text is hashed and the three implementations must agree byte for byte.
        /// </summary>
        private static string CanonicalProfileText(JsonElement profile)
        {
            var keys = new List<string>();
            foreach (JsonProperty property in profile.EnumerateObject())
            {
                keys.Add(property.Name);
            }

            keys.Sort(StringComparer.Ordinal);

            var builder = new StringBuilder(256);
            builder.Append('{');

            for (int i = 0; i < keys.Count; i++)
            {
                if (i != 0)
                {
                    builder.Append(',');
                }

                AppendString(builder, keys[i]);
                builder.Append(':');
                AppendValue(builder, profile.GetProperty(keys[i]));
            }

            builder.Append('}');
            return builder.ToString();
        }

        private static void AppendValue(StringBuilder builder, JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    AppendString(builder, value.GetString());
                    break;
                case JsonValueKind.True:
                    builder.Append("true");
                    break;
                case JsonValueKind.False:
                    builder.Append("false");
                    break;
                case JsonValueKind.Number:
                    builder.Append(value.GetRawText());
                    break;
                case JsonValueKind.Null:
                    builder.Append("null");
                    break;
                default:
                    // Nested containers would need an agreed canonical form on all three
                    // sides; until one exists they are refused rather than hashed differently.
                    throw new FormatException("The compile profile may only contain scalar values.");
            }
        }

        private static void AppendString(StringBuilder builder, string value)
        {
            builder.Append('"');

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                switch (character)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        // Python's json.dumps escapes every non-ASCII character by default.
                        if (character < 0x20 || character > 0x7E)
                        {
                            builder.Append("\\u").Append(((int)character).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            builder.Append('"');
        }
    }
}

