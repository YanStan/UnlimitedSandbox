using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;
using System.Text;

public partial class TerrainGenerator
{
// =========================================================
    // CREATE FAN JOB
    // =========================================================

    private PendingChunk CreateFanJob(
        ChunkType type,
        Vector2Int coord,
        float size,
        int cells,
        int segments)
    {
        int cellCount = cells * cells;

        int totalVertices = 0;
        int totalTriangles = 0;

        NativeArray<int> boundaryCounts =
            new NativeArray<int>(cellCount, Allocator.Persistent);

        NativeArray<int> vertexOffsets =
            new NativeArray<int>(cellCount, Allocator.Persistent);

        NativeArray<int> triangleOffsets =
            new NativeArray<int>(cellCount, Allocator.Persistent);

        for (int z = 0; z < cells; z++)
        {
            for (int x = 0; x < cells; x++)
            {
                int bottom = z == 0 ? segments : 1;
                int right = x == cells - 1 ? segments : 1;
                int top = z == cells - 1 ? segments : 1;
                int left = x == 0 ? segments : 1;

                int count = bottom + right + top + left;

                int index = z * cells + x;

                boundaryCounts[index] = count;

                vertexOffsets[index] = totalVertices;
                triangleOffsets[index] = totalTriangles;

                totalVertices += count + 1;
                totalTriangles += count * 3;
            }
        }

        NativeArray<Vector3> vertices =
            new NativeArray<Vector3>(totalVertices, Allocator.Persistent);

        NativeArray<Vector2> uvs =
            new NativeArray<Vector2>(totalVertices, Allocator.Persistent);

        NativeArray<int> triangles =
            new NativeArray<int>(totalTriangles, Allocator.Persistent);

        float startX = coord.x * size;
        float startZ = coord.y * size;

        FanGeometryJob job = new FanGeometryJob
        {
            boundaryCounts = boundaryCounts,
            vertexOffsets = vertexOffsets,
            triangleOffsets = triangleOffsets,

            vertices = vertices,
            uvs = uvs,
            triangles = triangles,

            cells = cells,
            segments = segments,

            startX = startX,
            startZ = startZ,
            size = size,

            height = height,
            noiseScale = noiseScale,
            detailScale = detailScale,
            detailStrength = detailStrength,

            textureScale = textureScale,
            originalTerrainSize = originalTerrainSize
        };

        JobHandle handle = job.Schedule(cellCount, 1);

        return new PendingChunk
        {
            type = type,
            coord = coord,
            handle = handle,

            vertices = vertices,
            uvs = uvs,
            triangles = triangles,

            boundaryCounts = boundaryCounts,
            vertexOffsets = vertexOffsets,
            triangleOffsets = triangleOffsets,

            startX = startX,
            startZ = startZ,
            size = size,
            cells = cells,
            segments = segments
        };
    }

    // =========================================================
    // CREATE FINE JOB
    // =========================================================

    private PendingChunk CreateFineJob(Vector2Int coord)
    {
        int resolution = cellsPerChunk;
        int side = resolution + 1;

        int vertexCount = side * side;

        NativeArray<Vector3> vertices =
            new NativeArray<Vector3>(vertexCount, Allocator.Persistent);

        NativeArray<Vector2> uvs =
            new NativeArray<Vector2>(vertexCount, Allocator.Persistent);

        float startX = coord.x * chunkSize;
        float startZ = coord.y * chunkSize;

        FineVertexJob job = new FineVertexJob
        {
            vertices = vertices,
            uvs = uvs,

            resolution = resolution,
            side = side,

            chunkSize = chunkSize,
            startX = startX,
            startZ = startZ,

            height = height,
            noiseScale = noiseScale,
            detailScale = detailScale,
            detailStrength = detailStrength,

            textureScale = textureScale,
            originalTerrainSize = originalTerrainSize
        };

        JobHandle handle = job.Schedule(vertexCount, 64);

        return new PendingChunk
        {
            type = ChunkType.Fine,
            coord = coord,
            handle = handle,

            vertices = vertices,
            uvs = uvs,

            startX = startX,
            startZ = startZ,
            size = chunkSize,
            cells = resolution
        };
    }

