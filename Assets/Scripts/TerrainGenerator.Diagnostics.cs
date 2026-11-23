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
    // DIAGNOSTICS
    // =========================================================

    // =========================================================
    // AUTOMATIC FALL DETECTION
    // =========================================================

    private void DetectTerrainFall()
    {
        if (player == null)
            return;

        Vector3 localPosition =
            transform.InverseTransformPoint(player.position);

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);

        if (!hasPreviousFallCheckPosition)
        {
            previousFallCheckLocalPosition = localPosition;
            hasPreviousFallCheckPosition = true;
            return;
        }

        float verticalSpeed =
            (localPosition.y - previousFallCheckLocalPosition.y) / dt;

        previousFallCheckLocalPosition = localPosition;

        float groundHeight = GetTerrainHeight(
            localPosition.x,
            localPosition.z);

        float depthBelowGround = groundHeight - localPosition.y;

        bool belowGround =
            depthBelowGround >= fallDetectionDepth;

        bool fallingDown =
            verticalSpeed <= -fallDetectionMinDownSpeed;

        if (!belowGround || !fallingDown)
        {
            if (depthBelowGround < fallDetectionDepth * 0.5f)
                fallIncidentActive = false;

            return;
        }

        if (fallIncidentActive)
            return;

        if (Time.unscaledTime - lastFallReportTime <
            fallDetectionCooldown)
        {
            return;
        }

        fallIncidentActive = true;
        lastFallReportTime = Time.unscaledTime;

        LogTerrainFallIncident(
            localPosition,
            groundHeight,
            depthBelowGround,
            verticalSpeed);
    }

    private void LogTerrainFallIncident(
        Vector3 playerPosition,
        float groundHeight,
        float depthBelowGround,
        float verticalSpeed)
    {
        Vector2Int superCoord = GetSuperCoord(playerPosition);
        Vector2Int fastCoord = GetFastCoord(playerPosition);
        Vector2Int fineCoord = GetFineCoord(playerPosition);

        StringBuilder report = new StringBuilder();

        report.AppendLine();
        report.AppendLine("==================================================");
        report.AppendLine("             TERRAIN FALL DETECTED");
        report.AppendLine("==================================================");

        report.AppendLine(
            $"Time: {Time.time:F2}s");

        report.AppendLine(
            $"Player local position: " +
            $"X={playerPosition.x:F2}, " +
            $"Y={playerPosition.y:F2}, " +
            $"Z={playerPosition.z:F2}");

        report.AppendLine(
            $"Ground height: {groundHeight:F2}");

        report.AppendLine(
            $"Depth below ground: {depthBelowGround:F2}m");

        report.AppendLine(
            $"Vertical speed: {verticalSpeed:F2}m/s");

        report.AppendLine(
            $"Total speed: {currentPlayerSpeed:F2}m/s");

        report.AppendLine(
            $"HighSpeed: {highSpeedMode}");

        report.AppendLine(
            $"Prewarm: {highSpeedPrewarmMode}");

        report.AppendLine(
            $"Movement direction: {movementDirection}");

        report.AppendLine();
        report.AppendLine("--- CHUNKS UNDER PLAYER ---");

        report.AppendLine(
            $"Super coordinate: {superCoord}");

        report.AppendLine(
            GetChunkDiagnostic(
                superChunks,
                pendingSuper,
                superQueue,
                superCoord));

        report.AppendLine(
            $"Fast coordinate: {fastCoord}");

        report.AppendLine(
            GetChunkDiagnostic(
                fastChunks,
                pendingFast,
                fastQueue,
                fastCoord));

        report.AppendLine(
            $"Fine coordinate: {fineCoord}");

        report.AppendLine(
            GetChunkDiagnostic(
                fineChunks,
                pendingFine,
                fineQueue,
                fineCoord));

        report.AppendLine();
        report.AppendLine("--- GENERATION STATE ---");

        report.AppendLine(
            $"Super chunks: {superChunks.Count}");

        report.AppendLine(
            $"Fast chunks: {fastChunks.Count}");

        report.AppendLine(
            $"Fine chunks: {fineChunks.Count}");

        report.AppendLine(
            $"Super queue: {superQueue.Count}");

        report.AppendLine(
            $"Fast queue: {fastQueue.Count}");

        report.AppendLine(
            $"Fine queue: {fineQueue.Count}");

        report.AppendLine(
            $"Pending Super: {pendingSuper.Count}");

        report.AppendLine(
            $"Pending Fast: {pendingFast.Count}");

        report.AppendLine(
            $"Pending Fine: {pendingFine.Count}");

        report.AppendLine(
            $"All pending Jobs: {pendingJobs.Count}");

        int completedJobs = 0;
        int unfinishedJobs = 0;

        for (int i = 0; i < pendingJobs.Count; i++)
        {
            if (pendingJobs[i].handle.IsCompleted)
                completedJobs++;
            else
                unfinishedJobs++;
        }

        report.AppendLine(
            $"Jobs completed/waiting: {completedJobs}");

        report.AppendLine(
            $"Jobs unfinished: {unfinishedJobs}");

        report.AppendLine();
        report.AppendLine("--- NEARBY SUPER COVERAGE ---");

        AppendFallCoverage(report, playerPosition);

        report.AppendLine();
        report.AppendLine("--- COLLIDER SETTINGS ---");

        report.AppendLine(
            $"Collider radius: {colliderRadius:F1}m");

        report.AppendLine(
            $"Core radius: {coreRadius:F1}m");

        report.AppendLine(
            $"Safety margin: {ColliderSafetyMargin:F1}m");

        report.AppendLine("==================================================");

        Debug.LogError(report.ToString());
    }

    private string GetChunkDiagnostic(
        Dictionary<Vector2Int, ChunkData> chunks,
        HashSet<Vector2Int> pending,
        List<Vector2Int> queue,
        Vector2Int coord)
    {
        if (chunks.TryGetValue(coord, out ChunkData data))
        {
            if (data == null)
                return "Chunk: EXISTS, DATA IS NULL";

            bool objectExists = data.gameObject != null;
            bool rendererEnabled =
                data.renderer != null && data.renderer.enabled;

            bool colliderExists = data.collider != null;
            bool colliderEnabled =
                colliderExists && data.collider.enabled;

            return
                $"Chunk: EXISTS | " +
                $"Visible={data.visible} | " +
                $"Object={objectExists} | " +
                $"Renderer={rendererEnabled} | " +
                $"Collider exists={colliderExists} | " +
                $"Collider enabled={colliderEnabled}";
        }

        if (pending.Contains(coord))
            return "Chunk: NOT CREATED | JOB PENDING";

        if (queue.Contains(coord))
            return "Chunk: NOT CREATED | IN GENERATION QUEUE";

        return "Chunk: NOT CREATED | NOT IN QUEUE";
    }

    private void AppendFallCoverage(
        StringBuilder report,
        Vector3 playerPosition)
    {
        Vector2Int center = GetSuperCoord(playerPosition);

        int existing = 0;
        int pending = 0;
        int queued = 0;
        int missing = 0;
        int invisible = 0;
        int colliderDisabled = 0;

        float nearestMissingDistance = float.MaxValue;
        Vector2Int nearestMissingCoord = center;
        string nearestMissingState = "none";

        const int radius = 2;

        for (int z = -radius; z <= radius; z++)
        {
            for (int x = -radius; x <= radius; x++)
            {
                Vector2Int coord = new Vector2Int(
                    center.x + x,
                    center.y + z);

                if (superChunks.TryGetValue(
                    coord, out ChunkData data))
                {
                    existing++;

                    if (data == null || !data.visible)
                        invisible++;

                    if (data != null &&
                        data.visible &&
                        (data.collider == null ||
                         !data.collider.enabled))
                    {
                        colliderDisabled++;
                    }

                    continue;
                }

                bool isPending = pendingSuper.Contains(coord);
                bool isQueued = superQueue.Contains(coord);

                if (isPending)
                    pending++;
                else if (isQueued)
                    queued++;
                else
                    missing++;

                float distance = Mathf.Sqrt(
                    GetChunkDistanceSqr(
                        coord,
                        superFastChunkSize,
                        playerPosition));

                if (distance < nearestMissingDistance)
                {
                    nearestMissingDistance = distance;
                    nearestMissingCoord = coord;

                    if (isPending)
                        nearestMissingState = "JOB_PENDING";
                    else if (isQueued)
                        nearestMissingState = "IN_QUEUE";
                    else
                        nearestMissingState = "NOT_QUEUED";
                }
            }
        }

        report.AppendLine(
            $"Super coverage 5x5: existing={existing}, " +
            $"pending={pending}, queued={queued}, " +
            $"missing={missing}, invisible={invisible}, " +
            $"collider disabled={colliderDisabled}");

        if (nearestMissingDistance < float.MaxValue)
        {
            report.AppendLine(
                $"Nearest missing: {nearestMissingCoord}, " +
                $"{nearestMissingDistance:F1}m, " +
                $"{nearestMissingState}");
        }
        else
        {
            report.AppendLine("Nearest missing: none");
        }
    }
}
