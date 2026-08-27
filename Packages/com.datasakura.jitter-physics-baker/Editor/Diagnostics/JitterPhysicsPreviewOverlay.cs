using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
using UnityEngine.UIElements;
using DataSakura.JitterPhysics.Editor.Api;

namespace DataSakura.JitterPhysics.Editor.Diagnostics
{
    /// <summary>Native Scene View controls for the Jitter Physics preview.</summary>
    [Overlay(typeof(SceneView), "Jitter Physics", true)]
    internal sealed class JitterPhysicsPreviewOverlay : Overlay
    {
        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();
            root.style.minWidth = 236f;
            root.style.paddingLeft = 6f;
            root.style.paddingRight = 6f;
            root.style.paddingTop = 4f;
            root.style.paddingBottom = 4f;

            Toggle sources = LayerToggle("Sources", () => JitterPhysicsPreviewApi.Current.Sources,
                value => JitterPhysicsPreviewApi.Apply(JitterPhysicsPreviewApi.Current.WithSources(value)));
            Toggle baked = LayerToggle("Baked", () => JitterPhysicsPreviewApi.Current.Baked,
                value => JitterPhysicsPreviewApi.Apply(JitterPhysicsPreviewApi.Current.WithBaked(value)));
            Toggle runtime = LayerToggle("Runtime", () => JitterPhysicsPreviewApi.Current.Runtime,
                value => JitterPhysicsPreviewApi.Apply(JitterPhysicsPreviewApi.Current.WithRuntime(value)));
            root.Add(sources);
            root.Add(baked);
            root.Add(runtime);

            var scope = new EnumField("Scope", JitterPhysicsPreviewApi.Current.Scope);
            scope.RegisterValueChangedCallback(change =>
                JitterPhysicsPreviewApi.Apply(JitterPhysicsPreviewApi.Current.WithScope(
                    (JitterPhysicsPreviewScope)change.newValue)));
            root.Add(scope);

            var occlusion = new EnumField("Occlusion", JitterPhysicsPreviewApi.Current.Occlusion);
            occlusion.RegisterValueChangedCallback(change =>
                JitterPhysicsPreviewApi.Apply(JitterPhysicsPreviewApi.Current.WithOcclusion(
                    (JitterPhysicsPreviewOcclusion)change.newValue)));
            root.Add(occlusion);

            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            actions.Add(new Button(FrameLevel) { text = "Frame Level" });
            actions.Add(new Button(() => SettingsService.OpenUserPreferences(
                "Preferences/DataSakura/Jitter Physics/Scene Preview")) { text = "Settings" });
            root.Add(actions);

            var status = new Label();
            status.style.whiteSpace = WhiteSpace.Normal;
            status.style.marginTop = 3f;
            root.Add(status);
            root.schedule.Execute(() => status.text = JitterPhysicsBakeGeometryOverlay.StatusText)
                .Every(250);
            root.schedule.Execute(() =>
            {
                JitterPhysicsPreviewState state = JitterPhysicsPreviewApi.Current;
                sources.SetValueWithoutNotify(state.Sources);
                baked.SetValueWithoutNotify(state.Baked);
                runtime.SetValueWithoutNotify(state.Runtime);
                scope.SetValueWithoutNotify(state.Scope);
                occlusion.SetValueWithoutNotify(state.Occlusion);
            }).Every(250);

            return root;
        }

        private static Toggle LayerToggle(
            string text,
            System.Func<bool> read,
            System.Action<bool> write)
        {
            var toggle = new Toggle(text) { value = read() };
            toggle.RegisterValueChangedCallback(change => write(change.newValue));
            return toggle;
        }

        private static void FrameLevel()
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view != null && JitterPhysicsBakeGeometryOverlay.TryGetFrameBounds(out Bounds bounds))
            {
                view.Frame(bounds, false);
            }
        }
    }
}
