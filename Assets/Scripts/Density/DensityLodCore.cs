using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

// Foundation only: MapMagic remains the sole ground terrain renderer.
public enum DensityLodLevel { Fine, Fast, SuperFast }
public static class DensityLodCore
{
    public const int CellsPerAxis = 16;
    public static int ChunkSize(DensityLodLevel level)
    {
        switch (level)
        {
            case DensityLodLevel.Fine: return 4;
            case DensityLodLevel.Fast: return 16;
            default: return 64;
        }
    }
    public static float CellSize(DensityLodLevel level) => ChunkSize(level) / (float)CellsPerAxis;
    public static int3 WorldToChunk(float3 world, DensityLodLevel level) => (int3)math.floor(world / ChunkSize(level));
    public static int3 Parent(int3 child) => (int3)math.floor((float3)child / 4f);
    public static int3 ChunkOrigin(int3 coord, DensityLodLevel level) => coord * ChunkSize(level);
}

// Burst-compatible density sampling foundation, not yet scheduled by gameplay.
[BurstCompile]
public struct DensitySampleJob : IJobParallelFor
{
    [WriteOnly] public NativeArray<float> Samples;
    public int SamplesPerAxis;
    public float CellSize;
    public float3 Origin;
    public float GroundHeight;
    public void Execute(int index)
    {
        int x = index % SamplesPerAxis;
        int y = (index / SamplesPerAxis) % SamplesPerAxis;
        int z = index / (SamplesPerAxis * SamplesPerAxis);
        float3 world = Origin + new float3(x,y,z) * CellSize;
        Samples[index] = world.y - GroundHeight;
    }
}
