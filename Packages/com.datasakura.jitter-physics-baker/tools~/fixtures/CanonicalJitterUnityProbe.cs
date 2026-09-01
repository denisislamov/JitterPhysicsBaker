using System;
using Jitter2;
using Jitter2.LinearMath;
using UnityEngine;

namespace DataSakura.JitterPhysics.DeliveryFixture
{
    /// <summary>Compile-time and runtime probe for a separately installed canonical Jitter DLL.</summary>
    public static class CanonicalJitterUnityProbe
    {
        /// <summary>Validates the public f32 StableMath contract from outside Jitter2.Core.</summary>
        public static void Run()
        {
            if (Precision.IsDoublePrecision)
                throw new InvalidOperationException("The canonical Unity Jitter profile must be f32.");
            if (!typeof(StableMath).IsPublic)
                throw new InvalidOperationException("StableMath must be public.");

            (float sin, float cos) = StableMath.SinCos(StableMath.QuarterPi);
            if (sin <= 0f || cos <= 0f || StableMath.Sqrt(4f) != 2f)
                throw new InvalidOperationException("StableMath numeric contract mismatch.");
            if (StableMath.QuantizeToInt64(-1.5f, 1f) != -2)
                throw new InvalidOperationException("StableMath quantization contract mismatch.");

            Debug.Log(
                "CANONICAL_JITTER_UNITY_OK assembly="
                + typeof(Precision).Assembly.GetName().Name
                + " precision=f32 stableMath=public");
        }
    }
}
