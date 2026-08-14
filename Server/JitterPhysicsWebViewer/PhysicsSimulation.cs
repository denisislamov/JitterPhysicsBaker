using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Jitter2;
using Jitter2.Collision.Shapes;
using Jitter2.Dynamics;
using Jitter2.LinearMath;

namespace DataSakura.JitterPhysics.WebViewer
{
    /// <summary>One dynamic body as the browser receives it.</summary>
    public sealed class DynamicBodyView
    {
        /// <summary>Identifier assigned by the server when the body was spawned.</summary>
        public int Id { get; set; }

        /// <summary>"sphere" or "box".</summary>
        public string Type { get; set; }

        /// <summary>Radius of a sphere; zero for a box.</summary>
        public float Radius { get; set; }

        /// <summary>Full extents of a box; null for a sphere.</summary>
        public float[] Size { get; set; }

        /// <summary>Colour index, so the page can keep a body's colour across frames.</summary>
        public int Tint { get; set; }

        /// <summary>World position.</summary>
        public float[] Position { get; set; }

        /// <summary>World orientation, as x, y, z, w.</summary>
        public float[] Orientation { get; set; }

        /// <summary>Whether the body is still awake.</summary>
        public bool Active { get; set; }
    }

    /// <summary>A frame of dynamic state.</summary>
    public sealed class SimulationStateView
    {
        /// <summary>Number of fixed steps executed since startup.</summary>
        public long Tick { get; set; }

        /// <summary>Rate the server steps at, taken from the artifact.</summary>
        public int TickRate { get; set; }

        /// <summary>Dynamic bodies currently in the world.</summary>
        public List<DynamicBodyView> Bodies { get; set; }
    }

    /// <summary>
    /// Owns the tick loop of this example server.
    /// <para>
    /// The package deliberately does not step the world — the tick loop belongs to the game —
    /// so a consumer has to provide one. This is the smallest honest version of it: a fixed
    /// timestep taken from the artifact, single threaded, with every mutation of the world
    /// funnelled through the same thread. Spawning a body from an HTTP request thread while
    /// the solver runs would corrupt the world in ways that surface much later and elsewhere.
    /// </para>
    /// </summary>
    public sealed class PhysicsSimulation : IDisposable
    {
        private readonly World _world;
        private readonly int _tickRate;
        private readonly float _timestep;

        private readonly object _gate = new object();
        private readonly List<DynamicBody> _dynamic = new List<DynamicBody>();
        private readonly Queue<Action> _commands = new Queue<Action>();

        private readonly Random _random = new Random(20260813);
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private Thread _thread;

        private long _tick;
        private int _nextId;
        private int _tint;

        /// <summary>Maximum number of dynamic bodies kept alive at once.</summary>
        public const int MaxDynamicBodies = 60;

        public PhysicsSimulation(World world, int tickRate)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _tickRate = tickRate;
            _timestep = 1f / tickRate;
        }

        /// <summary>Starts the fixed-step loop on its own thread.</summary>
        public void Start()
        {
            if (_thread != null)
            {
                return;
            }

            _thread = new Thread(Loop)
            {
                Name = "JitterPhysicsSimulation",
                IsBackground = true,
            };

            _thread.Start();
        }

        /// <summary>Queues a spawn; it happens between two steps, never during one.</summary>
        public void Spawn(string type, int count)
        {
            int clamped = Math.Clamp(count, 1, 25);

            lock (_gate)
            {
                _commands.Enqueue(() =>
                {
                    for (int i = 0; i < clamped; i++)
                    {
                        SpawnOne(type);
                    }
                });
            }
        }

        /// <summary>Queues the removal of every dynamic body. Static geometry is untouched.</summary>
        public void Reset()
        {
            lock (_gate)
            {
                _commands.Enqueue(RemoveAllDynamic);
            }
        }

