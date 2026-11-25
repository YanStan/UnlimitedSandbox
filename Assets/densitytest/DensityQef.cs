using Unity.Collections;
using Unity.Mathematics;

namespace Unlimited.DensityTest
{
    public static class DensityQef
    {
        // Box-constrained least squares, with a small mass-point regularizer.
        // Enumerate free/lower/upper states for each axis (27 candidates).
        public static bool Solve(in FixedList512Bytes<DensityHermiteSample> hits, out float3 vertex)
        {
            vertex = new float3(0.5f);
            if (hits.Length == 0) return false;
            float3 mass = float3.zero;
            float3x3 a = new float3x3(float3.zero, float3.zero, float3.zero);
            float3 b = float3.zero;
            int count = 0;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (!math.all(math.isfinite(h.Position)) || !math.all(math.isfinite(h.Normal))) continue;
                float3 n = math.normalizesafe(h.Normal);
                if (math.lengthsq(n) < 0.5f) continue;
                mass += h.Position;
                count++;
                a.c0 += n * n.x; a.c1 += n * n.y; a.c2 += n * n.z;
                b += n * math.dot(n, h.Position);
            }
            if (count == 0) return false;
            mass /= count;
            float lambda = 0.0001f * count;
            a.c0.x += lambda; a.c1.y += lambda; a.c2.z += lambda;
            b += lambda * mass;
            float best = float.PositiveInfinity;
            for (int state = 0; state < 27; state++)
            {
                int3 s = new int3(state % 3, state / 3 % 3, state / 9);
                float3 free = math.select(new float3(0), new float3(1), s == 0);
                float3 fixedPoint = math.select(new float3(0), new float3(1), s == 2);
                float3 rhs = free * (b - math.mul(a, fixedPoint)) + fixedPoint;
                float3x3 m = new float3x3(a.c0 * free * free.x, a.c1 * free * free.y, a.c2 * free * free.z);
                m.c0.x += 1 - free.x; m.c1.y += 1 - free.y; m.c2.z += 1 - free.z;
                float det = math.determinant(m);
                if (!math.isfinite(det) || math.abs(det) < 1e-15f) continue;
                float3 p = math.mul(math.inverse(m), rhs);
                if (!math.all(math.isfinite(p)) || math.any(p < -0.0001f) || math.any(p > 1.0001f)) continue;
                p = math.saturate(p);
                float cost = lambda * math.lengthsq(p - mass);
                for (int i = 0; i < hits.Length; i++)
                {
                    var h = hits[i];
                    if (!math.all(math.isfinite(h.Position)) || !math.all(math.isfinite(h.Normal))) continue;
                    float3 n = math.normalizesafe(h.Normal);
                    if (math.lengthsq(n) < 0.5f) continue;
                    float d = math.dot(n, p - h.Position);
                    cost += d * d;
                }
                if (cost < best) { best = cost; vertex = p; }
            }
            return math.isfinite(best);
        }
    }
}
