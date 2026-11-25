using Unity.Collections;
using Unity.Mathematics;

namespace Unlimited.DensityTest
{
    // Grid coordinates, with a one-metre sample spacing.
    // Negative density is solid; gradients point toward increasing density.
    public static class DensityGradient
    {
        private static float Sample(NativeArray<float> densities, int3 size, int3 p)
        {
            return densities[(p.x * size.y + p.y) * size.z + p.z];
        }

        // Requires a one-sample halo on every side of the requested point.
        public static float3 AtGridPoint(NativeArray<float> densities,
            int3 origin, int3 size, int3 point)
        {
            int3 p = point - origin;
            int3 x = new int3(1, 0, 0);
            int3 y = new int3(0, 1, 0);
            int3 z = new int3(0, 0, 1);
            return 0.5f * new float3(
                Sample(densities, size, p + x) - Sample(densities, size, p - x),
                Sample(densities, size, p + y) - Sample(densities, size, p - y),
                Sample(densities, size, p + z) - Sample(densities, size, p - z));
        }

        // Interpolate raw gradients before normalizing at an edge crossing.
        public static float3 EdgeNormal(NativeArray<float> densities,
            int3 origin, int3 size, int3 a, int3 b, float t)
        {
            float3 ga = AtGridPoint(densities, origin, size, a);
            float3 gb = AtGridPoint(densities, origin, size, b);
            return math.normalizesafe(math.lerp(ga, gb, math.saturate(t)));
        }
    }
}
