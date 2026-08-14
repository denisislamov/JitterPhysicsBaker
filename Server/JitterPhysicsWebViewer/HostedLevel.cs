using System;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Integration;
using Jitter2;

namespace DataSakura.JitterPhysics.WebViewer
{
    /// <summary>
    /// One baked level brought up on its own <see cref="World"/>: loaded, verified, its static
    /// geometry built, and a tick loop running.
    /// </summary>
    /// <remarks>
    /// Each level gets a separate world and simulation rather than sharing one, because the static
    /// geometry is baked into the world at build time and cannot be swapped in place — the loader
    /// refuses a second <c>Apply</c> for exactly that reason. Hosting several levels at once is
    /// what lets the viewer switch between them without a restart, and every one of them still goes
    /// through the package's full startup discipline before it is offered.
    /// </remarks>
    public sealed class HostedLevel : IDisposable
    {
        private readonly World world;

        private HostedLevel(
            World world, JitterPhysicsServerState state, LevelView view, PhysicsSimulation simulation)
        {
            this.world = world;
            State = state;
            View = view;
            Simulation = simulation;
        }

        /// <summary>The level identifier the client and server compare.</summary>
        public string LevelId => State.LevelId;

        /// <summary>Startup result: hashes, counts, fingerprint, self-check line.</summary>
        public JitterPhysicsServerState State { get; }

        /// <summary>The static geometry, projected for the browser. Sent once.</summary>
        public LevelView View { get; }

        /// <summary>The dynamic simulation running on this level.</summary>
        public PhysicsSimulation Simulation { get; }

        /// <summary>
        /// Brings up one level from its manifest, or returns null with a reason on <paramref name="error"/>.
        /// </summary>
        /// <remarks>
        /// A failure returns rather than throws, and the caller refuses to start the whole server:
        /// a gallery that quietly drops a level nobody asked it to drop is worse than one that will
        /// not start until every level it was given actually loads.
        /// </remarks>
        public static HostedLevel TryStart(
            string manifestPath, string runtimeCompatibilityId, out string error)
        {
            error = null;

            var world = new World();
            var provider = new FilePhysicsArtifactProvider(manifestPath);

            JitterPhysicsServerState state = JitterPhysicsServerStartup.Start(
                world,
                provider,
                new JitterPhysicsServerOptions(runtimeCompatibilityId, expectedLevelId: null, tickRate: 0));

            if (!state.IsReady)
            {
                world.Dispose();
                error = state.SelfCheck;
                return null;
            }

            LevelView view = LevelView.From(state.Artifact, state.ArtifactHash, state.TopologyFingerprint);

            var simulation = new PhysicsSimulation(world, state.TickRate);
            simulation.Start();

            return new HostedLevel(world, state, view, simulation);
        }

        public void Dispose()
        {
            Simulation.Dispose();
            world.Dispose();
        }
    }
}


