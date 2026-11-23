using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

internal class PendingChunk
{
    public ChunkType type;
    public Vector2Int coord;

    public JobHandle handle;

    public NativeArray<Vector3> vertices;
    public NativeArray<Vector2> uvs;
    public NativeArray<int> triangles;

    public NativeArray<int> boundaryCounts;
    public NativeArray<int> vertexOffsets;
    public NativeArray<int> triangleOffsets;

    public float startX;
    public float startZ;
    public float size;

    public int cells;
    public int segments;
}
