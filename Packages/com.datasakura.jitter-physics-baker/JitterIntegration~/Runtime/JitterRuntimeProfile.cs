using System;
using System.Runtime.InteropServices;
using DataSakura.JitterPhysics.Contracts;
using Jitter2;
using Jitter2.LinearMath;
#if !DATASAKURA_SERVER_GLOBAL_REAL
using Real = System.Single;
#endif

namespace DataSakura.JitterPhysics.Integration
{
    /// <summary>Result of verifying the installed Jitter scalar and public layout profile.</summary>
    public readonly struct JitterRuntimeProfileResult
    {
        internal JitterRuntimeProfileResult(PhysicsArtifactError error)
        {
            Error = error;
        }

        /// <summary>Typed incompatibility, or the default value when the profile is supported.</summary>
        public PhysicsArtifactError Error { get; }

        /// <summary>Whether the loaded Jitter runtime is the supported canonical f32 profile.</summary>
        public bool Succeeded => !Error.IsError;
    }

    /// <summary>
    /// Verifies the scalar mode and public math layouts before an artifact can affect a world.
    /// </summary>
    /// <remarks>
    /// The source alias is intentionally local to this compilation unit because Unity 6000.3
    /// compiles consumer scripts as C# 9 and cannot use C# 10 global aliases. Every installable
    /// source file that needs the scalar name must spell it exactly as
    /// <c>using Real = System.Single</c>. The server projection defines
    /// <c>DATASAKURA_SERVER_GLOBAL_REAL</c> and supplies the same alias through MSBuild; repository
    /// enforcement rejects variations.
    /// </remarks>
    public static class JitterRuntimeProfile
    {
        /// <summary>The only precision mode supported by this package release.</summary>
        public const string PrecisionMode = "f32";

        /// <summary>Checks the loaded canonical Jitter assembly without changing any state.</summary>
        public static JitterRuntimeProfileResult VerifyCanonicalF32()
        {
            Type vectorScalar = typeof(JVector).GetField(nameof(JVector.X))?.FieldType;
            Type quaternionScalar = typeof(JQuaternion).GetField(nameof(JQuaternion.X))?.FieldType;

            return VerifyLayout(
                Precision.IsDoublePrecision,
                typeof(Real),
                vectorScalar,
                quaternionScalar,
                Marshal.SizeOf<JVector>(),
                Marshal.SizeOf<JQuaternion>());
        }

        internal static JitterRuntimeProfileResult VerifyLayout(
            bool isDoublePrecision,
            Type declaredReal,
            Type vectorScalar,
            Type quaternionScalar,
            int vectorSize,
            int quaternionSize)
        {
            if (isDoublePrecision
                || declaredReal != typeof(float)
                || vectorScalar != typeof(float)
                || quaternionScalar != typeof(float)
                || vectorSize != 12
                || quaternionSize != 16)
            {
                return new JitterRuntimeProfileResult(new PhysicsArtifactError(
                    PhysicsArtifactErrorCode.IncompatibleRuntime,
                    "The loaded Jitter2.Core is not the supported f32 layout: expected Real=float, "
                    + "JVector=12 bytes and JQuaternion=16 bytes. Startup was refused before "
                    + "artifact loading or world mutation."));
            }

            return default;
        }
    }
}
