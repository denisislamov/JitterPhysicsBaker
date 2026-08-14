using System.Collections.Generic;
using DataSakura.JitterPhysics.Authoring;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace DataSakura.JitterPhysics.Demo.Editor
{
    /// <summary>
    /// Builds the demo arena scene from code.
    /// <para>
    /// The scene is generated rather than committed as a <c>.unity</c> file on purpose: a
    /// hand-authored fixture drifts as soon as somebody nudges a collider in the editor, and
    /// this level exists to produce a byte-stable artifact. Regenerating it is the cheapest
    /// way to prove that the same input still bakes into the same bytes.
    /// </para>
    /// </summary>
    public static class JitterPhysicsDemoScene
    {
        /// <summary>Identifier baked into the artifact and compared during the handshake.</summary>
        public const string LevelId = "demo_arena";

        /// <summary>Folder holding everything the demo generates inside the project.</summary>
        public const string DemoFolder = "Assets/JitterPhysicsBaker/Demo";

        /// <summary>Path of the generated scene.</summary>
        public const string ScenePath = DemoFolder + "/Scenes/JitterDemoArena.unity";

        /// <summary>Path of the generated world profile.</summary>
        public const string WorldProfilePath = DemoFolder + "/JitterDemoWorldProfile.asset";

        /// <summary>Path of the generated collision mesh used by the mesh collider body.</summary>
        public const string HillMeshPath = DemoFolder + "/Meshes/JitterDemoHill.asset";

        /// <summary>Folder the baked artifact is written to.</summary>
        public const string GeneratedFolder = "Assets/Generated/JitterPhysics";

        /// <summary>
        /// Creates the scene, saves it and returns its level component. Any previous version
        /// of the scene is replaced, because a demo that accumulates leftovers stops being a
        /// deterministic fixture.
        /// </summary>
        public static JitterPhysicsLevel Create()
        {
            EnsureFolder(DemoFolder + "/Scenes");
            EnsureFolder(DemoFolder + "/Meshes");

            JitterPhysicsWorldProfile profile = CreateWorldProfile();
            Mesh hill = CreateHillMesh();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            Material material = CreateSharedMaterial();

            var levelRoot = new GameObject("DemoArena");
            var geometryRoot = new GameObject("Geometry");
            geometryRoot.transform.SetParent(levelRoot.transform, false);

            BuildGeometry(geometryRoot.transform, hill, material);
            PlaceCamera(scene);

            JitterPhysicsLevel level = levelRoot.AddComponent<JitterPhysicsLevel>();
            ConfigureLevel(level, geometryRoot.transform, profile);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);

            return level;
        }

        private static void BuildGeometry(Transform root, Mesh hill, Material material)
        {
            // The floor is a single wide box rather than tiles: fewer bodies make the
            // artifact easier to read when comparing two bakes by hand.
            Box(root, material, "Ground", new Vector3(0f, -0.5f, 0f), new Vector3(60f, 1f, 60f),
                Quaternion.identity, friction: 0.6f);

            const float half = 30f;
            Box(root, material, "WallNorth", new Vector3(0f, 1.5f, half), new Vector3(60f, 3f, 1f),
                Quaternion.identity, friction: 0.3f);
            Box(root, material, "WallSouth", new Vector3(0f, 1.5f, -half), new Vector3(60f, 3f, 1f),
                Quaternion.identity, friction: 0.3f);
            Box(root, material, "WallEast", new Vector3(half, 1.5f, 0f), new Vector3(1f, 3f, 60f),
                Quaternion.identity, friction: 0.3f);
            Box(root, material, "WallWest", new Vector3(-half, 1.5f, 0f), new Vector3(1f, 3f, 60f),
                Quaternion.identity, friction: 0.3f);

            // A rotated box: the artifact has to carry the body's orientation, not just a
            // position, and a ramp is the shape where getting that wrong is immediately
            // visible — bodies either roll down it or fall through it.
            Box(root, material, "Ramp", new Vector3(-10f, 1.6f, 8f), new Vector3(12f, 0.5f, 8f),
                Quaternion.Euler(0f, 0f, -18f), friction: 0.4f);

            Box(root, material, "Platform", new Vector3(10f, 3f, -8f), new Vector3(10f, 0.5f, 10f),
                Quaternion.identity, friction: 0.5f);

            for (int i = 0; i < 4; i++)
            {
                float x = 6f + ((i % 2) * 8f);
                float z = -4f - ((i / 2) * 8f);
                Capsule(root, material, "Pillar" + i, new Vector3(x, 1.5f, z), radius: 0.5f, height: 3f);
            }

            Sphere(root, material, "Boulder", new Vector3(-6f, 2f, -10f), radius: 2f, restitution: 0.35f);

            // One body with several child colliders: the collector has to produce a stable
            // shape order inside a body, and a stack is the case where an unstable order
            // would silently change the artifact hash between two bakes.
            CrateStack(root, material);

            MeshBody(root, material, "Hill", hill, new Vector3(12f, 0f, 14f));
        }

        private static void CrateStack(Transform root, Material material)
        {
            var stack = new GameObject("CrateStack");
            stack.transform.SetParent(root, false);
            stack.transform.position = new Vector3(-14f, 0f, -6f);
            AddSource(stack, friction: 0.7f, restitution: 0f);

            for (int i = 0; i < 3; i++)
            {
                GameObject crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                crate.name = "Crate" + i;
                crate.transform.SetParent(stack.transform, false);
                crate.transform.localPosition = new Vector3(i * 0.35f, 0.6f + (i * 1.2f), 0f);
                crate.transform.localRotation = Quaternion.Euler(0f, i * 12f, 0f);
                crate.transform.localScale = new Vector3(1.2f, 1.2f, 1.2f);
                Paint(crate, material);
            }
        }

        private static GameObject Box(
            Transform root,
            Material material,
            string name,
            Vector3 position,
            Vector3 size,
            Quaternion rotation,
            float friction,
            float restitution = 0f)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = size;
            Paint(go, material);
            AddSource(go, friction, restitution);
            return go;
        }

        private static void Sphere(
            Transform root,
            Material material,
            string name,
            Vector3 position,
            float radius,
            float restitution)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.position = position;

            // Uniform on purpose: a non-uniform scale would make the baker warn and
            // over-approximate, which is correct behaviour but noise in a demo.
            go.transform.localScale = Vector3.one * (radius * 2f);
            Paint(go, material);
            AddSource(go, friction: 0.2f, restitution: restitution);
        }

        private static void Capsule(
            Transform root,
            Material material,
            string name,
            Vector3 position,
            float radius,
            float height)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.position = position;
            go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            Paint(go, material);
            AddSource(go, friction: 0.3f, restitution: 0f);
        }

        private static void MeshBody(
            Transform root,
            Material material,
            string name,
            Mesh mesh,
            Vector3 position)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            go.transform.SetParent(root, false);
            go.transform.position = position;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshCollider>().sharedMesh = mesh;
            Paint(go, material);
            AddSource(go, friction: 0.8f, restitution: 0f);
        }

        private static void AddSource(GameObject go, float friction, float restitution)
        {
            JitterStaticBodySource source = go.AddComponent<JitterStaticBodySource>();
            source.SetSourceId(go.name);

            // Friction and restitution are serialized privately so that a runtime script
            // cannot change what was baked; the demo writes them the same way the inspector
            // does.
            var serialized = new SerializedObject(source);
            serialized.FindProperty("friction").floatValue = friction;
            serialized.FindProperty("restitution").floatValue = restitution;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureLevel(
            JitterPhysicsLevel level,
            Transform geometryRoot,
            JitterPhysicsWorldProfile profile)
        {
            var serialized = new SerializedObject(level);
            serialized.FindProperty("levelId").stringValue = LevelId;
            serialized.FindProperty("geometryRoot").objectReferenceValue = geometryRoot;
            serialized.FindProperty("worldProfile").objectReferenceValue = profile;
            serialized.FindProperty("generatedFolder").stringValue = GeneratedFolder;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static JitterPhysicsWorldProfile CreateWorldProfile()
        {
            EnsureFolder(DemoFolder);

            var profile = AssetDatabase.LoadAssetAtPath<JitterPhysicsWorldProfile>(WorldProfilePath);
            bool isNew = profile == null;
            if (isNew)
            {
                profile = ScriptableObject.CreateInstance<JitterPhysicsWorldProfile>();
            }

            var serialized = new SerializedObject(profile);
            serialized.FindProperty("gravity").vector3Value = new Vector3(0f, -9.81f, 0f);
            serialized.FindProperty("tickRate").intValue = 60;
            serialized.FindProperty("substepCount").intValue = 1;
            serialized.FindProperty("solverIterations").intValue = 6;
            serialized.FindProperty("relaxationIterations").intValue = 4;
            serialized.FindProperty("allowDeactivation").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            if (isNew)
            {
                AssetDatabase.CreateAsset(profile, WorldProfilePath);
            }
            else
            {
                EditorUtility.SetDirty(profile);
            }

            AssetDatabase.SaveAssets();
            return profile;
        }

        /// <summary>
        /// Generates the collision mesh: a gentle height field, so the artifact carries a
        /// real triangle soup and the loader has to rebuild hundreds of triangle shapes.
        /// </summary>
        private static Mesh CreateHillMesh()
        {
            const int segments = 16;
            const float size = 20f;
            const float height = 2.5f;

            var vertices = new Vector3[(segments + 1) * (segments + 1)];
            var uv = new Vector2[vertices.Length];

            for (int z = 0; z <= segments; z++)
            {
                for (int x = 0; x <= segments; x++)
                {
                    float u = x / (float)segments;
                    float v = z / (float)segments;
                    float y = height
                        * Mathf.Sin(u * Mathf.PI)
                        * Mathf.Sin(v * Mathf.PI);

                    int index = (z * (segments + 1)) + x;
                    vertices[index] = new Vector3((u - 0.5f) * size, y, (v - 0.5f) * size);
                    uv[index] = new Vector2(u, v);
                }
            }

            var triangles = new List<int>(segments * segments * 6);
            for (int z = 0; z < segments; z++)
            {
                for (int x = 0; x < segments; x++)
                {
                    int i0 = (z * (segments + 1)) + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + segments + 1;
                    int i3 = i2 + 1;

                    triangles.Add(i0);
                    triangles.Add(i2);
                    triangles.Add(i1);
                    triangles.Add(i1);
                    triangles.Add(i2);
                    triangles.Add(i3);
                }
            }

            var mesh = new Mesh { name = "JitterDemoHill" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(HillMeshPath);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, HillMeshPath);
            }
            else
            {
                // Replaced in place so that the scene reference and the asset GUID survive a
                // regeneration; recreating the asset would break both silently.
                existing.Clear();
                existing.SetVertices(vertices);
                existing.SetUVs(0, uv);
                existing.SetTriangles(triangles, 0);
                existing.RecalculateNormals();
                existing.RecalculateBounds();
                EditorUtility.SetDirty(existing);
                mesh = existing;
            }

            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Mesh>(HillMeshPath);
        }

        private static Material CreateSharedMaterial()
        {
            // The demo project renders with URP; the built-in default material would show up
            // magenta and make a working bake look broken.
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null)
            {
                return null;
            }

            const string path = DemoFolder + "/JitterDemoSurface.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = "JitterDemoSurface" };
                material.color = new Color(0.75f, 0.76f, 0.78f);
                AssetDatabase.CreateAsset(material, path);
                AssetDatabase.SaveAssets();
            }

            return material;
        }

        private static void Paint(GameObject go, Material material)
        {
            if (material == null)
            {
                return;
            }

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private static void PlaceCamera(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                var camera = root.GetComponent<Camera>();
                if (camera == null)
                {
                    continue;
                }

                camera.transform.SetPositionAndRotation(
                    new Vector3(0f, 24f, -34f), Quaternion.Euler(28f, 0f, 0f));
                camera.farClipPlane = 300f;
            }
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
            {
                return;
            }

            string[] parts = assetFolder.Split('/');
            string current = parts[0];

            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }
}

