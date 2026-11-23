using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class BurstTest : MonoBehaviour
{
    [BurstCompile]
    private struct NoiseJob : IJobParallelFor
    {
        public NativeArray<float> Results;

        public void Execute(int index)
        {
            Results[index] = noise.cnoise(
                new float2(index * 0.1f, index * 0.2f)
            );
        }
    }

    private void Start()
    {
        var results = new NativeArray<float>(
            1000,
            Allocator.TempJob
        );

        var job = new NoiseJob
        {
            Results = results
        };

        job.Schedule(results.Length, 64).Complete();

        Debug.Log($"Burst Job работает. Результат: {results[0]}");

        results.Dispose();
    }
}
