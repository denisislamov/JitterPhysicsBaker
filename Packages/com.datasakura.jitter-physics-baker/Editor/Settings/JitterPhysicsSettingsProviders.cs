using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Editor.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor.Settings
{
    /// <summary>Native Unity settings surfaces for project defaults and personal preview.</summary>
    internal static class JitterPhysicsSettingsProviders
    {
        internal const string PreferencesPath = "Preferences/DataSakura/Jitter Physics/Scene Preview";

        [SettingsProvider]
        private static SettingsProvider CreateProjectProvider()
        {
            var provider = new SettingsProvider(
                JitterPhysicsProjectSettings.ProviderPath,
                SettingsScope.Project)
            {
                label = "Jitter Physics",
                guiHandler = _ => DrawProjectSettings(),
                keywords = new System.Collections.Generic.HashSet<string>(new[]
                {
                    "DataSakura", "Jitter", "Physics", "World Profile", "Generated Folder",
                }),
            };
            return provider;
        }

        [SettingsProvider]
        private static SettingsProvider CreatePreferencesProvider()
        {
            var provider = new SettingsProvider(PreferencesPath, SettingsScope.User)
            {
                label = "Scene Preview",
                guiHandler = _ => DrawPreviewPreferences(),
                keywords = new System.Collections.Generic.HashSet<string>(new[]
                {
                    "DataSakura", "Jitter", "Physics", "Scene", "Preview", "Overlay",
                }),
            };
            return provider;
        }

        private static void DrawProjectSettings()
        {
            JitterPhysicsProjectSettings settings = JitterPhysicsProjectSettings.instance;
            EditorGUILayout.LabelField("Shared authoring defaults", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "These values belong to the project and may be shared by many levels. Personal "
                + "Scene View display choices live in Preferences and never affect baked bytes.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            JitterPhysicsWorldProfile profile = (JitterPhysicsWorldProfile)EditorGUILayout.ObjectField(
                "Default World Profile",
                settings.DefaultWorldProfile,
                typeof(JitterPhysicsWorldProfile),
                false);
            string profilesFolder = EditorGUILayout.TextField("Profiles Folder", settings.ProfilesFolder);
            string generatedFolder = EditorGUILayout.TextField("Generated Folder", settings.GeneratedFolder);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(settings, "Change Jitter Physics project settings");
                settings.DefaultWorldProfile = profile;
                settings.ProfilesFolder = profilesFolder;
                settings.GeneratedFolder = generatedFolder;
                settings.SaveSettings();
            }

            if (!JitterPhysicsProjectSettings.IsValidAssetFolder(profilesFolder)
                || !JitterPhysicsProjectSettings.IsValidAssetFolder(generatedFolder))
            {
                EditorGUILayout.HelpBox("Folders must be project-relative paths under Assets/.", MessageType.Error);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create Defaults"))
                {
                    settings.CreateDefaults(true);
                }

                using (new EditorGUI.DisabledScope(settings.DefaultWorldProfile == null))
                {
                    if (GUILayout.Button("Edit Default Profile"))
                    {
                        Selection.activeObject = settings.DefaultWorldProfile;
                        EditorGUIUtility.PingObject(settings.DefaultWorldProfile);
                    }
                }
            }

            EditorGUILayout.HelpBox(
                "Create Defaults is explicit: merely opening Project Settings creates no assets. "
                + "Create Level may also create the same default profile when none exists.",
                MessageType.None);
        }

        private static void DrawPreviewPreferences()
        {
            EditorGUILayout.LabelField("Personal Scene View preview", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Stored for this editor user only. These choices never change a scene, world "
                + "profile, artifact hash or server payload.",
                MessageType.Info);

            EditorGUILayout.HelpBox(
                "Open the native Jitter Physics overlay in Scene View to choose Sources, Baked, "
                + "Runtime, Scope and Visible/X-Ray. There is one shared personal state; this "
                + "page does not maintain a second preview toggle.",
                MessageType.None);

            if (GUILayout.Button("Reset to Defaults", GUILayout.Width(160f)))
            {
                JitterPhysicsPreviewPreferences.ResetToDefaults();
            }
        }
    }
}
