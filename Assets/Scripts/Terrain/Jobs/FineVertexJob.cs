using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
internal struct FineVertexJob : IJobParallelFor
{
    [WriteOnly] public NativeArray<Vector3> vertices;
    [WriteOnly] public NativeArray<Vector2> uvs;

    public int resolution;
    public int side;
    public float chunkSize;
    public float startX;
    public float startZ;
    public float height;
    public float noiseScale;
    public float detailScale;
    public float detailStrength;
    public float textureScale;
    public float originalTerrainSize;

    public void Execute(int index)
    {
        int x = index % side;
        int z = index / side;
        float step = chunkSize / resolution;
        float wx = startX + x * step;
        float wz = startZ + z * step;

        float a = noise.cnoise(new float2(wx * noiseScale, wz * noiseScale));
        float b = noise.cnoise(new float2(wx * detailScale, wz * detailScale));
        float y = (a * 0.5f + 0.5f) * height +
                  (b * 0.5f + 0.5f) * detailStrength;

        vertices[index] = new Vector3(x * step, y, z * step);
        uvs[index] = new Vector2(
            wx / originalTerrainSize * textureScale,
            wz / originalTerrainSize * textureScale);
    }
}