    // =========================================================
    // COMPLETE JOBS
    // =========================================================

    private void CompleteFinishedJobs()
    {
        // Super-чанки отвечают за физическую поверхность в High Speed.
        // Поэтому выбираем завершённые Super Jobs по актуальному приоритету
        // относительно игрока, а не с конца списка pendingJobs.
        CompleteFinishedSuperJobs(maxSuperBuildsPerFrame);

        int totalBudget = Mathf.Max(1, maxOtherBuildsPerFrame);
        int fastBudget = (totalBudget + 1) / 2;
        int fineBudget = totalBudget / 2;

        // Fast нельзя собирать простым проходом с конца pendingJobs:
        // в список задачи добавляются от ближайшей группы к дальней,
        // а проход с конца фактически переворачивал этот порядок.
        // Собираем только ближайшую незавершённую Super-группу.
        int fastBuilt = CompleteFinishedFastJobs(fastBudget);

        int fineBuilt = CompleteFinishedJobsOfType(
            ChunkType.Fine, fineBudget);

        int remaining = totalBudget - fastBuilt - fineBuilt;

        if (remaining > 0)
        {
            int extraFast = CompleteFinishedFastJobs(remaining);
            remaining -= extraFast;
        }

        if (remaining > 0)
        {
            CompleteFinishedJobsOfType(
                ChunkType.Fine, remaining);
        }
    }

    private int CompleteFinishedSuperJobs(int budget)
    {
        if (budget <= 0)
            return 0;

        Vector3 playerPosition = GetPlayerPosition();
        int built = 0;

        while (built < budget)
        {
            int bestIndex = -1;
            float bestPriority = float.MaxValue;

            // В обычном режиме не ждём незавершённые задачи.
            // В High Speed ближайший Super Job завершаем принудительно:
            // иначе готовые дальние чанки могут обгонять поверхность
            // непосредственно перед игроком, пока тот уже летит к ней.
            for (int i = 0; i < pendingJobs.Count; i++)
            {
                PendingChunk pending = pendingJobs[i];

                if (pending.type != ChunkType.Super)
                    continue;

                if (!highSpeedPrewarmMode &&
                    !pending.handle.IsCompleted)
                {
                    continue;
                }

                float priority = GetSuperPriority(
                    pending.coord,
                    playerPosition);

                if (bestIndex < 0 || priority < bestPriority)
                {
                    bestIndex = i;
                    bestPriority = priority;
                }
            }

            // В High Speed Complete() может немного подождать завершения
            // ближайшего Burst Job. Это намеренно: локальная физическая
            // поверхность важнее сборки уже готовых дальних чанков.
            if (bestIndex < 0)
                break;

            PendingChunk ready = pendingJobs[bestIndex];
            ready.handle.Complete();
            BuildAndRegisterPending(ready);
            DisposePending(ready);
            pendingJobs.RemoveAt(bestIndex);
            built++;
        }

        return built;
    }

