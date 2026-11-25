using Unity.Collections;
using Unity.Mathematics;

namespace Unlimited.DensityTest
{
    public struct DensityHermiteSample
    {
        public float3 Position; // Cell-local coordinates, in metres.
        public float3 Normal;
        public int Edge;
    }

    public static class DensityHermite
    {
        private static float Sample(NativeArray<float> densities, int3 origin, int3 size, int3 point)
        {
            int3 p = point - origin;
            return densities[(p.x * size.y + p.y) * size.z + p.z];
        }

        // Requires a one-sample halo around all eight corners.
        // Output is cleared each call; edge IDs are axis * 4 + transverse corner.
        public static void Collect(NativeArray<float> densities, int3 origin, int3 size,
            int3 cell, ref FixedList512Bytes<DensityHermiteSample> output)
        {
            output.Clear();
            for (int axis = 0; axis < 3; axis++)
            {
                int3 step = axis == 0 ? new int3(1,0,0) : axis == 1 ? new int3(0,1,0) : new int3(0,0,1);
                for (int k = 0; k < 4; k++)
                {
                    int u = k & 1;
                    int v = k >> 1;
                    int3 offset = axis == 0 ? new int3(0,u,v) : axis == 1 ? new int3(u,0,v) : new int3(u,v,0);
                    int3 a = cell + offset;
                    int3 b = a + step;
                    float da = Sample(densities, origin, size, a);
                    float db = Sample(densities, origin, size, b);
                    if ((da < 0f) == (db < 0f)) continue;
                    float t = math.saturate(da / (da - db));
                    float3 normal = DensityGradient.EdgeNormal(densities, origin, size, a, b, t);
                    if (math.lengthsq(normal) < 1e-12f)
                        normal = (float3)step * (db > da ? 1f : -1f);
                    output.Add(new DensityHermiteSample { Position = (float3)offset + (float3)step * t,
                        Normal = normal, Edge = axis * 4 + k });
                }
            }
        }
    }
}
