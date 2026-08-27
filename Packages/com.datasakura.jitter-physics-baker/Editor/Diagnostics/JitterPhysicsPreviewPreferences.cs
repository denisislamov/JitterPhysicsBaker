using System;
using DataSakura.JitterPhysics.Editor.Api;
using UnityEditor;

namespace DataSakura.JitterPhysics.Editor.Diagnostics
{
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

        internal static JitterPhysicsPreviewState ReadState()
        {
            return new JitterPhysicsPreviewState(Sources, Baked, Runtime, Scope, Occlusion);
        }

        internal static void Apply(JitterPhysicsPreviewState state)
        {
            bool changed = SetBoolWithoutNotification(SourcesKey, state.Sources, false);
            changed |= SetBoolWithoutNotification(BakedKey, state.Baked, false);
            changed |= SetBoolWithoutNotification(RuntimeKey, state.Runtime, false);
            changed |= SetIntWithoutNotification(
                ScopeKey, (int)state.Scope, (int)JitterPhysicsPreviewScope.ActiveOrSelectedLevel);
            changed |= SetIntWithoutNotification(
                OcclusionKey, (int)state.Occlusion, (int)JitterPhysicsPreviewOcclusion.Visible);
            if (!changed) return;

            Changed?.Invoke();
            SceneView.RepaintAll();
        }

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
            if (!SetBoolWithoutNotification(key, value, defaultValue)) return;
            Changed?.Invoke();
            SceneView.RepaintAll();
        }

        private static void SetInt(string key, int value, int defaultValue)
        {
            if (!SetIntWithoutNotification(key, value, defaultValue)) return;
            Changed?.Invoke();
            SceneView.RepaintAll();
        }

        private static bool SetBoolWithoutNotification(string key, bool value, bool defaultValue)
        {
            if (EditorPrefs.GetBool(key, defaultValue) == value && EditorPrefs.HasKey(key)) return false;
            EditorPrefs.SetBool(key, value);
            return true;
        }

        private static bool SetIntWithoutNotification(string key, int value, int defaultValue)
        {
            if (EditorPrefs.GetInt(key, defaultValue) == value && EditorPrefs.HasKey(key)) return false;
            EditorPrefs.SetInt(key, value);
            return true;
        }
    }
}
