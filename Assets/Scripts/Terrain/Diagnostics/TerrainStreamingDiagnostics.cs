using System;
using System.Collections.Generic;
using UnityEngine;

internal sealed class TerrainStreamingDiagnostics
{
    private readonly Dictionary<Vector2Int, ChunkData> superChunks;
    private readonly List<Vector2Int> superQueue;
    private readonly HashSet<Vector2Int> pendingSuper;
    private readonly List<PendingChunk> pendingJobs;
    private readonly Func<Vector3, Vector2Int> getSuperCoord;
    private readonly Func<Vector2Int, float, Vector3, float> getChunkDistanceSqr;
    private float timer;

    public TerrainStreamingDiagnostics(
        Dictionary<Vector2Int, ChunkData> superChunks,
        List<Vector2Int> superQueue,
        HashSet<Vector2Int> pendingSuper,
        List<PendingChunk> pendingJobs,
        Func<Vector3, Vector2Int> getSuperCoord,
        Func<Vector2Int, float, Vector3, float> getChunkDistanceSqr)
    {
        this.superChunks = superChunks;
        this.superQueue = superQueue;
        this.pendingSuper = pendingSuper;
        this.pendingJobs = pendingJobs;
        this.getSuperCoord = getSuperCoord;
        this.getChunkDistanceSqr = getChunkDistanceSqr;
    }

    public void Run(bool enabled, float interval, int radius, Vector3 playerPosition,
        float superChunkSize, float playerSpeed, bool prewarmMode, bool highSpeedMode,
        float prewarmStartedAt, float highSpeedStartedAt, float exitTimer, float exitDelay,
        Vector2 movementDirection, int maxPendingJobs, int maxPendingSuperJobs)
    {
        if (!enabled)
            return;

        timer += Time.unscaledDeltaTime;
        if (timer < interval)
            return;

        timer = 0f;
        CountJobs(out int pendingSuperCount, out int completedSuperCount,
            out int pendingOtherCount, out int completedOtherCount);
        Vector2Int center = getSuperCoord(playerPosition);
        CountNearby(center, radius, superChunkSize, playerPosition, out int existing,
            out int pending, out int queued, out int missing, out int invisible,
            out float nearestDistance, out Vector2Int nearestCoord, out string nearestState);

        float prewarmAge = prewarmStartedAt >= 0f ? Time.time - prewarmStartedAt : 0f;
        float highSpeedAge = highSpeedStartedAt >= 0f ? Time.time - highSpeedStartedAt : 0f;
        string nearestInfo = nearestDistance < float.MaxValue
            ? $"{nearestCoord} ({nearestDistance:F0}m, {nearestState})" : "none";

        Debug.Log(
            "[TERRAIN DIAGNOSTICS]\n" +
            $"Speed: {playerSpeed:F1} m/s | Prewarm: {prewarmMode} ({prewarmAge:F2}s) | HighSpeed: {highSpeedMode} ({highSpeedAge:F2}s)\n" +
            $"HighSpeed exit timer: {exitTimer:F2}/{exitDelay:F2}s\n" +
            $"Player local: X={playerPosition.x:F1}, Z={playerPosition.z:F1} | Direction: X={movementDirection.x:F2}, Z={movementDirection.y:F2}\n" +
            $"Super chunks total: {superChunks.Count} | Super queue: {superQueue.Count} | Pending Super set: {pendingSuper.Count}\n" +
            $"Super Jobs: running/unfinished={pendingSuperCount - completedSuperCount}, completed/waiting={completedSuperCount} | " +
            $"Other Jobs: running/unfinished={pendingOtherCount - completedOtherCount}, completed/waiting={completedOtherCount}\n" +
            $"All pending Jobs: {pendingJobs.Count} | Pending limit: {(prewarmMode ? maxPendingJobs + maxPendingSuperJobs : maxPendingJobs)}\n" +
            $"Nearby Super area ({radius * 2 + 1}x{radius * 2 + 1}): existing={existing}, pending={pending}, queued={queued}, not queued={missing}, invisible={invisible}\n" +
            $"Nearest missing: {nearestInfo}");
    }

    private void CountJobs(out int pendingSuperCount, out int completedSuperCount,
        out int pendingOtherCount, out int completedOtherCount)
    {
        pendingSuperCount = completedSuperCount = pendingOtherCount = completedOtherCount = 0;
        for (int i = 0; i < pendingJobs.Count; i++)
        {
            PendingChunk pending = pendingJobs[i];
            if (pending.type == ChunkType.Super)
            {
                pendingSuperCount++;
                if (pending.handle.IsCompleted) completedSuperCount++;
            }
            else
            {
                pendingOtherCount++;
                if (pending.handle.IsCompleted) completedOtherCount++;
            }
        }
    }

    private void CountNearby(Vector2Int center, int radius, float chunkSize,
        Vector3 playerPosition, out int existing, out int pending, out int queued,
        out int missing, out int invisible, out float nearestDistance,
        out Vector2Int nearestCoord, out string nearestState)
    {
        existing = pending = queued = missing = invisible = 0;
        nearestDistance = float.MaxValue;
        nearestCoord = center;
        nearestState = "none";

        for (int z = -radius; z <= radius; z++)
        for (int x = -radius; x <= radius; x++)
        {
            Vector2Int coord = new Vector2Int(center.x + x, center.y + z);
            if (superChunks.TryGetValue(coord, out ChunkData data))
            {
                existing++;
                if (data == null || !data.visible) invisible++;
                continue;
            }

            bool isPending = pendingSuper.Contains(coord);
            bool isQueued = superQueue.Contains(coord);
            if (isPending) pending++;
            else if (isQueued) queued++;
            else missing++;

            float distance = Mathf.Sqrt(getChunkDistanceSqr(coord, chunkSize, playerPosition));
            if (distance >= nearestDistance) continue;
            nearestDistance = distance;
            nearestCoord = coord;
            nearestState = isPending ? "JOB_PENDING" : isQueued ? "IN_QUEUE" : "NOT_QUEUED";
        }
    }
}
