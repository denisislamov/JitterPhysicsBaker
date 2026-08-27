using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Editor.Baking;
using DataSakura.JitterPhysics.Editor.ProfileEditing;
using UnityEditor;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor
{
    /// <summary>
    /// Presents the common level workflow without running validation or baking during repaint.
    /// All mutations either use serialized properties or an explicit command button so prefab
    /// overrides and Undo remain owned by Unity.
    /// </summary>
    [CustomEditor(typeof(JitterPhysicsLevel))]
    [CanEditMultipleObjects]
    internal sealed class JitterPhysicsLevelEditor : UnityEditor.Editor
    {
        [SerializeField]
        private bool showAdvanced;

        private SerializedProperty levelId;
        private SerializedProperty geometryRoot;
        private SerializedProperty worldProfile;
        private SerializedProperty generatedFolder;
        private SerializedProperty lastArtifactHash;

        private void OnEnable()
        {
            levelId = serializedObject.FindProperty("levelId");
            geometryRoot = serializedObject.FindProperty("geometryRoot");
            worldProfile = serializedObject.FindProperty("worldProfile");
            generatedFolder = serializedObject.FindProperty("generatedFolder");
            lastArtifactHash = serializedObject.FindProperty("lastArtifactHash");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawSection("Level", levelId);
            DrawSection("Geometry Root", geometryRoot);
            DrawSection("Settings", worldProfile);
            DrawBakeStatus();

            serializedObject.ApplyModifiedProperties();

            if (!serializedObject.isEditingMultipleObjects)
            {
                JitterPhysicsWorldProfileActions.Draw((JitterPhysicsLevel)target);
            }

            EditorGUILayout.Space(6f);
            DrawActions();

            EditorGUILayout.Space(4f);
            showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced", true);
            if (showAdvanced)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    serializedObject.Update();
                    EditorGUILayout.PropertyField(generatedFolder);
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.PropertyField(lastArtifactHash);
                    }

                    serializedObject.ApplyModifiedProperties();
                }
            }
        }

        private static void DrawSection(string title, SerializedProperty property)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(property, GUIContent.none);
            EditorGUILayout.Space(4f);
        }

        private void DrawBakeStatus()
        {
            EditorGUILayout.LabelField("Bake Status", EditorStyles.boldLabel);

            string status;
            MessageType type;
            if (serializedObject.isEditingMultipleObjects)
            {
                status = "Select one level to validate or bake it.";
                type = MessageType.Info;
            }
            else
            {
                var inspectedLevel = (JitterPhysicsLevel)target;
                if (!inspectedLevel.HasCanonicalLevelId)
                {
                    status = "Invalid Level ID. Validate for the complete issue list.";
                    type = MessageType.Warning;
                }
                else if (string.IsNullOrEmpty(inspectedLevel.LastArtifactHash))
                {
                    status = "Not baked. Validate before creating the first artifact.";
                    type = MessageType.None;
                }
                else
                {
                    string hash = inspectedLevel.LastArtifactHash;
                    status = "Last bake: " + hash.Substring(0, Mathf.Min(12, hash.Length))
                        + ". Validate to check current geometry.";
                    type = MessageType.Info;
                }
            }

            EditorGUILayout.HelpBox(status, type);
        }

        private void DrawActions()
        {
            bool compact = EditorGUIUtility.currentViewWidth < 330f;
            using (new EditorGUI.DisabledScope(serializedObject.isEditingMultipleObjects))
            {
                if (compact)
                {
                    DrawActionButton("Validate", Validate);
                    DrawBakeButton();
                    DrawActionButton("Open", Open);
                    return;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawActionButton("Validate", Validate);
                    DrawBakeButton();
                    DrawActionButton("Open", Open);
                }
            }
        }

        private void DrawBakeButton()
        {
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                DrawActionButton("Bake", Bake);
            }
        }

        private static void DrawActionButton(string label, System.Action action)
        {
            if (GUILayout.Button(label))
            {
                action();
            }
        }

        private void Validate()
        {
            var inspectedLevel = (JitterPhysicsLevel)target;
            JitterPhysicsBuildResult result = JitterPhysicsBakeCommand.Validate(inspectedLevel);
            LogIssues(result.Issues);
            if (!result.Issues.HasErrors)
            {
                Debug.Log(
                    JitterPhysicsPackage.LogPrefix + $"'{inspectedLevel.LevelId}' is ready to bake "
                    + $"({result.Issues.WarningCount} warnings).",
                    inspectedLevel);
            }
        }

        private void Bake()
        {
            var inspectedLevel = (JitterPhysicsLevel)target;
            JitterPhysicsBakeResult result = JitterPhysicsBakeCommand.Execute(inspectedLevel);
            LogIssues(result.Issues);
            if (!result.Succeeded)
            {
                Debug.LogError(
                    JitterPhysicsPackage.LogPrefix
                    + "Bake failed; the previous artifact was left untouched.",
                    inspectedLevel);
                return;
            }

            Debug.Log(
                JitterPhysicsPackage.LogPrefix + $"Baked '{result.Output.Manifest.LevelId}': "
                + $"{result.Output.Manifest.BodyCount} bodies, {result.Output.Manifest.ShapeCount} shapes, "
                + $"{result.Output.PayloadSize} bytes, hash {result.Output.ArtifactHash}",
                AssetDatabase.LoadAssetAtPath<Object>(result.Output.AssetPath));
            serializedObject.Update();
            Repaint();
        }

        private static void Open()
        {
            JitterPhysicsBakerWindow.Open();
        }

        private static void LogIssues(JitterPhysicsIssueLog issues)
        {
            for (int i = 0; i < issues.Issues.Count; i++)
            {
                JitterPhysicsIssue issue = issues.Issues[i];
                string message = JitterPhysicsPackage.LogPrefix + issue;
                if (issue.IsError)
                {
                    Debug.LogError(message, issue.Context);
                }
                else
                {
                    Debug.LogWarning(message, issue.Context);
                }
            }
        }
    }
}
