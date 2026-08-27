using System;
using UnityEditor;

namespace DataSakura.JitterPhysics.Editor.Diagnostics
{
    internal enum JitterPhysicsPreviewScope
    {
        ActiveOrSelectedLevel = 0,
        AllLoadedLevels = 1,
    }

    internal enum JitterPhysicsPreviewOcclusion
    {
        Visible = 0,
        XRay = 1,
    }

    /// <summary>Personal, non-content settings shared by the Scene View overlay and renderer.</summary>
    internal static class JitterPhysicsPreviewPreferences
    {
        // This is intentionally the old Tools toggle key. The previous choice becomes the
        // Baked layer choice instead of creating a second master-enable state.
        internal const string BakedKey =
            "DataSakura.JitterPhysics.Editor.ShowBakedGeometryOverlay";

        internal const string SourcesKey =
            "DataSakura.JitterPhysics.Editor.PhysicsPreview.Sources";
        internal const string RuntimeKey =
            "DataSakura.JitterPhysics.Editor.PhysicsPreview.Runtime";
        internal const string ScopeKey =
            "DataSakura.JitterPhysics.Editor.PhysicsPreview.Scope";
        internal const string OcclusionKey =
            "DataSakura.JitterPhysics.Editor.PhysicsPreview.Occlusion";

        internal static event Action Changed;

        internal static bool Sources
        {
            get => EditorPrefs.GetBool(SourcesKey, false);
            set => SetBool(SourcesKey, value, false);
        }

        internal static bool Baked
        {
            get => EditorPrefs.GetBool(BakedKey, false);
            set => SetBool(BakedKey, value, false);
        }

        internal static bool Runtime
        {
            get => EditorPrefs.GetBool(RuntimeKey, false);
            set => SetBool(RuntimeKey, value, false);
        }

        internal static JitterPhysicsPreviewScope Scope
        {
            get => (JitterPhysicsPreviewScope)EditorPrefs.GetInt(
                ScopeKey, (int)JitterPhysicsPreviewScope.ActiveOrSelectedLevel);
            set => SetInt(ScopeKey, (int)value, (int)JitterPhysicsPreviewScope.ActiveOrSelectedLevel);
        }

        internal static JitterPhysicsPreviewOcclusion Occlusion
        {
            get => (JitterPhysicsPreviewOcclusion)EditorPrefs.GetInt(
                OcclusionKey, (int)JitterPhysicsPreviewOcclusion.Visible);
            set => SetInt(OcclusionKey, (int)value, (int)JitterPhysicsPreviewOcclusion.Visible);
        }

        internal static void ResetToDefaults()
        {
            EditorPrefs.DeleteKey(BakedKey);
            EditorPrefs.DeleteKey(SourcesKey);
            EditorPrefs.DeleteKey(RuntimeKey);
            EditorPrefs.DeleteKey(ScopeKey);
            EditorPrefs.DeleteKey(OcclusionKey);
            Changed?.Invoke();
            SceneView.RepaintAll();
        }

        private static void SetBool(string key, bool value, bool defaultValue)
        {
            if (EditorPrefs.GetBool(key, defaultValue) == value && EditorPrefs.HasKey(key))
            {
                return;
            }

            EditorPrefs.SetBool(key, value);
            Changed?.Invoke();
            SceneView.RepaintAll();
        }

        private static void SetInt(string key, int value, int defaultValue)
        {
            if (EditorPrefs.GetInt(key, defaultValue) == value && EditorPrefs.HasKey(key))
            {
                return;
            }

            EditorPrefs.SetInt(key, value);
            Changed?.Invoke();
            SceneView.RepaintAll();
        }
    }
}