    private int CompleteFinishedFastJobs(int budget)
    {
        if (budget <= 0)
            return 0;

        Vector3 playerPosition = GetPlayerPosition();
        int built = 0;

        // Выбираем ближайшую ИЗ УЖЕ ЗАВЕРШЁННЫХ Fast-задач.
        // Важно: не блокируем остальные группы, если ближайшая задача
        // ещё выполняется. Иначе один медленный Job мог остановить
        // регистрацию всех Fast-чанков на карте.
        while (built < budget)
        {
            int bestIndex = -1;
            float bestGroupDistance = float.MaxValue;
            float bestChunkDistance = float.MaxValue;

            for (int i = 0; i < pendingJobs.Count; i++)
            {
                PendingChunk pending = pendingJobs[i];

                if (pending.type != ChunkType.Fast ||
                    !pending.handle.IsCompleted)
                {
                    continue;
                }

                Vector2Int group = TerrainLodCoordinates.GetParent(
                    pending.coord, FastPerSuper);

                float groupDistance = GetChunkBoundsDistanceSqr(
                    group, superFastChunkSize, playerPosition);

                float chunkDistance = GetChunkDistanceSqr(
                    pending.coord, fastChunkSize, playerPosition);

                if (bestIndex < 0 ||
                    groupDistance < bestGroupDistance ||
                    (Mathf.Approximately(groupDistance, bestGroupDistance) &&
                     chunkDistance < bestChunkDistance))
                {
                    bestIndex = i;
                    bestGroupDistance = groupDistance;
                    bestChunkDistance = chunkDistance;
                }
            }

            // Нет ни одной завершённой Fast-задачи. Выходим,
            // но не блокируем генерацию Fine или следующих кадров.
            if (bestIndex < 0)
                break;

            PendingChunk ready = pendingJobs[bestIndex];
            ready.handle.Complete();
            BuildAndRegisterPending(ready);
            DisposePending(ready);
            pendingJobs.RemoveAt(bestIndex);
            built++;
        }

        return built;
    }

