
using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Unlimited.DensityTest
{
    public sealed class DensityBurstSampler : IDisposable
    {
        private readonly DensitySurfaceField field;
        private readonly DensitySampler sampler;

        private DensityNativeData nativeData;

        public long TotalSamples { get; private set; }
        public int TotalBatches { get; private set; }

        public double TotalJobMilliseconds
        {
            get;
            private set;
        }

        public double TotalCacheMilliseconds
        {
            get;
            private set;
        }

        public DensityBurstSampler(
            DensitySurfaceField field,
            DensitySampler sampler)
        {
            this.field = field;
            this.sampler = sampler;

            nativeData = new DensityNativeData(
                field,
                Allocator.Persistent
            );
        }

        public void PrepareChunk(
            int startX,
            int startY,
            int startZ,
            int endX,
            int endY,
            int endZ)
        {
            // Include shared boundary cells and their corners.

            int3 origin = new int3(
                startX - 1,
                startY - 1,
                startZ - 1
            );

            int3 size = new int3(
                endX - startX + 2,
                endY - startY + 2,
                endZ - startZ + 2
            );

            PrepareRegion(origin, size);
        }

        public void PrepareRegion(
            int3 origin,
            int3 size)
        {
            if (size.x <= 0 ||
                size.y <= 0 ||
                size.z <= 0)
            {
                return;
            }

            int count = checked(
                size.x * size.y * size.z
            );

            var densities = new NativeArray<float>(
                count,
                Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory
            );

            try
            {
                var job = new DensitySamplingJob
                {
                    Columns = nativeData.Columns,
                    Points = nativeData.Points,
                    Densities = densities,

                    WorldWidth = field.W,
                    WorldDepth = field.D,
                    WorldHeight = field.H,

                    GridOrigin = origin,
                    GridSize = size
                };

                var timer =
                    System.Diagnostics.Stopwatch.StartNew();

                JobHandle handle = job.Schedule(
                    count,
                    64
                );

                handle.Complete();

                timer.Stop();

                TotalJobMilliseconds +=
                    timer.Elapsed.TotalMilliseconds;

                // Transfer results into the existing cache.

                timer.Restart();

                int index = 0;

                for (int x = 0; x < size.x; x++)
                {
                    for (int y = 0; y < size.y; y++)
                    {
                        for (int z = 0; z < size.z; z++)
                        {
                            sampler.Cache.Store(
                                x + origin.x,
                                y + origin.y,
                                z + origin.z,
                                densities[index]
                            );

                            index++;
                        }
                    }
                }

                timer.Stop();

                TotalCacheMilliseconds +=
                    timer.Elapsed.TotalMilliseconds;

                TotalSamples += count;
                TotalBatches++;
            }
            finally
            {
                if (densities.IsCreated)
                    densities.Dispose();
            }
        }

        public void RefreshNativeData()
        {
            nativeData?.Dispose();

            nativeData = new DensityNativeData(
                field,
                Allocator.Persistent
            );

            sampler.ClearCache();
        }

        public void PrintStatistics()
        {
            Debug.Log(
                "[DensityBurstSampler] Statistics\n" +

                $"Batches: {TotalBatches}\n" +
                $"Computed samples: {TotalSamples}\n" +

                $"Burst execution: " +
                $"{TotalJobMilliseconds:F2} ms\n" +

                $"Cache transfer: " +
                $"{TotalCacheMilliseconds:F2} ms\n" +

                $"Cached samples: {sampler.Cache.Count}\n" +

                $"Cache hits: {sampler.Cache.Hits}\n" +
                $"Cache misses: {sampler.Cache.Misses}\n" +

                $"Cache hit rate: " +
                $"{sampler.Cache.HitRate:F4}%"
            );
        }

        public void Dispose()
        {
            nativeData?.Dispose();
            nativeData = null;
        }
    }
}
