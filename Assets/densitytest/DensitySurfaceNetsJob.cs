
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Unlimited.DensityTest
{
    public struct DensityCellVertex
    {
        public float3 position;
        public byte active;
    }

    [BurstCompile(
        FloatPrecision.Standard,
        FloatMode.Strict
    )]
    public struct DensitySurfaceNetsJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<float> Densities;

        [WriteOnly]
        public NativeArray<DensityCellVertex> CellVertices;

        public int3 DensityGridOrigin;
        public int3 DensityGridSize;

        public int3 CellGridOrigin;
        public int3 CellGridSize;

        public int WorldWidth;
        public int WorldDepth;

        public void Execute(int index)
        {
            int yzSize =
                CellGridSize.y * CellGridSize.z;

            int x = index / yzSize;
            int remainder = index % yzSize;

            int y = remainder / CellGridSize.z;
            int z = remainder % CellGridSize.z;

            int3 cell = new int3(
                x + CellGridOrigin.x,
                y + CellGridOrigin.y,
                z + CellGridOrigin.z
            );

            CellVertices[index] =
                BuildCellVertex(cell);
        }

        private int DensityIndex(int3 point)
        {
            int3 local =
                point - DensityGridOrigin;

            return
                local.x *
                DensityGridSize.y *
                DensityGridSize.z +

                local.y *
                DensityGridSize.z +

                local.z;
        }

        private float GetDensity(int3 point)
        {
            return Densities[DensityIndex(point)];
        }

        private int3 GetCorner(int index)
        {
            return new int3(
                index & 1,
                (index >> 2) & 1,
                (index >> 1) & 1
            );
        }

        private int2 GetEdge(int index)
        {
            switch (index)
            {
                case 0: return new int2(0, 1);
                case 1: return new int2(1, 3);
                case 2: return new int2(2, 3);
                case 3: return new int2(0, 2);

                case 4: return new int2(4, 5);
                case 5: return new int2(5, 7);
                case 6: return new int2(6, 7);
                case 7: return new int2(4, 6);

                case 8: return new int2(0, 4);
                case 9: return new int2(1, 5);
                case 10: return new int2(2, 6);
                case 11: return new int2(3, 7);

                default: return new int2(0, 0);
            }
        }

        private DensityCellVertex BuildCellVertex(
            int3 cell)
        {
            FixedList128Bytes<double> values = default;

            int negativeCount = 0;

            // Sample all eight cell corners.

            for (int i = 0; i < 8; i++)
            {
                int3 corner =
                    cell + GetCorner(i);

                double density =
                    GetDensity(corner);

                values.Add(density);

                if (density < 0.0)
                    negativeCount++;
            }

            // Completely solid or completely empty.

            if (negativeCount == 0 ||
                negativeCount == 8)
            {
                return default;
            }

            double3 intersectionSum =
                new double3(0.0);

            int intersectionCount = 0;

            // Find intersections along the twelve edges.

            for (int i = 0; i < 12; i++)
            {
                int2 edge = GetEdge(i);

                double da = values[edge.x];
                double db = values[edge.y];

                bool solidA = da < 0.0;
                bool solidB = db < 0.0;

                if (solidA == solidB)
                    continue;

                double denominator = da - db;

                if (denominator == 0.0)
                    continue;

                double t = da / denominator;

                int3 cornerA =
                    GetCorner(edge.x);

                int3 cornerB =
                    GetCorner(edge.y);

                double3 a = new double3(
                    cornerA.x,
                    cornerA.y,
                    cornerA.z
                );

                double3 b = new double3(
                    cornerB.x,
                    cornerB.y,
                    cornerB.z
                );

                double3 intersection =
                    a + (b - a) * t;

                intersectionSum += intersection;

                intersectionCount++;
            }

            if (intersectionCount == 0)
                return default;

            // Surface Nets vertex is the average
            // of all edge intersections.

            double3 average =
                intersectionSum / intersectionCount;

            double worldX =
                cell.x + average.x -
                WorldWidth * 0.5;

            double worldY =
                cell.y + average.y;

            double worldZ =
                cell.z + average.z -
                WorldDepth * 0.5;

            return new DensityCellVertex
            {
                position = new float3(
                    (float)worldX,
                    (float)worldY,
                    (float)worldZ
                ),

                active = 1
            };
        }
    }
}
