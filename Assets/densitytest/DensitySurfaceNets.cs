
using System;
using UnityEngine;

namespace Unlimited.DensityTest
{
    public sealed class DensitySurfaceNets
    {
        private const double EPS = 0.000001;

        private readonly DensitySurfaceField field;
        private readonly DensitySampler sampler;

        private readonly double[] densities =
            new double[8];

        private static readonly Vector3Int[] CornerOffsets =
        {
            new Vector3Int(0, 0, 0),
            new Vector3Int(1, 0, 0),
            new Vector3Int(0, 0, 1),
            new Vector3Int(1, 0, 1),

            new Vector3Int(0, 1, 0),
            new Vector3Int(1, 1, 0),
            new Vector3Int(0, 1, 1),
            new Vector3Int(1, 1, 1)
        };

        private static readonly int[,] EdgeCornerPairs =
        {
            { 0, 1 },
            { 1, 3 },
            { 2, 3 },
            { 0, 2 },

            { 4, 5 },
            { 5, 7 },
            { 6, 7 },
            { 4, 6 },

            { 0, 4 },
            { 1, 5 },
            { 2, 6 },
            { 3, 7 }
        };

        public DensitySurfaceNets(
            DensitySurfaceField field,
            DensitySampler sampler)
        {
            this.field = field;
            this.sampler = sampler;
        }

        public bool TryBuildCellVertex(
            int cellX,
            int cellY,
            int cellZ,
            out Vector3 vertex)
        {
            vertex = Vector3.zero;

            bool hasInside = false;
            bool hasOutside = false;

            // Sample eight cell corners.

            for (int i = 0; i < 8; i++)
            {
                Vector3Int offset = CornerOffsets[i];

                double density =
                    sampler.ComputeDensityAtGridPoint(
                        cellX + offset.x,
                        cellY + offset.y,
                        cellZ + offset.z
                    );

                densities[i] = density;

                if (density < 0.0)
                    hasInside = true;
                else
                    hasOutside = true;
            }

            // Completely solid or empty cell.

            if (!hasInside || !hasOutside)
                return false;

            double sumX = 0.0;
            double sumY = 0.0;
            double sumZ = 0.0;

            int crossings = 0;

            // Find surface intersections.

            for (int i = 0; i < 12; i++)
            {
                int ia = EdgeCornerPairs[i, 0];
                int ib = EdgeCornerPairs[i, 1];

                double da = densities[ia];
                double db = densities[ib];

                if ((da < 0.0) == (db < 0.0))
                    continue;

                Vector3Int a = CornerOffsets[ia];
                Vector3Int b = CornerOffsets[ib];

                double ax = cellX + a.x;
                double ay = cellY + a.y;
                double az = cellZ + a.z;

                double bx = cellX + b.x;
                double by = cellY + b.y;
                double bz = cellZ + b.z;

                double denom = da - db;

                double t = Math.Abs(denom) <= EPS
                    ? 0.5
                    : da / denom;

                double clampedT = Math.Max(
                    0.0,
                    Math.Min(1.0, t)
                );

                sumX += ax + (bx - ax) * clampedT;
                sumY += ay + (by - ay) * clampedT;
                sumZ += az + (bz - az) * clampedT;

                crossings++;
            }

            if (crossings == 0)
                return false;

            // Average intersection positions.

            double px = sumX / crossings;
            double py = sumY / crossings;
            double pz = sumZ / crossings;

            // Convert grid coordinates to world coordinates.

            vertex = new Vector3(
                (float)(px - field.W * 0.5),
                (float)py,
                (float)(pz - field.D * 0.5)
            );

            return true;
        }
    }
}
