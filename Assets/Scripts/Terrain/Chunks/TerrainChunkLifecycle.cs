using System;
using System.Collections.Generic;
using UnityEngine;

internal sealed class TerrainChunkLifecycle
{
    private readonly Func<Vector2Int, float, Vector3, float, bool>
        chunkIntersectsRadius;

    public TerrainChunkLifecycle(
        Func<Vector2Int, float, Vector3, float, bool> chunkIntersectsRadius)
    {
        this.chunkIntersectsRadius = chunkIntersectsRadius;
    }

    public void SetVisible(ChunkData data, bool visible)
    {
        if (data == null || data.gameObject == null)
            return;

        data.visible = visible;

        if (data.renderer != null)
            data.renderer.enabled = visible;

        if (data.gridRenderer != null)
            data.gridRenderer.enabled = visible;

        if (visible)
        {
            EnsureCollider(data);
            if (data.collider != null)
                data.collider.enabled = true;
        }
        else if (data.collider != null)
        {
            data.collider.enabled = false;
        }
    }

    public void UpdateVisibleColliders(
        Dictionary<Vector2Int, ChunkData> superChunks,
        Dictionary<Vector2Int, ChunkData> fastChunks,
        Dictionary<Vector2Int, ChunkData> fineChunks,
        float superChunkSize,
        float fastChunkSize,
        float fineChunkSize,
        Vector3 playerPosition,
        float colliderRadius,
        float coreRadius,
        float safetyMargin)
    {
        float activeColliderRadius =
            Mathf.Max(colliderRadius, coreRadius) + safetyMargin;

        UpdateColliders(
            superChunks, superChunkSize, playerPosition, activeColliderRadius);
        UpdateColliders(
            fastChunks, fastChunkSize, playerPosition, activeColliderRadius);
        UpdateColliders(
            fineChunks, fineChunkSize, playerPosition, activeColliderRadius);
    }

    public void Destroy(ChunkData data)
    {
        if (data == null)
            return;

        if (data.collider != null)
            UnityEngine.Object.Destroy(data.collider);

        if (data.mesh != null)
            UnityEngine.Object.Destroy(data.mesh);

        if (data.gridMesh != null)
            UnityEngine.Object.Destroy(data.gridMesh);

        if (data.gridObject != null)
        {
            Renderer renderer = data.gridObject.GetComponent<Renderer>();

            if (renderer != null && renderer.sharedMaterial != null)
                UnityEngine.Object.Destroy(renderer.sharedMaterial);

            UnityEngine.Object.Destroy(data.gridObject);
        }

        if (data.gameObject != null)
            UnityEngine.Object.Destroy(data.gameObject);
    }

    private void UpdateColliders(
        Dictionary<Vector2Int, ChunkData> chunks,
        float chunkSize,
        Vector3 playerPosition,
        float activeColliderRadius)
    {
        foreach (var pair in chunks)
        {
            ChunkData data = pair.Value;

            if (data == null || !data.visible || data.gameObject == null)
                continue;

            bool shouldHaveCollider = chunkIntersectsRadius(
                pair.Key,
                chunkSize,
                playerPosition,
                activeColliderRadius);

            if (shouldHaveCollider)
            {
                EnsureCollider(data);

                if (data.collider != null)
                    data.collider.enabled = true;
            }
            else if (data.collider != null)
            {
                data.collider.enabled = false;
            }
        }
    }

    private static void EnsureCollider(ChunkData data)
    {
        if (data == null || data.gameObject == null || data.mesh == null)
            return;

        if (data.collider != null)
            return;

        data.collider = data.gameObject.AddComponent<MeshCollider>();
        data.collider.sharedMesh = data.mesh;
    }
}
