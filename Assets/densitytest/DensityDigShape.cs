
using UnityEngine;

namespace Unlimited.DensityTest
{
    public struct DensityBowlOperation
    {
        public Vector3 center;
        public float radius;
        public float depth;
    }

    public static class DensityDigShape
    {
        public static DensityBowlOperation ComputeBowl(
            Vector3 hit,
            Vector3 direction,
            DensityWorldConfig config,
            bool fill = false,
            float strength = 0f,
            float angleFactor = 0.25f)
        {
            float length = direction.magnitude;

            Vector3 normalizedDirection =
                direction / (length > 0f ? length : 1f);

            strength = Mathf.Clamp01(strength);

            angleFactor = Mathf.Clamp01(angleFactor);

            float depth = Mathf.Clamp(
                config.minDigDepth +
                angleFactor * 0.28f +
                strength * 0.50f,

                config.minDigDepth,
                config.maxDigDepth
            );

            float centerOffset = Mathf.Max(
                0f,
                config.digRadius - depth
            );

            float sign = fill ? 1f : -1f;

            Vector3 center =
                hit +
                normalizedDirection *
                centerOffset *
                sign;

            return new DensityBowlOperation
            {
                center = center,
                radius = config.digRadius,
                depth = depth
            };
        }
    }
}
