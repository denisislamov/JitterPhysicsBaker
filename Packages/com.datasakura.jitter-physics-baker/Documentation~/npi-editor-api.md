# NPI editor API handoff

The integration boundary is the editor-only `DataSakura.JitterPhysics.Editor` assembly. A
consumer adds that assembly to its own Editor asmdef; runtime, portable and server assemblies do
not reference this API. The package has no NPI, EFT or navigation dependency.

## Level ID ownership

Standalone authoring keeps the existing component-owned ID:

```csharp
using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Editor.Api;

namespace MyGame.EditorTools
{
    public static class NpiPhysicsBakeAdapter
    {
        public static JitterPhysicsEditorResult ValidateStandalone(JitterPhysicsLevel level)
        {
            return JitterPhysicsEditorApi.Validate(
                level,
                JitterPhysicsLevelIdBinding.Standalone);
        }

        public static JitterPhysicsEditorResult ValidateManaged(
            JitterPhysicsLevel level,
            string externalLevelId)
        {
            JitterPhysicsLevelIdBinding id = JitterPhysicsLevelIdBinding.External(
                "NPI",
                externalLevelId);
            return JitterPhysicsEditorApi.Validate(level, id);
        }

        public static JitterPhysicsEditorResult BakeManaged(
            JitterPhysicsLevel level,
            string externalLevelId)
        {
            JitterPhysicsLevelIdBinding id = JitterPhysicsLevelIdBinding.External(
                "NPI",
                externalLevelId);
            return JitterPhysicsEditorApi.Bake(level, id);
        }

        public static JitterPhysicsEditorResult ReadManaged(
            JitterPhysicsLevel level,
            string externalLevelId)
        {
            JitterPhysicsLevelIdBinding id = JitterPhysicsLevelIdBinding.External(
                "NPI",
                externalLevelId);
            return JitterPhysicsEditorApi.ReadSummary(level, id);
        }
    }
}
```

An external owner is explicit and is represented only by strings, so no consumer assembly type
leaks into the package. Put this adapter in an Editor-only assembly definition that references
`DataSakura.JitterPhysics.Authoring` and `DataSakura.JitterPhysics.Editor`.

The externally managed ID is used for that operation and is not written back to the standalone
`JitterPhysicsLevel`. Empty owners, non-canonical IDs and IDs already owned by another loaded
level are rejected before baking.

`JitterPhysicsEditorResult` reports status, ownership, owner, resolved Level ID, artifact/payload/
manifest paths, full SHA-256 digest, payload size, available body/shape/vertex/triangle counts and
the existing `JitterPhysicsIssueLog`. `ReadSummary` verifies the stored payload and manifest but
does not assign IDs, import assets, change preview preferences or write files.

## Shared preview state

`JitterPhysicsPreviewApi.Current` is an immutable snapshot of the exact EditorPrefs-backed state
used by the package Scene View overlay. Reading it has no side effects. Apply a modified copy with
`JitterPhysicsPreviewApi.Apply(...)`; this updates the existing Sources/Baked/Runtime, Scope and
Visible/X-Ray keys and raises one shared change notification. A consumer must not add a second
master toggle.

```csharp
using DataSakura.JitterPhysics.Editor.Api;

namespace MyGame.EditorTools
{
    public static class NpiPhysicsPreviewPreset
    {
        public static void ShowBakedGeometry()
        {
            JitterPhysicsPreviewState state = JitterPhysicsPreviewApi.Current;
            JitterPhysicsPreviewApi.Apply(state.WithBaked(true));
        }
    }
}
```

Preview access needs neither Jitter2 nor a navigation package. Runtime preview data remains the
portable `IJitterPhysicsRuntimePreviewSource` contract and is optional.

## Migration notes

- Replace direct calls to `JitterPhysicsBakeCommand` with `JitterPhysicsEditorApi` in external
  adapters. Package-owned menus and windows may continue using their command layer.
- Replace copied path/hash/count DTOs with `JitterPhysicsEditorResult`.
- Replace copied preview toggles or direct EditorPrefs keys with `JitterPhysicsPreviewApi`.
- Do not pass a runtime compatibility hash. The editor API derives it from the one project-owned
  Jitter2 and refuses missing, incompatible or duplicate installations.
- JP-04 legacy file migration stays an explicit package action. The JP-05 API reports the current
  paths and never moves `.meta` files while reading.

## Delivery fixtures

- `Tests/Editor/JitterPhysicsEditorApiTests.cs`: standalone/external ownership, invalid bindings,
  ID conflict, separate bake result, read-only summary and identical file/Unity loader bytes.
- `Tests/Editor/JitterPhysicsGeometryOverlayTests.cs`: side-effect-free read and shared overlay/API
  state.
- `Samples~/Demos/Editor/JitterPhysicsEditorApiExample.cs`: minimal consumer caller.
- `Samples~/Demos/Tests/PlayMode/JitterPhysicsSampleDeliveryTests.cs`: imported sample runtime load,
  Jitter world construction and exact artifact verification.

## Verified revision

JP-05 implementation commit:

`b9a2a116b97b4fce0243a9ca7046f10144d308b7`

Verified against that exact tree on Unity `6000.3.19f1`:

- package metadata and Jitter2 lock checks passed;
- portable/server tests: 78/78;
- Unity EditMode: 97/97;
- Unity PlayMode: 57/57;
- isolated delivery: one project-owned Jitter2, editor API fixtures 7/7, imported sample
  runtime fixture 1/1.

Use only the exact recorded commit or a later package tag containing it for NPI-01. A branch name
is not delivery evidence.
