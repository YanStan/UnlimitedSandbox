
using System.Collections.Generic;
using UnityEngine;

namespace Unlimited.DensityTest
{
    public struct DensityTouchedColumn
    {
        public int x;
        public int z;

        public float minY;
        public float maxY;

        public DensityTouchedColumn(
            int x,
            int z,
            float minY,
            float maxY)
        {
            this.x = x;
            this.z = z;
            this.minY = minY;
            this.maxY = maxY;
        }
    }

    public sealed class DensityDigResult
    {
        public bool Changed =>
            TouchedColumns.Count > 0;

        public readonly List<DensityTouchedColumn>
            TouchedColumns =
                new List<DensityTouchedColumn>();
    }

    public sealed class DensityDigOperation
    {
        private const float Epsilon = 0.000001f;

        private readonly DensitySurfaceField field;

        private readonly DensityWorldConfig config;

        public DensityDigOperation(
            DensitySurfaceField field,
            DensityWorldConfig config)
        {
            this.field = field;
            this.config = config;
        }

        public DensityDigResult BuildSphere(Vector3 center, float radius)
        {
            if (radius <= 0f) return new DensityDigResult();
            return BuildShape(new Bounds(center, Vector3.one * (2f * radius)), radius, true);
        }

        public DensityDigResult BuildBox(Bounds box, float rounding = 0f)
        {
            return BuildShape(box, rounding, false);
        }

        private DensityDigResult BuildShape(Bounds box, float rounding, bool sphere)
        {
            var result = new DensityDigResult();
            Vector3 e = box.extents;
            float r = Mathf.Clamp(rounding, 0f, Mathf.Min(e.x, e.y, e.z));
            int minX = Mathf.Max(0, field.WorldToGridX(box.min.x));
            int maxX = Mathf.Min(field.W - 1, field.WorldToGridX(box.max.x));
            int minZ = Mathf.Max(0, field.WorldToGridZ(box.min.z));
            int maxZ = Mathf.Min(field.D - 1, field.WorldToGridZ(box.max.z));
            for (int x = minX; x <= maxX; x++)
            for (int z = minZ; z <= maxZ; z++)
            {
                if (field.IsNoDigBorderColumn(x, z)) continue;
                float cx = field.GridToWorldX(x);
                float cz = field.GridToWorldZ(z);
                float dx = Mathf.Abs(cx - box.center.x);
                float dz = Mathf.Abs(cz - box.center.z);
                if (dx > e.x || dz > e.z) continue;
                float halfY = e.y;
                if (sphere || r > 0f)
                {
                    dx = Mathf.Max(0f, dx - (e.x - r));
                    dz = Mathf.Max(0f, dz - (e.z - r));
                    float remaining = r * r - dx * dx - dz * dz;
                    if (remaining <= 0f) continue;
                    halfY = e.y - r + Mathf.Sqrt(remaining);
                }
                float bottom = field.ClampY(box.center.y - halfY);
                float top = field.ClampY(box.center.y + halfY);
                if (top <= bottom + Epsilon) continue;
                int index = field.IDX(x, z);
                var column = field.surfaceCache[index];
                var old = DensitySolidIntervals.ColumnToIntervals(column);
                var merged = DensitySolidIntervals.UnionSpan(old, bottom, top);
                if (!DensitySolidIntervals.IntervalsDiffer(old, merged)) continue;
                var walls = column == null ? new List<SurfacePoint>() :
                    column.FindAll(p => p.kind == SurfaceKind.WallCut &&
                        (p.y < bottom || p.y > top));
                if (column == null)
                    field.surfaceCache[index] = column = new List<SurfacePoint>();
                column.Clear();
                foreach (var interval in merged)
                {
                    DensityWallCuts.AddPoint(field, config, x, z, interval.bottom,
                        SurfaceKind.Ceiling, cx, cz);
                    DensityWallCuts.AddPoint(field, config, x, z, interval.top,
                        SurfaceKind.Top, cx, cz);
                }
                column.AddRange(walls);
                DensityWallCuts.SortColumn(column);
                result.TouchedColumns.Add(new DensityTouchedColumn(x, z, bottom, top));
            }
            return result;
        }