    private int CompleteFinishedJobsOfType(ChunkType type, int budget)
    {
        if (budget <= 0) return 0;
        int built = 0;
        Vector3 p = GetPlayerPosition();
        while (built < budget)
        {
            int best = -1;
            float bestGroup = float.PositiveInfinity;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < pendingJobs.Count; i++)
            {
                PendingChunk job = pendingJobs[i];
                if (job.type != type || !job.handle.IsCompleted) continue;
                float size = type == ChunkType.Fine ? chunkSize : superFastChunkSize;
                Vector2Int group = type == ChunkType.Fine
                    ? TerrainLodCoordinates.GetParent(job.coord, FinePerFast)
                    : job.coord;
                float groupSize = type == ChunkType.Fine ? fastChunkSize : size;
                float groupDist = GetChunkBoundsDistanceSqr(group, groupSize, p);
                float distance = GetChunkBoundsDistanceSqr(job.coord, size, p);
                if (groupDist < bestGroup || (groupDist == bestGroup && distance < bestDistance))
                {
                    best = i;
                    bestGroup = groupDist;
                    bestDistance = distance;
                }
            }
            if (best < 0) break;
            PendingChunk ready = pendingJobs[best];
            ready.handle.Complete();
            BuildAndRegisterPending(ready);
            DisposePending(ready);
            pendingJobs.RemoveAt(best);
            built++;
        }
        return built;
    }

    private void BuildAndRegisterPending(PendingChunk pending)
    {
        ChunkData data = BuildChunkFromPending(pending);

        switch (pending.type)
        {
            case ChunkType.Super:
                pendingSuper.Remove(pending.coord);

                if (!superChunks.ContainsKey(pending.coord))
                    superChunks.Add(pending.coord, data);
                else
                    chunkLifecycle.Destroy(data);

                lodVisibility.RefreshSuper(pending.coord, highSpeedMode);
                break;

            case ChunkType.Fast:
                pendingFast.Remove(pending.coord);

                if (!fastChunks.ContainsKey(pending.coord))
                    fastChunks.Add(pending.coord, data);
                else
                    chunkLifecycle.Destroy(data);

                lodVisibility.RefreshSuper(
                    TerrainLodCoordinates.GetParent(
                        pending.coord, FastPerSuper),
                    highSpeedMode);
                break;

            case ChunkType.Fine:
                pendingFine.Remove(pending.coord);

                if (!fineChunks.ContainsKey(pending.coord))
                    fineChunks.Add(pending.coord, data);
                else
                    chunkLifecycle.Destroy(data);

                lodVisibility.RefreshFast(
                    TerrainLodCoordinates.GetParent(
                        pending.coord, FinePerFast),
                    highSpeedMode);
                break;
        }
    }

    private void DisposePending(PendingChunk pending)
    {
        if (pending == null)
            return;

        if (pending.vertices.IsCreated)
            pending.vertices.Dispose();

        if (pending.uvs.IsCreated)
            pending.uvs.Dispose();

        if (pending.triangles.IsCreated)
            pending.triangles.Dispose();

        if (pending.boundaryCounts.IsCreated)
            pending.boundaryCounts.Dispose();

        if (pending.vertexOffsets.IsCreated)
            pending.vertexOffsets.Dispose();

        if (pending.triangleOffsets.IsCreated)
            pending.triangleOffsets.Dispose();
    }

    // =========================================================
    // BUILD MESH FROM JOB
    // =========================================================

    private ChunkData BuildChunkFromPending(PendingChunk pending)
    {
        Mesh mesh = new Mesh
        {
            name = $"{pending.type}Mesh_{pending.coord.x}_{pending.coord.y}",
            indexFormat = IndexFormat.UInt32
        };

        Vector3[] vertices = pending.vertices.ToArray();
        Vector2[] uvs = pending.uvs.ToArray();

        mesh.vertices = vertices;
        mesh.uv = uvs;

        if (pending.type == ChunkType.Fine)
        {
            mesh.triangles = TerrainMeshTopology.BuildFineTriangles(
                pending.cells);
        }
        else
        {
            mesh.triangles = pending.triangles.ToArray();
        }

        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        Color color;
        int gridSegments;

        switch (pending.type)
        {
            case ChunkType.Super:
                color = superGridColor;
                gridSegments = superFastCellsPerSide;
                break;

            case ChunkType.Fast:
                color = fastGridColor;
                gridSegments = fastCellsPerSide;
                break;

            default:
                color = fineGridColor;
                gridSegments = cellsPerChunk;
                break;
        }

        return TerrainChunkObjectFactory.Create(
            $"{pending.type}Chunk_{pending.coord.x}_{pending.coord.y}",
            mesh,
            pending.startX,
            pending.startZ,
            color,
            pending.size,
            gridSegments,
            transform,
            groundMaterial,
            gridMaterial,
            showGrid,
            gridHeightOffset,
            GetTerrainHeight);
    }

    // =========================================================
    // CHUNK OBJECT
    // =========================================================

    private float GetTerrainHeight(float x, float z)
    {
        float a = noise.cnoise(
            new float2(x * noiseScale, z * noiseScale));

        float b = noise.cnoise(
            new float2(x * detailScale, z * detailScale));

        return (a * 0.5f + 0.5f) * height +
               (b * 0.5f + 0.5f) * detailStrength;
    }

    private Material CreateGridMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");

        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader == null)
        {
            Debug.LogWarning(
                "TerrainGenerator: no suitable grid shader found.");

            return null;
        }

        Material material = new Material(shader);
        material.name = "Terrain_Grid_Material";

        return material;
    }

    // =========================================================
    // MATERIAL
    // =========================================================

    private Material CreateGroundMaterial()
    {
        Shader shader = Shader.Find("Unlimited/TriplanarGrass");

        if (shader == null)
        {
            Debug.LogError(
                "TerrainGenerator: Shader Unlimited/TriplanarGrass not found!");

            return null;
        }

        Material material = new Material(shader);
        material.name = "Unlimited_Ground_Material";

        return material;
    }

    private void OnDestroy()
    {
        foreach (PendingChunk pending in pendingJobs)
        {
            pending.handle.Complete();
            DisposePending(pending);
        }

        pendingJobs.Clear();

        foreach (var pair in superChunks)
            chunkLifecycle?.Destroy(pair.Value);

        foreach (var pair in fastChunks)
            chunkLifecycle?.Destroy(pair.Value);

        foreach (var pair in fineChunks)
            chunkLifecycle?.Destroy(pair.Value);

        superChunks.Clear();
        fastChunks.Clear();
        fineChunks.Clear();

        if (groundMaterial != null)
            Destroy(groundMaterial);

        if (gridMaterial != null)
            Destroy(gridMaterial);
    }
}
