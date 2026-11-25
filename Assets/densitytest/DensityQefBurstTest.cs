using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Unlimited.DensityTest
{
    public static class DensityQefBurstTest
    {
        [BurstCompile(CompileSynchronously = true)]
        private struct SolveJob : IJobParallelFor
        {
            [WriteOnly] public NativeArray<int> Results;
            public void Execute(int index)
            {
                var hits = new FixedList512Bytes<DensityHermiteSample>();
                int mode = index % 6;
                float x = .1f + .8f * ((index % 97) / 96f);
                float3 expected = new float3(x, .5f, .5f);
                if (mode != 0)
                {
                    hits.Add(new DensityHermiteSample { Position = new float3(x,0,0), Normal = new float3(1,0,0) });
                    hits.Add(new DensityHermiteSample { Position = new float3(x,1,1), Normal = new float3(1,0,0) });
                }
                if (mode == 2 || mode == 3)
                {
                    hits.Add(new DensityHermiteSample { Position = new float3(x,.75f,.5f), Normal = new float3(0,1,0) });
                    expected.y = .75f;
                }
                if (mode == 3)
                {
                    hits.Add(new DensityHermiteSample { Position = new float3(x,.75f,.3f), Normal = new float3(0,0,1) });
                    expected.z = .3f;
                }
                if (mode == 4)
                {
                    hits.Clear();
                    hits.Add(new DensityHermiteSample { Position = new float3(2,.5f,.5f), Normal = new float3(1,0,0) });
                    expected.x = 1f;
                }
                if (mode == 5)
                {
                    hits.Clear();
                    hits.Add(new DensityHermiteSample { Position = new float3(.5f), Normal = float3.zero });
                }
                bool solved = DensityQef.Solve(hits, out float3 p);
                bool ok = mode == 0 || mode == 5 ? !solved : solved && math.all(math.isfinite(p)) && math.all(p >= 0f) && math.all(p <= 1f) && math.distance(p, expected) < .002f;
                Results[index] = ok ? 1 : 0;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            const int count = 1536;
            var results = new NativeArray<int>(count, Allocator.TempJob);
            try
            {
                new SolveJob { Results = results }.Schedule(count, 32).Complete();
                int passed = 0;
                for (int i = 0; i < count; i++) passed += results[i];
                if (passed == count) Debug.Log($"[DensityQefBurst] PASS: {passed}/{count} parallel cases; Burst enabled: {BurstCompiler.IsEnabled}");
                else Debug.LogError($"[DensityQefBurst] FAIL: {passed}/{count} parallel cases");
            }
            finally { results.Dispose(); }
        }
    }
}
