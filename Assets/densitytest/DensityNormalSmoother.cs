
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Unlimited.DensityTest
{
    [RequireComponent(typeof(DensityChunkTest))]
    public sealed class DensityNormalSmoother : MonoBehaviour
    {
        [Header("Normal Smoothing")]

        [Min(0.000001f)]
        public float positionPrecision = 0.0001f;

        public bool smoothOnGenerationComplete = true;

        private DensityChunkTest chunkGenerator;

        public void SmoothAtStartup()
        {
            if (isActiveAndEnabled && smoothOnGenerationComplete)
                SmoothNormals();
        }

        [ContextMenu("Smooth Normals")]
        public void SmoothNormals()
        {
            var filters = GetComponentsInChildren<MeshFilter>();
            SmoothFilters(filters, filters);
        }

        public void SmoothLocalNormals(List<Vector3Int> sections)
        {
            if (chunkGenerator == null) chunkGenerator = GetComponent<DensityChunkTest>();
            var targets = CollectFilters(sections, 1);
            var sources = CollectFilters(sections, 2);
            SmoothFilters(sources, targets);
        }

        private HashSet<MeshFilter> CollectFilters(List<Vector3Int> sections, int radius)
        {
            var filters = new HashSet<MeshFilter>();
            foreach (var section in sections)
                for (int x = -radius; x <= radius; x++)
                for (int y = -radius; y <= radius; y++)
                for (int z = -radius; z <= radius; z++)
                    if (chunkGenerator.TryGetSection(section + new Vector3Int(x,y,z), out var obj))
                    {
                        var filter = obj.GetComponent<MeshFilter>();
                        if (filter != null) filters.Add(filter);
                    }
            return filters;
        }

        private void SmoothFilters(IEnumerable<MeshFilter> meshFilters, IEnumerable<MeshFilter> targets)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();

            var normalSums =
                new Dictionary<Vector3Int, Vector3>();

            var positionCounts =
                new Dictionary<Vector3Int, int>();

            int meshCount = 0;
            int totalVertices = 0;
            int sharedVertices = 0;

            // PASS 1:
            // Accumulate area-weighted triangle normals.

            foreach (MeshFilter filter in meshFilters)
            {
                Mesh mesh = filter.sharedMesh;

                if (mesh == null)
                    continue;

                Vector3[] vertices = mesh.vertices;
                int[] triangles = mesh.triangles;

                var worldVertices =
                    new Vector3[vertices.Length];

                var keys =
                    new Vector3Int[vertices.Length];

                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 worldPosition =
                        filter.transform.TransformPoint(
                            vertices[i]
                        );

                    worldVertices[i] = worldPosition;

                    Vector3Int key =
                        QuantizePosition(worldPosition);

                    keys[i] = key;

                    if (positionCounts.TryGetValue(
                        key,
                        out int count))
                    {
                        positionCounts[key] = count + 1;
                    }
                    else
                    {
                        positionCounts.Add(key, 1);
                    }
                }

                for (int i = 0;
                     i < triangles.Length;
                     i += 3)
                {
                    int i0 = triangles[i];
                    int i1 = triangles[i + 1];
                    int i2 = triangles[i + 2];

                    Vector3 p0 = worldVertices[i0];
                    Vector3 p1 = worldVertices[i1];
                    Vector3 p2 = worldVertices[i2];

                    Vector3 normal = Vector3.Cross(
                        p1 - p0,
                        p2 - p0
                    );

                    if (normal.sqrMagnitude < 1e-12f)
                        continue;

                    AddNormal(
                        normalSums,
                        keys[i0],
                        normal
                    );

                    AddNormal(
                        normalSums,
                        keys[i1],
                        normal
                    );

                    AddNormal(
                        normalSums,
                        keys[i2],
                        normal
                    );
                }

                meshCount++;
                totalVertices += vertices.Length;
            }

            // PASS 2:
            // Apply matching normals to shared vertices.

            foreach (MeshFilter filter in targets)
            {
                Mesh mesh = filter.sharedMesh;

                if (mesh == null)
                    continue;

                Vector3[] vertices = mesh.vertices;

                Vector3[] normals = mesh.normals;

                if (normals.Length != vertices.Length)
                {
                    normals =
                        new Vector3[vertices.Length];
                }

                Matrix4x4 worldToLocalNormal =
                    filter.transform.localToWorldMatrix
                        .transpose;

                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 worldPosition =
                        filter.transform.TransformPoint(
                            vertices[i]
                        );

                    Vector3Int key =
                        QuantizePosition(worldPosition);

                    if (normalSums.TryGetValue(
                        key,
                        out Vector3 normalSum))
                    {
                        if (normalSum.sqrMagnitude > 1e-12f)
                        {
                            Vector3 worldNormal =
                                normalSum.normalized;

                            Vector3 localNormal =
                                worldToLocalNormal
                                    .MultiplyVector(
                                        worldNormal
                                    )
                                    .normalized;

                            normals[i] = localNormal;
                        }
                    }

                    if (positionCounts.TryGetValue(
                        key,
                        out int count) && count > 1)
                    {
                        sharedVertices++;
                    }
                }

                mesh.normals = normals;
            }

            timer.Stop();

            Debug.Log(
                "[DensityNormals] Smoothing completed\n" +

                $"Meshes: {meshCount}\n" +

                $"Total vertices: {totalVertices}\n" +

                $"Unique positions: {positionCounts.Count}\n" +

                $"Shared vertices processed: {sharedVertices}\n" +

                $"Processing time: {timer.Elapsed.TotalMilliseconds:F2} ms"
            );
        }

        private Vector3Int QuantizePosition(
            Vector3 position)
        {
            float precision = Mathf.Max(
                positionPrecision,
                0.000001f
            );

            return new Vector3Int(
                Mathf.RoundToInt(position.x / precision),
                Mathf.RoundToInt(position.y / precision),
                Mathf.RoundToInt(position.z / precision)
            );
        }

        private static void AddNormal(
            Dictionary<Vector3Int, Vector3> normalSums,
            Vector3Int key,
            Vector3 normal)
        {
            if (normalSums.TryGetValue(
                key,
                out Vector3 existing))
            {
                normalSums[key] = existing + normal;
            }
            else
            {
                normalSums.Add(key, normal);
            }
        }
    }
}
