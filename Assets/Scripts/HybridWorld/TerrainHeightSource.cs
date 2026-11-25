using UnityEngine;
using Unity.Mathematics;

namespace Unlimited.HybridWorld
{
    // Main-thread adapter. Does not modify terrain or depend on MapMagic internals.
    public static class TerrainHeightSource
    {
        public static bool TrySample(double3 global, double3 origin, out float height)
        {
            height = 0;
            Vector3 p = WorldCoordinates.ToLocal(global, origin);
            Terrain best = null;
            foreach (Terrain t in Terrain.activeTerrains)
            {
                if (!t || !t.terrainData || !t.drawHeightmap) continue;
                Vector3 a = t.GetPosition(), s = t.terrainData.size;
                if (s.x <= 0 || s.z <= 0 || p.x < a.x || p.z < a.z || p.x > a.x+s.x || p.z > a.z+s.z) continue;
                if (!best || t.terrainData.heightmapResolution > best.terrainData.heightmapResolution) best = t;
            }
            if (!best) return false;
            Vector3 b = best.GetPosition(), size = best.terrainData.size;
            height = best.terrainData.GetInterpolatedHeight((p.x-b.x)/size.x, (p.z-b.z)/size.z) + b.y;
            return true;
        }
    }
}
