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
    // PLAYER SPEED
    // =========================================================

    private void UpdatePlayerSpeed()
    {
        Vector3 currentPosition = player.position;

        if (!hasPreviousPosition)
        {
            previousWorldPosition = currentPosition;
            hasPreviousPosition = true;
            currentPlayerSpeed = 0f;
            return;
        }

        Vector3 delta = currentPosition - previousWorldPosition;

        previousWorldPosition = currentPosition;

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);

        currentPlayerSpeed = delta.magnitude / dt;

        if (delta.sqrMagnitude > 0.0001f)
        {
            Vector3 localDirection =
                transform.InverseTransformDirection(delta.normalized);

            movementDirection = new Vector2(
                localDirection.x,
                localDirection.z);

            if (movementDirection.sqrMagnitude > 0.0001f)
                movementDirection.Normalize();
        }
    }

    private bool UpdateSpeedMode()
    {
        bool previousMode = highSpeedMode;

        // В обычном режиме вход в High Speed происходит сразу.
        if (!highSpeedMode)
        {
            highSpeedBelowExitTimer = 0f;

            if (currentPlayerSpeed >= highSpeedEnterThreshold)
            {
                highSpeedMode = true;
                highSpeedBelowExitTimer = 0f;
            }
        }
        else
        {
            // В High Speed режиме не выключаем его из-за
            // кратковременного падения измеренной скорости.
            if (currentPlayerSpeed < highSpeedExitThreshold)
            {
                highSpeedBelowExitTimer += Time.deltaTime;

                // Выход только после непрерывного нахождения
                // ниже порога в течение заданного времени.
                if (highSpeedBelowExitTimer >= highSpeedExitDelay)
                {
                    highSpeedMode = false;
                    highSpeedBelowExitTimer = 0f;
                }
            }
            else
            {
                // Скорость снова поднялась выше порога.
                // Отменяем накопленное время выключения.
                highSpeedBelowExitTimer = 0f;
            }
        }

        return previousMode != highSpeedMode;
    }

    private Vector3 GetPlayerPosition()
    {
        Vector3 p = transform.InverseTransformPoint(player.position);
        p.y = 0f;
        return p;
    }

    // =========================================================
    // INITIAL SUPERFAST COVERAGE
    // =========================================================

    private void GenerateInitialSuperCoverage(Vector3 p)
    {
        int range = Mathf.CeilToInt(loadRadius / superFastChunkSize) + 2;

        Vector2Int center = GetSuperCoord(p);

        List<Vector2Int> initial = new();

        for (int z = -range; z <= range; z++)
        {
            for (int x = -range; x <= range; x++)
            {
                Vector2Int c = new Vector2Int(
                    center.x + x,
                    center.y + z);

                if (!ChunkIntersectsRadius(
                    c, superFastChunkSize, p, loadRadius))
                    continue;

                initial.Add(c);
            }
        }

        initial.Sort((a, b) =>
        {
            float da = GetChunkDistanceSqr(a, superFastChunkSize, p);
            float db = GetChunkDistanceSqr(b, superFastChunkSize, p);

            return da.CompareTo(db);
        });

        foreach (Vector2Int c in initial)
        {
            if (superChunks.ContainsKey(c))
                continue;

            PendingChunk pending = CreateFanJob(
                ChunkType.Super,
                c,
                superFastChunkSize,
                superFastCellsPerSide,
                fastCellsPerSide * cellsPerChunk);

            pending.handle.Complete();

            ChunkData data = BuildChunkFromPending(pending);

            superChunks.Add(c, data);

            chunkLifecycle.SetVisible(data, true);

            DisposePending(pending);
        }

        Physics.SyncTransforms();
    }

    // =========================================================
    // COORDINATES
    // =========================================================

    private Vector2Int GetSuperCoord(Vector3 p)
    {
        return new Vector2Int(
            Mathf.FloorToInt(p.x / superFastChunkSize),
            Mathf.FloorToInt(p.z / superFastChunkSize));
    }

    private Vector2Int GetFastCoord(Vector3 p)
    {
        return new Vector2Int(
            Mathf.FloorToInt(p.x / fastChunkSize),
            Mathf.FloorToInt(p.z / fastChunkSize));
    }

    private Vector2Int GetFineCoord(Vector3 p)
    {
        return new Vector2Int(
            Mathf.FloorToInt(p.x / chunkSize),
            Mathf.FloorToInt(p.z / chunkSize));
    }

    private float GetChunkDistanceSqr(
        Vector2Int c,
        float size,
        Vector3 p)
    {
        float cx = (c.x + 0.5f) * size;
        float cz = (c.y + 0.5f) * size;

        float dx = cx - p.x;
        float dz = cz - p.z;

        return dx * dx + dz * dz;
    }

    // Расстояние от игрока до ближайшей точки чанка.
    // Для приоритета LOD-групп это точнее, чем расстояние до центра:
    // группа, внутри которой стоит игрок, получает приоритет 0.
    private float GetChunkBoundsDistanceSqr(
        Vector2Int coord,
        float size,
        Vector3 p)
    {
        float minX = coord.x * size;
        float maxX = minX + size;
        float minZ = coord.y * size;
        float maxZ = minZ + size;

        float dx = Mathf.Max(minX - p.x, 0f, p.x - maxX);
        float dz = Mathf.Max(minZ - p.z, 0f, p.z - maxZ);

        return dx * dx + dz * dz;
    }

    private bool ChunkIntersectsRadius(
        Vector2Int c,
        float size,
        Vector3 p,
        float radius)
    {
        float minX = c.x * size;
        float maxX = minX + size;

        float minZ = c.y * size;
        float maxZ = minZ + size;

        float dx = Mathf.Max(minX - p.x, 0f, p.x - maxX);
        float dz = Mathf.Max(minZ - p.z, 0f, p.z - maxZ);

        return dx * dx + dz * dz <= radius * radius;
    }

    // =========================================================
    // HIGH SPEED COVERAGE AREA
    // =========================================================

    private bool ChunkIntersectsHighSpeedArea(
        Vector2Int coord,
        Vector3 playerPosition)
    {
        if (ChunkIntersectsRadius(
            coord,
            superFastChunkSize,
            playerPosition,
            highSpeedSafetyRadius))
        {
            return true;
        }

        if (movementDirection.sqrMagnitude < 0.001f)
        {
            float fallbackRadius = Mathf.Max(
                highSpeedForwardDistance,
                highSpeedSideDistance,
                highSpeedBackwardDistance);

            return ChunkIntersectsRadius(
                coord,
                superFastChunkSize,
                playerPosition,
                fallbackRadius);
        }

        float centerX = (coord.x + 0.5f) * superFastChunkSize;
        float centerZ = (coord.y + 0.5f) * superFastChunkSize;

        float dx = centerX - playerPosition.x;
        float dz = centerZ - playerPosition.z;

        float forward =
            dx * movementDirection.x +
            dz * movementDirection.y;

        float side =
            Mathf.Abs(
                -dx * movementDirection.y +
                 dz * movementDirection.x);

        float chunkPadding =
            superFastChunkSize * 0.70710678f;

        bool insideForwardRange =
            forward >= -highSpeedBackwardDistance - chunkPadding &&
            forward <= highSpeedForwardDistance + chunkPadding;

        bool insideSideRange =
            side <= highSpeedSideDistance + chunkPadding;

        return insideForwardRange && insideSideRange;
    }

    private void GetHighSpeedSearchExtents(
        out float extentX,
        out float extentZ)
    {
        float maxForwardDistance = Mathf.Max(
            highSpeedForwardDistance,
            highSpeedBackwardDistance);

        if (movementDirection.sqrMagnitude < 0.001f)
        {
            extentX = maxForwardDistance;
            extentZ = maxForwardDistance;
            return;
        }

        float dirX = Mathf.Abs(movementDirection.x);
        float dirZ = Mathf.Abs(movementDirection.y);

        float sideX = dirZ;
        float sideZ = dirX;

        extentX =
            dirX * maxForwardDistance +
            sideX * highSpeedSideDistance;

        extentZ =
            dirZ * maxForwardDistance +
            sideZ * highSpeedSideDistance;

        extentX = Mathf.Max(extentX, highSpeedSafetyRadius);
        extentZ = Mathf.Max(extentZ, highSpeedSafetyRadius);

        extentX += superFastChunkSize;
        extentZ += superFastChunkSize;
    }

    // =========================================================
    // CHUNK PRIORITY
    // =========================================================

    private float GetSuperPriority(Vector2Int coord, Vector3 p)
    {
        float centerX = (coord.x + 0.5f) * superFastChunkSize;
        float centerZ = (coord.y + 0.5f) * superFastChunkSize;

        float dx = centerX - p.x;
        float dz = centerZ - p.z;

        float distance = Mathf.Sqrt(dx * dx + dz * dz);

        if (!highSpeedPrewarmMode ||
            movementDirection.sqrMagnitude < 0.001f)
        {
            return distance;
        }

        float forward =
            dx * movementDirection.x +
            dz * movementDirection.y;

        float side = Mathf.Abs(
            -dx * movementDirection.y +
             dz * movementDirection.x);

        float forwardBonus =
            Mathf.Clamp(
                forward,
                0f,
                highSpeedForwardDistance) * 0.9f;

        float behindPenalty =
            Mathf.Max(0f, -forward) * 0.5f;

        float sidePenalty = side * 0.15f;

        return distance - forwardBonus +
               behindPenalty + sidePenalty;
    }

    // =========================================================
    // QUEUES
    // =========================================================

    private float GetHighGroundRadius()
    {
        if (!highGroundEnabled || player == null)
            return loadRadius;

        float altitude = player.position.y - seaLevelY;
        float t = Mathf.InverseLerp(
            highGroundStartHeight, highGroundMaxHeight, altitude);
        return Mathf.Lerp(loadRadius, highGroundMaxRadius, t);
    }

    private bool IsInsideSuperCoverage(Vector2Int coord, Vector3 p, float radius)
    {
        if (ChunkIntersectsRadius(coord, superFastChunkSize, p, radius))
            return true;
        return highSpeedPrewarmMode && ChunkIntersectsHighSpeedArea(coord, p);
    }

    // Fine is short-lived; Fast/SuperFast are retained for 120 seconds
    // after leaving their streaming area. One shared periodic scan replaces
    // thousands of per-chunk Update() calls or coroutines.
    private void CleanupStreamingChunks(Vector3 p)
    {
        // Fine: unload promptly when outside core radius.
        List<Vector2Int> removeFine = new List<Vector2Int>();
        foreach (var entry in fineChunks)
            if (!ChunkIntersectsRadius(entry.Key, chunkSize, p, coreRadius))
                removeFine.Add(entry.Key);

        foreach (Vector2Int coord in removeFine)
        {
            ChunkData data = fineChunks[coord];
            fineChunks.Remove(coord);
            chunkLifecycle.Destroy(data);
            lodVisibility.RefreshFast(
                TerrainLodCoordinates.GetParent(coord, FinePerFast), highSpeedMode);
        }

        float now = Time.time;
        int budget = maxRetentionRemovalsPerScan;
        List<Vector2Int> removeFast = new List<Vector2Int>();
        foreach (var entry in fastChunks)
        {
            bool inside = ChunkIntersectsRadius(entry.Key, fastChunkSize, p, loadRadius);
            if (inside)
            {
                fastOutsideSince.Remove(entry.Key);
                continue;
            }
            if (!fastOutsideSince.TryGetValue(entry.Key, out float since))
                fastOutsideSince[entry.Key] = now;
            else if (now - since >= retainedChunkSeconds && removeFast.Count < budget)
                removeFast.Add(entry.Key);
        }
        foreach (Vector2Int coord in removeFast)
        {
            ChunkData data = fastChunks[coord];
            fastChunks.Remove(coord);
            fastOutsideSince.Remove(coord);
            chunkLifecycle.Destroy(data);
            lodVisibility.RefreshSuper(
                TerrainLodCoordinates.GetParent(coord, FastPerSuper), highSpeedMode);
        }
        budget -= removeFast.Count;

        float radius = GetHighGroundRadius() + highGroundUnloadMargin;
        List<Vector2Int> removeSuper = new List<Vector2Int>();
        foreach (var entry in superChunks)
        {
            bool inside = IsInsideSuperCoverage(entry.Key, p, radius);
            if (inside)
            {
                superOutsideSince.Remove(entry.Key);
                continue;
            }
            if (!superOutsideSince.TryGetValue(entry.Key, out float since))
                superOutsideSince[entry.Key] = now;
            else if (now - since >= retainedChunkSeconds && removeSuper.Count < budget)
                removeSuper.Add(entry.Key);
        }
        foreach (Vector2Int coord in removeSuper)
        {
            ChunkData data = superChunks[coord];
            superChunks.Remove(coord);
            superOutsideSince.Remove(coord);
            chunkLifecycle.Destroy(data);
            lodVisibility.RefreshSuper(coord, highSpeedMode);
        }
    }

    private void RebuildSuperQueue(Vector3 p)
    {
        superQueue.Clear();

        Vector2Int center = GetSuperCoord(p);

        float extentX;
        float extentZ;

        float highGroundRadius = GetHighGroundRadius();
        extentX = highGroundRadius;
        extentZ = highGroundRadius;

        if (highSpeedPrewarmMode)
        {
            GetHighSpeedSearchExtents(out float speedX, out float speedZ);
            extentX = Mathf.Max(extentX, speedX);
            extentZ = Mathf.Max(extentZ, speedZ);
        }

        int rangeX = Mathf.CeilToInt(
            extentX / superFastChunkSize) + 2;

        int rangeZ = Mathf.CeilToInt(
            extentZ / superFastChunkSize) + 2;

        for (int z = -rangeZ; z <= rangeZ; z++)
        {
            for (int x = -rangeX; x <= rangeX; x++)
            {
                Vector2Int c = new Vector2Int(
                    center.x + x,
                    center.y + z);

                bool insideArea = IsInsideSuperCoverage(
                    c, p, highGroundRadius);

                if (!insideArea)
                    continue;

                if (superChunks.ContainsKey(c) ||
                    pendingSuper.Contains(c))
                {
                    continue;
                }

                superQueue.Add(c);
            }
        }

        superQueue.Sort((a, b) =>
            GetSuperPriority(a, p).CompareTo(
                GetSuperPriority(b, p)));
    }

    private void RebuildFastQueue(Vector3 p)
    {
        fastQueue.Clear();

        if (highSpeedMode)
            return;

        int range = Mathf.CeilToInt(loadRadius / fastChunkSize) + 2;
        Vector2Int center = GetFastCoord(p);

        for (int z = -range; z <= range; z++)
        {
            for (int x = -range; x <= range; x++)
            {
                Vector2Int c = new Vector2Int(
                    center.x + x,
                    center.y + z);

                if (!ChunkIntersectsRadius(
                    c, fastChunkSize, p, loadRadius))
                    continue;

                if (fastChunks.ContainsKey(c) || pendingFast.Contains(c))
                    continue;

                fastQueue.Add(c);
            }
        }

        // Сначала обслуживаем ближайшую Super-группу целиком.
        // Расстояние считается до границы группы, а не до её центра:
        // центральная группа, содержащая игрока, получает приоритет 0.
        // Это не позволяет группе на дальней стороне круга 350 м
        // обгонять группу непосредственно под игроком.
        fastQueue.Sort((a, b) =>
        {
            Vector2Int superA = TerrainLodCoordinates.GetParent(
                a, FastPerSuper);
            Vector2Int superB = TerrainLodCoordinates.GetParent(
                b, FastPerSuper);

            int groupCompare = GetChunkBoundsDistanceSqr(
                superA, superFastChunkSize, p).CompareTo(
                GetChunkBoundsDistanceSqr(superB, superFastChunkSize, p));

            if (groupCompare != 0)
                return groupCompare;

            return GetChunkDistanceSqr(
                a, fastChunkSize, p).CompareTo(
                GetChunkDistanceSqr(b, fastChunkSize, p));
        });
    }

    private void RebuildFineQueue(Vector3 p)
    {
        fineQueue.Clear();

        if (highSpeedMode)
            return;

        int range = Mathf.CeilToInt(coreRadius / chunkSize) + 2;
        Vector2Int center = GetFineCoord(p);

        for (int z = -range; z <= range; z++)
        {
            for (int x = -range; x <= range; x++)
            {
                Vector2Int c = new Vector2Int(
                    center.x + x,
                    center.y + z);

                if (!ChunkIntersectsRadius(
                    c, chunkSize, p, coreRadius))
                    continue;

                if (fineChunks.ContainsKey(c) || pendingFine.Contains(c))
                    continue;

                Vector2Int fast = TerrainLodCoordinates.GetParent(
                    c, FinePerFast);

                // Не строим Fine заранее под ещё видимым Super.
                // Сначала игрок получает полноценный Fast-уровень,
                // затем этот Fast-чанк начинает повышать детализацию до Fine.
                if (!lodVisibility.CanGenerateFineForFast(fast))
                    continue;

                fineQueue.Add(c);
            }
        }

        // Завершаем ближайшие Fast-группы целиком: каждый Fast требует
        // 16 дочерних Fine-чанков для переключения без дыр и наложений.
        fineQueue.Sort((a, b) =>
        {
            Vector2Int fastA = TerrainLodCoordinates.GetParent(
                a, FinePerFast);
            Vector2Int fastB = TerrainLodCoordinates.GetParent(
                b, FinePerFast);

            int groupCompare = GetChunkBoundsDistanceSqr(
                fastA, fastChunkSize, p).CompareTo(
                GetChunkBoundsDistanceSqr(fastB, fastChunkSize, p));

            if (groupCompare != 0)
                return groupCompare;

            return GetChunkDistanceSqr(
                a, chunkSize, p).CompareTo(
                GetChunkDistanceSqr(b, chunkSize, p));
        });
    }

    // =========================================================
    // JOB SCHEDULING
    // =========================================================

    private void ProcessSuperGeneration()
    {
        int count = 0;

        int superPendingLimit = highSpeedPrewarmMode
            ? maxPendingSuperJobs
            : maxPendingJobs;

        int totalPendingLimit = highSpeedPrewarmMode
            ? maxPendingJobs + maxPendingSuperJobs
            : maxPendingJobs;

        while (superQueue.Count > 0 &&
               count < maxSuperJobsPerFrame &&
               pendingSuper.Count < superPendingLimit &&
               pendingJobs.Count < totalPendingLimit)
        {
            Vector2Int c = superQueue[0];
            superQueue.RemoveAt(0);

            if (superChunks.ContainsKey(c) || pendingSuper.Contains(c))
                continue;

            PendingChunk pending = CreateFanJob(
                ChunkType.Super,
                c,
                superFastChunkSize,
                superFastCellsPerSide,
                fastCellsPerSide * cellsPerChunk);

            pendingJobs.Add(pending);
            pendingSuper.Add(c);

            count++;
        }
    }

    private void ProcessFastGeneration()
    {
        if (highSpeedMode)
            return;

        int count = 0;

        while (fastQueue.Count > 0 &&
               count < maxFastJobsPerFrame &&
               pendingFast.Count < maxPendingFastJobs &&
               pendingJobs.Count < maxPendingJobs)
        {
            Vector2Int c = fastQueue[0];
            fastQueue.RemoveAt(0);

            if (fastChunks.ContainsKey(c) || pendingFast.Contains(c))
                continue;

            PendingChunk pending = CreateFanJob(
                ChunkType.Fast,
                c,
                fastChunkSize,
                fastCellsPerSide,
                cellsPerChunk);

            pendingJobs.Add(pending);
            pendingFast.Add(c);

            count++;
        }
    }

    private void ProcessFineGeneration()
    {
        if (highSpeedMode)
            return;

        int count = 0;

        while (fineQueue.Count > 0 &&
               count < maxFineJobsPerFrame &&
               pendingFine.Count < maxPendingFineJobs &&
               pendingJobs.Count < maxPendingJobs)
        {
            Vector2Int c = fineQueue[0];
            fineQueue.RemoveAt(0);

            if (fineChunks.ContainsKey(c) || pendingFine.Contains(c))
                continue;

            Vector2Int fast = TerrainLodCoordinates.GetParent(
                c, FinePerFast);

            // Повторная проверка нужна на случай, если игрок переместился
            // после последней перестройки очереди.
            if (!lodVisibility.CanGenerateFineForFast(fast))
                continue;

            PendingChunk pending = CreateFineJob(c);

            pendingJobs.Add(pending);
            pendingFine.Add(c);

            count++;
        }
    }
}
