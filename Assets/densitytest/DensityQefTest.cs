using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Unlimited.DensityTest
{
    public static class DensityQefTest
    {
        private static void Add(ref FixedList512Bytes<DensityHermiteSample> h, float3 p, float3 n)
        { h.Add(new DensityHermiteSample { Position = p, Normal = n }); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            var h = new FixedList512Bytes<DensityHermiteSample>();
            bool ok = !DensityQef.Solve(h, out _);
            Add(ref h, new float3(.25f,0,0), new float3(1,0,0));
            Add(ref h, new float3(.25f,1,1), new float3(1,0,0));
            ok &= DensityQef.Solve(h, out float3 p) && math.distance(p,new float3(.25f,.5f,.5f)) < .002f;
            Add(ref h, new float3(0,.75f,.5f), new float3(0,1,0));
            ok &= DensityQef.Solve(h, out p) && math.distance(p,new float3(.25f,.75f,.5f)) < .002f;
            Add(ref h, new float3(.5f,.5f,.3f), new float3(0,0,1));
            ok &= DensityQef.Solve(h, out p) && math.distance(p,new float3(.25f,.75f,.3f)) < .002f;
            h.Clear();
            Add(ref h, new float3(2,.5f,.5f), new float3(1,0,0));
            ok &= DensityQef.Solve(h, out p) && math.distance(p,new float3(1,.5f,.5f)) < .002f;
            h.Clear();
            Add(ref h, new float3(.5f), float3.zero);
            ok &= !DensityQef.Solve(h, out _);
            if (ok) Debug.Log("[DensityQef] PASS: plane, edge, corner, bounds and degenerate cases");
            else Debug.LogError("[DensityQef] FAIL: QEF solver tests");
        }
    }
}