        /// <summary>Takes a consistent snapshot of the dynamic bodies.</summary>
        public SimulationStateView Snapshot()
        {
            lock (_gate)
            {
                var bodies = new List<DynamicBodyView>(_dynamic.Count);

                for (int i = 0; i < _dynamic.Count; i++)
                {
                    DynamicBody entry = _dynamic[i];
                    JVector position = entry.Body.Position;
                    JQuaternion orientation = entry.Body.Orientation;

                    bodies.Add(new DynamicBodyView
                    {
                        Id = entry.Id,
                        Type = entry.Type,
                        Radius = entry.Radius,
                        Size = entry.Size,
                        Tint = entry.Tint,
                        Position = new[] { (float)position.X, (float)position.Y, (float)position.Z },
                        Orientation = new[]
                        {
                            (float)orientation.X,
                            (float)orientation.Y,
                            (float)orientation.Z,
                            (float)orientation.W,
                        },
                        Active = entry.Body.IsActive,
                    });
                }

                return new SimulationStateView
                {
                    Tick = _tick,
                    TickRate = _tickRate,
                    Bodies = bodies,
                };
            }
        }

        private void Loop()
        {
            var clock = Stopwatch.StartNew();
            double simulated = 0d;
            double nextSpawn = 0.5d;

            while (!_cancellation.IsCancellationRequested)
            {
                double elapsed = clock.Elapsed.TotalSeconds;

                // Bounded catch-up: after a pause — a breakpoint, a suspended laptop — the
                // alternative is a burst of hundreds of steps that stalls the server and
                // teleports every body through the level.
                int steps = 0;
                while (simulated + _timestep <= elapsed && steps < 4)
                {
                    DrainCommands();

                    if (elapsed >= nextSpawn && CountDynamic() < MaxDynamicBodies)
                    {
                        SpawnOne(null);
                        nextSpawn = elapsed + 0.6d;
                    }

                    _world.Step(_timestep, multiThread: false);

                    simulated += _timestep;
                    steps++;

                    lock (_gate)
                    {
                        _tick++;
                    }
                }

                if (steps == 0)
                {
                    Thread.Sleep(1);
                }
                else if (simulated < elapsed - 1d)
                {
                    // Too far behind to ever catch up; resync instead of accumulating debt.
                    simulated = elapsed;
                }
            }
        }

        private void DrainCommands()
        {
            while (true)
            {
                Action command;
                lock (_gate)
                {
                    if (_commands.Count == 0)
                    {
                        return;
                    }

                    command = _commands.Dequeue();
                }

                command();
            }
        }

        private int CountDynamic()
        {
            lock (_gate)
            {
                return _dynamic.Count;
            }
        }

        private void SpawnOne(string type)
        {
            if (string.IsNullOrEmpty(type))
            {
                type = _random.Next(2) == 0 ? "sphere" : "box";
            }

            RigidBody body = _world.CreateRigidBody();

            float radius = 0f;
            float[] size = null;

            if (type == "box")
            {
                float extent = 0.6f + ((float)_random.NextDouble() * 0.6f);
                size = new[] { extent, extent, extent };
                body.AddShape(new BoxShape(extent, extent, extent));
            }
            else
            {
                type = "sphere";
                radius = 0.4f + ((float)_random.NextDouble() * 0.4f);
                body.AddShape(new SphereShape(radius));
            }

            body.Position = new JVector(
                (float)((_random.NextDouble() - 0.5d) * 24d),
                14f + ((float)_random.NextDouble() * 6f),
                (float)((_random.NextDouble() - 0.5d) * 24d));

            body.Friction = 0.4f;
            body.Restitution = 0.25f;

            lock (_gate)
            {
                _dynamic.Add(new DynamicBody
                {
                    Id = _nextId++,
                    Type = type,
                    Radius = radius,
                    Size = size,
                    Tint = _tint++ % 6,
                    Body = body,
                });

                // The oldest body leaves when the budget is reached: an example server that
                // grows without bound turns into a memory report instead of a demo.
                if (_dynamic.Count > MaxDynamicBodies)
                {
                    DynamicBody oldest = _dynamic[0];
                    _dynamic.RemoveAt(0);
                    _world.Remove(oldest.Body);
                }
            }
        }

        private void RemoveAllDynamic()
        {
            lock (_gate)
            {
                for (int i = _dynamic.Count - 1; i >= 0; i--)
                {
                    _world.Remove(_dynamic[i].Body);
                }

                _dynamic.Clear();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _cancellation.Cancel();
            _thread?.Join(TimeSpan.FromSeconds(2));
            _cancellation.Dispose();
        }

        private sealed class DynamicBody
        {
            internal int Id;
            internal string Type;
            internal float Radius;
            internal float[] Size;
            internal int Tint;
            internal RigidBody Body;
        }
    }
}

