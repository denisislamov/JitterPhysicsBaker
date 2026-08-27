using System;
using System.IO;
using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Editor.Baking;
using DataSakura.JitterPhysics.Editor.Export;
using DataSakura.JitterPhysics.UnityArtifact;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DataSakura.JitterPhysics.Demo.Editor
{
    /// <summary>
    /// One command that takes the demo from nothing to a delivered artifact: generate the
    /// scene, bake it, and export the exact bytes next to the standalone web server.
    /// <para>
    /// It is a single entry point so that the editor menu and a headless
    /// <c>-executeMethod</c> run do the same thing. A batch script with its own copy of these
    /// steps is the copy nobody keeps up to date.
    /// </para>
    /// </summary>
    public static class JitterPhysicsDemoPipeline
    {
        /// <summary>Where the exported artifact is delivered, relative to the repository root.</summary>
        public const string ServerArtifactsFolder = "Server/artifacts";

        private const string MenuRoot = "Assets/DataSakura/Jitter Physics/Demo/";

        [MenuItem(MenuRoot + "Create Demo Scene And Bake", false, 100)]
        public static void RunFromMenu()
        {
            Run();
        }

        [MenuItem(MenuRoot + "Export Baked Demo Artifact To Server", false, 101)]
        public static void ExportFromMenu()
        {
            var asset = AssetDatabase.LoadAssetAtPath<JitterPhysicsArtifactAsset>(
                JitterPhysicsArtifactPaths.ArtifactAssetPath(
                    JitterPhysicsDemoScene.GeneratedFolder, JitterPhysicsDemoScene.LevelId));

            if (asset == null)
            {
                Debug.LogError(
                    JitterPhysicsPackage.LogPrefix
                    + "The demo level has not been baked yet; run 'Create Demo Scene And Bake' first.");
                return;
            }

            Export(asset, asset.LevelId);
        }

        /// <summary>Folder holding the committed demo scenes.</summary>
        public const string ScenesFolder = "Assets/JitterPhysicsBaker/Demo/Scenes";

        [MenuItem(MenuRoot + "Bake All Demo Scenes", false, 102)]
        public static void BakeAllFromMenu()
        {
            BakeAllScenes();
        }

        /// <summary>
        /// Opens every committed demo scene, bakes its level and exports the artifact to the server
        /// folder. This is the authoritative path: it replaces the seed artifacts with the exact
        /// bytes a Unity bake produces from the scenes the user can open and edit.
        /// </summary>
        /// <remarks>
        /// It refuses to run in Play Mode and asks to save unsaved changes first, because opening
        /// scenes one after another discards whatever is in the current one.
        /// </remarks>
        public static bool BakeAllScenes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError(JitterPhysicsPackage.LogPrefix + "Leave Play Mode before baking.");
                return false;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return false;
            }

            string[] scenePaths = Directory.GetFiles(ScenesFolder, "*.unity", SearchOption.TopDirectoryOnly);
            Array.Sort(scenePaths, StringComparer.Ordinal);

            if (scenePaths.Length == 0)
            {
                Debug.LogError(
                    JitterPhysicsPackage.LogPrefix
                    + $"No scenes found under '{ScenesFolder}'. Run tools/author-demo-scenes.py first.");
                return false;
            }

            int baked = 0;
            foreach (string scenePath in scenePaths)
            {
                if (BakeScene(scenePath))
                {
                    baked++;
                }
            }

            Debug.Log(
                JitterPhysicsPackage.LogPrefix
                + $"Baked {baked} of {scenePaths.Length} demo scenes into {ServerArtifactsFolder}.");

            return baked == scenePaths.Length;
        }

        private static bool BakeScene(string scenePath)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            JitterPhysicsLevel level = FindLevel(scene);
            if (level == null)
            {
                Debug.LogWarning(
                    JitterPhysicsPackage.LogPrefix + $"'{scenePath}' has no JitterPhysicsLevel; skipped.");
                return false;
            }

            JitterPhysicsBakeResult result = JitterPhysicsBakeCommand.Execute(level);
            LogIssues(result.Issues);

            if (!result.Succeeded)
            {
                Debug.LogError(
                    JitterPhysicsPackage.LogPrefix + $"Baking '{scenePath}' failed; nothing was written.", level);
                return false;
            }

            var asset = AssetDatabase.LoadAssetAtPath<JitterPhysicsArtifactAsset>(result.Output.AssetPath);
            if (asset == null)
            {
                Debug.LogError(
                    JitterPhysicsPackage.LogPrefix
                    + $"The bake reported '{result.Output.AssetPath}', but it did not import.");
                return false;
            }

            Debug.Log(
                JitterPhysicsPackage.LogPrefix
                + $"Baked '{result.Output.Manifest.LevelId}': {result.Output.Manifest.BodyCount} bodies, "
                + $"{result.Output.Manifest.ShapeCount} shapes, hash {result.Output.ArtifactHash}");

            WireRuntimeArtifact(scene, level, asset);
            return Export(asset, result.Output.Manifest.LevelId);
        }

        private static void WireRuntimeArtifact(
            Scene scene,
            JitterPhysicsLevel level,
            JitterPhysicsArtifactAsset asset)
        {
            // The demo runtime is optional, so the editor pipeline cannot reference its assembly.
            // When integration is installed, bind the exact baked asset into the scene so the same
            // UI also works in a player where AssetDatabase lookup is unavailable.
            Type viewerType = Type.GetType(
                "DataSakura.JitterPhysics.Demo.JitterPhysicsDemoRuntimeViewer, "
                + "DataSakura.JitterPhysics.Demo.Runtime");
            if (viewerType == null)
            {
                return;
            }

            Component viewer = level.GetComponent(viewerType);
            if (viewer == null)
            {
                viewer = level.gameObject.AddComponent(viewerType);
            }

            var serialized = new SerializedObject(viewer);
            serialized.FindProperty("artifact").objectReferenceValue = asset;
            serialized.FindProperty("level").objectReferenceValue = level;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static JitterPhysicsLevel FindLevel(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                JitterPhysicsLevel level = root.GetComponentInChildren<JitterPhysicsLevel>(true);
                if (level != null)
                {
                    return level;
                }
            }

            return null;
        }

        /// <summary>
        /// Entry point for <c>Unity -batchmode -executeMethod</c>. Exits with a non-zero code
        /// on failure, because a CI step that reports success without an artifact is worse
        /// than one that fails loudly.
        /// </summary>
        public static void RunBatch()
        {
            bool succeeded;
            try
            {
                succeeded = Run();
            }
            catch (Exception exception)
            {
                Debug.LogError(JitterPhysicsPackage.LogPrefix + "Demo pipeline threw: " + exception);
                succeeded = false;
            }

            EditorApplication.Exit(succeeded ? 0 : 1);
        }

        /// <summary>Generates the scene, bakes it and exports the result. True when everything worked.</summary>
        public static bool Run()
        {
            JitterPhysicsLevel level = JitterPhysicsDemoScene.Create();

            JitterPhysicsBakeResult result = JitterPhysicsBakeCommand.Execute(level);
            LogIssues(result.Issues);

            if (!result.Succeeded)
            {
                Debug.LogError(
                    JitterPhysicsPackage.LogPrefix + "Demo bake failed; nothing was written.", level);
                return false;
            }

            Debug.Log(
                JitterPhysicsPackage.LogPrefix
                + $"Baked '{result.Output.Manifest.LevelId}': {result.Output.Manifest.BodyCount} bodies, "
                + $"{result.Output.Manifest.ShapeCount} shapes, {result.Output.Manifest.TriangleCount} triangles, "
                + $"{result.Output.PayloadSize} bytes, hash {result.Output.ArtifactHash}");

            var asset = AssetDatabase.LoadAssetAtPath<JitterPhysicsArtifactAsset>(result.Output.AssetPath);
            if (asset == null)
            {
                Debug.LogError(
                    JitterPhysicsPackage.LogPrefix
                    + $"The bake reported '{result.Output.AssetPath}', but it did not import.");
                return false;
            }

            WireRuntimeArtifact(level.gameObject.scene, level, asset);
            return Export(asset, asset.LevelId);
        }

        private static bool Export(JitterPhysicsArtifactAsset asset, string levelId)
        {
            string targetFolder = Path.Combine(RepositoryRoot(), ServerArtifactsFolder);

            // Stale deliveries of this level are removed first: the server hosts one manifest per
            // level, and two of the same level would make "which world is it running" depend on
            // directory order.
            RemovePreviousDelivery(targetFolder, levelId);

            JitterPhysicsExportResult export = JitterPhysicsArtifactExporter.ExportBinary(asset, targetFolder);
            LogIssues(export.Issues);

            if (!export.Succeeded)
            {
                Debug.LogError(JitterPhysicsPackage.LogPrefix + "Exporting the demo artifact failed.", asset);
                return false;
            }

            for (int i = 0; i < export.Files.Length; i++)
            {
                Debug.Log(JitterPhysicsPackage.LogPrefix + "Exported " + export.Files[i]);
            }

            return true;
        }

        private static void RemovePreviousDelivery(string targetFolder, string levelId)
        {
            if (!Directory.Exists(targetFolder))
            {
                return;
            }

            string prefix = levelId + ".";
            foreach (string path in Directory.GetFiles(targetFolder))
            {
                string name = Path.GetFileName(path);
                if (!name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (name.EndsWith(JitterPhysicsArtifactNaming.BinaryExtension, StringComparison.Ordinal)
                    || name.EndsWith(JitterPhysicsArtifactNaming.ManifestExtension, StringComparison.Ordinal))
                {
                    File.Delete(path);
                }
            }
        }

        private static string RepositoryRoot()
        {
            return Path.GetDirectoryName(Application.dataPath) ?? Directory.GetCurrentDirectory();
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




