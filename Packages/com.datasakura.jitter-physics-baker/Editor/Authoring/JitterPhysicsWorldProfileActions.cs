using System.IO;
using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Editor.Settings;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor.ProfileEditing
{
    /// <summary>Explicit profile asset operations shared by the main window and Inspector.</summary>
    internal static class JitterPhysicsWorldProfileActions
    {
        internal static void Draw(JitterPhysicsLevel level)
        {
            if (level == null)
            {
                return;
            }

            JitterPhysicsWorldProfile profile = level.WorldProfile;
            if (profile != null)
            {
                int users = CountLoadedLevelUsers(profile);
                EditorGUILayout.HelpBox(
                    users > 1
                        ? $"Shared profile: editing it changes {users} loaded levels. Use Make Local Copy to isolate this level."
                        : "This profile is shared authoring data used by both Unity and the .NET server.",
                    users > 1 ? MessageType.Warning : MessageType.Info);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(profile == null))
                {
                    if (GUILayout.Button("Edit"))
                    {
                        Selection.activeObject = profile;
                        EditorGUIUtility.PingObject(profile);
                    }
                }

                if (GUILayout.Button("New"))
                {
                    CreateAndAssign(level);
                }

                using (new EditorGUI.DisabledScope(profile == null))
                {
                    if (GUILayout.Button("Make Local Copy"))
                    {
                        MakeLocalCopy(level);
                    }
                }
            }
        }

        internal static JitterPhysicsWorldProfile MakeLocalCopy(JitterPhysicsLevel level)
        {
            if (level == null || level.WorldProfile == null)
            {
                return null;
            }

            JitterPhysicsProjectSettings settings = JitterPhysicsProjectSettings.instance;
            string path = settings.ProfilesFolder + "/" + SafeLevelName(level) + "_WorldProfile.asset";
            JitterPhysicsWorldProfile copy = settings.CreateProfile(path, level.WorldProfile);
            Undo.RegisterCreatedObjectUndo(copy, "Create local Jitter Physics world profile");
            Assign(level, copy, "Make local Jitter Physics world profile");
            Selection.activeObject = copy;
            EditorGUIUtility.PingObject(copy);
            return copy;
        }

        private static void CreateAndAssign(JitterPhysicsLevel level)
        {
            JitterPhysicsProjectSettings settings = JitterPhysicsProjectSettings.instance;
            string defaultName = SafeLevelName(level) + "_WorldProfile.asset";
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Jitter Physics World Profile",
                Path.GetFileNameWithoutExtension(defaultName),
                "asset",
                "Create a profile for this level. It starts from the project default profile.",
                settings.ProfilesFolder);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            JitterPhysicsWorldProfile created = settings.CreateProfile(path, settings.DefaultWorldProfile);
            Undo.RegisterCreatedObjectUndo(created, "Create Jitter Physics world profile");
            Assign(level, created, "Assign new Jitter Physics world profile");
            Selection.activeObject = created;
            EditorGUIUtility.PingObject(created);
        }

        private static void Assign(
            JitterPhysicsLevel level,
            JitterPhysicsWorldProfile profile,
            string undoName)
        {
            Undo.RecordObject(level, undoName);
            var serialized = new SerializedObject(level);
            serialized.FindProperty("worldProfile").objectReferenceValue = profile;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(level);
            if (level.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(level.gameObject.scene);
            }
        }

        private static int CountLoadedLevelUsers(JitterPhysicsWorldProfile profile)
        {
            int count = 0;
            JitterPhysicsLevel[] levels = Resources.FindObjectsOfTypeAll<JitterPhysicsLevel>();
            for (int i = 0; i < levels.Length; i++)
            {
                if (levels[i] != null
                    && levels[i].gameObject.scene.IsValid()
                    && levels[i].WorldProfile == profile)
                {
                    count++;
                }
            }

            return count;
        }

        private static string SafeLevelName(JitterPhysicsLevel level)
        {
            return string.IsNullOrEmpty(level.LevelId) ? "Level" : level.LevelId;
        }
    }
}
