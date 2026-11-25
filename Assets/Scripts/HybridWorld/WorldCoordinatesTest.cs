using System;
using Unity.Mathematics;
using UnityEngine;

namespace Unlimited.HybridWorld
{
    public static class WorldCoordinatesTest
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            try
            {
                int count = 0;
                foreach (long x in new long[] { -129, -65, -64, -63, -1, 0, 1, 63, 64, 65, 129, 450000, -450000 })
                foreach (long z in new long[] { -65, -1, 0, 63, 64 })
                {
                    var key = new WorldChunkKey(x, -2, z);
                    var p = WorldCoordinates.DensityOrigin(key) + new double3(.25, .5, 15.75);
                    var tile = WorldCoordinates.TileOf(key);
                    var local = WorldCoordinates.ChunkInTile(key);
                    var hole = WorldCoordinates.HoleOffset(key);
                    if (!WorldCoordinates.DensityAt(p).Equals(key) || !WorldCoordinates.TileAt(p).Equals(tile) ||
                        tile.X * 64 + local.x != x || tile.Z * 64 + local.y != z ||
                        hole.x < 0 || hole.y < 0 || hole.x + 32 > 2048 || hole.y + 32 > 2048)
                        throw new Exception("Address mismatch: " + key);
                    var origin = WorldCoordinates.TileOrigin(tile);
                    origin.y = -32;
                    if (math.distance(WorldCoordinates.ToGlobal(WorldCoordinates.ToLocal(p, origin), origin), p) > .0001)
                        throw new Exception("Coordinate round trip: " + key);
                    count++;
                }
                if (WorldCoordinates.DensityAt(new double3(-.001, -16, 1024)).Equals(new WorldChunkKey(-1,-1,64)) == false ||
                    WorldCoordinates.FloorDiv(long.MinValue, 64) != long.MinValue / 64 || WorldCoordinates.FloorMod(-1,64) != 63)
                    throw new Exception("Boundary mismatch");
                Debug.Log($"[HybridWorldCoordinates] PASS: {count} address cases, negative boundaries, holes and origin round trips");
            }
            catch (Exception e) { Debug.LogError("[HybridWorldCoordinates] FAIL: " + e); }
        }
    }
}
