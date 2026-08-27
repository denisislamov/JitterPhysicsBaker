using System;
using System.Collections.Generic;
using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Editor.Baking;
using DataSakura.JitterPhysics.UnityArtifact;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DataSakura.JitterPhysics.Editor.Diagnostics
{
    /// <summary>Cached Scene View renderer for authored, baked and active-runtime physics.</summary>
    /// <remarks>
    /// Collider conversion and artifact decoding happen after invalidation, never from a Scene
    /// View repaint. The drawing path consumes immutable records and cannot bake or mutate content.
    /// </remarks>
    [InitializeOnLoad]
    internal static class JitterPhysicsBakeGeometryOverlay
    {
        internal const string PreferenceKey = JitterPhysicsPreviewPreferences.BakedKey;

        // JP-03 70s muted palette. Line style also identifies every layer without color.
        internal static readonly Color SourcesColor = Hex(0xC5AC83, 0.96f);
        internal static readonly Color BakedColor = Hex(0xBD984F, 0.96f);
        internal static readonly Color RuntimeColor = Hex(0xD5B975, 1f);
        internal static readonly Color ChangedColor = Hex(0xA87945, 1f);
        internal static readonly Color ErrorColor = Hex(0x684779, 1f);
        internal static readonly Color ErrorBackdropColor = Hex(0xF0DEB8, 0.92f);

        private static readonly List<LevelPreview> Levels = new List<LevelPreview>();
        private static readonly List<Transform> TrackedTransforms = new List<Transform>();
        private static readonly List<PhysicsBodyRecord> RuntimeScratch = new List<PhysicsBodyRecord>();
        private static readonly Dictionary<string, CachedArtifact> ArtifactCache =
            new Dictionary<string, CachedArtifact>(StringComparer.Ordinal);

        private static bool dirty = true;
        private static bool refreshQueued;
        private static double nextRuntimeRefresh;
        private static string cacheError;

        static JitterPhysicsBakeGeometryOverlay()
        {
            SceneView.duringSceneGui -= DuringSceneGui;
            SceneView.duringSceneGui += DuringSceneGui;
            EditorApplication.hierarchyChanged += MarkDirty;
            EditorApplication.projectChanged += InvalidateArtifacts;
            EditorApplication.playModeStateChanged += _ => MarkDirty();
            EditorApplication.update += OnEditorUpdate;
            Undo.undoRedoPerformed += MarkDirty;
            EditorSceneManager.sceneOpened += (_, __) => InvalidateArtifacts();
            EditorSceneManager.sceneClosed += _ => InvalidateArtifacts();
            EditorSceneManager.activeSceneChangedInEditMode += (_, __) => MarkDirty();
            AssemblyReloadEvents.beforeAssemblyReload += ClearCache;
            EditorApplication.quitting += ClearCache;
            JitterPhysicsPreviewPreferences.Changed += OnPreferencesChanged;
            QueueRefresh();
        }

        internal static bool Enabled => JitterPhysicsPreviewPreferences.Baked;
        internal static void SetEnabled(bool value) => JitterPhysicsPreviewPreferences.Baked = value;
        internal static void ResetPreference()
        {
            EditorPrefs.DeleteKey(PreferenceKey);
            SceneView.RepaintAll();
        }

        internal static string StatusText
        {
            get
            {
                if (!string.IsNullOrEmpty(cacheError)) return "Preview unavailable: " + cacheError;
                IReadOnlyList<LevelPreview> visible = VisibleLevels();
                if (visible.Count == 0) return "No active JitterPhysicsLevel.";

                int sources = 0, baked = 0, runtime = 0, changed = 0, moved = 0, removed = 0, errors = 0;
                string artifactIssue = null;
                string runtimeIssue = null;
                for (int i = 0; i < visible.Count; i++)
                {
                    sources += visible[i].Sources.Count;
                    baked += visible[i].Baked.Count;
                    runtime += visible[i].Runtime.Count;
                    changed += visible[i].Changed.Count;
                    errors += visible[i].Errors.Count;
                    artifactIssue ??= visible[i].ArtifactError;
                    runtimeIssue ??= visible[i].RuntimeError;
                    for (int j = 0; j < visible[i].Changed.Count; j++)
                    {
                        if (visible[i].Changed[j].Change == ChangeKind.Moved) moved++;
                        if (visible[i].Changed[j].Change == ChangeKind.Removed) removed++;
                    }
                }

                string runtimeText = JitterPhysicsPreviewPreferences.Runtime && !string.IsNullOrEmpty(runtimeIssue)
                    ? "Runtime error: " + runtimeIssue
                    : JitterPhysicsPreviewPreferences.Runtime && runtime == 0
                        ? "No runtime data"
                    : "runtime " + runtime;
                return $"sources {sources} · baked {baked} · changed {changed}"
                    + (moved == 0 ? string.Empty : $"/moved {moved}")
                    + (removed == 0 ? string.Empty : $"/removed {removed}")
                    + $" · {runtimeText}"
                    + (JitterPhysicsPreviewPreferences.Baked && !string.IsNullOrEmpty(artifactIssue)
                        ? " · " + artifactIssue
                        : string.Empty)
                    + (errors == 0 ? string.Empty : $" · errors {errors}");
            }
        }

        internal static bool TryGetFrameBounds(out Bounds bounds)
        {
            IReadOnlyList<LevelPreview> visible = VisibleLevels();
            bool found = false;
            bounds = default;
            for (int i = 0; i < visible.Count; i++)
            {
                found |= Encapsulate(visible[i].Sources, ref bounds, found);
                found |= Encapsulate(visible[i].Baked, ref bounds, found);
                found |= Encapsulate(visible[i].Runtime, ref bounds, found);
                for (int j = 0; j < visible[i].Errors.Count; j++)
                {
                    if (!found) bounds = visible[i].Errors[j].Bounds;
                    else bounds.Encapsulate(visible[i].Errors[j].Bounds);
                    found = true;
                }
            }

            if (found && bounds.extents.sqrMagnitude < 0.01f) bounds.Expand(1f);
            return found;
        }

        private static bool Encapsulate(IReadOnlyList<DrawRecord> records, ref Bounds result, bool found)
        {
            bool any = false;
            for (int i = 0; i < records.Count; i++)
            {
                Bounds candidate = ShapeBounds(records[i].Body, records[i].Shape);
                if (!found && !any) result = candidate;
                else result.Encapsulate(candidate);
                any = true;
            }
            return any;
        }

        private static void OnPreferencesChanged()
        {
            if (JitterPhysicsPreviewPreferences.Runtime) MarkDirty();
        }

        private static void OnEditorUpdate()
        {
            for (int i = 0; i < TrackedTransforms.Count; i++)
            {
                Transform transform = TrackedTransforms[i];
                if (transform != null && transform.hasChanged)
                {
                    transform.hasChanged = false;
                    MarkDirty();
                }
            }

            if (EditorApplication.isPlaying && JitterPhysicsPreviewPreferences.Runtime
                && EditorApplication.timeSinceStartup >= nextRuntimeRefresh)
            {
                nextRuntimeRefresh = EditorApplication.timeSinceStartup + 0.25d;
                MarkDirty();
            }
        }

        private static void MarkDirty()
        {
            dirty = true;
            QueueRefresh();
        }

        private static void InvalidateArtifacts()
        {
            ArtifactCache.Clear();
            MarkDirty();
        }

        private static void QueueRefresh()
        {
            if (refreshQueued) return;
            refreshQueued = true;
            EditorApplication.delayCall += RefreshCache;
        }

        private static void RefreshCache()
        {
            refreshQueued = false;
            if (!dirty || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                if (dirty) QueueRefresh();
                return;
            }

            dirty = false;
            cacheError = null;
            Levels.Clear();
            TrackedTransforms.Clear();
            try
            {
                JitterPhysicsLevel[] levels = UnityEngine.Object.FindObjectsByType<JitterPhysicsLevel>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                for (int i = 0; i < levels.Length; i++)
                {
                    JitterPhysicsLevel level = levels[i];
                    if (level != null && level.gameObject.scene.IsValid() && level.gameObject.scene.isLoaded)
                        Levels.Add(BuildLevel(level));
                }

                if (JitterPhysicsPreviewPreferences.Runtime && EditorApplication.isPlaying)
                    CollectRuntimePreviews();
            }
            catch (Exception exception)
            {
                cacheError = exception.Message;
            }
            SceneView.RepaintAll();
        }

        private static LevelPreview BuildLevel(JitterPhysicsLevel level)
        {
            var preview = new LevelPreview(level);
            var bakedBodies = new Dictionary<string, PhysicsBodyRecord>(StringComparer.Ordinal);
            PhysicsArtifact artifact = LoadArtifact(level, out string artifactError);
            preview.ArtifactError = artifactError;
            if (artifact != null)
            {
                for (int i = 0; i < artifact.Bodies.Count; i++)
                {
                    PhysicsBodyRecord body = artifact.Bodies[i];
                    bakedBodies[body.SourceId] = body;
                    AddBody(preview.Baked, body);
                }
            }

            IReadOnlyList<JitterStaticBodySource> sources = level.CollectSources();
            var currentIds = new HashSet<string>(StringComparer.Ordinal);
            var currentShapeKeys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            for (int i = 0; i < sources.Count; i++)
            {
                JitterStaticBodySource source = sources[i];
                if (source == null) continue;
                Track(source.transform);
                currentIds.Add(source.SourceId);
                var keys = new HashSet<string>(StringComparer.Ordinal);
                currentShapeKeys[source.SourceId] = keys;
                bakedBodies.TryGetValue(source.SourceId, out PhysicsBodyRecord bakedBody);
                bool poseMatches = TryBodyPoseMatches(bakedBody, source.transform);

                var colliders = new List<Collider>();
                if (source.IncludeChildren)
                    source.transform.GetComponentsInChildren(JitterStaticBodySource.IncludeInactiveChildren, colliders);
                else colliders.AddRange(source.GetComponents<Collider>());

                for (int j = 0; j < colliders.Count; j++)
                {
                    Collider collider = colliders[j];
                    if (!IsCurrentGeometry(collider)) continue;
                    Track(collider.transform);
                    string key = JitterPhysicsColliderKey.Build(source.transform, collider);
                    keys.Add(key);
                    JitterPhysicsConversionResult conversion = TryConvert(source.transform, collider, key);
                    if (!conversion.Succeeded)
                    {
                        preview.Errors.Add(new ErrorRecord(collider.bounds, conversion.Message));
                        continue;
                    }

                    PhysicsBodyRecord current = CurrentBody(source, conversion.Shape);
                    var record = new DrawRecord(current, conversion.Shape);
                    preview.Sources.Add(record);
                    PhysicsShapeRecord bakedShape = FindShape(bakedBody, key);
                    if (!poseMatches)
                        preview.Changed.Add(new DrawRecord(current, conversion.Shape, ChangeKind.Moved));
                    else if (!JitterPhysicsGeometryComparer.ShapesMatch(bakedShape, conversion.Shape))
                        preview.Changed.Add(new DrawRecord(current, conversion.Shape,
                            bakedShape == null ? ChangeKind.Added : ChangeKind.Changed));
                }
            }

            // Old artifact records remain visible after a source is removed.
            foreach (KeyValuePair<string, PhysicsBodyRecord> pair in bakedBodies)
            {
                if (!currentIds.Contains(pair.Key))
                {
                    AddBody(preview.Changed, pair.Value, ChangeKind.Removed);
                    continue;
                }

                HashSet<string> keys = currentShapeKeys[pair.Key];
                for (int i = 0; i < pair.Value.Shapes.Count; i++)
                {
                    PhysicsShapeRecord shape = pair.Value.Shapes[i];
                    if (!keys.Contains(shape.ShapeKey))
                        preview.Changed.Add(new DrawRecord(pair.Value, shape, ChangeKind.Removed));
                }
            }
            return preview;
        }

        private static void CollectRuntimePreviews()
        {
            MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (!(behaviours[i] is IJitterPhysicsRuntimePreviewSource provider)
                    || !provider.IsPhysicsPreviewReady) continue;
                LevelPreview level = FindLevel(provider.PhysicsPreviewLevelId);
                if (level == null) continue;
                RuntimeScratch.Clear();
                try
                {
                    provider.CopyPhysicsPreviewBodies(RuntimeScratch);
                    for (int j = 0; j < RuntimeScratch.Count; j++) AddBody(level.Runtime, RuntimeScratch[j]);
                }
                catch (Exception exception) { level.RuntimeError = exception.Message; }
            }
        }

        private static void AddBody(
            ICollection<DrawRecord> records,
            PhysicsBodyRecord body,
            ChangeKind change = ChangeKind.None)
        {
            if (body == null) return;
            for (int i = 0; i < body.Shapes.Count; i++)
                records.Add(new DrawRecord(body, body.Shapes[i], change));
        }

        private static LevelPreview FindLevel(string id)
        {
            for (int i = 0; i < Levels.Count; i++)
                if (string.Equals(Levels[i].LevelId, id, StringComparison.Ordinal)) return Levels[i];
            return null;
        }

        private static void Track(Transform transform)
        {
            if (transform == null || TrackedTransforms.Contains(transform)) return;
            transform.hasChanged = false;
            TrackedTransforms.Add(transform);
        }

        private static PhysicsBodyRecord CurrentBody(JitterStaticBodySource source, PhysicsShapeRecord shape)
        {
            Vector3 position = source.transform.position;
            Quaternion rotation = source.transform.rotation;
            return new PhysicsBodyRecord(source.SourceId, ToPhysics(position), ToPhysics(rotation),
                source.Friction, source.Restitution, new[] { shape });
        }

        private static bool IsCurrentGeometry(Collider collider) =>
            collider != null && collider.enabled && collider.gameObject.activeInHierarchy;

        private static bool TryBodyPoseMatches(PhysicsBodyRecord baked, Transform current)
        {
            try { return JitterPhysicsGeometryComparer.BodyPoseMatches(baked, current); }
            catch (ArgumentException) { return false; }
        }

        private static JitterPhysicsConversionResult TryConvert(
            Transform root, Collider collider, string key)
        {
            try { return JitterPhysicsColliderConverter.Convert(root, collider, key); }
            catch (Exception exception)
            {
                return JitterPhysicsConversionResult.Failure(
                    JitterPhysicsConversionStatus.NotFinite, exception.Message);
            }
        }

        private static PhysicsShapeRecord FindShape(PhysicsBodyRecord body, string key)
        {
            if (body != null)
                for (int i = 0; i < body.Shapes.Count; i++)
                    if (string.Equals(body.Shapes[i].ShapeKey, key, StringComparison.Ordinal)) return body.Shapes[i];
            return null;
        }

        private static PhysicsArtifact LoadArtifact(JitterPhysicsLevel level, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(level.LevelId)) { error = "level id is empty"; return null; }
            string path = JitterPhysicsArtifactPaths.ArtifactAssetPath(level.GeneratedFolder, level.LevelId);
            var asset = AssetDatabase.LoadAssetAtPath<JitterPhysicsArtifactAsset>(path);
            if (asset == null) { error = "no baked artifact"; return null; }
            string payloadPath = AssetDatabase.GetAssetPath(asset.Payload);
            string payloadSignature = string.IsNullOrEmpty(payloadPath)
                ? "<missing>"
                : AssetDatabase.GetAssetDependencyHash(payloadPath).ToString();
            string signature = asset.ArtifactHash + ":" + payloadSignature;
            if (ArtifactCache.TryGetValue(path, out CachedArtifact cached)
                && string.Equals(cached.Signature, signature, StringComparison.Ordinal))
            {
                error = cached.Error;
                return cached.Artifact;
            }
            PhysicsArtifactResult result = JitterPhysicsArtifactLoader.Load(asset);
            var replacement = result.Succeeded
                ? new CachedArtifact(signature, result.Artifact, null)
                : new CachedArtifact(signature, null, result.Error.ToString());
            ArtifactCache[path] = replacement;
            error = replacement.Error;
            return replacement.Artifact;
        }

        private static void DuringSceneGui(SceneView sceneView)
        {
            if (Event.current.type != EventType.Repaint || sceneView == null || sceneView.camera == null) return;
            if (!JitterPhysicsPreviewPreferences.Sources
                && !JitterPhysicsPreviewPreferences.Baked
                && !JitterPhysicsPreviewPreferences.Runtime) return;
            if (dirty) QueueRefresh();
            IReadOnlyList<LevelPreview> visible = VisibleLevels();
            CompareFunction oldZ = Handles.zTest;
            Color oldColor = Handles.color;
            Matrix4x4 oldMatrix = Handles.matrix;
            try
            {
                Handles.zTest = JitterPhysicsPreviewPreferences.Occlusion == JitterPhysicsPreviewOcclusion.XRay
                    ? CompareFunction.Always : CompareFunction.LessEqual;
                for (int i = 0; i < visible.Count; i++) DrawLevel(visible[i]);
            }
            finally
            {
                Handles.zTest = oldZ;
                Handles.color = oldColor;
                Handles.matrix = oldMatrix;
            }
            DrawLegend(sceneView, visible);
        }

        private static void DrawLevel(LevelPreview level)
        {
            if (JitterPhysicsPreviewPreferences.Sources) DrawRecords(level.Sources, PreviewStyle.Sources);
            if (JitterPhysicsPreviewPreferences.Baked)
            {
                DrawRecords(level.Baked, PreviewStyle.Baked);
                DrawRecords(level.Changed, PreviewStyle.Changed);
                DrawErrors(level.Errors);
            }
            if (JitterPhysicsPreviewPreferences.Runtime) DrawRecords(level.Runtime, PreviewStyle.Runtime);
        }

        private static void DrawRecords(IReadOnlyList<DrawRecord> records, PreviewStyle style)
        {
            for (int i = 0; i < records.Count; i++) DrawShape(records[i], style);
        }

        private static void DrawShape(DrawRecord record, PreviewStyle style)
        {
            PhysicsBodyRecord body = record.Body;
            PhysicsShapeRecord shape = record.Shape;
            Matrix4x4 previous = Handles.matrix;
            Handles.matrix = Matrix4x4.TRS(ToVector(body.Position), ToQuaternion(body.Orientation), Vector3.one)
                * Matrix4x4.TRS(ToVector(shape.LocalPosition), ToQuaternion(shape.LocalRotation), Vector3.one);
            Color color = StyleColor(style);
            float width = style == PreviewStyle.Runtime ? 3f : 2f;
            bool dotted = style == PreviewStyle.Sources;
            bool fill = style == PreviewStyle.Baked || style == PreviewStyle.Runtime;
            switch (shape.ShapeType)
            {
                case PhysicsShapeType.Box: DrawBox(ToVector(shape.Size), color, width, dotted, fill); break;
                case PhysicsShapeType.Sphere: DrawSphere(shape.Radius, color, width, dotted, fill); break;
                case PhysicsShapeType.Capsule: DrawCapsule(shape.Radius, shape.Length, color, width, dotted); break;
                case PhysicsShapeType.Mesh: DrawMesh(shape, color, width, dotted, fill); break;
            }
            if (style == PreviewStyle.Changed)
            {
                Bounds localBounds = LocalBounds(shape);
                DrawChangeHatch(localBounds, color);
                Handles.color = color;
                Handles.Label(localBounds.center, record.Change.ToString());
            }
            if (style == PreviewStyle.Runtime)
            {
                Handles.color = color;
                Handles.DotHandleCap(0, Vector3.zero, Quaternion.identity,
                    HandleUtility.GetHandleSize(Vector3.zero) * 0.055f, EventType.Repaint);
            }
            Handles.matrix = previous;
        }

        private static void DrawBox(Vector3 size, Color color, float width, bool dotted, bool fill)
        {
            Vector3 e = size * 0.5f;
            Vector3[] p = {
                new Vector3(-e.x,-e.y,-e.z), new Vector3(e.x,-e.y,-e.z),
                new Vector3(e.x,e.y,-e.z), new Vector3(-e.x,e.y,-e.z),
                new Vector3(-e.x,-e.y,e.z), new Vector3(e.x,-e.y,e.z),
                new Vector3(e.x,e.y,e.z), new Vector3(-e.x,e.y,e.z) };
            if (fill)
            {
                Color face = color; face.a = 0.14f; Handles.color = face;
                Handles.DrawAAConvexPolygon(p[0],p[1],p[2],p[3]);
                Handles.DrawAAConvexPolygon(p[4],p[7],p[6],p[5]);
            }
            int[] edges = {0,1,1,2,2,3,3,0,4,5,5,6,6,7,7,4,0,4,1,5,2,6,3,7};
            for (int i = 0; i < edges.Length; i += 2) DrawLine(p[edges[i]], p[edges[i+1]], color, width, dotted);
        }

        private static void DrawSphere(float radius, Color color, float width, bool dotted, bool fill)
        {
            if (fill)
            {
                Color face = color; face.a = 0.14f; Handles.color = face;
                Handles.SphereHandleCap(0, Vector3.zero, Quaternion.identity, radius * 2f, EventType.Repaint);
            }
            DrawDisc(Vector3.zero, Vector3.right, radius, color, width, dotted);
            DrawDisc(Vector3.zero, Vector3.up, radius, color, width, dotted);
            DrawDisc(Vector3.zero, Vector3.forward, radius, color, width, dotted);
        }

        private static void DrawCapsule(float radius, float length, Color color, float width, bool dotted)
        {
            Vector3 top = Vector3.up * length * 0.5f;
            Vector3 bottom = -top;
            DrawDisc(top, Vector3.right, radius, color, width, dotted);
            DrawDisc(top, Vector3.forward, radius, color, width, dotted);
            DrawDisc(bottom, Vector3.right, radius, color, width, dotted);
            DrawDisc(bottom, Vector3.forward, radius, color, width, dotted);
            DrawLine(top+Vector3.right*radius,bottom+Vector3.right*radius,color,width,dotted);
            DrawLine(top+Vector3.left*radius,bottom+Vector3.left*radius,color,width,dotted);
            DrawLine(top+Vector3.forward*radius,bottom+Vector3.forward*radius,color,width,dotted);
            DrawLine(top+Vector3.back*radius,bottom+Vector3.back*radius,color,width,dotted);
        }

        private static void DrawMesh(PhysicsShapeRecord shape, Color color, float width, bool dotted, bool fill)
        {
            for (int i = 0; i + 2 < shape.Indices.Length; i += 3)
            {
                Vector3 a=ToVector(shape.Vertices[shape.Indices[i]]), b=ToVector(shape.Vertices[shape.Indices[i+1]]),
                    c=ToVector(shape.Vertices[shape.Indices[i+2]]);
                if (fill) { Color face=color; face.a=0.14f; Handles.color=face; Handles.DrawAAConvexPolygon(a,b,c); }
                DrawLine(a,b,color,width,dotted); DrawLine(b,c,color,width,dotted); DrawLine(c,a,color,width,dotted);
            }
        }

        private static void DrawDisc(Vector3 center, Vector3 normal, float radius, Color color, float width, bool dotted)
        {
            const int segments=40;
            Vector3 tangent=Vector3.Cross(normal,Mathf.Abs(normal.y)<0.9f?Vector3.up:Vector3.right).normalized;
            Vector3 bitangent=Vector3.Cross(normal,tangent), previous=center+tangent*radius;
            for (int i=1;i<=segments;i++)
            {
                float angle=i*Mathf.PI*2f/segments;
                Vector3 next=center+(tangent*Mathf.Cos(angle)+bitangent*Mathf.Sin(angle))*radius;
                DrawLine(previous,next,color,width,dotted); previous=next;
            }
        }

        private static void DrawLine(Vector3 a, Vector3 b, Color color, float width, bool dotted)
        {
            Handles.color=color;
            if (dotted) Handles.DrawDottedLine(a,b,4f); else Handles.DrawAAPolyLine(width,a,b);
        }

        private static void DrawChangeHatch(Bounds bounds, Color color)
        {
            for (int i=1;i<=4;i++)
            {
                float t=i/5f;
                DrawLine(new Vector3(Mathf.Lerp(bounds.min.x,bounds.max.x,t),bounds.min.y,bounds.min.z),
                    new Vector3(bounds.min.x,Mathf.Lerp(bounds.min.y,bounds.max.y,t),bounds.max.z),color,2f,false);
            }
        }

        private static void DrawErrors(IReadOnlyList<ErrorRecord> errors)
        {
            for (int i=0;i<errors.Count;i++)
            {
                Matrix4x4 previous=Handles.matrix; Handles.matrix=Matrix4x4.identity; Handles.color=ErrorColor;
                Handles.DrawWireCube(errors[i].Bounds.center,errors[i].Bounds.size);
                Handles.DrawWireCube(errors[i].Bounds.center,errors[i].Bounds.size+Vector3.one*0.025f);
                var style=new GUIStyle(EditorStyles.boldLabel) { alignment=TextAnchor.MiddleCenter };
                style.normal.textColor=ErrorColor;
                style.normal.background=Texture2D.whiteTexture;
                Color previousBackground=GUI.backgroundColor;
                GUI.backgroundColor=ErrorBackdropColor;
                Handles.Label(errors[i].Bounds.center,new GUIContent("!",errors[i].Message),style);
                GUI.backgroundColor=previousBackground;
                Handles.matrix=previous;
            }
        }

        private static void DrawLegend(SceneView view, IReadOnlyList<LevelPreview> visible)
        {
            if (visible.Count==0) return;
            Handles.BeginGUI();
            try
            {
                var area=new Rect(12f,view.position.height-74f,455f,46f);
                GUI.Box(area,GUIContent.none,GUI.skin.window);
                GUI.Label(new Rect(area.x+9f,area.y+5f,area.width-18f,18f),"Jitter Physics",EditorStyles.boldLabel);
                GUI.Label(new Rect(area.x+9f,area.y+23f,area.width-18f,18f),StatusText,EditorStyles.miniLabel);
            }
            finally { Handles.EndGUI(); }
        }

        private static IReadOnlyList<LevelPreview> VisibleLevels()
        {
            if (JitterPhysicsPreviewPreferences.Scope==JitterPhysicsPreviewScope.AllLoadedLevels) return Levels;
            LevelPreview selected=SelectedLevel();
            return selected==null?Array.Empty<LevelPreview>():new[]{selected};
        }

        private static LevelPreview SelectedLevel()
        {
            GameObject selected=Selection.activeGameObject;
            if (selected!=null)
                for (int i=0;i<Levels.Count;i++)
                {
                    JitterPhysicsLevel level=Levels[i].Level;
                    Transform root=level.GeometryRoot!=null?level.GeometryRoot:level.transform.root;
                    if (selected.scene==level.gameObject.scene &&
                        (selected.transform==level.transform||selected.transform.IsChildOf(root))) return Levels[i];
                }
            Scene active=SceneManager.GetActiveScene();
            for (int i=0;i<Levels.Count;i++) if (Levels[i].Level.gameObject.scene==active) return Levels[i];
            return Levels.Count>0?Levels[0]:null;
        }

        private static Bounds ShapeBounds(PhysicsBodyRecord body, PhysicsShapeRecord shape)
        {
            Matrix4x4 matrix=Matrix4x4.TRS(ToVector(body.Position),ToQuaternion(body.Orientation),Vector3.one)
                *Matrix4x4.TRS(ToVector(shape.LocalPosition),ToQuaternion(shape.LocalRotation),Vector3.one);
            Bounds local=LocalBounds(shape);
            Vector3 center=matrix.MultiplyPoint3x4(local.center), e=local.extents;
            Vector3 x=matrix.MultiplyVector(new Vector3(e.x,0,0)), y=matrix.MultiplyVector(new Vector3(0,e.y,0)),
                z=matrix.MultiplyVector(new Vector3(0,0,e.z));
            e=new Vector3(Mathf.Abs(x.x)+Mathf.Abs(y.x)+Mathf.Abs(z.x),Mathf.Abs(x.y)+Mathf.Abs(y.y)+Mathf.Abs(z.y),
                Mathf.Abs(x.z)+Mathf.Abs(y.z)+Mathf.Abs(z.z));
            return new Bounds(center,e*2f);
        }

        private static Bounds LocalBounds(PhysicsShapeRecord shape)
        {
            if (shape.ShapeType==PhysicsShapeType.Box) return new Bounds(Vector3.zero,ToVector(shape.Size));
            if (shape.ShapeType==PhysicsShapeType.Sphere) return new Bounds(Vector3.zero,Vector3.one*shape.Radius*2f);
            if (shape.ShapeType==PhysicsShapeType.Capsule) return new Bounds(Vector3.zero,
                new Vector3(shape.Radius*2f,shape.Length+shape.Radius*2f,shape.Radius*2f));
            if (shape.Vertices.Length>0)
            {
                var bounds=new Bounds(ToVector(shape.Vertices[0]),Vector3.zero);
                for (int i=1;i<shape.Vertices.Length;i++) bounds.Encapsulate(ToVector(shape.Vertices[i]));
                return bounds;
            }
            return new Bounds(Vector3.zero,Vector3.one*0.1f);
        }

        private static void ClearCache()
        {
            Levels.Clear();
            TrackedTransforms.Clear();
            RuntimeScratch.Clear();
            ArtifactCache.Clear();
        }
        private static Color StyleColor(PreviewStyle style) => style==PreviewStyle.Sources?SourcesColor:
            style==PreviewStyle.Baked?BakedColor:style==PreviewStyle.Runtime?RuntimeColor:ChangedColor;
        private static Color Hex(int rgb,float alpha)=>new Color(((rgb>>16)&255)/255f,((rgb>>8)&255)/255f,(rgb&255)/255f,alpha);
        private static Vector3 ToVector(PhysicsVector3 value)=>new Vector3(value.X,value.Y,value.Z);
        private static Quaternion ToQuaternion(PhysicsQuaternion value)=>new Quaternion(value.X,value.Y,value.Z,value.W);
        private static PhysicsVector3 ToPhysics(Vector3 value)=>new PhysicsVector3(value.x,value.y,value.z).Canonical();
        private static PhysicsQuaternion ToPhysics(Quaternion value)=>new PhysicsQuaternion(value.x,value.y,value.z,value.w).Canonical();

        private enum PreviewStyle { Sources, Baked, Runtime, Changed }
        private enum ChangeKind { None, Added, Changed, Moved, Removed }
        private readonly struct DrawRecord
        {
            internal DrawRecord(
                PhysicsBodyRecord body,
                PhysicsShapeRecord shape,
                ChangeKind change = ChangeKind.None)
            {
                Body=body;
                Shape=shape;
                Change=change;
            }
            internal PhysicsBodyRecord Body { get; }
            internal PhysicsShapeRecord Shape { get; }
            internal ChangeKind Change { get; }
        }
        private readonly struct ErrorRecord
        {
            internal ErrorRecord(Bounds bounds,string message) { Bounds=bounds; Message=message; }
            internal Bounds Bounds { get; }
            internal string Message { get; }
        }
        private sealed class LevelPreview
        {
            internal LevelPreview(JitterPhysicsLevel level) { Level=level; LevelId=string.IsNullOrEmpty(level.LevelId)?level.name:level.LevelId; }
            internal JitterPhysicsLevel Level { get; }
            internal string LevelId { get; }
            internal string ArtifactError { get; set; }
            internal string RuntimeError { get; set; }
            internal List<DrawRecord> Sources { get; }=new List<DrawRecord>();
            internal List<DrawRecord> Baked { get; }=new List<DrawRecord>();
            internal List<DrawRecord> Runtime { get; }=new List<DrawRecord>();
            internal List<DrawRecord> Changed { get; }=new List<DrawRecord>();
            internal List<ErrorRecord> Errors { get; }=new List<ErrorRecord>();
        }
        private sealed class CachedArtifact
        {
            internal CachedArtifact(string signature, PhysicsArtifact artifact, string error)
            {
                Signature = signature;
                Artifact = artifact;
                Error = error;
            }

            internal string Signature { get; }
            internal PhysicsArtifact Artifact { get; }
            internal string Error { get; }
        }
    }
}
