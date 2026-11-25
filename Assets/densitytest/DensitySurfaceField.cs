
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unlimited.DensityTest
{
    public enum SurfaceKind
    {
        Top,
        Ceiling,
        WallCut
    }

    [System.Serializable]
    public struct SurfacePoint
    {
        public float y;
        public SurfaceKind kind;
        public float realX;
        public float realZ;
    }

    public sealed class DensitySurfaceField
    {
        public readonly int W;
        public readonly int D;
        public readonly int H;

        public readonly List<SurfacePoint>[] surfaceCache;

        private readonly HashSet<Vector3Int> sharpCells = new HashSet<Vector3Int>();

        public Vector3Int[] CopySharpCells()
        {
            var copy = new Vector3Int[sharpCells.Count];
            sharpCells.CopyTo(copy);
            return copy;
        }

        public void RestoreSharpCells(Vector3Int[] cells, int shiftY)
        {
            sharpCells.Clear();
            foreach (var cell in cells) sharpCells.Add(new Vector3Int(cell.x, cell.y + shiftY, cell.z));
        }

        public bool HasSharpCells => sharpCells.Count != 0;

        public bool IsSharpCell(int x, int y, int z) => sharpCells.Contains(new Vector3Int(x,y,z));

        public void MarkBrushCells(DensityDigResult result, bool sharp)
        {
            foreach (var column in result.TouchedColumns)
            for (int x = column.x - 1; x <= column.x; x++)
            for (int z = column.z - 1; z <= column.z; z++)
            for (int y = Mathf.FloorToInt(column.minY) - 1; y <= Mathf.CeilToInt(column.maxY); y++)
            {
                var key = new Vector3Int(x,y,z);
                if (sharp) sharpCells.Add(key); else sharpCells.Remove(key);
            }
        }

        private readonly short[] baseTerrainTop;
        private readonly int noDigBorderVoxels;

        private readonly int terrainBaseLevel;
        private readonly int edgeBlendWidth;
        private readonly int edgeFlatLevel;

        private readonly DensityNoise noise;

        public DensitySurfaceField(DensityWorldConfig config)
        {
            W = config.width;
            D = config.depth;
            H = config.height;

            noDigBorderVoxels = config.noDigBorderVoxels;

            terrainBaseLevel = config.terrainBaseLevel;
            edgeBlendWidth = Math.Max(0, config.edgeBlendWidth);
            edgeFlatLevel = config.edgeFlatLevel;

            noise = new DensityNoise(config.seed);

            surfaceCache = new List<SurfacePoint>[W * D];
            baseTerrainTop = new short[W * D];
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

        public bool IsNoDigBorderColumn(int x, int z)
        {
            int b = noDigBorderVoxels;

            return x < b || z < b ||
                   x >= W - b || z >= D - b;
        }

        public bool IsWorldPointInNoDigBorder(
            float worldX, float worldZ)
        {
            return IsNoDigBorderColumn(
                WorldToGridX(worldX),
                WorldToGridZ(worldZ)
            );
        }

        public float ClampY(float y)
        {
            return Mathf.Clamp(y, 0f, H - 1f);
        }

        public float GridToWorldX(int x)
        {
            return x - W * 0.5f;
        }

        public float GridToWorldZ(int z)
        {
            return z - D * 0.5f;
        }

        public int WorldToGridX(float worldX)
        {
            return Mathf.FloorToInt(worldX + W * 0.5f);
        }

        public int WorldToGridZ(float worldZ)
        {
            return Mathf.FloorToInt(worldZ + D * 0.5f);
        }

        // ==============================
        // TERRAIN HEIGHT
        // ==============================

        private double HeightAt(int x, int z)
        {
            const double HILL_WIDTH_XZ = 1.5;

            double sx = x / HILL_WIDTH_XZ;
            double sz = z / HILL_WIDTH_XZ;

            double n = noise.Noise2(
                sx * 0.02,
                sz * 0.02
            );

            double a = Math.Abs(n);
            const double k = 0.8;

            double compressed =
                n * (1.0 / (1.0 + a * k));

            double baseNoise = noise.Noise2(
                sx * 0.004,
                sz * 0.004
            ) * 3.5;

            double h =
                terrainBaseLevel +
                baseNoise +
                compressed * 10.0;

            if (edgeBlendWidth > 0)
            {
                double distToEdge = Math.Min(
                    Math.Min(x, z),
                    Math.Min(W - 1 - x, D - 1 - z)
                );

                double tRaw = Math.Max(
                    0.0,
                    Math.Min(
                        1.0,
                        distToEdge / edgeBlendWidth
                    )
                );

                double t =
                    tRaw * tRaw * (3.0 - 2.0 * tRaw);

                h = edgeFlatLevel +
                    (h - edgeFlatLevel) * t;
            }

            return Math.Max(
                1.0,
                Math.Min(H - 2.0, h)
            );
        }

        // ==============================
        // INITIAL SOLID TERRAIN
        // ==============================

        private void AddTerrainBase(
            int x, int z, double terrainTopY)
        {
            if (!InBounds(x, z))
                return;

            double topY = Math.Max(
                0.0,
                Math.Min(H - 1.0, terrainTopY)
            );

            int index = IDX(x, z);

            baseTerrainTop[index] = (short)topY;

            float realX = GridToWorldX(x);
            float realZ = GridToWorldZ(z);

            surfaceCache[index] =
                new List<SurfacePoint>(2)
                {
                    new SurfacePoint
                    {
                        y = 0f,
                        kind = SurfaceKind.Ceiling,
                        realX = realX,
                        realZ = realZ
                    },

                    new SurfacePoint
                    {
                        y = (float)topY,
                        kind = SurfaceKind.Top,
                        realX = realX,
                        realZ = realZ
                    }
                };
        }

        public void BuildSurfaceCache()
        {
            sharpCells.Clear();
            Array.Clear(
                surfaceCache,
                0,
                surfaceCache.Length
            );

            Array.Clear(
                baseTerrainTop,
                0,
                baseTerrainTop.Length
            );

            for (int x = 0; x < W; x++)
            {
                for (int z = 0; z < D; z++)
                {
                    double h = HeightAt(x, z);

                    AddTerrainBase(x, z, h);
                }
            }
        }

        public int GetBaseTerrainTop(int x, int z)
        {
            if (!InBounds(x, z))
                return 1;

            return baseTerrainTop[IDX(x, z)];
        }

        public float GetFlatWorldY()
        {
            return Mathf.Clamp(
                edgeFlatLevel,
                1f,
                H - 2f
            );
        }
    }
}
