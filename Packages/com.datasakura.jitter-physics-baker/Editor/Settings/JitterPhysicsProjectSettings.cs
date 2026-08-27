using System.IO;
using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.UnityArtifact;
using UnityEditor;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor.Settings
{
    /// <summary>Project-wide authoring defaults. Personal Scene View choices live elsewhere.</summary>
    [FilePath(SettingsFilePath, FilePathAttribute.Location.ProjectFolder)]
    internal sealed class JitterPhysicsProjectSettings : ScriptableSingleton<JitterPhysicsProjectSettings>
    {
        internal const string SettingsFilePath = "ProjectSettings/DataSakuraJitterPhysicsSettings.asset";
        internal const string ProviderPath = "Project/DataSakura/Jitter Physics";
        internal const string DefaultProfilesFolder = "Assets/JitterPhysics/Settings";

        [SerializeField]
        private JitterPhysicsWorldProfile defaultWorldProfile;

        [SerializeField]
        private string profilesFolder = DefaultProfilesFolder;

        [SerializeField]
        private string generatedFolder = JitterPhysicsArtifactPaths.DefaultGeneratedFolder;

        internal JitterPhysicsWorldProfile DefaultWorldProfile
        {
            get => defaultWorldProfile;
            set => defaultWorldProfile = value;
        }

        internal string ProfilesFolder
        {
            get => NormalizeAssetFolder(profilesFolder, DefaultProfilesFolder);
            set => profilesFolder = value;
        }

        internal string GeneratedFolder
        {
            get => NormalizeAssetFolder(generatedFolder, JitterPhysicsArtifactPaths.DefaultGeneratedFolder);
            set => generatedFolder = value;
        }

        internal void SaveSettings()
        {
            Save(true);
        }

        /// <summary>Creates the one shared default profile only after an explicit command.</summary>
        internal JitterPhysicsWorldProfile CreateDefaults(bool selectAsset)
        {
            if (defaultWorldProfile == null)
            {
                EnsureAssetFolder(ProfilesFolder);
                string path = ProfilesFolder + "/JitterPhysicsWorldDefaults.asset";
                var existing = AssetDatabase.LoadAssetAtPath<JitterPhysicsWorldProfile>(path);
                if (existing == null && AssetDatabase.LoadMainAssetAtPath(path) != null)
                {
                    path = AssetDatabase.GenerateUniqueAssetPath(path);
                }

                if (existing == null)
                {
                    existing = CreateInstance<JitterPhysicsWorldProfile>();
                    existing.name = Path.GetFileNameWithoutExtension(path);
                    AssetDatabase.CreateAsset(existing, path);
                }

                defaultWorldProfile = existing;
                SaveSettings();
                AssetDatabase.SaveAssets();
            }

            if (selectAsset && defaultWorldProfile != null)
            {
                Selection.activeObject = defaultWorldProfile;
                EditorGUIUtility.PingObject(defaultWorldProfile);
            }

            return defaultWorldProfile;
        }

        internal JitterPhysicsWorldProfile CreateProfile(string path, JitterPhysicsWorldProfile source)
        {
            string folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureAssetFolder(string.IsNullOrEmpty(folder) ? ProfilesFolder : folder);
            path = AssetDatabase.GenerateUniqueAssetPath(path);

            JitterPhysicsWorldProfile created;
            string sourcePath = source != null ? AssetDatabase.GetAssetPath(source) : null;
            if (!string.IsNullOrEmpty(sourcePath) && AssetDatabase.CopyAsset(sourcePath, path))
            {
                created = AssetDatabase.LoadAssetAtPath<JitterPhysicsWorldProfile>(path);
            }
            else
            {
                created = CreateInstance<JitterPhysicsWorldProfile>();
                if (source != null)
                {
                    EditorUtility.CopySerialized(source, created);
                }

                created.name = Path.GetFileNameWithoutExtension(path);
                AssetDatabase.CreateAsset(created, path);
            }

            AssetDatabase.SaveAssets();
            return created;
        }

        internal static bool IsValidAssetFolder(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                   && (value == "Assets" || value.StartsWith("Assets/", System.StringComparison.Ordinal))
                   && value.IndexOf("..", System.StringComparison.Ordinal) < 0;
        }

        internal static void EnsureAssetFolder(string folderPath)
        {
            if (!IsValidAssetFolder(folderPath))
            {
                throw new System.ArgumentException("Folder must be a project-relative path under Assets/.");
            }

            string[] parts = folderPath.TrimEnd('/').Split('/');
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

        private static string NormalizeAssetFolder(string value, string fallback)
        {
            if (!IsValidAssetFolder(value))
            {
                return fallback;
            }

            return value.TrimEnd('/');
        }
    }
}
