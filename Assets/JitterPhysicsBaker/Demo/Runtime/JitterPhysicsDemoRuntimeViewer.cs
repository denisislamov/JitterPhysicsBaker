using System.Collections.Generic;
using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Integration;
using DataSakura.JitterPhysics.UnityArtifact;
using Jitter2;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;
using UnityEngine;

namespace DataSakura.JitterPhysics.Demo
{
    /// <summary>
    /// Makes a baked demo scene come alive in Play Mode: it loads the level's artifact, builds the
    /// Jitter2 world from it and drops bodies onto the baked geometry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A demo scene on its own is only static geometry and authoring components — the input to the
    /// baker. Nothing moves in Play Mode because the package never owns a tick loop; this component
    /// is where the demo project supplies one, exactly as a game would.
    /// </para>
    /// <para>
    /// The assembly it lives in is gated behind the <c>DATASAKURA_JITTER_INTEGRATION</c> define,
    /// which the integration installer sets. Until the adapter is installed this code is not
    /// compiled at all, so a scene that references it degrades to a missing component rather than a
    /// project-wide compile error.
    /// </para>
    /// </remarks>
    [AddComponentMenu("DataSakura/Jitter Physics/Demo Runtime Viewer")]
    public sealed class JitterPhysicsDemoRuntimeViewer : MonoBehaviour
    {
        [Tooltip("The baked artifact to simulate. Left empty, it is found from the level in the scene.")]
        [SerializeField]
        private JitterPhysicsArtifactAsset artifact;

        [Tooltip("The level in this scene, used to find the artifact when none is assigned.")]
        [SerializeField]
        private JitterPhysicsLevel level;

        [Header("Dropping")]
        [Tooltip("Bodies dropped when the scene starts.")]
        [SerializeField]
        [Range(0, 40)]
        private int initialDrops = 12;

        [Tooltip("Seconds between automatic drops. Zero drops only the initial batch.")]
        [SerializeField]
        [Range(0f, 3f)]
        private float dropInterval = 0.6f;

        [Tooltip("Upper bound on live bodies, so a long run does not fill the broadphase.")]
        [SerializeField]
        [Range(10, 200)]
        private int maxBodies = 90;

        [SerializeField]
        private Vector3 dropCenter = new Vector3(0f, 14f, 0f);

        [SerializeField]
        private float dropSpread = 6f;

        private readonly List<Proxy> proxies = new List<Proxy>();
        private readonly Color[] palette =
        {
            new Color(0.35f, 0.65f, 1f), new Color(0.25f, 0.73f, 0.31f),
            new Color(0.82f, 0.60f, 0.13f), new Color(0.97f, 0.32f, 0.29f),
            new Color(0.64f, 0.44f, 0.97f), new Color(0.22f, 0.77f, 0.81f),
        };

        private struct Proxy
        {
            public RigidBody Body;
            public Transform View;
        }

        private World world;
        private PhysicsArtifact sourceArtifact;
        private float timestep;
        private float accumulator;
        private float nextDrop;
        private int spawned;
        private bool ready;
        private bool paused;
        private bool autoDrop = true;
        private string status = "Starting…";

