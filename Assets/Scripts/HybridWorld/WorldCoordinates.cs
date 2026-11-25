using System;
using Unity.Mathematics;
using UnityEngine;

namespace Unlimited.HybridWorld
{
    // Logical addresses are independent of Unity transforms and floating origin.
    [Serializable]
    public struct WorldChunkKey : IEquatable<WorldChunkKey>
    {
        public readonly long X, Y, Z;
        public WorldChunkKey(long x, long y, long z) { X = x; Y = y; Z = z; }
        public bool Equals(WorldChunkKey other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is WorldChunkKey other && Equals(other);
        public override int GetHashCode() { unchecked { return ((X.GetHashCode() * 397) ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode(); } }
        public override string ToString() => $"({X}, {Y}, {Z})";
    }

    public static class WorldCoordinates
    {
        public const int TileSize = 1024;
        public const int DensitySize = 16;
        public const int ChunksPerTile = TileSize / DensitySize;
        public const int HeightResolution = 2049;
        public const double HeightStep = 0.5;

        public static long FloorDiv(long value, int divisor)
        {
            if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
            long q = value / divisor;
            return value % divisor < 0 ? q - 1 : q;
        }
        public static int FloorMod(long value, int divisor)
        {
            if (divisor <= 0) throw new ArgumentOutOfRangeException(nameof(divisor));
            long r = value % divisor;
            return (int)(r < 0 ? r + divisor : r);
        }
        private static long Index(double value, int size)
        {
            double q = Math.Floor(value / size);
            if (double.IsNaN(q) || q < -9223372036854775808d || q >= 9223372036854775808d)
                throw new ArgumentOutOfRangeException(nameof(value));
            return checked((long)q);
        }
        public static WorldChunkKey DensityAt(double3 p) => new WorldChunkKey(Index(p.x, DensitySize), Index(p.y, DensitySize), Index(p.z, DensitySize));
        public static WorldChunkKey TileAt(double3 p) => new WorldChunkKey(Index(p.x, TileSize), 0, Index(p.z, TileSize));
        public static WorldChunkKey TileOf(WorldChunkKey chunk) => new WorldChunkKey(FloorDiv(chunk.X, ChunksPerTile), 0, FloorDiv(chunk.Z, ChunksPerTile));
        public static int2 ChunkInTile(WorldChunkKey chunk) => new int2(FloorMod(chunk.X, ChunksPerTile), FloorMod(chunk.Z, ChunksPerTile));
        public static int2 HoleOffset(WorldChunkKey chunk) => ChunkInTile(chunk) * (DensitySize * 2);
        public static double3 DensityOrigin(WorldChunkKey key) => new double3((double)key.X * DensitySize, (double)key.Y * DensitySize, (double)key.Z * DensitySize);
        public static double3 TileOrigin(WorldChunkKey key) => new double3((double)key.X * TileSize, 0, (double)key.Z * TileSize);
        public static double3 ToGlobal(Vector3 local, double3 origin) => origin + new double3(local.x, local.y, local.z);
        public static Vector3 ToLocal(double3 global, double3 origin)
        {
            double3 p = global - origin;
            return new Vector3((float)p.x, (float)p.y, (float)p.z);
        }
    }
}
