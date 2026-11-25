
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Unlimited.DensityTest
{
    [RequireComponent(typeof(DensityWorldController))]
    public sealed class DensityChunkTest : MonoBehaviour
    {
        [Header("Rendering")]

        public Material terrainMaterial;

        [Header("Generation")]

        [Min(1)]
        public int chunksPerFrame = 2;

        public bool useAdaptiveFrameBudget = true;
        public bool generateImmediatelyOnStartup = true;
        [Min(1f)] public float generationBudgetMs = 16f;

        [Header("Debug")]

        public bool logRebuilds = true;

        private DensityWorldConfig config;

        private DensitySurfaceField field;

        private DensityMeshPipeline pipeline;

        private DensityChunkColliders chunkColliders;

        private Material runtimeMaterial;

        private readonly Dictionary<Vector3Int, GameObject>
            chunkObjects =
                new Dictionary<Vector3Int, GameObject>();

        private int totalVertices;

        private int totalTriangles;

        public bool IsGenerationComplete
        {
            get;
            private set;
        }

        public int VisibleSectionCount =>
            chunkObjects.Count;

        private IEnumerator Start()
        {
            IsGenerationComplete = false;

            var controller =
                GetComponent<DensityWorldController>();

            config =
                GetComponent<DensityWorldConfig>();

            chunkColliders =
                GetComponent<DensityChunkColliders>();

            if (controller.SurfaceField == null)
                controller.GenerateWorld();

            field = controller.SurfaceField;

            var nativeTimer = System.Diagnostics.Stopwatch.StartNew();
            pipeline =
                new DensityMeshPipeline(field);
            Debug.Log($"[DensityChunks] Native data initialization: {nativeTimer.Elapsed.TotalMilliseconds:F2} ms");

            CreateMaterial();

            Debug.Log("[DensityStartup] Immediate mesh generation started");
            yield return GenerateWorldChunks();
            var smoother = GetComponent<DensityNormalSmoother>();
            if (smoother != null) smoother.SmoothAtStartup();
            Debug.Log("[DensityStartup] Meshes and normals ready");

            IsGenerationComplete = true;
        }

        private void CreateMaterial()
        {
            if (terrainMaterial != null)
                return;

            Shader shader = Shader.Find(
                "Universal Render Pipeline/Lit"
            );

            if (shader == null)
                return;

            runtimeMaterial = new Material(shader);

            runtimeMaterial.color =
                new Color(0.35f, 0.55f, 0.2f);
        }

        private IEnumerator GenerateWorldChunks()
        {
            var timer =
                System.Diagnostics.Stopwatch.StartNew();

            int size = config.chunkSize;

            int height = config.sectionHeight;

            int countX = Mathf.CeilToInt(
                field.W / (float)size
            );

            int countZ = Mathf.CeilToInt(
                field.D / (float)size
            );

            int countY = Mathf.CeilToInt(
                field.H / (float)height
            );

            int generatedThisFrame = 0;
            int yieldedFrames = 0;
            var frameTimer = System.Diagnostics.Stopwatch.StartNew();

            for (int cx = 0; cx < countX; cx++)
            {
                for (int cz = 0; cz < countZ; cz++)
                {
                    GetSurfaceRange(
                        cx,
                        cz,
                        out float minY,
                        out float maxY
                    );

                    if (float.IsInfinity(minY) ||
                        float.IsInfinity(maxY))
                    {
                        continue;
                    }

                    int firstY = Mathf.Clamp(
                        Mathf.FloorToInt(
                            (minY - 1f) / height
                        ),
                        0,
                        countY - 1
                    );

                    int lastY = Mathf.Clamp(
                        Mathf.FloorToInt(
                            (maxY + 1f) / height
                        ),
                        0,
                        countY - 1
                    );

                    for (int cy = firstY;
                         cy <= lastY;
                         cy++)
                    {
                        GenerateOrReplaceSection(
                            new Vector3Int(cx, cy, cz),
                            false
                        );

                        generatedThisFrame++;


                    }
                }
            }

            timer.Stop();

            Debug.Log(
                "[DensityChunks] World completed\n" +

                $"World: {field.W} x {field.D}\n" +

                $"Horizontal chunks: {countX * countZ}\n" +

                $"Visible sections: {chunkObjects.Count}\n" +

                $"Vertices: {totalVertices}\n" +

                $"Triangles: {totalTriangles}\n" +

                $"Yielded frames: {yieldedFrames}\n" +
                $"Generation time: {timer.Elapsed.TotalMilliseconds:F2} ms"
            );

            pipeline.PrintStatistics();
            yield break;
        }

        private void GetSurfaceRange(
            int chunkX,
            int chunkZ,
            out float minY,
            out float maxY)
        {
            minY = float.PositiveInfinity;

            maxY = float.NegativeInfinity;

            int size = config.chunkSize;

            int startX = chunkX * size;

            int startZ = chunkZ * size;

            int endX = startX + size;

            int endZ = startZ + size;

            for (int x = startX - 1;
                 x <= endX;
                 x++)
            {
                for (int z = startZ - 1;
                     z <= endZ;
                     z++)
                {
                    if (!field.InBounds(x, z))
                        continue;

                    var column =
                        field.surfaceCache[
                            field.IDX(x, z)
                        ];

                    if (column == null)
                        continue;

                    foreach (SurfacePoint point
                             in column)
                    {
                        if (point.kind != SurfaceKind.Top)
                            continue;

                        minY = Mathf.Min(
                            minY,
                            point.y
                        );

                        maxY = Mathf.Max(
                            maxY,
                            point.y
                        );
                    }
                }
            }
        }

        public bool IsValidSection(
            Vector3Int section)
        {
            if (field == null || config == null)
                return false;

            if (section.x < 0 ||
                section.y < 0 ||
                section.z < 0)
            {
                return false;
            }

            return
                section.x * config.chunkSize < field.W &&

                section.y * config.sectionHeight < field.H &&

                section.z * config.chunkSize < field.D;
        }

        public bool TryGetSection(
            Vector3Int section,
            out GameObject chunkObject)
        {
            return chunkObjects.TryGetValue(
                section,
                out chunkObject
            );
        }

        private Mesh BuildSectionMesh(
            Vector3Int section)
        {
            int size = config.chunkSize;

            int height = config.sectionHeight;

            int startX = section.x * size;

            int startY = section.y * height;

            int startZ = section.z * size;

            int endX = Mathf.Min(
                startX + size,
                field.W
            );

            int endY = Mathf.Min(
                startY + height,
                field.H
            );

            int endZ = Mathf.Min(
                startZ + size,
                field.D
            );

            return pipeline.BuildChunkMesh(
                startX,
                startY,
                startZ,
                endX,
                endY,
                endZ
            );
        }

        private GameObject CreateSectionObject(
            Vector3Int section)
        {
            var chunkObject = new GameObject(
                $"DensityChunk_{section.x}_{section.y}_{section.z}"
            );

            chunkObject.transform.SetParent(
                transform,
                false
            );

            chunkObject.AddComponent<MeshFilter>();

            var renderer =
                chunkObject.AddComponent<MeshRenderer>();

            renderer.sharedMaterial =
                terrainMaterial != null
                    ? terrainMaterial
                    : runtimeMaterial;

            chunkObjects.Add(
                section,
                chunkObject
            );

            return chunkObject;
        }

        private void GenerateOrReplaceSection(
            Vector3Int section,
            bool updateCollider)
        {
            if (!IsValidSection(section))
                return;

            Mesh newMesh =
                BuildSectionMesh(section);

            if (newMesh == null)
            {
                RemoveSection(section);

                return;
            }

            newMesh.name =
                $"DensityMesh_{section.x}_{section.y}_{section.z}";

            if (!chunkObjects.TryGetValue(
                section,
                out GameObject chunkObject))
            {
                chunkObject =
                    CreateSectionObject(section);
            }

            MeshFilter filter =
                chunkObject.GetComponent<MeshFilter>();

            Mesh oldMesh =
                filter.sharedMesh;

            if (oldMesh != null)
            {
                totalVertices -=
                    oldMesh.vertexCount;

                totalTriangles -=
                    (int)(oldMesh.GetIndexCount(0) / 3);
            }

            filter.sharedMesh = newMesh;

            totalVertices +=
                newMesh.vertexCount;

            totalTriangles +=
                (int)(newMesh.GetIndexCount(0) / 3);

            if (updateCollider &&
                chunkColliders != null &&
                chunkColliders.generateColliders)
            {
                chunkColliders.UpdateChunkCollider(
                    filter
                );
            }

            if (oldMesh != null)
            {
                Destroy(oldMesh);
            }
        }

        private void RemoveSection(
            Vector3Int section)
        {
            if (!chunkObjects.TryGetValue(
                section,
                out GameObject chunkObject))
            {
                return;
            }

            chunkObjects.Remove(section);

            if (chunkObject == null)
                return;

            MeshFilter filter =
                chunkObject.GetComponent<MeshFilter>();

            MeshCollider collider =
                chunkObject.GetComponent<MeshCollider>();

            if (collider != null)
            {
                collider.sharedMesh = null;

                collider.enabled = false;
            }

            if (filter != null &&
                filter.sharedMesh != null)
            {
                Mesh mesh = filter.sharedMesh;

                totalVertices -=
                    mesh.vertexCount;

                totalTriangles -=
                    (int)(mesh.GetIndexCount(0) / 3);

                filter.sharedMesh = null;

                Destroy(mesh);
            }

            chunkObject.SetActive(false);

            Destroy(chunkObject);
        }

        public void RebuildSections(
            List<Vector3Int> sections, List<DensityTouchedColumn> touched = null)
        {
            if (!IsGenerationComplete)
                return;

            if (pipeline == null)
                return;

            if (sections == null ||
                sections.Count == 0)
            {
                return;
            }

            var totalTimer =
                System.Diagnostics.Stopwatch.StartNew();

            var nativeTimer =
                System.Diagnostics.Stopwatch.StartNew();

            pipeline.RefreshNativeData(touched);

            nativeTimer.Stop();

            pipeline.ResetStatistics();

            int rebuilt = 0;

            int created = 0;

            int removed = 0;

            int empty = 0;

            var uniqueSections =
                new HashSet<Vector3Int>(sections);

            foreach (Vector3Int section
                     in uniqueSections)
            {
                if (!IsValidSection(section))
                    continue;

                bool existedBefore =
                    chunkObjects.ContainsKey(section);

                GenerateOrReplaceSection(
                    section,
                    true
                );

                bool existsAfter =
                    chunkObjects.ContainsKey(section);

                if (existedBefore && existsAfter)
                {
                    rebuilt++;
                }
                else if (!existedBefore && existsAfter)
                {
                    created++;
                }
                else if (existedBefore && !existsAfter)
                {
                    removed++;
                }
                else
                {
                    empty++;
                }
            }

            Physics.SyncTransforms();

            totalTimer.Stop();

            if (logRebuilds)
            {
                Debug.Log(
                    "[DensityChunks] Local rebuild completed\n" +

                    $"Requested sections: {uniqueSections.Count}\n" +

                    $"Rebuilt sections: {rebuilt}\n" +

                    $"Created sections: {created}\n" +

                    $"Removed sections: {removed}\n" +

                    $"Empty sections: {empty}\n" +

                    $"Visible sections: {chunkObjects.Count}\n" +

                    $"Total vertices: {totalVertices}\n" +

                    $"Total triangles: {totalTriangles}\n" +

                    $"Native refresh: {nativeTimer.Elapsed.TotalMilliseconds:F2} ms\n" +

                    $"Total rebuild time: {totalTimer.Elapsed.TotalMilliseconds:F2} ms"
                );

                pipeline.PrintStatistics();
            }
        }

        private void OnDestroy()
        {
            pipeline?.Dispose();

            pipeline = null;

            foreach (var pair in chunkObjects)
            {
                GameObject chunk = pair.Value;

                if (chunk == null)
                    continue;

                MeshCollider collider =
                    chunk.GetComponent<MeshCollider>();

                if (collider != null)
                    collider.sharedMesh = null;

                MeshFilter filter =
                    chunk.GetComponent<MeshFilter>();

                if (filter != null &&
                    filter.sharedMesh != null)
                {
                    Destroy(filter.sharedMesh);

                    filter.sharedMesh = null;
                }

                Destroy(chunk);
            }

            chunkObjects.Clear();

            if (runtimeMaterial != null)
                Destroy(runtimeMaterial);
        }
    }
}
