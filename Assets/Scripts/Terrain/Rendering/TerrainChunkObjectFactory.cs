using System;
using UnityEngine;
using UnityEngine.Rendering;

internal static class TerrainChunkObjectFactory
{
    public static ChunkData Create(
        string objectName,
        Mesh mesh,
        float startX,
        float startZ,
        Color gridColor,
        float size,
        int gridSegments,
        Transform parent,
        Material groundMaterial,
        Material gridMaterial,
        bool showGrid,
        float gridHeightOffset,
        Func<float, float, float> getTerrainHeight)
    {
        GameObject chunkObject = new GameObject(
            objectName,
            typeof(MeshFilter),
            typeof(MeshRenderer));

        chunkObject.transform.SetParent(parent, false);
        chunkObject.transform.localPosition = new Vector3(startX, 0f, startZ);

        MeshFilter filter = chunkObject.GetComponent<MeshFilter>();
        MeshRenderer renderer = chunkObject.GetComponent<MeshRenderer>();
        filter.sharedMesh = mesh;
        renderer.sharedMaterial = groundMaterial;

        ChunkData data = new ChunkData
        {
            gameObject = chunkObject,
            mesh = mesh,
            renderer = renderer,
            visible = false
        };

        if (showGrid && gridMaterial != null)
        {
            CreateGridObject(
                data,
                objectName + "_Grid",
                startX,
                startZ,
                size,
                gridSegments,
                gridColor,
                parent,
                gridMaterial,
                gridHeightOffset,
                getTerrainHeight);
        }

        return data;
    }

    private static void CreateGridObject(
        ChunkData data,
        string objectName,
        float startX,
        float startZ,
        float size,
        int segments,
        Color color,
        Transform parent,
        Material gridMaterial,
        float gridHeightOffset,
        Func<float, float, float> getTerrainHeight)
    {
        GameObject gridObject = new GameObject(objectName);
        gridObject.transform.SetParent(parent, false);
        gridObject.transform.localPosition = new Vector3(startX, 0f, startZ);

        Mesh gridMesh = TerrainGridMeshBuilder.Build(
            startX,
            startZ,
            size,
            segments,
            gridHeightOffset,
            getTerrainHeight);

        MeshFilter filter = gridObject.AddComponent<MeshFilter>();
        MeshRenderer renderer = gridObject.AddComponent<MeshRenderer>();
        filter.sharedMesh = gridMesh;

        Material material = new Material(gridMaterial);
        material.color = color;

        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        data.gridObject = gridObject;
        data.gridMesh = gridMesh;
        data.gridRenderer = renderer;
    }
}
