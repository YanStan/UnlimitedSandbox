using System.Collections.Generic;
using UnityEngine;

/// <summary>Closed, procedural island shell. Independent from TerrainGenerator.
/// Later density edits should replace clipped regions, not overlap this shell.</summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class FloatingIslandMesh : MonoBehaviour
{
    [Header("Shape (metres)")]
    [Min(2f)] public float radiusX = 100f;
    [Min(2f)] public float radiusZ = 100f;
    [Min(1f)] public float thickness = 45f;
    [Range(4, 128)] public int radialSegments = 48;
    [Range(1, 64)] public int rings = 16;
    [Header("Surface")]
    [Min(0.0001f)] public float noiseScale = 0.025f;
    [Min(0f)] public float topVariation = 6f;
    [Min(0f)] public float bottomVariation = 12f;
    public int seed = 12345;
    [Header("Physics")]
    public bool createCollider = true;
    public bool generateOnStart = true;

    [Header("Material")]
    public Material islandMaterial;
    public bool useTerrainMaterial = true;
    private Material generatedMaterial;
    private Mesh ownedMesh;
    private Mesh gridMesh;
    private GameObject gridObject;
    private Material gridMaterial;
    [Header("LOD (controlled by streamer)")]
    [HideInInspector] public int currentLod = -1;
    [HideInInspector] public bool showLodGrid;
    [HideInInspector] public Color lodGridColor = Color.green;

    private void Start()
    {
        EnsureMaterial();
        if (generateOnStart) Generate();
    }

    public void EnsureMaterial()
    {
        if (GetComponent<MeshRenderer>().sharedMaterial != null) return;
        ApplyMaterial();
    }

    private void ApplyMaterial()
    {
        var renderer = GetComponent<MeshRenderer>();
        if (islandMaterial != null) { renderer.sharedMaterial = islandMaterial; return; }
        if (useTerrainMaterial)
        {
            var terrains = FindObjectsByType<TerrainGenerator>(FindObjectsSortMode.None);
            foreach (var terrain in terrains)
            {
                var terrainRenderer = terrain.GetComponent<MeshRenderer>();
                if (terrainRenderer != null && terrainRenderer.sharedMaterial != null &&
                    terrainRenderer.sharedMaterial.shader != null &&
                    terrainRenderer.sharedMaterial.shader.name == "Unlimited/TriplanarGrass")
                {
                    renderer.sharedMaterial = terrainRenderer.sharedMaterial;
                    return;
                }
            }
        }
        Shader shader = Shader.Find("Unlimited/TriplanarGrass");
        if (shader == null)
        {
            Debug.LogError("FloatingIslandMesh: Unlimited/TriplanarGrass shader not found. Add shader to Always Included Shaders or assign a material.", this);
            return;
        }
        generatedMaterial = new Material(shader) { name = "FloatingIsland_TriplanarGrass" };
        renderer.sharedMaterial = generatedMaterial;
    }
    private void OnDestroy()
    {
        if (ownedMesh != null) Destroy(ownedMesh);
        if (gridMesh != null) Destroy(gridMesh);
        if (gridMaterial != null) Destroy(gridMaterial);
        if (generatedMaterial != null) Destroy(generatedMaterial);
    }
    private void OnValidate()
    {
        radiusX = Mathf.Max(2f, radiusX);
        radiusZ = Mathf.Max(2f, radiusZ);
        thickness = Mathf.Max(1f, thickness);
    }

    public void ConfigureLod(int lod, int segments, int ringCount, bool grid, Color color)
    {
        showLodGrid = grid;
        lodGridColor = color;
        if (currentLod == lod && ownedMesh != null)
        {
            if (gridObject != null) gridObject.SetActive(grid);
            if (gridMaterial != null) gridMaterial.color = color;
            return;
        }
        currentLod = lod;
        radialSegments = Mathf.Max(8, segments);
        rings = Mathf.Max(1, ringCount);
        Generate();
    }

    [ContextMenu("Generate Island Mesh")]
    public void Generate()
    {
        int n = Mathf.Max(4, radialSegments);
        int rCount = Mathf.Max(1, rings);
        var vertices = new List<Vector3>(2 * (1 + rCount * n));
        var uvs = new List<Vector2>(vertices.Capacity);
        var tris = new List<int>(rCount * n * 12);
        // Keep Perlin coordinates small: 32-bit seeds multiplied directly by a float
        // can reach tens of millions, losing sub-metre coordinate precision.
        float offset = 10f + (unchecked((uint)seed) % 65521u) * 0.013f;
        int sideCount = 1 + rCount * n;

        // Two matching surfaces. The outer boundary is shared geometrically,
        // but vertices are duplicated to give top and underside separate normals.
        for (int surface = 0; surface < 2; surface++)
        {
            bool top = surface == 0;
            for (int ring = 0; ring <= rCount; ring++)
            {
                int count = ring == 0 ? 1 : n;
                float t = (float)ring / rCount;
                for (int s = 0; s < count; s++)
                {
                    float a = 2f * Mathf.PI * s / n;
                    float x = Mathf.Cos(a) * radiusX * t;
                    float z = Mathf.Sin(a) * radiusZ * t;
                    float hNoise = Mathf.PerlinNoise(x * noiseScale + offset, z * noiseScale + offset);
                    float rim = Mathf.SmoothStep(0f, 1f, t);
                    float y;
                    if (top)
                        y = topVariation * (hNoise - 0.5f) * 2f + 2f * (1f - rim);
                    else
                        y = -thickness * (1f - 0.85f * rim) + bottomVariation * (hNoise - 0.5f) * (1f - rim);
                    vertices.Add(new Vector3(x, y, z));
                    uvs.Add(new Vector2((x / radiusX + 1f) * 0.5f, (z / radiusZ + 1f) * 0.5f));
                }
            }
        }

        for (int surface = 0; surface < 2; surface++)
        {
            int baseIndex = surface * sideCount;
            bool top = surface == 0;
            for (int s = 0; s < n; s++)
                AddTriangle(tris, baseIndex, baseIndex + 1 + s, baseIndex + 1 + (s + 1) % n, top);
            for (int ring = 2; ring <= rCount; ring++)
            {
                int inner = baseIndex + 1 + (ring - 2) * n;
                int outer = baseIndex + 1 + (ring - 1) * n;
                for (int s = 0; s < n; s++)
                {
                    int next = (s + 1) % n;
                    AddTriangle(tris, inner + s, outer + s, inner + next, top);
                    AddTriangle(tris, outer + s, outer + next, inner + next, top);
                }
            }
        }

        // Seal the perimeter. A closed shell needs these side faces.
        int outerTop = 1 + (rCount - 1) * n;
        int outerBottom = sideCount + outerTop;
        for (int s = 0; s < n; s++)
        {
            int next = (s + 1) % n;
            // perimeter faces oriented outwards
            tris.Add(outerTop + s); tris.Add(outerTop + next); tris.Add(outerBottom + s);
            tris.Add(outerTop + next); tris.Add(outerBottom + next); tris.Add(outerBottom + s);
        }

        var mesh = new Mesh { name = "FloatingIsland_Closed" };
        if (vertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        var filter = GetComponent<MeshFilter>();
        var old = ownedMesh;
        ownedMesh = mesh;
        filter.sharedMesh = mesh;
        if (createCollider)
        {
            var collider = GetComponent<MeshCollider>();
            if (collider == null) collider = gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = null;
            collider.sharedMesh = mesh;
        }
        else
        {
            var collider = GetComponent<MeshCollider>();
            if (collider != null) collider.sharedMesh = null;
        }
        UpdateGrid(mesh);
        if (old != null)
        {
            if (Application.isPlaying) Destroy(old);
            else DestroyImmediate(old);
        }
    }

    private void UpdateGrid(Mesh source)
    {
        if (!showLodGrid)
        {
            if (gridObject != null) gridObject.SetActive(false);
            return;
        }
        if (gridObject == null)
        {
            gridObject = new GameObject("LOD_Grid");
            gridObject.transform.SetParent(transform, false);
            gridObject.AddComponent<MeshFilter>();
            gridObject.AddComponent<MeshRenderer>();
        }
        gridObject.SetActive(true);
        if (gridMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) { Debug.LogWarning("LOD grid shader unavailable", this); return; }
            gridMaterial = new Material(shader) { name = "Island_LOD_Grid" };
        }
        gridMaterial.color = lodGridColor;
        gridObject.GetComponent<MeshRenderer>().sharedMaterial = gridMaterial;
        if (gridMesh != null) Destroy(gridMesh);
        var verts = source.vertices;
        var normals = source.normals;
        var raised = new Vector3[verts.Length];
        for (int i = 0; i < verts.Length; i++) raised[i] = verts[i] + normals[i] * 0.14f;
        var indices = source.triangles;
        var edges = new HashSet<ulong>();
        var lines = new List<int>();
        for (int i = 0; i < indices.Length; i += 3)
        {
            AddEdge(indices[i], indices[i+1], edges, lines);
            AddEdge(indices[i+1], indices[i+2], edges, lines);
            AddEdge(indices[i+2], indices[i], edges, lines);
        }
        gridMesh = new Mesh { name = "Island_LOD_Grid" };
        if (raised.Length > 65535) gridMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        gridMesh.vertices = raised;
        gridMesh.SetIndices(lines.ToArray(), MeshTopology.Lines, 0);
        gridMesh.RecalculateBounds();
        gridObject.GetComponent<MeshFilter>().sharedMesh = gridMesh;
    }
    private static void AddEdge(int a, int b, HashSet<ulong> edges, List<int> lines)
    {
        uint lo = (uint)Mathf.Min(a,b), hi = (uint)Mathf.Max(a,b);
        ulong key = ((ulong)lo << 32) | hi;
        if (edges.Add(key)) { lines.Add(a); lines.Add(b); }
    }

    private static void AddTriangle(List<int> triangles, int a, int b, int c, bool top)
    {
        if (!top) { triangles.Add(a); triangles.Add(b); triangles.Add(c); }
        else { triangles.Add(a); triangles.Add(c); triangles.Add(b); }
    }
}
