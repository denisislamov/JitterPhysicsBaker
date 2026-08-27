using System.IO;
using System.Linq;
using System.Reflection;
using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Editor.Diagnostics;
using DataSakura.JitterPhysics.Editor.ProfileEditing;
using DataSakura.JitterPhysics.Editor.Settings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor.Tests
{
    /// <summary>JP-02 coverage for shared project defaults and isolated personal preview.</summary>
    public sealed class JitterPhysicsSettingsTests
    {
        private const string TestFolder = "Assets/__JitterPhysicsSettingsTests";

        private JitterPhysicsProjectSettings settings;
        private JitterPhysicsWorldProfile oldDefaultProfile;
        private string oldProfilesFolder;
        private string oldGeneratedFolder;
        private bool settingsFileExisted;
        private byte[] oldSettingsFile;
        private bool previewKeyExisted;
        private bool oldPreview;

        [SetUp]
        public void SetUp()
        {
            settings = JitterPhysicsProjectSettings.instance;
            oldDefaultProfile = settings.DefaultWorldProfile;
            oldProfilesFolder = settings.ProfilesFolder;
            oldGeneratedFolder = settings.GeneratedFolder;
            settingsFileExisted = File.Exists(JitterPhysicsProjectSettings.SettingsFilePath);
            oldSettingsFile = settingsFileExisted
                ? File.ReadAllBytes(JitterPhysicsProjectSettings.SettingsFilePath)
                : null;
            previewKeyExisted = EditorPrefs.HasKey(JitterPhysicsBakeGeometryOverlay.PreferenceKey);
            oldPreview = JitterPhysicsBakeGeometryOverlay.Enabled;
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
            AssetDatabase.DeleteAsset(TestFolder);

            settings.DefaultWorldProfile = oldDefaultProfile;
            settings.ProfilesFolder = oldProfilesFolder;
            settings.GeneratedFolder = oldGeneratedFolder;
            settings.SaveSettings();
            if (settingsFileExisted)
            {
                File.WriteAllBytes(JitterPhysicsProjectSettings.SettingsFilePath, oldSettingsFile);
            }
            else if (File.Exists(JitterPhysicsProjectSettings.SettingsFilePath))
            {
                File.Delete(JitterPhysicsProjectSettings.SettingsFilePath);
            }

            if (previewKeyExisted)
            {
                JitterPhysicsBakeGeometryOverlay.SetEnabled(oldPreview);
            }
            else
            {
                JitterPhysicsBakeGeometryOverlay.ResetPreference();
            }

            AssetDatabase.Refresh();
        }

        [Test]
        public void ProjectAndPreviewProvidersHaveDistinctNativePathsAndScopes()
        {
            SettingsProvider project = InvokeProvider("CreateProjectProvider");
            SettingsProvider preferences = InvokeProvider("CreatePreferencesProvider");

            Assert.That(project.settingsPath, Is.EqualTo("Project/DataSakura/Jitter Physics"));
            Assert.That(project.scope, Is.EqualTo(SettingsScope.Project));
            Assert.That(preferences.settingsPath,
                Is.EqualTo("Preferences/DataSakura/Jitter Physics/Scene Preview"));
            Assert.That(preferences.scope, Is.EqualTo(SettingsScope.User));
            Assert.That(JitterPhysicsBakeGeometryOverlay.PreferenceKey,
                Is.EqualTo("DataSakura.JitterPhysics.Editor.ShowBakedGeometryOverlay"));
        }

        [Test]
        public void PreviewPreferenceResetDoesNotChangeProjectSettings()
        {
            string before = EditorJsonUtility.ToJson(settings);

            JitterPhysicsBakeGeometryOverlay.SetEnabled(true);
            JitterPhysicsBakeGeometryOverlay.ResetPreference();

            Assert.That(JitterPhysicsBakeGeometryOverlay.Enabled, Is.False);
            Assert.That(EditorPrefs.HasKey(JitterPhysicsBakeGeometryOverlay.PreferenceKey), Is.False);
            Assert.That(EditorJsonUtility.ToJson(settings), Is.EqualTo(before));
        }

        [Test]
        public void LocalCopyPreservesValuesAndReassignsOnlyTheRequestedLevel()
        {
            settings.ProfilesFolder = TestFolder;
            settings.DefaultWorldProfile = null;
            JitterPhysicsWorldProfile shared = settings.CreateDefaults(false);

            var firstObject = new GameObject("First");
            var secondObject = new GameObject("Second");
            try
            {
                JitterPhysicsLevel first = firstObject.AddComponent<JitterPhysicsLevel>();
                JitterPhysicsLevel second = secondObject.AddComponent<JitterPhysicsLevel>();
                Assign(first, shared);
                Assign(second, shared);

                JitterPhysicsWorldProfile local = JitterPhysicsWorldProfileActions.MakeLocalCopy(first);

                Assert.That(local, Is.Not.Null);
                Assert.That(local, Is.Not.SameAs(shared));
                Assert.That(first.WorldProfile, Is.SameAs(local));
                Assert.That(second.WorldProfile, Is.SameAs(shared));
                PhysicsWorldSettings localSettings = local.ToWorldSettings();
                PhysicsWorldSettings sharedSettings = shared.ToWorldSettings();
                Assert.That(localSettings.Gravity.X, Is.EqualTo(sharedSettings.Gravity.X));
                Assert.That(localSettings.Gravity.Y, Is.EqualTo(sharedSettings.Gravity.Y));
                Assert.That(localSettings.Gravity.Z, Is.EqualTo(sharedSettings.Gravity.Z));
                Assert.That(localSettings.TickRate, Is.EqualTo(sharedSettings.TickRate));
                Assert.That(localSettings.SubstepCount, Is.EqualTo(sharedSettings.SubstepCount));
                Assert.That(localSettings.SolverIterations, Is.EqualTo(sharedSettings.SolverIterations));
                Assert.That(localSettings.RelaxationIterations,
                    Is.EqualTo(sharedSettings.RelaxationIterations));
                Assert.That(localSettings.AllowDeactivation,
                    Is.EqualTo(sharedSettings.AllowDeactivation));

                Undo.PerformUndo();
                Assert.That(first.WorldProfile, Is.SameAs(shared), "Profile assignment must support Undo.");
                Assert.That(second.WorldProfile, Is.SameAs(shared));
            }
            finally
            {
                Object.DestroyImmediate(firstObject);
                Object.DestroyImmediate(secondObject);
            }
        }

        [Test]
        public void MerelyReadingSettingsDoesNotCreateDefaults()
        {
            settings.DefaultWorldProfile = null;
            settings.ProfilesFolder = TestFolder;

            JitterPhysicsWorldProfile read = settings.DefaultWorldProfile;

            Assert.That(read, Is.Null);
            Assert.That(AssetDatabase.IsValidFolder(TestFolder), Is.False);
        }

        [Test]
        public void LocalCopyOnPrefabInstanceIsAWorldProfileOverride()
        {
            settings.ProfilesFolder = TestFolder;
            settings.DefaultWorldProfile = null;
            JitterPhysicsWorldProfile shared = settings.CreateDefaults(false);

            var source = new GameObject("Prefab level");
            JitterPhysicsLevel sourceLevel = source.AddComponent<JitterPhysicsLevel>();
            Assign(sourceLevel, shared);
            string prefabPath = TestFolder + "/Level.prefab";
            PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
            Object.DestroyImmediate(source);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            try
            {
                JitterPhysicsLevel instanceLevel = instance.GetComponent<JitterPhysicsLevel>();
                JitterPhysicsWorldProfile local = JitterPhysicsWorldProfileActions.MakeLocalCopy(instanceLevel);

                Assert.That(instanceLevel.WorldProfile, Is.SameAs(local));
                PropertyModification modification = PrefabUtility.GetPropertyModifications(instanceLevel)
                    .SingleOrDefault(item => item.propertyPath == "worldProfile");
                Assert.That(modification, Is.Not.Null);
                Assert.That(modification.objectReference, Is.SameAs(local));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static SettingsProvider InvokeProvider(string methodName)
        {
            MethodInfo method = typeof(JitterPhysicsSettingsProviders).GetMethod(
                methodName,
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            return (SettingsProvider)method.Invoke(null, null);
        }

        private static void Assign(JitterPhysicsLevel level, JitterPhysicsWorldProfile profile)
        {
            var serialized = new SerializedObject(level);
            serialized.FindProperty("worldProfile").objectReferenceValue = profile;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
