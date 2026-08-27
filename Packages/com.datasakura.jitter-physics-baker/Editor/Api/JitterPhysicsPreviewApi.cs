using System;
using DataSakura.JitterPhysics.Editor.Diagnostics;

namespace DataSakura.JitterPhysics.Editor.Api
{
    /// <summary>Which loaded levels the shared Scene View preview includes.</summary>
    public enum JitterPhysicsPreviewScope
    {
        /// <summary>The active level, or the level containing the current selection.</summary>
        ActiveOrSelectedLevel = 0,

        /// <summary>Every Jitter Physics level in loaded scenes.</summary>
        AllLoadedLevels = 1,
    }

    /// <summary>How preview geometry interacts with Scene View depth.</summary>
    public enum JitterPhysicsPreviewOcclusion
    {
        /// <summary>Geometry behind scene surfaces is hidden.</summary>
        Visible = 0,

        /// <summary>Geometry is visible through scene surfaces.</summary>
        XRay = 1,
    }

    /// <summary>Immutable snapshot of the package's one shared preview state.</summary>
    public sealed class JitterPhysicsPreviewState
    {
        /// <summary>Creates one complete immutable preview-state snapshot.</summary>
        public JitterPhysicsPreviewState(
            bool sources,
            bool baked,
            bool runtime,
            JitterPhysicsPreviewScope scope,
            JitterPhysicsPreviewOcclusion occlusion)
        {
            if (!Enum.IsDefined(typeof(JitterPhysicsPreviewScope), scope))
                throw new ArgumentOutOfRangeException(nameof(scope));
            if (!Enum.IsDefined(typeof(JitterPhysicsPreviewOcclusion), occlusion))
                throw new ArgumentOutOfRangeException(nameof(occlusion));

            Sources = sources;
            Baked = baked;
            Runtime = runtime;
            Scope = scope;
            Occlusion = occlusion;
        }

        /// <summary>Whether authored source geometry is visible.</summary>
        public bool Sources { get; }

        /// <summary>Whether the saved bake is visible.</summary>
        public bool Baked { get; }

        /// <summary>Whether registered runtime geometry is visible.</summary>
        public bool Runtime { get; }

        /// <summary>Loaded-level selection scope.</summary>
        public JitterPhysicsPreviewScope Scope { get; }

        /// <summary>Depth behavior.</summary>
        public JitterPhysicsPreviewOcclusion Occlusion { get; }

        /// <summary>Returns a copy with the Sources layer changed.</summary>
        public JitterPhysicsPreviewState WithSources(bool value) =>
            new JitterPhysicsPreviewState(value, Baked, Runtime, Scope, Occlusion);

        /// <summary>Returns a copy with the Baked layer changed.</summary>
        public JitterPhysicsPreviewState WithBaked(bool value) =>
            new JitterPhysicsPreviewState(Sources, value, Runtime, Scope, Occlusion);

        /// <summary>Returns a copy with the Runtime layer changed.</summary>
        public JitterPhysicsPreviewState WithRuntime(bool value) =>
            new JitterPhysicsPreviewState(Sources, Baked, value, Scope, Occlusion);

        /// <summary>Returns a copy with the loaded-level scope changed.</summary>
        public JitterPhysicsPreviewState WithScope(JitterPhysicsPreviewScope value) =>
            new JitterPhysicsPreviewState(Sources, Baked, Runtime, value, Occlusion);

        /// <summary>Returns a copy with the depth behavior changed.</summary>
        public JitterPhysicsPreviewState WithOcclusion(JitterPhysicsPreviewOcclusion value) =>
            new JitterPhysicsPreviewState(Sources, Baked, Runtime, Scope, value);
    }

    /// <summary>
    /// Public access to the exact state used by the package overlay. Reading is side-effect free;
    /// applying a state updates the existing EditorPrefs keys rather than creating another toggle.
    /// </summary>
    public static class JitterPhysicsPreviewApi
    {
        /// <summary>Current shared preview state. This getter does not write preferences or repaint.</summary>
        public static JitterPhysicsPreviewState Current => JitterPhysicsPreviewPreferences.ReadState();

        /// <summary>Raised after the shared preview state changes.</summary>
        public static event Action Changed
        {
            add => JitterPhysicsPreviewPreferences.Changed += value;
            remove => JitterPhysicsPreviewPreferences.Changed -= value;
        }

        /// <summary>Applies a complete state to the package overlay and every external caller.</summary>
        public static void Apply(JitterPhysicsPreviewState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            JitterPhysicsPreviewPreferences.Apply(state);
        }
    }
}
