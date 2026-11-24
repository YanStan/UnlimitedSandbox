using System.Collections.Generic;
using UnityEngine;

/// <summary>Deterministic, player-centered streaming of closed floating islands.
/// Independent of terrain streaming. Only nearby island GameObjects exist.</summary>
public sealed class FloatingIslandStreamer : MonoBehaviour
{
    [Header("Player")]
    public Transform player;
    [Header("World generation")]
    public int worldSeed = 12345;
    [Min(100f)] public float regionSize = 512f;
    [Range(0f, 1f)] public float islandProbability = 0.65f;
    public float minIslandAltitude = 140f;
    public float maxIslandAltitude = 450f;
    [Min(10f)] public float minIslandRadius = 35f;
    [Min(10f)] public float maxIslandRadius = 110f;
    [Header("Streaming")]
    [Min(100f)] public float loadRadius = 900f;
    [Min(0f)] public float unloadMargin = 150f;
    [Min(0.05f)] public float updateInterval = 0.5f;
    [Range(1, 16)] public int maxIslandsPerUpdate = 2;
    [Header("Spatial LOD (4 / 16 / 64 m)")]
    public bool useTerrainChunkLod = true;
    [Min(0f)] public float spatialFineDistance = 32f;
    [Min(0f)] public float spatialFastDistance = 100f;
    [Range(1,12)] public int spatialBuildsPerFrame = 3;
    [Header("Island mesh")]
    [Range(8, 128)] public int radialSegments = 32;
    [Range(1, 64)] public int rings = 10;
    public Material islandMaterial;
    public bool createColliders = true;
    [Header("Optional physics optimization")]
    [Min(0f)] public float colliderActivationRadius = 180f;

    private readonly Dictionary<Vector2Int, FloatingIslandMesh> active = new Dictionary<Vector2Int, FloatingIslandMesh>();
    private readonly List<Vector2Int> removal = new List<Vector2Int>();
    private float nextUpdate;
    private Material runtimeIslandMaterial;
    [Header("Island LOD (distance to nearest surface)")]
    public bool enableLod = true;
    [Min(0f)] public float fineDistance = 100f;
    [Min(0f)] public float fastDistance = 350f;
    [Min(0f)] public float lodHysteresis = 15f;
    [Range(8,128)] public int fineRadialSegments = 96;
    [Range(1,64)] public int fineRings = 32;
    [Range(8,128)] public int fastRadialSegments = 32;
    [Range(1,64)] public int fastRings = 10;
    [Range(8,128)] public int superRadialSegments = 12;
    [Range(1,64)] public int superRings = 3;
    [Header("Colored LOD mesh grid")]
    public bool showGrid = true;
    public Color fineGridColor = new Color(0.04f, 0.30f, 0.10f, 1f);
    public Color fastGridColor = new Color(0.55f, 0.43f, 0.02f, 1f);
    public Color superGridColor = new Color(0.9f, 0.025f, 0.025f, 1f);
    [Header("LOD rebuild budget")]
    [Range(1,16)] public int maxLodRebuildsPerUpdate = 2;
    private readonly List<FloatingIslandMesh> lodCandidates = new List<FloatingIslandMesh>();


