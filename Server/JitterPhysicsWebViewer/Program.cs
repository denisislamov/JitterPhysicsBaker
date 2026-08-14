using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Contracts;
using DataSakura.JitterPhysics.Integration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DataSakura.JitterPhysics.WebViewer
{
    /// <summary>
    /// A dedicated server that hosts every baked level it is given and shows them in a browser.
    /// <para>
    /// The order of startup is the point of this example, and it is the order the package
    /// prescribes: for each level, obtain the artifact, check it against what this build claims to
    /// be, build the static world, and only then consider it ready. The port opens only once every
    /// level has passed that discipline; if any one fails, the server refuses to start rather than
    /// quietly hosting a subset nobody asked it to choose.
    /// </para>
    /// </summary>
    public static class Program
    {
        /// <summary>Runs the server. Returns a non-zero exit code when a level did not come up.</summary>
        public static int Main(string[] args)
        {
            string[] manifests;
            try
            {
                manifests = ResolveManifestPaths(args);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(JitterPhysicsPackage.LogPrefix + exception.Message);
                return 2;
            }

            JitterLock jitterLock = JitterLock.Load(Path.Combine(AppContext.BaseDirectory, "jitter2.lock.json"));
            string runtimeCompatibilityId = jitterLock.RuntimeCompatibilityId;

            var levels = new List<HostedLevel>();
            foreach (string manifest in manifests)
            {
                HostedLevel level = HostedLevel.TryStart(manifest, runtimeCompatibilityId, out string error);

                if (level == null)
                {
                    Console.Error.WriteLine(JitterPhysicsPackage.LogPrefix + error);
                    Console.Error.WriteLine(
                        JitterPhysicsPackage.LogPrefix
                        + "Refusing to serve: a level did not load. Re-bake the scenes in Unity and "
                        + "export them, or check that this build's runtime id (" + runtimeCompatibilityId
                        + ") matches the artifacts.");
                    DisposeAll(levels);
                    return 1;
                }

                if (levels.Any(existing => existing.LevelId == level.LevelId))
                {
                    Console.Error.WriteLine(
                        JitterPhysicsPackage.LogPrefix
                        + $"Two artifacts claim level '{level.LevelId}'. Remove the stale one.");
                    level.Dispose();
                    DisposeAll(levels);
                    return 1;
                }

                Console.WriteLine(level.State.SelfCheck);
                levels.Add(level);
            }

            if (levels.Count == 0)
            {
                Console.Error.WriteLine(JitterPhysicsPackage.LogPrefix + "No levels were found to host.");
                return 1;
            }

            try
            {
                RunWebHost(args, levels, runtimeCompatibilityId, jitterLock);
            }
            finally
            {
                DisposeAll(levels);
            }

            return 0;
        }

        private static void RunWebHost(
            string[] args,
            IReadOnlyList<HostedLevel> levels,
            string runtimeCompatibilityId,
            JitterLock jitterLock)
        {
            var byId = levels.ToDictionary(level => level.LevelId, StringComparer.Ordinal);

            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            builder.Services.ConfigureHttpJsonOptions(options =>
            {
                options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });

            WebApplication app = builder.Build();

            app.UseDefaultFiles();
            app.UseStaticFiles();

            // The catalogue the page shows in its level picker. Ordered so the list does not
            // reshuffle between runs.
            app.MapGet("/api/levels", () => Results.Ok(
                levels
                    .OrderBy(level => level.LevelId, StringComparer.Ordinal)
                    .Select(level => new
                    {
                        levelId = level.LevelId,
                        artifactHash = level.State.ArtifactHash,
                        topologyFingerprint = level.State.TopologyFingerprint,
                        bodyCount = level.State.BodyCount,
                        shapeCount = level.State.ShapeCount,
                        triangleCount = level.State.Artifact.TriangleCount,
                        tickRate = level.State.TickRate,
                    })
                    .ToList()));

            // The static level never changes while the process runs — the world cannot be rebuilt
            // in place — so it is sent once and cached by the page.
            app.MapGet("/api/level/{id}", (string id) =>
                byId.TryGetValue(id, out HostedLevel level)
                    ? Results.Ok(level.View)
                    : Results.NotFound());

            app.MapGet("/api/state/{id}", (string id) =>
                byId.TryGetValue(id, out HostedLevel level)
                    ? Results.Ok(level.Simulation.Snapshot())
                    : Results.NotFound());

            app.MapGet("/api/status/{id}", (string id) =>
            {
                if (!byId.TryGetValue(id, out HostedLevel level))
                {
                    return Results.NotFound();
                }

                JitterPhysicsServerState state = level.State;
                return Results.Ok(new
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
                });
            });

            app.MapPost("/api/spawn/{id}", (string id, string type, int? count) =>
            {
                if (!byId.TryGetValue(id, out HostedLevel level))
                {
                    return Results.NotFound();
                }

                level.Simulation.Spawn(type, count ?? 1);
                return Results.Accepted();
            });

            app.MapPost("/api/reset/{id}", (string id) =>
            {
                if (!byId.TryGetValue(id, out HostedLevel level))
                {
                    return Results.NotFound();
                }

                level.Simulation.Reset();
                return Results.Accepted();
            });

            app.Run();
        }

        private static void DisposeAll(IEnumerable<HostedLevel> levels)
        {
            foreach (HostedLevel level in levels)
            {
                level.Dispose();
            }
        }

        /// <summary>
        /// Finds the manifests to host: an explicit <c>--manifest</c> (one level), or every manifest
        /// in the delivery folder (the gallery). An empty folder is an error, because a server with
        /// nothing to host is a configuration mistake, not a running server.
        /// </summary>
        private static string[] ResolveManifestPaths(string[] args)
        {
            string explicitPath = Argument(args, "--manifest");
            if (!string.IsNullOrEmpty(explicitPath))
            {
                return new[] { Path.GetFullPath(explicitPath) };
            }

            string folder = Argument(args, "--artifacts") ?? FindArtifactsFolder();
            if (folder == null || !Directory.Exists(folder))
            {
                throw new InvalidOperationException(
                    "No artifact folder was found. Bake the demo levels in Unity "
                    + "(Tools > DataSakura > Jitter Physics > Demo > Bake All Demo Scenes) "
                    + "or seed them with Server/tools/GenerateSampleArtifact, or pass "
                    + "--manifest <path to .manifest.json>.");
            }

            string[] manifests = Directory
                .GetFiles(folder, "*" + JitterPhysicsArtifactNaming.ManifestExtension)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            if (manifests.Length == 0)
            {
                throw new InvalidOperationException(
                    $"'{folder}' contains no '*{JitterPhysicsArtifactNaming.ManifestExtension}'. "
                    + "Export baked artifacts there first.");
            }

            return manifests;
        }

        /// <summary>
        /// Walks up from the binary looking for the delivery folder, so that <c>dotnet run</c>
        /// works from anywhere in the repository without configuration.
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


