using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;
using System.Text;

[DefaultExecutionOrder(-1000)]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public partial class TerrainGenerator : MonoBehaviour
{
    [Header("Player")]
    public Transform player;

    [Header("Chunk Sizes")]
    public float chunkSize = 4f;
    public int cellsPerChunk = 16;

    public float fastChunkSize = 16f;
    public int fastCellsPerSide = 4;

    public float superFastChunkSize = 64f;
    public int superFastCellsPerSide = 4;

    [Header("Streaming")]
    public float loadRadius = 350f;
    public float coreRadius = 80f;
    public float colliderRadius = 350f;

    [Header("High Ground Streaming")]
    public bool highGroundEnabled = true;
    [Tooltip("World Y coordinate of sea level.")]
    public float seaLevelY = 0f;
    public float highGroundStartHeight = 40f;
    public float highGroundMaxHeight = 1000f;
    public float highGroundMaxRadius = 1000f;
    [Tooltip("Extra distance before unloading SuperFast chunks to avoid thrashing.")]
    public float highGroundUnloadMargin = 64f;
    public int maxHighGroundUnloadsPerUpdate = 32;

    [Header("Chunk Retention")]
    [Tooltip("Seconds outside streaming coverage before Fast/SuperFast are destroyed.")]
    public float retainedChunkSeconds = 120f;
    [Tooltip("How often to inspect retained chunks (seconds). No per-chunk timers.")]
    public float retentionScanInterval = 1f;
    [Tooltip("Maximum retained Fast/SuperFast removals per scan.")]
    public int maxRetentionRemovalsPerScan = 32;
    private readonly Dictionary<Vector2Int, float> fastOutsideSince = new();
    private readonly Dictionary<Vector2Int, float> superOutsideSince = new();
    private float nextRetentionScan;

    [Header("High Speed Streaming")]
    public float highSpeedPrewarmThreshold = 350f;
    public float highSpeedEnterThreshold = 700f;
    public float highSpeedExitThreshold = 500f;

    [Header("High Speed Stability")]
    [Tooltip("Время, в течение которого скорость должна оставаться ниже порога выхода, прежде чем High Speed выключится.")]
    public float highSpeedExitDelay = 0.75f;

    public float highSpeedForwardDistance = 1500f;
    public float highSpeedSideDistance = 600f;
    public float highSpeedBackwardDistance = 400f;
    public float highSpeedSafetyRadius = 450f;

    [Header("High Speed Generation")]
    public int maxPendingSuperJobs = 12;
    public int maxSuperBuildsPerFrame = 4;
    public int maxOtherBuildsPerFrame = 4;

    [Header("Generation Budget")]
    public int maxSuperJobsPerFrame = 12;
    public int maxFastJobsPerFrame = 4;
    public int maxFineJobsPerFrame = 8;

    [Header("LOD Queue Limits")]
    [Tooltip("Максимум одновременно ожидающих Fast-задач. Оставляет место для Fine.")]
    public int maxPendingFastJobs = 20;

    [Tooltip("Максимум одновременно ожидающих Fine-задач после готовности Fast-области.")]
    public int maxPendingFineJobs = 12;

    public int maxPendingJobs = 32;

    [Header("Update")]
    public float updateInterval = 0.1f;

    [Header("Diagnostics")]
    public bool diagnosticsEnabled = true;
    public float diagnosticsInterval = 0.5f;
    [Range(1, 10)]
    public int diagnosticsChunkRadius = 2;
    [Header("Fall Detection")]
    [Tooltip("На сколько метров игрок должен оказаться ниже поверхности для регистрации провала.")]
    public float fallDetectionDepth = 2.5f;

    [Tooltip("Минимальная скорость движения вниз для регистрации провала.")]
    public float fallDetectionMinDownSpeed = 2f;

    [Tooltip("Задержка между регистрацией отдельных инцидентов.")]
    public float fallDetectionCooldown = 3f;

    private Vector3 previousFallCheckLocalPosition;
    private bool hasPreviousFallCheckPosition;

    private float lastFallReportTime = -100f;
    private bool fallIncidentActive;

    [Header("Terrain")]
    public float height = 8f;

    [Header("Noise")]
    public float noiseScale = 0.045f;
    public float detailScale = 0.12f;
    public float detailStrength = 1.5f;

    [Header("Ground Texture")]
    public float textureScale = 5f;
    public float originalTerrainSize = 100f;

    [Header("Debug Grid")]
    public bool showGrid = true;
    public float gridHeightOffset = 0.12f;
    public float gridLineWidth = 0.025f;

    public Color fineGridColor = new Color(0.04f, 0.30f, 0.10f, 1f);
    public Color fastGridColor = new Color(0.55f, 0.43f, 0.02f, 1f);
    public Color superGridColor = new Color(0.9f, 0.025f, 0.025f, 1f);

    private readonly Dictionary<Vector2Int, ChunkData> superChunks = new();
    private readonly Dictionary<Vector2Int, ChunkData> fastChunks = new();
    private readonly Dictionary<Vector2Int, ChunkData> fineChunks = new();

    private readonly List<Vector2Int> superQueue = new();
    private readonly List<Vector2Int> fastQueue = new();
    private readonly List<Vector2Int> fineQueue = new();

    private readonly HashSet<Vector2Int> pendingSuper = new();
    private readonly HashSet<Vector2Int> pendingFast = new();
    private readonly HashSet<Vector2Int> pendingFine = new();

    private readonly List<PendingChunk> pendingJobs = new();

    private Material groundMaterial;
    private Material gridMaterial;
    private TerrainLodVisibilityController lodVisibility;
    private TerrainChunkLifecycle chunkLifecycle;
    private TerrainStreamingDiagnostics streamingDiagnostics;

    private float updateTimer;
    private bool initialized;
    private bool highSpeedMode;
    private bool highSpeedPrewarmMode;

    private Vector3 previousWorldPosition;
    private bool hasPreviousPosition;

    private Vector2 movementDirection;
    private float currentPlayerSpeed;

    // Таймер стабилизации выхода из High Speed
    private float highSpeedBelowExitTimer;

    // Диагностика
    private float highSpeedModeStartedAt = -1f;
    private float prewarmModeStartedAt = -1f;
    private bool previousPrewarmMode;

    private const int FinePerFast = 4;
    private const int FastPerSuper = 4;

    private const float ColliderSafetyMargin = 32f;

    // =========================================================
    // BURST JOBS
    // =========================================================

    // =========================================================
    // INITIALIZATION
    // =========================================================

    private void OnValidate()
    {
        NormalizeSettings();
    }

    private void NormalizeSettings()
    {
        chunkSize = Mathf.Max(0.1f, chunkSize);

        cellsPerChunk = Mathf.Max(1, cellsPerChunk);
        fastCellsPerSide = Mathf.Max(1, fastCellsPerSide);
        superFastCellsPerSide = Mathf.Max(1, superFastCellsPerSide);

        fastChunkSize = chunkSize * FinePerFast;
        superFastChunkSize = fastChunkSize * FastPerSuper;

        loadRadius = Mathf.Max(superFastChunkSize, loadRadius);

        coreRadius = Mathf.Clamp(coreRadius, 0f, loadRadius);
        colliderRadius = Mathf.Max(colliderRadius, loadRadius);

        retainedChunkSeconds = Mathf.Max(1f, retainedChunkSeconds);
        retentionScanInterval = Mathf.Max(0.1f, retentionScanInterval);
        maxRetentionRemovalsPerScan = Mathf.Max(1, maxRetentionRemovalsPerScan);
        highGroundStartHeight = Mathf.Max(0f, highGroundStartHeight);
        highGroundMaxHeight = Mathf.Max(highGroundStartHeight + 0.01f, highGroundMaxHeight);
        highGroundMaxRadius = Mathf.Max(loadRadius, highGroundMaxRadius);
        highGroundUnloadMargin = Mathf.Max(0f, highGroundUnloadMargin);
        maxHighGroundUnloadsPerUpdate = Mathf.Max(1, maxHighGroundUnloadsPerUpdate);

        highSpeedForwardDistance = Mathf.Max(
            superFastChunkSize,
            highSpeedForwardDistance);

        highSpeedSideDistance = Mathf.Max(
            superFastChunkSize,
            highSpeedSideDistance);

        highSpeedBackwardDistance = Mathf.Max(
            superFastChunkSize,
            highSpeedBackwardDistance);

        highSpeedSafetyRadius = Mathf.Max(
            superFastChunkSize,
            highSpeedSafetyRadius);

        highSpeedPrewarmThreshold = Mathf.Max(
            0f,
            highSpeedPrewarmThreshold);

        highSpeedEnterThreshold = Mathf.Max(
            0f,
            highSpeedEnterThreshold);

        highSpeedExitThreshold = Mathf.Max(
            0f,
            highSpeedExitThreshold);

        highSpeedEnterThreshold = Mathf.Max(
            highSpeedExitThreshold,
            highSpeedEnterThreshold);

        highSpeedExitDelay = Mathf.Max(
            0f,
            highSpeedExitDelay);

        maxSuperJobsPerFrame = Mathf.Max(1, maxSuperJobsPerFrame);
        maxFastJobsPerFrame = Mathf.Max(1, maxFastJobsPerFrame);
        maxFineJobsPerFrame = Mathf.Max(1, maxFineJobsPerFrame);

        maxPendingJobs = Mathf.Max(1, maxPendingJobs);
        maxPendingSuperJobs = Mathf.Max(1, maxPendingSuperJobs);

        maxPendingFastJobs = Mathf.Clamp(
            maxPendingFastJobs, 1, maxPendingJobs);

        maxPendingFineJobs = Mathf.Clamp(
            maxPendingFineJobs, 1, maxPendingJobs);

        maxSuperBuildsPerFrame = Mathf.Max(1, maxSuperBuildsPerFrame);
        maxOtherBuildsPerFrame = Mathf.Max(1, maxOtherBuildsPerFrame);

        updateInterval = Mathf.Max(0.02f, updateInterval);

        diagnosticsInterval = Mathf.Max(0.1f, diagnosticsInterval);
        diagnosticsChunkRadius = Mathf.Clamp(
            diagnosticsChunkRadius, 1, 10);
    }

    private void Start()
    {
        NormalizeSettings();

        Terrain oldTerrain = GetComponent<Terrain>();

        if (oldTerrain != null)
            oldTerrain.enabled = false;

        if (player == null)
        {
            GameObject obj = GameObject.FindGameObjectWithTag("Player");

            if (obj != null)
                player = obj.transform;
        }

        if (player == null)
        {
            Debug.LogError("TerrainGenerator: Player not found!");
            enabled = false;
            return;
        }

        groundMaterial = CreateGroundMaterial();
        gridMaterial = CreateGridMaterial();

        if (groundMaterial == null)
        {
            enabled = false;
            return;
        }

        chunkLifecycle = new TerrainChunkLifecycle(ChunkIntersectsRadius);

        lodVisibility = new TerrainLodVisibilityController(
            superChunks,
            fastChunks,
            fineChunks,
            FinePerFast,
            FastPerSuper,
            chunkLifecycle.SetVisible);

        streamingDiagnostics = new TerrainStreamingDiagnostics(
            superChunks,
            superQueue,
            pendingSuper,
            pendingJobs,
            GetSuperCoord,
            GetChunkDistanceSqr);

        previousWorldPosition = player.position;
        hasPreviousPosition = true;

        Vector3 p = GetPlayerPosition();

        Debug.Log("TerrainGenerator: generating initial SuperFast coverage with Burst.");

        GenerateInitialSuperCoverage(p);

        RebuildSuperQueue(p);
        RebuildFastQueue(p);
        RebuildFineQueue(p);

        initialized = true;

        Debug.Log(
            $"TerrainGenerator: initial SuperFast chunks: {superChunks.Count}");
    }

    private void Update()
    {
        if (!initialized || player == null)
            return;

        Vector3 p = GetPlayerPosition();

        UpdatePlayerSpeed();

        bool modeChanged = UpdateSpeedMode();

        highSpeedPrewarmMode =
            highSpeedMode ||
            currentPlayerSpeed >= highSpeedPrewarmThreshold;

        // Фиксируем моменты включения режимов только для диагностики.
        if (highSpeedMode && highSpeedModeStartedAt < 0f)
            highSpeedModeStartedAt = Time.time;

        if (!highSpeedMode)
            highSpeedModeStartedAt = -1f;

        if (highSpeedPrewarmMode && prewarmModeStartedAt < 0f)
            prewarmModeStartedAt = Time.time;

        if (!highSpeedPrewarmMode)
            prewarmModeStartedAt = -1f;

        previousPrewarmMode = highSpeedPrewarmMode;

        updateTimer += Time.deltaTime;

        if (modeChanged)
        {
            if (highSpeedMode)
            {
                lodVisibility.ApplyHighSpeedVisibility();

                Debug.Log(
                    $"TerrainGenerator: HIGH SPEED MODE ON ({currentPlayerSpeed:F0} m/s)");
            }
            else
            {
                lodVisibility.RefreshAllNormalVisibility();

                Debug.Log(
                    $"TerrainGenerator: HIGH SPEED MODE OFF ({currentPlayerSpeed:F0} m/s)");
            }
        }

        if (highSpeedPrewarmMode)
        {
            RebuildSuperQueue(p);
        }

        bool streamingTick = modeChanged || updateTimer >= updateInterval;
        if (streamingTick && !highSpeedPrewarmMode)
            RebuildSuperQueue(p);

        if (!highSpeedMode && streamingTick)
        {
            updateTimer = 0f;

            RebuildFastQueue(p);
            RebuildFineQueue(p);
        }

        CompleteFinishedJobs();

        if (Time.time >= nextRetentionScan)
        {
            nextRetentionScan = Time.time + retentionScanInterval;
            CleanupStreamingChunks(p);
        }

        ProcessSuperGeneration();

        if (!highSpeedMode)
        {
            ProcessFastGeneration();
            ProcessFineGeneration();
        }

        chunkLifecycle.UpdateVisibleColliders(
            superChunks,
            fastChunks,
            fineChunks,
            superFastChunkSize,
            fastChunkSize,
            chunkSize,
            p,
            colliderRadius,
            coreRadius,
            ColliderSafetyMargin);

        DetectTerrainFall();

        streamingDiagnostics.Run(
            diagnosticsEnabled,
            diagnosticsInterval,
            diagnosticsChunkRadius,
            p,
            superFastChunkSize,
            currentPlayerSpeed,
            highSpeedPrewarmMode,
            highSpeedMode,
            prewarmModeStartedAt,
            highSpeedModeStartedAt,
            highSpeedBelowExitTimer,
            highSpeedExitDelay,
            movementDirection,
            maxPendingJobs,
            maxPendingSuperJobs);
    }
}
