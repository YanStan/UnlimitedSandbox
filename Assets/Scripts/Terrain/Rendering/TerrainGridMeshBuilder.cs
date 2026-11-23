using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

internal static class TerrainGridMeshBuilder
{
    public static Mesh Build(
        float startX,
        float startZ,
        float size,
        int segments,
        float heightOffset,
        Func<float, float, float> getTerrainHeight)
    {
        List<Vector3> vertices = new();
        List<int> indices = new();

        for (int i = 0; i <= segments; i++)
        {
            float z = (float)i / segments * size;
            AddLine(vertices, indices, startX, startZ, 0f, z, size, z,
                heightOffset, getTerrainHeight);
        }

        for (int i = 0; i <= segments; i++)
        {
            float x = (float)i / segments * size;
            AddLine(vertices, indices, startX, startZ, x, 0f, x, size,
                heightOffset, getTerrainHeight);
        }

        Mesh mesh = new Mesh
        {
            name = "TerrainGrid",
            indexFormat = IndexFormat.UInt32
        };

        mesh.SetVertices(vertices);
        mesh.SetIndices(indices, MeshTopology.Lines, 0);
        mesh.RecalculateBounds();

        return mesh;
    }

    private static void AddLine(
        List<Vector3> vertices,
        List<int> indices,
        float startX,
        float startZ,
        float x1,
        float z1,
        float x2,
        float z2,
        float heightOffset,
        Func<float, float, float> getTerrainHeight)
    {
        int steps = Mathf.Max(
            2,
            Mathf.CeilToInt(Vector2.Distance(
                new Vector2(x1, z1),
                new Vector2(x2, z2))));

        int first = vertices.Count;

        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            float lx = Mathf.Lerp(x1, x2, t);
            float lz = Mathf.Lerp(z1, z2, t);
            float wx = startX + lx;
            float wz = startZ + lz;
            float y = getTerrainHeight(wx, wz) + heightOffset;

            vertices.Add(new Vector3(lx, y, lz));

            if (i > 0)
            {
                indices.Add(first + i - 1);
                indices.Add(first + i);
            }
        }
    }
}