    private void Awake()
    {
        if (player == null)
        {
            var controller = FindFirstObjectByType<PlayerController>();
            if (controller != null) player = controller.transform;
        }
    }
    private void Update()
    {
        if (player == null || Time.time < nextUpdate) return;
        nextUpdate = Time.time + updateInterval;
        float cellSize = Mathf.Max(100f, regionSize);
        float radius = Mathf.Max(100f, loadRadius);
        float unload = radius + Mathf.Max(0f, unloadMargin);
        Vector3 pos = player.position;
        removal.Clear();
        lodCandidates.Clear();
        foreach (var kv in active)
        {
            if (kv.Value == null || HorizontalDistanceSqr(pos, kv.Value.transform.position) > unload * unload)
                removal.Add(kv.Key);
            else
            {
                lodCandidates.Add(kv.Value);
                var collider = kv.Value.GetComponent<MeshCollider>();
                if (collider != null)
                {
                    float near = colliderActivationRadius + Mathf.Max(kv.Value.radiusX, kv.Value.radiusZ);
                    collider.enabled = createColliders && (pos - kv.Value.transform.position).sqrMagnitude < near * near;
                }
            }
        }
        foreach (var key in removal)
        {
            if (active.TryGetValue(key, out var island) && island != null) Destroy(island.gameObject);
            active.Remove(key);
        }
        int cx = Mathf.FloorToInt(pos.x / cellSize);
        int cz = Mathf.FloorToInt(pos.z / cellSize);
        int range = Mathf.CeilToInt((radius + cellSize) / cellSize);
        int created = 0;
        // Prioritize nearby regions instead of row-major order.
        for (int shell = 0; shell <= range && created < maxIslandsPerUpdate; shell++)
        for (int dz = -shell; dz <= shell && created < maxIslandsPerUpdate; dz++)
        for (int dx = -shell; dx <= shell && created < maxIslandsPerUpdate; dx++)
        {
            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != shell) continue;
            var key = new Vector2Int(cx + dx, cz + dz);
            if (active.ContainsKey(key)) continue;
            uint state = Hash(key.x, key.y, worldSeed);
            if (Next01(ref state) > islandProbability) continue;
            float x = (key.x + 0.15f + 0.7f * Next01(ref state)) * cellSize;
            float z = (key.y + 0.15f + 0.7f * Next01(ref state)) * cellSize;
            float y = Mathf.Lerp(minIslandAltitude, maxIslandAltitude, Next01(ref state));
            var location = new Vector3(x, y, z);
            if (HorizontalDistanceSqr(pos, location) > radius * radius) continue;
            float size = Mathf.Lerp(minIslandRadius, maxIslandRadius, Next01(ref state));
            var go = new GameObject($"FloatingIsland_{key.x}_{key.y}");
            go.transform.SetParent(transform, false);
            go.transform.position = location;
            var island = go.AddComponent<FloatingIslandMesh>();
            island.generateOnStart = false;
            island.seed = unchecked((int)state);
            island.radiusX = size;
            island.radiusZ = size * Mathf.Lerp(0.7f, 1.3f, Next01(ref state));
            island.thickness = size * Mathf.Lerp(0.25f, 0.65f, Next01(ref state));
            island.radialSegments = enableLod ? superRadialSegments : radialSegments;
            island.rings = enableLod ? superRings : rings;
            island.islandMaterial = islandMaterial;
            island.createCollider = createColliders;
            if (useTerrainChunkLod)
            {
                var spatial = go.AddComponent<FloatingIslandSpatialLod>();
                spatial.player = player; spatial.surfaceMaterial = islandMaterial;
                if (spatial.surfaceMaterial == null)
                {
                    if (spatial.surfaceMaterial == null)
                    {
                        Shader shader = Shader.Find("Unlimited/TriplanarGrass");
                        if (shader != null)
                        {
                            if (runtimeIslandMaterial == null) runtimeIslandMaterial = new Material(shader);
                            spatial.surfaceMaterial = runtimeIslandMaterial;
                        }
                    }
                }
                spatial.radiusX = island.radiusX; spatial.radiusZ = island.radiusZ;
                spatial.thickness = island.thickness; spatial.noiseScale = island.noiseScale;
                spatial.topVariation = island.topVariation; spatial.bottomVariation = island.bottomVariation;
                spatial.seed = island.seed; spatial.showGrid = showGrid;
                spatial.createColliders = createColliders; spatial.colliderDistance = colliderActivationRadius;
                spatial.fineDistance = spatialFineDistance; spatial.fastDistance = spatialFastDistance;
                spatial.maxBuildsPerFrame = spatialBuildsPerFrame;
                spatial.fineColor = fineGridColor; spatial.fastColor = fastGridColor; spatial.superColor = superGridColor;
                island.enabled = false;
            }
            else island.ConfigureLod(enableLod ? 2 : 0,
                island.radialSegments, island.rings, showGrid,
                enableLod ? superGridColor : fineGridColor);
            var collider = island.GetComponent<MeshCollider>();
            if (collider != null)
            {
                float near = colliderActivationRadius + Mathf.Max(island.radiusX, island.radiusZ);
                collider.enabled = (pos - location).sqrMagnitude < near * near;
            }
            active.Add(key, island);
            created++;
            lodCandidates.Add(island);
        }
        if (enableLod)
        {
            lodCandidates.Sort((a,b) => SurfaceDistance(a, pos).CompareTo(SurfaceDistance(b, pos)));
            int rebuilt = 0;
            foreach (var island in lodCandidates)
            {
                if (island == null) continue;
                float distance = SurfaceDistance(island, pos);
                int target = distance <= fineDistance ? 0 : distance <= fastDistance ? 1 : 2;
                // Avoid rebuilding repeatedly when player hovers near a boundary.
                if (island.currentLod == 0 && distance < fineDistance + lodHysteresis) target = 0;
                if (island.currentLod == 1 && distance > fineDistance - lodHysteresis && distance < fastDistance + lodHysteresis) target = 1;
                if (island.currentLod == 2 && distance > fastDistance - lodHysteresis) target = 2;
                if (useTerrainChunkLod) continue;
                if (target != island.currentLod && rebuilt >= maxLodRebuildsPerUpdate) continue;
                island.ConfigureLod(target,
                    target == 0 ? fineRadialSegments : target == 1 ? fastRadialSegments : superRadialSegments,
                    target == 0 ? fineRings : target == 1 ? fastRings : superRings,
                    showGrid,
                    target == 0 ? fineGridColor : target == 1 ? fastGridColor : superGridColor);
                if (target != 2) rebuilt++;
            }
        }
        else
        {
            foreach (var island in lodCandidates)
                if (island != null && !useTerrainChunkLod) island.ConfigureLod(0, radialSegments, rings, showGrid, fineGridColor);
        }
    }
    private static float SurfaceDistance(FloatingIslandMesh island, Vector3 pos)
    {
        Vector3 center = island.transform.position;
        float dx = (pos.x-center.x)/Mathf.Max(1f,island.radiusX);
        float dz = (pos.z-center.z)/Mathf.Max(1f,island.radiusZ);
        float horizontal = Mathf.Max(0f, Mathf.Sqrt(dx*dx+dz*dz)-1f) * Mathf.Min(island.radiusX,island.radiusZ);
        float top = center.y + island.topVariation + 2f;
        float bottom = center.y - island.thickness - island.bottomVariation;
        float vertical = pos.y > top ? pos.y-top : pos.y < bottom ? bottom-pos.y : 0f;
        return Mathf.Sqrt(horizontal*horizontal+vertical*vertical);
    }
    private static float HorizontalDistanceSqr(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return dx * dx + dz * dz;
    }
    private static uint Hash(int x, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)seed ^ 0x9E3779B9u;
            h ^= (uint)x * 0x85EBCA6Bu;
            h = (h ^ (h >> 16)) * 0xC2B2AE35u;
            h ^= (uint)z * 0x27D4EB2Fu;
            h ^= h >> 15;
            return h == 0 ? 1u : h;
        }
    }
    private static float Next01(ref uint state)
    {
        state ^= state << 13; state ^= state >> 17; state ^= state << 5;
        return (state & 0x00FFFFFFu) / 16777216f;
    }
    private void OnDestroy()
    {
        active.Clear();
        if (runtimeIslandMaterial != null) Destroy(runtimeIslandMaterial);
    }
}
