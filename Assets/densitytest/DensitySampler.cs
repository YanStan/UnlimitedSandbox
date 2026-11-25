
using System;
using System.Collections.Generic;

namespace Unlimited.DensityTest
{
    public sealed class DensitySampler
    {
        private const double EPS = 0.000001;
        private const double FarOutsideDensity = 1e6;
        private const int NeighborRadius = 2;

        private readonly DensitySurfaceField field;

        public DensitySampleCache Cache { get; } =
            new DensitySampleCache();

        private struct SolidInterval
        {
            public double bottom;
            public double top;

            public SolidInterval(
                double bottom, double top)
            {
                this.bottom = bottom;
                this.top = top;
            }
        }

        public DensitySampler(DensitySurfaceField field)
        {
            this.field = field;
        }

        private List<SurfacePoint> GetRawColumn(
            int x, int z)
        {
            if (!field.InBounds(x, z))
                return null;

            return field.surfaceCache[field.IDX(x, z)];
        }

        private bool ColumnHasWallCuts(int x, int z)
        {
            var column = GetRawColumn(x, z);

            if (column == null)
                return false;

            foreach (var point in column)
            {
                if (point.kind == SurfaceKind.WallCut)
                    return true;
            }

            return false;
        }

        private List<SolidInterval> GetColumnIntervals(
            int x, int z)
        {
            var intervals = new List<SolidInterval>();
            var column = GetRawColumn(x, z);

            if (column == null)
                return intervals;

            double? currentBottom = null;

            foreach (var point in column)
            {
                if (point.kind == SurfaceKind.Ceiling)
                {
                    currentBottom = point.y;
                    continue;
                }

                if (point.kind == SurfaceKind.Top &&
                    currentBottom.HasValue)
                {
                    if (point.y > currentBottom.Value + EPS)
                    {
                        intervals.Add(
                            new SolidInterval(
                                currentBottom.Value,
                                point.y
                            )
                        );
                    }

                    currentBottom = null;
                }
            }

            return intervals;
        }

        private double SignedDistanceToIntervals(
            double y,
            List<SolidInterval> intervals)
        {
            double best = FarOutsideDensity;

            foreach (var interval in intervals)
            {
                if (y >= interval.bottom &&
                    y <= interval.top)
                {
                    double distToBottom =
                        y - interval.bottom;

                    double distToTop =
                        interval.top - y;

                    double inside = -Math.Max(
                        EPS,
                        Math.Min(
                            distToBottom,
                            distToTop
                        )
                    );

                    best = Math.Min(best, inside);
                    continue;
                }

                double outside = Math.Min(
                    Math.Abs(y - interval.bottom),
                    Math.Abs(y - interval.top)
                );

                best = Math.Min(best, outside);
            }

            return best;
        }

        private double Hypot(double x, double z)
        {
            return Math.Sqrt(x * x + z * z);
        }

        private double? InterpolateSideRadiusAtY(
            int x, int z, double y)
        {
            var column = GetRawColumn(x, z);

            if (column == null || column.Count == 0)
                return null;

            if (!ColumnHasWallCuts(x, z))
                return null;

            double centerX = field.GridToWorldX(x);
            double centerZ = field.GridToWorldZ(z);

            for (int i = 0; i < column.Count - 1; i++)
            {
                var a = column[i];
                var b = column[i + 1];

                if (y < a.y - EPS || y > b.y + EPS)
                    continue;

                double dy = b.y - a.y;

                double t = Math.Abs(dy) <= EPS
                    ? 0.0
                    : (y - a.y) / dy;

                t = Math.Max(0.0, Math.Min(1.0, t));

                double boundaryX =
                    a.realX + (b.realX - a.realX) * t;

                double boundaryZ =
                    a.realZ + (b.realZ - a.realZ) * t;

                return Hypot(
                    boundaryX - centerX,
                    boundaryZ - centerZ
                );
            }

            foreach (var point in column)
            {
                if (Math.Abs(y - point.y) <= 0.5 + EPS)
                {
                    return Hypot(
                        point.realX - centerX,
                        point.realZ - centerZ
                    );
                }
            }

            return null;
        }

        private double ColumnDensityAt(
            double sampleX,
            double sampleY,
            double sampleZ,
            int colX,
            int colZ)
        {
            var intervals =
                GetColumnIntervals(colX, colZ);

            if (intervals.Count == 0)
                return FarOutsideDensity;

            double vertical =
                SignedDistanceToIntervals(
                    sampleY, intervals
                );

            double? sideRadius =
                InterpolateSideRadiusAtY(
                    colX, colZ, sampleY
                );

            if (!sideRadius.HasValue)
                return vertical;

            double centerX = field.GridToWorldX(colX);
            double centerZ = field.GridToWorldZ(colZ);

            double distance = Hypot(
                sampleX - centerX,
                sampleZ - centerZ
            );

            double horizontal =
                distance - sideRadius.Value;

            return Math.Max(vertical, horizontal);
        }

        // Cached density sampling.

        public float ComputeDensityAtGridPoint(
            int x, int y, int z)
        {
            if (Cache.TryGet(
                x, y, z, out float cachedDensity))
            {
                return cachedDensity;
            }

            float density = ComputeDensityUncached(
                x, y, z
            );

            Cache.Store(x, y, z, density);

            return density;
        }

        // Existing density algorithm.

        private float ComputeDensityUncached(
            int x, int y, int z)
        {
            if (!field.InBounds(x, z) ||
                y < 0 || y >= field.H)
            {
                return (float)FarOutsideDensity;
            }

            var intervals = GetColumnIntervals(x, z);

            double best = SignedDistanceToIntervals(
                y, intervals
            );

            double wx = field.GridToWorldX(x);
            double wz = field.GridToWorldZ(z);

            for (int dx = -NeighborRadius;
                 dx <= NeighborRadius; dx++)
            {
                for (int dz = -NeighborRadius;
                     dz <= NeighborRadius; dz++)
                {
                    if (dx == 0 && dz == 0)
                        continue;

                    int nx = x + dx;
                    int nz = z + dz;

                    if (!field.InBounds(nx, nz))
                        continue;

                    if (!ColumnHasWallCuts(nx, nz))
                        continue;

                    double candidate = ColumnDensityAt(
                        wx, y, wz, nx, nz
                    );

                    best = Math.Min(best, candidate);
                }
            }

            return (float)best;
        }

        public void InvalidateDensityRegion(
            int minX, int minY, int minZ,
            int maxX, int maxY, int maxZ)
        {
            Cache.InvalidateRegion(
                minX - NeighborRadius,
                minY,
                minZ - NeighborRadius,

                maxX + NeighborRadius,
                maxY,
                maxZ + NeighborRadius
            );
        }

        public void ClearCache()
        {
            Cache.Clear();
        }
    }
}
