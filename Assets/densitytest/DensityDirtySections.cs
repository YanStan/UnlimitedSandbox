
using System.Collections.Generic;
using UnityEngine;

namespace Unlimited.DensityTest
{
    public sealed class DensityDirtySections
    {
        private readonly DensitySurfaceField field;

        private readonly DensityWorldConfig config;

        private readonly HashSet<Vector3Int> dirtySections =
            new HashSet<Vector3Int>();

        private const int HorizontalPadding = 4;

        private const int VerticalPadding = 2;

        public int Count => dirtySections.Count;

        public DensityDirtySections(
            DensitySurfaceField field,
            DensityWorldConfig config)
        {
            this.field = field;
            this.config = config;
        }

        public void MarkDigResult(
            DensityDigResult result)
        {
            if (result == null || !result.Changed)
                return;

            foreach (DensityTouchedColumn column
                     in result.TouchedColumns)
            {
                MarkColumn(column);
            }
        }

        private void MarkColumn(
            DensityTouchedColumn column)
        {
            int minX = column.x - HorizontalPadding;

            int maxX = column.x + HorizontalPadding;

            int minZ = column.z - HorizontalPadding;

            int maxZ = column.z + HorizontalPadding;

            int minY =
                Mathf.FloorToInt(column.minY) -
                VerticalPadding;

            int maxY =
                Mathf.CeilToInt(column.maxY) +
                VerticalPadding;

            MarkRegion(
                minX,
                minY,
                minZ,
                maxX,
                maxY,
                maxZ
            );
        }

        public void MarkRegion(
            int minX,
            int minY,
            int minZ,
            int maxX,
            int maxY,
            int maxZ)
        {
            if (maxX < 0 ||
                maxY < 0 ||
                maxZ < 0 ||
                minX >= field.W ||
                minY >= field.H ||
                minZ >= field.D)
            {
                return;
            }

            minX = Mathf.Clamp(minX, 0, field.W - 1);
            maxX = Mathf.Clamp(maxX, 0, field.W - 1);

            minY = Mathf.Clamp(minY, 0, field.H - 1);
            maxY = Mathf.Clamp(maxY, 0, field.H - 1);

            minZ = Mathf.Clamp(minZ, 0, field.D - 1);
            maxZ = Mathf.Clamp(maxZ, 0, field.D - 1);

            int chunkSize =
                Mathf.Max(1, config.chunkSize);

            int sectionHeight =
                Mathf.Max(1, config.sectionHeight);

            int firstChunkX = minX / chunkSize;
            int lastChunkX = maxX / chunkSize;

            int firstChunkZ = minZ / chunkSize;
            int lastChunkZ = maxZ / chunkSize;

            int firstSectionY = minY / sectionHeight;
            int lastSectionY = maxY / sectionHeight;

            for (int x = firstChunkX;
                 x <= lastChunkX;
                 x++)
            {
                for (int y = firstSectionY;
                     y <= lastSectionY;
                     y++)
                {
                    for (int z = firstChunkZ;
                         z <= lastChunkZ;
                         z++)
                    {
                        dirtySections.Add(
                            new Vector3Int(x, y, z)
                        );
                    }
                }
            }
        }

        public List<Vector3Int> Consume()
        {
            var result =
                new List<Vector3Int>(dirtySections);

            dirtySections.Clear();

            result.Sort(
                (a, b) =>
                {
                    int x = a.x.CompareTo(b.x);

                    if (x != 0)
                        return x;

                    int y = a.y.CompareTo(b.y);

                    if (y != 0)
                        return y;

                    return a.z.CompareTo(b.z);
                }
            );

            return result;
        }

        public void Clear()
        {
            dirtySections.Clear();
        }
    }
}

