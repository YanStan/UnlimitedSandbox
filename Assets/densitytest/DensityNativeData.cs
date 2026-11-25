
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace Unlimited.DensityTest
{
    public struct NativeSurfacePoint
    {
        public float y;
        public SurfaceKind kind;

        public float realX;
        public float realZ;
    }

    public struct NativeSurfaceColumn
    {
        public int start;
        public int count;

        public byte hasWallCuts;
    }

    public sealed class DensityNativeData : IDisposable
    {
        public int W { get; private set; }
        public int D { get; private set; }
        public int H { get; private set; }

        public NativeArray<NativeSurfaceColumn> Columns;

        public NativeArray<NativeSurfacePoint> Points;
        private int usedPoints;
        private readonly Dictionary<int,int> editedCapacity = new Dictionary<int,int>();

        public bool IsCreated =>
            Columns.IsCreated && Points.IsCreated;

        public DensityNativeData(
            DensitySurfaceField field,
            Allocator allocator = Allocator.Persistent)
        {
            W = field.W;
            D = field.D;
            H = field.H;

            int columnCount = W * D;
            int pointCount = 0;

            // Count all surface points.

            for (int i = 0; i < columnCount; i++)
            {
                var column = field.surfaceCache[i];

                if (column != null)
                    pointCount += column.Count;
            }

            // Allocate Burst-compatible memory.

            try
            {
                Columns =
                    new NativeArray<NativeSurfaceColumn>(
                        columnCount,
                        allocator
                    );

                Points =
                    new NativeArray<NativeSurfacePoint>(
                        pointCount,
                        allocator
                    );

                CopySurfaceData(field);
                usedPoints = pointCount;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void CopySurfaceData(
            DensitySurfaceField field)
        {
            int offset = 0;

            for (int x = 0; x < W; x++)
            {
                for (int z = 0; z < D; z++)
                {
                    int index = field.IDX(x, z);

                    var source =
                        field.surfaceCache[index];

                    int count =
                        source != null ? source.Count : 0;

                    byte hasWallCuts = 0;

                    for (int i = 0; i < count; i++)
                    {
                        var point = source[i];

                        Points[offset + i] =
                            new NativeSurfacePoint
                            {
                                y = point.y,
                                kind = point.kind,
                                realX = point.realX,
                                realZ = point.realZ
                            };

                        if (point.kind == SurfaceKind.WallCut)
                            hasWallCuts = 1;
                    }

                    Columns[index] =
                        new NativeSurfaceColumn
                        {
                            start = offset,
                            count = count,
                            hasWallCuts = hasWallCuts
                        };

                    offset += count;
                }
            }
        }

        public void RefreshColumns(DensitySurfaceField field, List<DensityTouchedColumn> touched)
        {
            foreach (var item in touched)
            {
                int index = IDX(item.x, item.z);
                var source = field.surfaceCache[index];
                int count = source == null ? 0 : source.Count;
                var column = Columns[index];
                int capacity = editedCapacity.TryGetValue(index, out int reserved) ? reserved : column.count;
                if (count > capacity)
                {
                    capacity = Math.Max(count, Math.Max(4, capacity * 2));
                    int required = checked(usedPoints + capacity);
                    if (required > Points.Length)
                    {
                        int length = Math.Max(required, Points.Length + Math.Max(1024, Points.Length / 4));
                        var replacement = new NativeArray<NativeSurfacePoint>(length, Allocator.Persistent);
                        NativeArray<NativeSurfacePoint>.Copy(Points, replacement, usedPoints);
                        Points.Dispose();
                        Points = replacement;
                    }
                    column.start = usedPoints;
                    usedPoints = required;
                }
                editedCapacity[index] = capacity;
                column.count = count;
                column.hasWallCuts = 0;
                for (int i = 0; i < count; i++)
                {
                    var point = source[i];
                    Points[column.start + i] = new NativeSurfacePoint { y = point.y, kind = point.kind, realX = point.realX, realZ = point.realZ };
                    if (point.kind == SurfaceKind.WallCut) column.hasWallCuts = 1;
                }
                Columns[index] = column;
            }
        }

        public int IDX(int x, int z)
        {
            return x * D + z;
        }

        public bool InBounds(int x, int z)
        {
            return x >= 0 && x < W &&
                   z >= 0 && z < D;
        }

        public float2 GridToWorld(int x, int z)
        {
            return new float2(
                x - W * 0.5f,
                z - D * 0.5f
            );
        }

        public void Dispose()
        {
            if (Columns.IsCreated)
                Columns.Dispose();

            if (Points.IsCreated)
                Points.Dispose();

            Columns = default;
            Points = default;
        }
    }
}