        private void Start()
        {
            JitterPhysicsArtifactAsset asset = ResolveArtifact();
            if (asset == null)
            {
                status = "No baked artifact for this level. Bake it first:\n"
                    + "Tools > DataSakura > Jitter Physics > Demo > Bake All Demo Scenes.";
                Debug.LogWarning("[JitterPhysics] demo viewer: " + status, this);
                return;
            }

            // Re-validated here even though the bake produced it: what plays is the asset on disk,
            // and it can be replaced by a stale copy after baking.
            PhysicsArtifactResult loaded = JitterPhysicsArtifactLoader.Load(asset);
            if (!loaded.Succeeded)
            {
                status = $"Artifact did not load: {loaded.Error.Code}: {loaded.Error.Message}";
                Debug.LogError("[JitterPhysics] demo viewer: " + status, this);
                return;
            }

            world = new World();
            PhysicsWorldBuildResult built = JitterPhysicsWorldBuilder.Apply(world, loaded.Artifact);
            if (!built.Succeeded)
            {
                // The builder rolls back on failure, so nothing was created; dispose the empty world
                // rather than leave a half-built one running.
                world.Dispose();
                world = null;
                status = $"World build failed: {built.Error.Code}: {built.Error.Message}";
                Debug.LogError("[JitterPhysics] demo viewer: " + status, this);
                return;
            }

            sourceArtifact = loaded.Artifact;
            timestep = 1f / Mathf.Max(1, loaded.Artifact.WorldSettings.TickRate);
            ready = true;
            status = $"{loaded.Artifact.LevelId}: {built.BodyCount} static bodies, "
                + $"{loaded.Artifact.WorldSettings.TickRate} Hz";

            for (int i = 0; i < initialDrops; i++)
            {
                Drop();
            }
        }

        private void Update()
        {
            if (!ready)
            {
                return;
            }

            if (paused)
            {
                return;
            }

            if (autoDrop && dropInterval > 0f && Time.time >= nextDrop && proxies.Count < maxBodies)
            {
                nextDrop = Time.time + dropInterval;
                Drop();
            }

            // Fixed timestep with bounded catch-up, so a hitch cannot spiral into a stall.
            accumulator += Time.deltaTime;
            int steps = 0;
            while (accumulator >= timestep && steps < 4)
            {
                world.Step(timestep, multiThread: false);
                accumulator -= timestep;
                steps++;
            }

            for (int i = 0; i < proxies.Count; i++)
            {
                Proxy proxy = proxies[i];
                if (proxy.View == null)
                {
                    continue;
                }

                proxy.View.SetPositionAndRotation(
                    new Vector3(proxy.Body.Position.X, proxy.Body.Position.Y, proxy.Body.Position.Z),
                    new Quaternion(
                        proxy.Body.Orientation.X, proxy.Body.Orientation.Y,
                        proxy.Body.Orientation.Z, proxy.Body.Orientation.W));
            }
        }

        /// <summary>Drops one body while the configured live-body limit has not been reached.</summary>
        public void Drop()
        {
            DropBody((spawned & 1) == 0);
        }

        /// <summary>Drops one sphere through the demo UI.</summary>
        public void DropSphere()
        {
            DropBody(sphere: true);
        }

        /// <summary>Drops one box through the demo UI.</summary>
        public void DropBox()
        {
            DropBody(sphere: false);
        }

        private void DropBody(bool sphere)
        {
            if (!ready)
            {
                return;
            }

            if (proxies.Count >= maxBodies)
            {
                status = $"Body limit reached ({maxBodies}). Clear the demo to start over.";
                return;
            }

            Vector3 position = dropCenter + new Vector3(
                Random.Range(-dropSpread, dropSpread),
                Random.Range(0f, dropSpread),
                Random.Range(-dropSpread, dropSpread));

            RigidBody body = world.CreateRigidBody();
            PrimitiveType primitive;
            float scale;

            if (sphere)
            {
                float radius = Random.Range(0.4f, 0.8f);
                body.AddShape(new SphereShape(radius));
                primitive = PrimitiveType.Sphere;
                scale = radius * 2f;
            }
            else
            {
                float side = Random.Range(0.7f, 1.2f);
                body.AddShape(new BoxShape(new JVector(side, side, side)));
                primitive = PrimitiveType.Cube;
                scale = side;
            }

            body.Position = new JVector(position.x, position.y, position.z);
            body.SetMassInertia(1f);
            body.Restitution = 0.3f;
            body.Friction = 0.5f;

            GameObject viewObject = GameObject.CreatePrimitive(primitive);
            viewObject.name = sphere ? "Drop Sphere" : "Drop Box";

            // The visual collider would be a second physics representation racing the real one.
            Destroy(viewObject.GetComponent<Collider>());
            viewObject.transform.localScale = Vector3.one * scale;
            viewObject.transform.SetParent(transform, worldPositionStays: true);

            var renderer = viewObject.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.material.color = palette[spawned % palette.Length];
            }

