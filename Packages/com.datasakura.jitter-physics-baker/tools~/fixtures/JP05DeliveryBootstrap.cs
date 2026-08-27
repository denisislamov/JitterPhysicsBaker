using System;
using System.Reflection;
using DataSakura.JitterPhysics.Editor.Install;
using UnityEditor;

namespace DataSakura.JitterPhysics.DeliveryFixture
{
    /// <summary>Batch entry points used only by the isolated JP-05 delivery project.</summary>
    public static class JP05DeliveryBootstrap
    {
        /// <summary>Installs the package-owned fallback as the project's single Jitter2.</summary>
        public static void InstallJitter()
        {
            Require(JitterPhysicsInstaller.InstallJitter(), "Jitter2 install");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>Installs the adapter against the one project-owned Jitter2.</summary>
        public static void InstallIntegration()
        {
            Require(JitterPhysicsInstaller.InstallIntegration(), "integration install");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>Invokes the imported sample builder without making the fixture depend on it.</summary>
        public static void BuildBouncingBallSample()
        {
            Type type = Type.GetType(
                "DataSakura.JitterPhysics.Samples.Editor.JitterPhysicsSampleScenes, "
                + "DataSakura.JitterPhysics.Samples.Editor",
                throwOnError: true);
            MethodInfo method = type.GetMethod("BuildBouncingBall", BindingFlags.Public | BindingFlags.Static);
            if (method == null) throw new MissingMethodException(type.FullName, "BuildBouncingBall");
            method.Invoke(null, null);
            string[] scenes = AssetDatabase.FindAssets("SampleBouncingBall t:Scene");
            if (scenes.Length != 1)
                throw new InvalidOperationException("Expected exactly one generated SampleBouncingBall scene.");
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(AssetDatabase.GUIDToAssetPath(scenes[0]), true),
            };
            AssetDatabase.SaveAssets();
        }

        private static void Require(JitterPhysicsInstallResult result, string operation)
        {
            if (!result.Succeeded) throw new InvalidOperationException(operation + " failed:\n" + result.Issues.Format());
        }
    }
}
