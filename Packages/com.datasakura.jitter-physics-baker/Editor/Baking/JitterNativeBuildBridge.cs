using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using DataSakura.JitterPhysics.ArtifactCodec;
using DataSakura.JitterPhysics.Authoring;
using DataSakura.JitterPhysics.Contracts;
using UnityEngine;

namespace DataSakura.JitterPhysics.Editor.Baking
{
    /// <summary>
    /// Late-bound handoff to the explicitly installed Jitter-native baker. Reflection is used
    /// only at the command boundary so the package Editor assembly still compiles before Setup.
    /// </summary>
    internal static class JitterNativeBuildBridge
    {
        private const string BuilderTypeName =
            "DataSakura.JitterPhysics.JitterNative.UnityBoundary.JitterNativeUnityArtifactBuilder";
        private const string CodecTypeName =
            "DataSakura.JitterPhysics.JitterNative.Codec.PhysicsArtifactCodec";

        internal static bool IsAvailable => FindType(BuilderTypeName) != null && FindType(CodecTypeName) != null;

        internal static JitterPhysicsBuildResult Build(
            JitterPhysicsLevel level,
            string runtimeCompatibilityId,
            string managedLevelId)
        {
            var issues = new JitterPhysicsIssueLog();
            Type builder = FindType(BuilderTypeName);
            Type codec = FindType(CodecTypeName);
            if (builder == null || codec == null)
            {
                issues.Error(
                    "The Jitter-native bake adapter is not installed. Open Jitter Physics Setup "
                    + "and run Install Integration after installing Jitter2.",
                    level);
                return new JitterPhysicsBuildResult(null, issues);
            }

            try
            {
                MethodInfo build = builder.GetMethod(
                    "Build", BindingFlags.Public | BindingFlags.Static);
                object nativeResult = build.Invoke(
                    null, new object[] { level, runtimeCompatibilityId, managedLevelId });
                CopyDiagnostics(nativeResult, issues);

                object nativeArtifact = nativeResult.GetType().GetProperty("Artifact").GetValue(nativeResult);
                if (nativeArtifact == null || issues.HasErrors)
                {
                    return new JitterPhysicsBuildResult(null, issues);
                }

                MethodInfo write = codec.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Single(method => method.Name == "Write" && method.GetParameters().Length == 1);
                byte[] payload = (byte[])write.Invoke(null, new[] { nativeArtifact });
                PhysicsArtifactResult decoded = PhysicsArtifactReader.Read(payload);
                if (!decoded.Succeeded)
                {
                    issues.Error(
                        "The native builder produced schema-one bytes that failed verification: "
                        + decoded.Error,
                        level);
                    return new JitterPhysicsBuildResult(null, issues);
                }

                return new JitterPhysicsBuildResult(decoded.Artifact, issues);
            }
            catch (Exception exception)
            {
                Exception cause = exception is TargetInvocationException invocation
                    && invocation.InnerException != null
                    ? invocation.InnerException
                    : exception;
                issues.Error("The Jitter-native bake adapter failed: " + cause.Message, level);
                return new JitterPhysicsBuildResult(null, issues);
            }
        }

        private static void CopyDiagnostics(object nativeResult, JitterPhysicsIssueLog issues)
        {
            Type resultType = nativeResult.GetType();
            var errors = (IEnumerable)resultType.GetProperty("Errors").GetValue(nativeResult);
            foreach (object error in errors)
            {
                Type type = error.GetType();
                string message = (string)type.GetProperty("Message").GetValue(error);
                var context = (UnityEngine.Object)type.GetProperty("Context").GetValue(error);
                issues.Error(message, context);
            }

            var warnings = (IEnumerable)resultType.GetProperty("Warnings").GetValue(nativeResult);
            foreach (object warning in warnings) issues.Warning((string)warning);
        }

        private static Type FindType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int index = 0; index < assemblies.Length; index++)
            {
                Type type = assemblies[index].GetType(fullName, throwOnError: false);
                if (type != null) return type;
            }

            return null;
        }
    }
}
