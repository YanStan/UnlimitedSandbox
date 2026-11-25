
using System.Collections;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Unlimited.DensityTest
{
    [RequireComponent(typeof(DensityWorldController))]
    public sealed class DensityBurstTest : MonoBehaviour
    {
        [Header("Test Settings")]
        public int testHeight = 32;

        [Min(1)]
        public int batchSize = 64;

        public float tolerance = 0.00001f;

        private IEnumerator Start()
        {
            var controller =
                GetComponent<DensityWorldController>();

            while (controller.SurfaceField == null)
                yield return null;

            // Wait until the initial frame is completed.
            yield return null;

            RunTest(controller.SurfaceField);
        }

        private void RunTest(DensitySurfaceField field)
        {
            var config =
                GetComponent<DensityWorldConfig>();

            int height = Mathf.Clamp(
                testHeight, 1, field.H
            );

            int startY = Mathf.Clamp(
                config.terrainBaseLevel - height / 2,
                0,
                field.H - height
            );

            int3 origin = new int3(
                0, startY, 0
            );

            int3 size = new int3(
                field.W, height, field.D
            );

            int count = size.x * size.y * size.z;

            var sampler = new DensitySampler(field);

            var timer =
                System.Diagnostics.Stopwatch.StartNew();

            DensityNativeData nativeData = null;

            NativeArray<float> burstResults = default;
            NativeArray<float> managedResults = default;

            try
            {
                nativeData = new DensityNativeData(
                    field,
                    Allocator.Persistent
                );

                burstResults = new NativeArray<float>(
                    count,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory
                );

                managedResults = new NativeArray<float>(
                    count,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory
                );

                timer.Stop();

                double preparationMs =
                    timer.Elapsed.TotalMilliseconds;

                var job = new DensitySamplingJob
                {
                    Columns = nativeData.Columns,
                    Points = nativeData.Points,
                    Densities = burstResults,

                    WorldWidth = field.W,
                    WorldDepth = field.D,
                    WorldHeight = field.H,

                    GridOrigin = origin,
                    GridSize = size
                };

                // Warm up Burst before measurement.

                timer.Restart();

                JobHandle warmup = job.Schedule(
                    count,
                    Mathf.Max(1, batchSize)
                );

                warmup.Complete();

                timer.Stop();

                double warmupMs =
                    timer.Elapsed.TotalMilliseconds;

                // Measure Burst execution.

                timer.Restart();

                JobHandle handle = job.Schedule(
                    count,
                    Mathf.Max(1, batchSize)
                );

                handle.Complete();

                timer.Stop();

                double burstMs =
                    timer.Elapsed.TotalMilliseconds;

                // Measure the original managed sampler.

                timer.Restart();

                int index = 0;

                for (int x = 0; x < size.x; x++)
                {
                    for (int y = 0; y < size.y; y++)
                    {
                        for (int z = 0; z < size.z; z++)
                        {
                            managedResults[index] =
                                sampler.ComputeDensityAtGridPoint(
                                    x + origin.x,
                                    y + origin.y,
                                    z + origin.z
                                );

                            index++;
                        }
                    }
                }

                timer.Stop();

                double managedMs =
                    timer.Elapsed.TotalMilliseconds;

                // Compare results.

                int exactMatches = 0;
                int mismatches = 0;
                int signMismatches = 0;
                int invalidValues = 0;

                float maxDifference = 0f;
                double totalDifference = 0.0;

                for (int i = 0; i < count; i++)
                {
                    float a = managedResults[i];
                    float b = burstResults[i];

                    if (float.IsNaN(a) ||
                        float.IsNaN(b) ||
                        float.IsInfinity(a) ||
                        float.IsInfinity(b))
                    {
                        invalidValues++;
                        continue;
                    }

                    float difference =
                        Mathf.Abs(a - b);

                    totalDifference += difference;

                    maxDifference = Mathf.Max(
                        maxDifference,
                        difference
                    );

                    if (a == b)
                        exactMatches++;

                    if (difference > tolerance)
                        mismatches++;

                    if ((a < 0f) != (b < 0f))
                        signMismatches++;
                }

                double speedup = burstMs > 0.0
                    ? managedMs / burstMs
                    : 0.0;

                int validCount = count - invalidValues;

                double averageDifference =
                    validCount > 0
                    ? totalDifference / validCount
                    : 0.0;

                bool passed =
                    mismatches == 0 &&
                    signMismatches == 0 &&
                    invalidValues == 0;

                string report =
                    "[DensityBurstTest] " +
                    (passed ? "PASSED" : "FAILED") + "\n" +

                    $"Grid: {size.x} x {size.y} x {size.z}\n" +
                    $"Y range: {startY} - " +
                    $"{startY + height - 1}\n" +

                    $"Samples: {count}\n" +
                    $"Preparation: {preparationMs:F2} ms\n" +
                    $"Burst warmup: {warmupMs:F2} ms\n" +
                    $"Managed sampler: {managedMs:F2} ms\n" +
                    $"Burst sampler: {burstMs:F2} ms\n" +
                    $"Speedup: {speedup:F2}x\n" +

                    $"Exact matches: {exactMatches}\n" +
                    $"Mismatches: {mismatches}\n" +
                    $"Sign mismatches: {signMismatches}\n" +
                    $"Invalid values: {invalidValues}\n" +

                    $"Max difference: {maxDifference:G9}\n" +
                    $"Average difference: {averageDifference:G9}";

                if (passed)
                    Debug.Log(report);
                else
                    Debug.LogError(report);
            }
            finally
            {
                if (managedResults.IsCreated)
                    managedResults.Dispose();

                if (burstResults.IsCreated)
                    burstResults.Dispose();

                nativeData?.Dispose();

                sampler.ClearCache();
            }
        }
    }
}
