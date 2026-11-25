
using System.Collections.Generic;
using UnityEngine;

namespace Unlimited.DensityTest
{
    public sealed class DensitySampleCache
    {
        private readonly Dictionary<Vector3Int, float> cache =
            new Dictionary<Vector3Int, float>(65536);

        public long Hits { get; private set; }
        public long Misses { get; private set; }

        public int Count => cache.Count;

        public bool TryGet(
            int x, int y, int z,
            out float density)
        {
            var key = new Vector3Int(x, y, z);

            if (cache.TryGetValue(key, out density))
            {
                Hits++;
                return true;
            }

            Misses++;
            return false;
        }

        public void Store(
            int x, int y, int z, float density)
        {
            var key = new Vector3Int(x, y, z);

            cache[key] = density;
        }

        public void InvalidateRegion(
            int minX, int minY, int minZ,
            int maxX, int maxY, int maxZ)
        {
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        cache.Remove(
                            new Vector3Int(x, y, z)
                        );
                    }
                }
            }
        }

        public void Clear()
        {
            cache.Clear();

            Hits = 0;
            Misses = 0;
        }

        public float HitRate
        {
            get
            {
                long total = Hits + Misses;

                if (total == 0)
                    return 0f;

                return (float)(
                    Hits * 100.0 / total
                );
            }
        }
    }
}
