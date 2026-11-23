using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[BurstCompile]
internal struct FanGeometryJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<int> boundaryCounts;
    [ReadOnly] public NativeArray<int> vertexOffsets;
    [ReadOnly] public NativeArray<int> triangleOffsets;

    [WriteOnly, NativeDisableParallelForRestriction]
    public NativeArray<Vector3> vertices;

    [WriteOnly, NativeDisableParallelForRestriction]
    public NativeArray<Vector2> uvs;

    [WriteOnly, NativeDisableParallelForRestriction]
    public NativeArray<int> triangles;

    public int cells;
    public int segments;

    public float startX;
    public float startZ;
    public float size;

    public float height;
    public float noiseScale;
    public float detailScale;
    public float detailStrength;

    public float textureScale;
    public float originalTerrainSize;

    private float GetHeight(float x, float z)
    {
        float a = noise.cnoise(new float2(x * noiseScale, z * noiseScale));
        float b = noise.cnoise(new float2(x * detailScale, z * detailScale));

        return (a * 0.5f + 0.5f) * height +
               (b * 0.5f + 0.5f) * detailStrength;
    }

    private void WriteVertex(int index, float x, float z)
    {
        float wx = startX + x;
        float wz = startZ + z;

        vertices[index] = new Vector3(x, GetHeight(wx, wz), z);
        uvs[index] = new Vector2(
            wx / originalTerrainSize * textureScale,
            wz / originalTerrainSize * textureScale);
    }

    public void Execute(int index)
    {
        int x = index % cells;
        int z = index / cells;
        float coarse = size / cells;
        float sx = x * coarse;
        float sz = z * coarse;

        int bottom = z == 0 ? segments : 1;
        int right = x == cells - 1 ? segments : 1;
        int top = z == cells - 1 ? segments : 1;
        int left = x == 0 ? segments : 1;
        int count = boundaryCounts[index];
        int vertexStart = vertexOffsets[index];
        int triangleStart = triangleOffsets[index];

        float centerX = sx + coarse * 0.5f;
        float centerZ = sz + coarse * 0.5f;
        WriteVertex(vertexStart, centerX, centerZ);

        int v = vertexStart + 1;
        for (int i = 0; i < bottom; i++)
            WriteVertex(v++, sx + (float)i / bottom * coarse, sz);

        for (int i = 0; i < right; i++)
            WriteVertex(v++, sx + coarse, sz + (float)i / right * coarse);

        for (int i = 0; i < top; i++)
            WriteVertex(v++, sx + coarse - (float)i / top * coarse, sz + coarse);

        for (int i = 0; i < left; i++)
            WriteVertex(v++, sx, sz + coarse - (float)i / left * coarse);

        int t = triangleStart;
        for (int i = 0; i < count; i++)
        {
            int current = vertexStart + 1 + i;
            int next = vertexStart + 1 + ((i + 1) % count);

            triangles[t++] = vertexStart;
            triangles[t++] = next;
            triangles[t++] = current;
        }
    }
}