            proxies.Add(new Proxy { Body = body, View = viewObject.transform });
            spawned++;
        }

        /// <summary>Removes every dropped body and reconstructs the baked static level.</summary>
        public void Clear()
        {
            if (!ready || sourceArtifact == null)
            {
                return;
            }

            // Jitter2's single-body removal relies on modern Dictionary enumeration semantics that
            // Unity Mono does not provide. Rebuilding the tiny demo world also leaves no stale
            // contacts behind and keeps the package-owned Jitter snapshot untouched.
            var replacement = new World();
            PhysicsWorldBuildResult rebuilt = JitterPhysicsWorldBuilder.Apply(
                replacement, sourceArtifact);
            if (!rebuilt.Succeeded)
            {
                replacement.Dispose();
                status = $"World rebuild failed: {rebuilt.Error.Code}: {rebuilt.Error.Message}";
                Debug.LogError("[JitterPhysics] demo viewer: " + status, this);
                return;
            }

            for (int i = 0; i < proxies.Count; i++)
            {
                if (proxies[i].View != null)
                {
                    Destroy(proxies[i].View.gameObject);
                }
            }

            proxies.Clear();
            world.Dispose();
            world = replacement;
            accumulator = 0f;
            nextDrop = Time.time + dropInterval;
            status = $"{sourceArtifact.LevelId}: {rebuilt.BodyCount} static bodies, "
                + $"{sourceArtifact.WorldSettings.TickRate} Hz";
        }

        private JitterPhysicsArtifactAsset ResolveArtifact()
        {
            if (artifact != null)
            {
                return artifact;
            }

#if UNITY_EDITOR
            JitterPhysicsLevel source = level;
            if (source == null)
            {
#if UNITY_2023_1_OR_NEWER
                source = Object.FindFirstObjectByType<JitterPhysicsLevel>();
#else
                source = Object.FindObjectOfType<JitterPhysicsLevel>();
#endif
            }

            if (source != null)
            {
                string path = JitterPhysicsArtifactPaths.ArtifactAssetPath(
                    source.GeneratedFolder, source.LevelId);
                return UnityEditor.AssetDatabase.LoadAssetAtPath<JitterPhysicsArtifactAsset>(path);
            }
#endif
            return null;
        }

        private void OnDestroy()
        {
            world?.Dispose();
            world = null;
        }

        private void OnGUI()
        {
            const float width = 420f;
            GUILayout.BeginArea(new Rect(12f, 12f, width, 150f), GUI.skin.box);
            GUILayout.Label("Jitter Physics Demo", GUI.skin.label);
            GUILayout.Label(status);

            using (new GuiEnabledScope(ready))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Drop sphere"))
                {
                    DropSphere();
                }

                if (GUILayout.Button("Drop box"))
                {
                    DropBox();
                }

                if (GUILayout.Button("Clear"))
                {
                    Clear();
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(paused ? "Resume physics" : "Pause physics"))
                {
                    paused = !paused;
                }

                autoDrop = GUILayout.Toggle(autoDrop, "Auto drop");
                GUILayout.EndHorizontal();
            }

            GUILayout.Label($"Dynamic bodies: {proxies.Count} / {maxBodies}");
            GUILayout.EndArea();
        }

        private readonly struct GuiEnabledScope : System.IDisposable
        {
            private readonly bool previous;

            public GuiEnabledScope(bool enabled)
            {
                previous = GUI.enabled;
                GUI.enabled = enabled;
            }

            public void Dispose()
            {
                GUI.enabled = previous;
            }
        }
    }
}
