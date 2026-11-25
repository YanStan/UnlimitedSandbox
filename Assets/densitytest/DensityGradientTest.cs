using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Unlimited.DensityTest
{
    public static class DensityGradientTest
    {
        [BurstCompile]
        private struct TestJob : IJob
        {
            [ReadOnly] public NativeArray<float> Samples;
            public NativeArray<float3> Result;
            public void Execute()
            {
                int3 size = new int3(4);
                int3 origin = new int3(-7, 12, 20);
                int3 a = origin + new int3(1);
                Result[0] = DensityGradient.AtGridPoint(Samples, origin, size, a);
                Result[1] = DensityGradient.EdgeNormal(Samples, origin, size, a,
                    a + new int3(1, 0, 0), 0.5f);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            var samples = new NativeArray<float>(64, Allocator.TempJob);
            var result = default(NativeArray<float3>);
            try
            {
                result = new NativeArray<float3>(2, Allocator.TempJob);
                for (int x = 0; x < 4; x++)
                    for (int y = 0; y < 4; y++)
                        for (int z = 0; z < 4; z++)
                            samples[(x * 4 + y) * 4 + z] = 2*x - 3*y + 4*z;
                new TestJob { Samples = samples, Result = result }.Schedule().Complete();
                float3 expected = new float3(2, -3, 4);
                bool ok = math.distance(result[0], expected) < 0.0001f &&
                    math.distance(result[1], math.normalize(expected)) < 0.0001f;
                if (ok) Debug.Log("[DensityGradient] PASS: gradient and edge normal");
                else Debug.LogError("[DensityGradient] FAIL: " + result[0] + " / " + result[1]);
            }
            finally
            {
                if (result.IsCreated) result.Dispose();
                if (samples.IsCreated) samples.Dispose();
            }
        }
    }
}
