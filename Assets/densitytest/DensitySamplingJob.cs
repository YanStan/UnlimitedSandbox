
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Unlimited.DensityTest
{
    [BurstCompile(
        FloatPrecision.Standard,
        FloatMode.Strict
    )]
    public struct DensitySamplingJob : IJobParallelFor
    {
        private const double EPS = 0.000001;
        private const double FarOutsideDensity = 1e6;
        private const int NeighborRadius = 2;

        [ReadOnly]
        public NativeArray<NativeSurfaceColumn> Columns;

        [ReadOnly]
        public NativeArray<NativeSurfacePoint> Points;

        [WriteOnly]
        public NativeArray<float> Densities;

        public int WorldWidth;
        public int WorldDepth;
        public int WorldHeight;

        public int3 GridOrigin;
        public int3 GridSize;

        public void Execute(int index)
        {
            int yzSize = GridSize.y * GridSize.z;

            int x = index / yzSize;
            int remainder = index % yzSize;

            int y = remainder / GridSize.z;
            int z = remainder % GridSize.z;

            x += GridOrigin.x;
            y += GridOrigin.y;
            z += GridOrigin.z;

            Densities[index] =
                ComputeDensityAtGridPoint(x, y, z);
        }

        private bool InBounds(int x, int z)
        {
            return x >= 0 && x < WorldWidth &&
                   z >= 0 && z < WorldDepth;
        }

        private int IDX(int x, int z)
        {
            return x * WorldDepth + z;
        }

        private double GridToWorldX(int x)
        {
            return x - WorldWidth * 0.5;
        }

        private double GridToWorldZ(int z)
        {
            return z - WorldDepth * 0.5;
        }

        private double Hypot(double x, double z)
        {
            return math.sqrt(x * x + z * z);
        }

        private double SignedDistanceToColumn(
            int x,
            int z,
            double y)
        {
            if (!InBounds(x, z))
                return FarOutsideDensity;

            NativeSurfaceColumn column =
                Columns[IDX(x, z)];

            double best = FarOutsideDensity;

            double currentBottom = 0.0;
            bool hasBottom = false;

            for (int i = 0; i < column.count; i++)
            {
                NativeSurfacePoint point =
                    Points[column.start + i];

                if (point.kind == SurfaceKind.Ceiling)
                {
                    currentBottom = point.y;
                    hasBottom = true;
                    continue;
                }

                if (point.kind != SurfaceKind.Top ||
                    !hasBottom)
                {
                    continue;
                }

                double bottom = currentBottom;
                double top = point.y;

                hasBottom = false;

                if (top <= bottom + EPS)
                    continue;

                if (y >= bottom && y <= top)
                {
                    double distToBottom = y - bottom;
                    double distToTop = top - y;

                    double inside = -math.max(
                        EPS,
                        math.min(
                            distToBottom,
                            distToTop
                        )
                    );

                    best = math.min(best, inside);
                }
                else
                {
                    double outside = math.min(
                        math.abs(y - bottom),
                        math.abs(y - top)
                    );

                    best = math.min(best, outside);
                }
            }

            return best;
        }

        private bool TryInterpolateSideRadius(
            int x,
            int z,
            double y,
            out double radius)
        {
            radius = 0.0;

            if (!InBounds(x, z))
                return false;

            NativeSurfaceColumn column =
                Columns[IDX(x, z)];

            if (column.count == 0 ||
                column.hasWallCuts == 0)
            {
                return false;
            }

            double centerX = GridToWorldX(x);
            double centerZ = GridToWorldZ(z);

            for (int i = 0; i < column.count - 1; i++)
            {
                NativeSurfacePoint a =
                    Points[column.start + i];

                NativeSurfacePoint b =
                    Points[column.start + i + 1];

                if (y < a.y - EPS ||
                    y > b.y + EPS)
                {
                    continue;
                }

                double dy = (double)b.y - a.y;

                double t = math.abs(dy) <= EPS
                    ? 0.0
                    : (y - a.y) / dy;

                t = math.clamp(t, 0.0, 1.0);

                double boundaryX =
                    a.realX +
                    (b.realX - a.realX) * t;

                double boundaryZ =
                    a.realZ +
                    (b.realZ - a.realZ) * t;

                radius = Hypot(
                    boundaryX - centerX,
                    boundaryZ - centerZ
                );

                return true;
            }

            for (int i = 0; i < column.count; i++)
            {
                NativeSurfacePoint point =
                    Points[column.start + i];

                if (math.abs(y - point.y) <= 0.5 + EPS)
                {
                    radius = Hypot(
                        point.realX - centerX,
                        point.realZ - centerZ
                    );

                    return true;
                }
            }

            return false;
        }

        private double ColumnDensityAt(
            double sampleX,
            double sampleY,
            double sampleZ,
            int colX,
            int colZ)
        {
            double vertical =
                SignedDistanceToColumn(
                    colX,
                    colZ,
                    sampleY
                );

            if (!TryInterpolateSideRadius(
                colX,
                colZ,
                sampleY,
                out double sideRadius))
            {
                return vertical;
            }

            double centerX = GridToWorldX(colX);
            double centerZ = GridToWorldZ(colZ);

            double distance = Hypot(
                sampleX - centerX,
                sampleZ - centerZ
            );

            double horizontal =
                distance - sideRadius;

            return math.max(
                vertical,
                horizontal
            );
        }

        private float ComputeDensityAtGridPoint(
            int x,
            int y,
            int z)
        {
            if (!InBounds(x, z) ||
                y < 0 ||
                y >= WorldHeight)
            {
                return (float)FarOutsideDensity;
            }

            double best =
                SignedDistanceToColumn(x, z, y);

            double wx = GridToWorldX(x);
            double wz = GridToWorldZ(z);

            for (int dx = -NeighborRadius;
                 dx <= NeighborRadius;
                 dx++)
            {
                for (int dz = -NeighborRadius;
                     dz <= NeighborRadius;
                     dz++)
                {
                    if (dx == 0 && dz == 0)
                        continue;

                    int nx = x + dx;
                    int nz = z + dz;

                    if (!InBounds(nx, nz))
                        continue;

                    NativeSurfaceColumn neighbor =
                        Columns[IDX(nx, nz)];

                    if (neighbor.hasWallCuts == 0)
                        continue;

                    double candidate =
                        ColumnDensityAt(
                            wx,
                            y,
                            wz,
                            nx,
                            nz
                        );

                    best = math.min(
                        best,
                        candidate
                    );
                }
            }

            return (float)best;
        }
    }
}
