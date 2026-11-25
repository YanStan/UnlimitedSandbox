
using System.Collections.Generic;
using UnityEngine;

namespace Unlimited.DensityTest
{
    public static class DensityWallCuts
    {
        private const float Epsilon = 0.000001f;

        public static void AddSphereWallCutBand(
            DensitySurfaceField field,
            DensityWorldConfig config,
            int x,
            int z,
            Vector3 sphereCenter,
            float radius,
            float columnWorldX,
            float columnWorldZ,
            float bandBottom,
            float bandTop)
        {
            bool AddBoundarySample(float sampleY)
            {
                if (!PickSphereBoundaryPointAtY(
                    sphereCenter,
                    radius,
                    columnWorldX,
                    columnWorldZ,
                    sampleY,
                    out Vector2 boundary))
                {
                    return false;
                }

                AddPoint(
                    field,
                    config,
                    x,
                    z,
                    sampleY,
                    SurfaceKind.WallCut,
                    boundary.x,
                    boundary.y
                );

                return true;
            }

            float innerBottom = Mathf.Min(
                bandTop,
                bandBottom + 0.15f
            );

            float innerTop = Mathf.Max(
                bandBottom,
                bandTop - 0.15f
            );

            bool inserted = false;

            if (innerBottom < bandTop - Epsilon)
            {
                inserted =
                    AddBoundarySample(innerBottom) ||
                    inserted;
            }

            if (innerTop > bandBottom + Epsilon &&
                Mathf.Abs(innerTop - innerBottom) >
                Epsilon)
            {
                inserted =
                    AddBoundarySample(innerTop) ||
                    inserted;
            }

            int yStart = Mathf.CeilToInt(bandBottom);

            int yEnd = Mathf.FloorToInt(bandTop);

            for (int y = yStart; y <= yEnd; y++)
            {
                if (y <= bandBottom + Epsilon ||
                    y >= bandTop - Epsilon)
                {
                    continue;
                }

                inserted =
                    AddBoundarySample(y) ||
                    inserted;
            }

            float midY =
                (bandBottom + bandTop) * 0.5f;

            if (!inserted ||
                (midY > bandBottom + Epsilon &&
                 midY < bandTop - Epsilon))
            {
                AddBoundarySample(midY);
            }
        }

        public static bool PickSphereBoundaryPointAtY(
            Vector3 sphereCenter,
            float radius,
            float columnWorldX,
            float columnWorldZ,
            float y,
            out Vector2 boundary)
        {
            boundary = default;

            float dy = y - sphereCenter.y;

            float horizontalRadiusSq =
                radius * radius - dy * dy;

            if (horizontalRadiusSq < 0f)
                return false;

            float horizontalRadius = Mathf.Sqrt(
                Mathf.Max(0f, horizontalRadiusSq)
            );

            float vx =
                columnWorldX - sphereCenter.x;

            float vz =
                columnWorldZ - sphereCenter.z;

            float length = Mathf.Sqrt(
                vx * vx + vz * vz
            );

            if (length <= Epsilon)
            {
                boundary = new Vector2(
                    sphereCenter.x,
                    sphereCenter.z + horizontalRadius
                );

                return true;
            }

            vx /= length;
            vz /= length;

            boundary = new Vector2(
                sphereCenter.x +
                vx * horizontalRadius,

                sphereCenter.z +
                vz * horizontalRadius
            );

            return true;
        }

        public static void AddPoint(
            DensitySurfaceField field,
            DensityWorldConfig config,
            int x,
            int z,
            float y,
            SurfaceKind kind,
            float realX,
            float realZ)
        {
            if (!field.InBounds(x, z))
                return;

            float yy = field.ClampY(y);

            int index = field.IDX(x, z);

            List<SurfacePoint> column =
                field.surfaceCache[index];

            if (column == null)
            {
                column = new List<SurfacePoint>();

                field.surfaceCache[index] = column;
            }

            for (int i = 0; i < column.Count; i++)
            {
                if (IsSamePoint(
                    column[i],
                    yy,
                    kind,
                    realX,
                    realZ))
                {
                    return;
                }
            }

            if (kind == SurfaceKind.WallCut)
            {
                for (int i = 0; i < column.Count; i++)
                {
                    if (!IsNearWallCut(
                        column[i],
                        yy,
                        realX,
                        realZ,
                        config))
                    {
                        continue;
                    }

                    SurfacePoint point = column[i];

                    point.y = yy;
                    point.realX = realX;
                    point.realZ = realZ;

                    column[i] = point;

                    SortColumn(column);

                    return;
                }
            }

            column.Add(
                new SurfacePoint
                {
                    y = yy,
                    kind = kind,
                    realX = realX,
                    realZ = realZ
                }
            );

            SortColumn(column);
        }

        public static void SortColumn(
            List<SurfacePoint> column)
        {
            column.Sort(
                (a, b) =>
                {
                    int yComparison =
                        a.y.CompareTo(b.y);

                    if (yComparison != 0)
                        return yComparison;

                    return GetKindOrder(a.kind)
                        .CompareTo(
                            GetKindOrder(b.kind)
                        );
                }
            );
        }

        private static int GetKindOrder(
            SurfaceKind kind)
        {
            if (kind == SurfaceKind.Ceiling)
                return 0;

            if (kind == SurfaceKind.WallCut)
                return 1;

            return 2;
        }

        private static bool IsSamePoint(
            SurfacePoint point,
            float y,
            SurfaceKind kind,
            float realX,
            float realZ)
        {
            return
                point.kind == kind &&

                Mathf.Abs(point.y - y) <
                Epsilon &&

                Mathf.Abs(point.realX - realX) <
                Epsilon &&

                Mathf.Abs(point.realZ - realZ) <
                Epsilon;
        }

        private static bool IsNearWallCut(
            SurfacePoint point,
            float y,
            float realX,
            float realZ,
            DensityWorldConfig config)
        {
            return
                point.kind == SurfaceKind.WallCut &&

                Mathf.Abs(point.y - y) <=
                config.wallCutEpsY &&

                Mathf.Abs(point.realX - realX) <=
                config.wallCutEpsXZ &&

                Mathf.Abs(point.realZ - realZ) <=
                config.wallCutEpsXZ;
        }
    }
}
