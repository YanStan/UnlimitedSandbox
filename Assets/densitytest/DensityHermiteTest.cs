using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Unlimited.DensityTest
{
    public static class DensityHermiteTest
    {
        [BurstCompile]
        private struct TestJob : IJob
        {
            [ReadOnly] public NativeArray<float> Samples;
            public NativeArray<int> Result;
            public void Execute()
            {
                var hits = new FixedList512Bytes<DensityHermiteSample>();
                int3 origin = new int3(-7,12,20);
                int3 cell = origin + new int3(1);
                DensityHermite.Collect(Samples, origin, new int3(5), cell, ref hits);
                bool ok = hits.Length == 4;
                int mask = 0;
                for (int i = 0; i < hits.Length; i++)
                {
                    var h = hits[i];
                    ok &= math.abs(h.Position.x - 0.25f) < 0.0001f &&
                        math.distance(h.Normal, new float3(1,0,0)) < 0.0001f && h.Edge >= 0 && h.Edge < 4;
                    mask |= 1 << h.Edge;
                }
                ok &= mask == 15;
                DensityHermite.Collect(Samples, origin, new int3(5), cell + new int3(1,0,0), ref hits);
                ok &= hits.Length == 0;
                Result[0] = ok ? 1 : 0;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            var samples = new NativeArray<float>(125, Allocator.TempJob);
            var result = default(NativeArray<int>);
            try
            {
                result = new NativeArray<int>(1, Allocator.TempJob);
                for (int x = 0; x < 5; x++)
                    for (int y = 0; y < 5; y++)
                        for (int z = 0; z < 5; z++)
                            samples[(x * 5 + y) * 5 + z] = x - 1.25f;
                new TestJob { Samples = samples, Result = result }.Schedule().Complete();
                if (result[0] == 1) Debug.Log("[DensityHermite] PASS: edge crossings, normals and empty cell");
                else Debug.LogError("[DensityHermite] FAIL: edge crossings, normals or empty cell");
            }
            finally
            {
                if (result.IsCreated) result.Dispose();
                if (samples.IsCreated) samples.Dispose();
            }
        }
    }
}
