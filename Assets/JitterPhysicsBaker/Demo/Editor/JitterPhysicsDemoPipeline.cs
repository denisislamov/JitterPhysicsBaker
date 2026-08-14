using System;
using System.IO;
using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Editor.Baking;
using DataSakura.JitterPhysics.Editor.Export;
using DataSakura.JitterPhysics.UnityArtifact;
using UnityEditor;
using UnityEngine;

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

        private const string MenuRoot = "Tools/DataSakura/Jitter Physics/Demo/";

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

            Export(asset);
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

            return Export(asset);
        }

        private static bool Export(JitterPhysicsArtifactAsset asset)
        {
            string targetFolder = Path.Combine(RepositoryRoot(), ServerArtifactsFolder);

            // Stale deliveries are removed first: the server picks a manifest out of this
            // folder, and two manifests of the same level would make "which world is it
            // running" depend on directory order.
            RemovePreviousDelivery(targetFolder);

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

        private static void RemovePreviousDelivery(string targetFolder)
        {
            if (!Directory.Exists(targetFolder))
            {
                return;
            }

            string prefix = JitterPhysicsDemoScene.LevelId + ".";
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

