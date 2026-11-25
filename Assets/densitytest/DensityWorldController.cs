
using UnityEngine;
using System.Diagnostics;

namespace Unlimited.DensityTest
{
    [RequireComponent(typeof(DensityWorldConfig))]
    public sealed class DensityWorldController : MonoBehaviour
    {
        private DensityWorldConfig config;

        public DensitySurfaceField SurfaceField
        {
            get;
            private set;
        }

        private void Start()
        {
            if (SurfaceField == null)
                GenerateWorld();
        }

        public void GenerateWorld()
        {
            config = GetComponent<DensityWorldConfig>();

            Stopwatch timer = Stopwatch.StartNew();

            SurfaceField = new DensitySurfaceField(config);

            SurfaceField.BuildSurfaceCache();

            timer.Stop();

            int columns = 0;
            int points = 0;
            int invalidColumns = 0;

            float minHeight = float.PositiveInfinity;
            float maxHeight = float.NegativeInfinity;

            for (int x = 0; x < SurfaceField.W; x++)
            {
                for (int z = 0; z < SurfaceField.D; z++)
                {
                    int index = SurfaceField.IDX(x, z);

                    var column = SurfaceField.surfaceCache[index];

                    if (column == null || column.Count == 0)
                    {
                        invalidColumns++;
                        continue;
                    }

                    columns++;
                    points += column.Count;

                    bool invalid = false;

                    foreach (var point in column)
                    {
                        if (float.IsNaN(point.y) ||
                            float.IsInfinity(point.y))
                        {
                            invalid = true;
                            continue;
                        }

                        if (point.kind != SurfaceKind.Top)
                            continue;

                        minHeight = Mathf.Min(
                            minHeight, point.y
                        );

                        maxHeight = Mathf.Max(
                            maxHeight, point.y
                        );
                    }

                    if (invalid)
                        invalidColumns++;
                }
            }

            UnityEngine.Debug.Log(
                "[DensityWorld] Generation completed\n" +
                $"World: {config.width} x " +
                $"{config.height} x {config.depth}\n" +
                $"Chunk size: {config.chunkSize}\n" +
                $"Density step: {config.densityStep} m\n" +
                $"Columns: {columns}\n" +
                $"Surface points: {points}\n" +
                $"Min height: {minHeight:F2} m\n" +
                $"Max height: {maxHeight:F2} m\n" +
                $"Invalid columns: {invalidColumns}\n" +
                $"Generation time: " +
                $"{timer.Elapsed.TotalMilliseconds:F2} ms"
            );

            if (invalidColumns > 0)
            {
                UnityEngine.Debug.LogError(
                    "[DensityWorld] Invalid surface data detected."
                );
            }
        }
    }
}
