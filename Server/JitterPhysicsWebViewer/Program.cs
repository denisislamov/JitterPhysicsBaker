using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Integration;
using Jitter2;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DataSakura.JitterPhysics.WebViewer
{
    /// <summary>
    /// A dedicated server that hosts a baked level and shows it in a browser.
    /// <para>
    /// The order of startup is the point of this example, and it is the order the package
    /// prescribes: obtain the artifact, check it against what this build claims to be, build
    /// the static world, and only then open the port. A server that starts serving before its
    /// walls exist produces a session where every client looks like it is cheating.
    /// </para>
    /// </summary>
    public static class Program
    {
        /// <summary>Runs the server. Returns a non-zero exit code when physics did not come up.</summary>
        public static int Main(string[] args)
        {
            string manifestPath;
            try
            {
                manifestPath = ResolveManifestPath(args);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(JitterPhysicsPackage.LogPrefix + exception.Message);
                return 2;
            }

            JitterLock jitterLock = JitterLock.Load(Path.Combine(AppContext.BaseDirectory, "jitter2.lock.json"));
            string runtimeCompatibilityId = jitterLock.RuntimeCompatibilityId;

            var world = new World();
            var provider = new FilePhysicsArtifactProvider(manifestPath);

            JitterPhysicsServerState state = JitterPhysicsServerStartup.Start(
                world,
                provider,
                new JitterPhysicsServerOptions(
                    runtimeCompatibilityId,
                    Argument(args, "--level"),
                    ParseTickRate(args)));

            Console.WriteLine(state.SelfCheck);

            if (!state.IsReady)
            {
                // Fail fast and loudly: the alternative is a running server whose world is
                // empty, which looks healthy from the outside for as long as it matters.
                Console.Error.WriteLine(
                    JitterPhysicsPackage.LogPrefix
                    + "Refusing to serve a level that did not load. Re-bake the scene in Unity and "
                    + "export it again, or point --manifest at the artifact this build supports "
                    + "(runtime id " + runtimeCompatibilityId + ").");
                return 1;
            }

            LevelView level = LevelView.From(state.Artifact, state.ArtifactHash, state.TopologyFingerprint);

            using var simulation = new PhysicsSimulation(world, state.TickRate);
            simulation.Start();

            RunWebHost(args, state, level, simulation, runtimeCompatibilityId, jitterLock);
            return 0;
        }

        private static void RunWebHost(
            string[] args,
            JitterPhysicsServerState state,
            LevelView level,
            PhysicsSimulation simulation,
            string runtimeCompatibilityId,
            JitterLock jitterLock)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });

            WebApplication app = builder.Build();

            app.UseDefaultFiles();
            app.UseStaticFiles();

            // The static level never changes while the process runs — the world cannot be
            // rebuilt in place — so it is sent once and cached by the page.
            app.MapGet("/api/level", () => Results.Ok(level));

            app.MapGet("/api/state", () => Results.Ok(simulation.Snapshot()));

            app.MapGet("/api/status", () => Results.Ok(new
            {
                selfCheck = state.SelfCheck,
                levelId = state.LevelId,
                artifactHash = state.ArtifactHash,
                topologyFingerprint = state.TopologyFingerprint,
                bodyCount = state.BodyCount,
                shapeCount = state.ShapeCount,
                triangleCount = state.Artifact.TriangleCount,
                tickRate = state.TickRate,
                elapsedMilliseconds = state.ElapsedMilliseconds,
                source = state.Source,
                runtimeCompatibilityId,
                jitterSourceHash = jitterLock.SourceContentHash,
                jitterUpstreamCommit = jitterLock.UpstreamCommit,
                packageVersion = JitterPhysicsPackage.PackageVersion,
                artifactSchemaVersion = JitterPhysicsPackage.ArtifactSchemaVersion,
            }));

            app.MapPost("/api/spawn", (string type, int? count) =>
            {
                simulation.Spawn(type, count ?? 1);
                return Results.Accepted();
            });

            app.MapPost("/api/reset", () =>
            {
                simulation.Reset();
                return Results.Accepted();
            });

            app.Run();
        }

        /// <summary>
        /// Finds the manifest to load: an explicit <c>--manifest</c>, or the single manifest
        /// in the delivery folder. Ambiguity is refused rather than resolved by directory
        /// order, because "which level is this server hosting" must not depend on a file
        /// system detail.
        /// </summary>
        private static string ResolveManifestPath(string[] args)
        {
            string explicitPath = Argument(args, "--manifest");
            if (!string.IsNullOrEmpty(explicitPath))
            {
                return Path.GetFullPath(explicitPath);
            }

            string folder = Argument(args, "--artifacts") ?? FindArtifactsFolder();
            if (folder == null || !Directory.Exists(folder))
            {
                throw new InvalidOperationException(
                    "No artifact folder was found. Bake the demo level in Unity "
                    + "(Tools > DataSakura > Jitter Physics > Demo > Create Demo Scene And Bake) "
                    + "or pass --manifest <path to .manifest.json>.");
            }

            string[] manifests = Directory
                .GetFiles(folder, "*" + JitterPhysicsArtifactNaming.ManifestExtension)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            if (manifests.Length == 0)
            {
                throw new InvalidOperationException(
                    $"'{folder}' contains no '*{JitterPhysicsArtifactNaming.ManifestExtension}'. "
                    + "Export a baked artifact there first.");
            }

            if (manifests.Length > 1)
            {
                throw new InvalidOperationException(
                    $"'{folder}' contains {manifests.Length} manifests; pass --manifest to say which "
                    + "level this server hosts.");
            }

            return manifests[0];
        }

        /// <summary>
        /// Walks up from the binary looking for the delivery folder, so that
        /// <c>dotnet run</c> works from anywhere in the repository without configuration.
        /// </summary>
        private static string FindArtifactsFolder()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            for (int depth = 0; depth < 8 && directory != null; depth++)
            {
                string candidate = Path.Combine(directory.FullName, "artifacts");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                candidate = Path.Combine(directory.FullName, "Server", "artifacts");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return null;
        }

        private static int ParseTickRate(string[] args)
        {
            string value = Argument(args, "--tick-rate");
            return int.TryParse(value, out int tickRate) ? tickRate : 0;
        }

        private static string Argument(IReadOnlyList<string> args, string name)
        {
            for (int i = 0; i < args.Count - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}