        public DensityDigResult DigBox(Bounds box, float rounding = 0f)
        {
            var result = new DensityDigResult();
            Vector3 min = box.min;
            Vector3 max = box.max;
            float bottom = field.ClampY(min.y);
            float top = field.ClampY(max.y);
            if (top <= bottom + Epsilon) return result;

            int minX = Mathf.Max(0, field.WorldToGridX(min.x));
            int maxX = Mathf.Min(field.W - 1, field.WorldToGridX(max.x));
            int minZ = Mathf.Max(0, field.WorldToGridZ(min.z));
            int maxZ = Mathf.Min(field.D - 1, field.WorldToGridZ(max.z));
            for (int x = minX; x <= maxX; x++)
            for (int z = minZ; z <= maxZ; z++)
            {
                if (field.IsNoDigBorderColumn(x, z)) continue;
                float cx = field.GridToWorldX(x);
                float cz = field.GridToWorldZ(z);
                if (cx < min.x || cx >= max.x || cz < min.z || cz >= max.z)
                    continue;
                var column = field.surfaceCache[field.IDX(x, z)];
                if (column == null || column.Count == 0) continue;
                var old = DensitySolidIntervals.ColumnToIntervals(column);
                float cutBottom = bottom;
                float cutTop = top;
                if (rounding > 0f)
                {
                    float r = Mathf.Min(rounding, box.extents.x, box.extents.y, box.extents.z);
                    float dx = Mathf.Max(0f, Mathf.Abs(cx - box.center.x) - (box.extents.x - r));
                    float dz = Mathf.Max(0f, Mathf.Abs(cz - box.center.z) - (box.extents.z - r));
                    float remain = r * r - dx * dx - dz * dz;
                    if (remain <= 0f) continue;
                    float halfY = box.extents.y - r + Mathf.Sqrt(remain);
                    cutBottom = field.ClampY(box.center.y - halfY);
                    cutTop = field.ClampY(box.center.y + halfY);
                }
                if (cutTop <= cutBottom + Epsilon) continue;
                var carved = DensitySolidIntervals.SubtractSpanFromIntervals(old, cutBottom, cutTop);
                if (!DensitySolidIntervals.IntervalsDiffer(old, carved)) continue;
                // Keep prior wall samples outside the new cut. Do not sanitize:
                // the existing cleanup can remove thin solids or close narrow cuts.
                var walls = column.FindAll(p => p.kind == SurfaceKind.WallCut &&
                    (p.y < cutBottom || p.y > cutTop));
                column.Clear();
                foreach (var interval in carved)
                {
                    DensityWallCuts.AddPoint(field, config, x, z, interval.bottom,
                        SurfaceKind.Ceiling, cx, cz);
                    DensityWallCuts.AddPoint(field, config, x, z, interval.top,
                        SurfaceKind.Top, cx, cz);
                }
                column.AddRange(walls);
                DensityWallCuts.SortColumn(column);
                result.TouchedColumns.Add(new DensityTouchedColumn(x, z, bottom, top));
            }
            return result;
        }

        public DensityDigResult DigSphere(
            Vector3 center,
            float radius)
        {
            var result = new DensityDigResult();

            if (radius <= 0f)
                return result;

            int minX = Mathf.Max(
                0,
                field.WorldToGridX(
                    center.x - radius - 1f
                )
            );

            int maxX = Mathf.Min(
                field.W - 1,
                field.WorldToGridX(
                    center.x + radius + 1f
                )
            );

            int minZ = Mathf.Max(
                0,
                field.WorldToGridZ(
                    center.z - radius - 1f
                )
            );

            int maxZ = Mathf.Min(
                field.D - 1,
                field.WorldToGridZ(
                    center.z + radius + 1f
                )
            );

            float radiusSq = radius * radius;

            for (int x = minX; x <= maxX; x++)
            {
                float cx = field.GridToWorldX(x);

                for (int z = minZ; z <= maxZ; z++)
                {
                    if (field.IsNoDigBorderColumn(x, z))
                        continue;

                    float cz = field.GridToWorldZ(z);

                    float dx = cx - center.x;

                    float dz = cz - center.z;

                    float distXZSq =
                        dx * dx + dz * dz;

                    if (distXZSq > radiusSq)
                        continue;

                    int index = field.IDX(x, z);

                    List<SurfacePoint> column =
                        field.surfaceCache[index];

                    if (column == null ||
                        column.Count == 0)
                    {
                        continue;
                    }

                    var existingIntervals =
                        DensitySolidIntervals
                            .ColumnToIntervals(column);

                    if (existingIntervals.Count == 0)
                        continue;

                    float maxVerticalCut =
                        Mathf.Sqrt(
                            Mathf.Max(
                                0f,
                                radiusSq - distXZSq
                            )
                        );

                    float cutBottom = field.ClampY(
                        center.y -
                        maxVerticalCut -
                        config.digExpandMargin
                    );

                    float cutTop = field.ClampY(
                        center.y +
                        maxVerticalCut +
                        config.digExpandMargin
                    );

                    if (cutTop <= cutBottom + Epsilon)
                        continue;

                    var carvedIntervals =
                        DensitySolidIntervals
                            .SubtractSpanFromIntervals(
                                existingIntervals,
                                cutBottom,
                                cutTop
                            );

                    carvedIntervals =
                        DensitySolidIntervals
                            .SanitizeIntervals(
                                carvedIntervals,
                                field,
                                config
                            );

                    if (!DensitySolidIntervals
                        .IntervalsDiffer(
                            existingIntervals,
                            carvedIntervals
                        ))
                    {
                        continue;
                    }

                    column.Clear();

                    foreach (DensitySolidInterval interval
                             in carvedIntervals)
                    {
                        DensityWallCuts.AddPoint(
                            field,
                            config,
                            x,
                            z,
                            interval.bottom,
                            SurfaceKind.Ceiling,
                            cx,
                            cz
                        );

                        DensityWallCuts.AddPoint(
                            field,
                            config,
                            x,
                            z,
                            interval.top,
                            SurfaceKind.Top,
                            cx,
                            cz
                        );
                    }

                    var cavitySlices =
                        DensitySolidIntervals
                            .CollectCavitySlices(
                                existingIntervals,
                                cutBottom,
                                cutTop,
                                config
                            );

                    foreach (DensitySolidInterval slice
                             in cavitySlices)
                    {
                        DensityWallCuts
                            .AddSphereWallCutBand(
                                field,
                                config,
                                x,
                                z,
                                center,
                                radius,
                                cx,
                                cz,
                                slice.bottom,
                                slice.top
                            );
                    }

                    DensityWallCuts.SortColumn(column);

                    result.TouchedColumns.Add(
                        new DensityTouchedColumn(
                            x,
                            z,
                            cutBottom,
                            cutTop
                        )
                    );
                }
            }

            return result;
        }
    }
}
