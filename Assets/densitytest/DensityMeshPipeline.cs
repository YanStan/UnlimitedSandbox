
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unlimited.DensityTest
{
    public sealed class DensityMeshPipeline : IDisposable
    {
        private readonly DensitySurfaceField field;

        private DensityNativeData nativeData;

        public int TotalSections { get; private set; }
        public int DualSections { get; private set; }
        public double SharpMaskMilliseconds { get; private set; }

        public long TotalDensitySamples { get; private set; }
        public long TotalCells { get; private set; }

        public long TotalVertices { get; private set; }
        public long TotalTriangles { get; private set; }

        public double TotalJobsMilliseconds
        {
            get;
            private set;
        }

        public double TotalAssemblyMilliseconds
        {
            get;
            private set;
        }

        public DensityMeshPipeline(
            DensitySurfaceField field)
        {
            this.field = field;

            nativeData = new DensityNativeData(
                field,
                Allocator.Persistent
            );
        }

        public Mesh BuildChunkMesh(
            int startX,
            int startY,
            int startZ,
            int endX,
            int endY,
            int endZ)
        {
            int3 faceOrigin = new int3(
                startX,
                startY,
                startZ
            );

            int3 faceSize = new int3(
                endX - startX,
                endY - startY,
                endZ - startZ
            );

            if (math.any(faceSize <= 0))
                return null;

            // Include one additional layer of cells
            // for shared chunk boundaries.

            int3 cellOrigin = faceOrigin - 1;
            int3 cellSize = faceSize + 1;

            // Each cell requires its eight corners.

            int3 densityOrigin = cellOrigin - 1;
            int3 densitySize = cellSize + 3;

            int densityCount = checked(
                densitySize.x *
                densitySize.y *
                densitySize.z
            );

            int cellCount = checked(
                cellSize.x *
                cellSize.y *
                cellSize.z
            );

            int faceCount = checked(
                faceSize.x *
                faceSize.y *
                faceSize.z
            );

            NativeArray<float> densities = default;
            NativeArray<byte> sharpMask = default;

            NativeArray<DensityCellVertex>
                cellVertices = default;

            NativeArray<DensityCellFaces>
                faces = default;

            NativeArray<int> vertexRemap = default;

            JobHandle lastHandle = default;

            try
            {
                densities = new NativeArray<float>(
                    densityCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory
                );

                cellVertices =
                    new NativeArray<DensityCellVertex>(
                        cellCount,
                        Allocator.TempJob,
                        NativeArrayOptions.UninitializedMemory
                    );

                faces =
                    new NativeArray<DensityCellFaces>(
                        faceCount,
                        Allocator.TempJob,
                        NativeArrayOptions.UninitializedMemory
                    );

                vertexRemap = new NativeArray<int>(
                    cellCount,
                    Allocator.TempJob,
                    NativeArrayOptions.UninitializedMemory
                );

                var timer =
                    System.Diagnostics.Stopwatch.StartNew();

                // JOB 1: Calculate density.

                var samplingJob = new DensitySamplingJob
                {
                    Columns = nativeData.Columns,
                    Points = nativeData.Points,

                    Densities = densities,

                    WorldWidth = field.W,
                    WorldDepth = field.D,
                    WorldHeight = field.H,

                    GridOrigin = densityOrigin,
                    GridSize = densitySize
                };

                lastHandle = samplingJob.Schedule(
                    densityCount,
                    64
                );

                // JOB 2: Surface Nets for all cells; replace only marked sharp cells.
                bool hasSharp = false;
                var maskTimer = System.Diagnostics.Stopwatch.StartNew();
                if (field.HasSharpCells)
                {
                    sharpMask = new NativeArray<byte>(cellCount, Allocator.TempJob);
                    for (int i = 0; i < cellCount; i++)
                    {
                        int3 c = cellOrigin + new int3(i / (cellSize.y * cellSize.z), i / cellSize.z % cellSize.y, i % cellSize.z);
                        bool sharp = field.IsSharpCell(c.x, c.y, c.z);
                        sharpMask[i] = (byte)(sharp ? 1 : 0);
                        hasSharp |= sharp;
                    }
                }
                SharpMaskMilliseconds += maskTimer.Elapsed.TotalMilliseconds;
                var smoothJob = new DensitySurfaceNetsJob
                {
                    Densities = densities, CellVertices = cellVertices,
                    DensityGridOrigin = densityOrigin, DensityGridSize = densitySize,
                    CellGridOrigin = cellOrigin, CellGridSize = cellSize,
                    WorldWidth = field.W, WorldDepth = field.D
                };
                lastHandle = smoothJob.Schedule(cellCount, 64, lastHandle);
                if (hasSharp)
                {
                    DualSections++;
                    var dualJob = new DualContouringPipelineJob
                    {
                        SharpMask = sharpMask,
                        Densities = densities,
                        CellVertices = cellVertices,
                        DensityGridOrigin = densityOrigin,
                        DensityGridSize = densitySize,
                        CellGridOrigin = cellOrigin,
                        CellGridSize = cellSize,
                        WorldWidth = field.W,
                        WorldDepth = field.D
                    };
                    lastHandle = dualJob.Schedule(cellCount, 64, lastHandle);
                }

                // JOB 3: Generate faces.

                var facesJob = new DensityFacesJob
                {
                    Densities = densities,

                    CellVertices = cellVertices,

                    Faces = faces,

                    DensityGridOrigin = densityOrigin,
                    DensityGridSize = densitySize,

                    CellGridOrigin = cellOrigin,
                    CellGridSize = cellSize,

                    FaceGridOrigin = faceOrigin,
                    FaceGridSize = faceSize
                };

                lastHandle = facesJob.Schedule(
                    faceCount,
                    64,
                    lastHandle
                );

                // Wait for the entire dependency chain.

                lastHandle.Complete();

                timer.Stop();

                TotalJobsMilliseconds +=
                    timer.Elapsed.TotalMilliseconds;

                TotalSections++;
                TotalDensitySamples += densityCount;
                TotalCells += cellCount;

                // Assemble the final Unity mesh.

                timer.Restart();

                Mesh mesh = AssembleMesh(
                    cellVertices,
                    faces,
                    vertexRemap
                );

                timer.Stop();

                TotalAssemblyMilliseconds +=
                    timer.Elapsed.TotalMilliseconds;

                return mesh;
            }
            finally
            {
                // Ensure no job is using native memory
                // before releasing it.

                lastHandle.Complete();

                if (sharpMask.IsCreated) sharpMask.Dispose();

                if (vertexRemap.IsCreated)
                    vertexRemap.Dispose();

                if (faces.IsCreated)
                    faces.Dispose();

                if (cellVertices.IsCreated)
                    cellVertices.Dispose();

                if (densities.IsCreated)
                    densities.Dispose();
            }
        }

        private Mesh AssembleMesh(
            NativeArray<DensityCellVertex> cellVertices,
            NativeArray<DensityCellFaces> faces,
            NativeArray<int> vertexRemap)
        {
            var vertices = new List<Vector3>();

            var triangles = new List<int>();

            var uvs = new List<Vector2>();

            // Convert sparse cell indices into
            // compact mesh vertex indices.

            for (int i = 0;
                 i < cellVertices.Length;
                 i++)
            {
                DensityCellVertex cell =
                    cellVertices[i];

                if (cell.active == 0)
                {
                    vertexRemap[i] = -1;
                    continue;
                }

                vertexRemap[i] = vertices.Count;

                Vector3 position = new Vector3(
                    cell.position.x,
                    cell.position.y,
                    cell.position.z
                );

                vertices.Add(position);

                uvs.Add(
                    new Vector2(
                        position.x * 0.2f,
                        position.z * 0.2f
                    )
                );
            }

            // Convert quads into triangles.

            for (int i = 0; i < faces.Length; i++)
            {
                DensityCellFaces cellFaces =
                    faces[i];

                AppendQuad(
                    cellFaces.xFace,
                    vertexRemap,
                    triangles
                );

                AppendQuad(
                    cellFaces.yFace,
                    vertexRemap,
                    triangles
                );

                AppendQuad(
                    cellFaces.zFace,
                    vertexRemap,
                    triangles
                );
            }

            if (triangles.Count == 0)
                return null;

            var mesh = new Mesh();

            mesh.indexFormat = IndexFormat.UInt32;

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            TotalVertices += vertices.Count;
            TotalTriangles += triangles.Count / 3;

            return mesh;
        }

        private static void AppendQuad(
            DensityQuad quad,
            NativeArray<int> vertexRemap,
            List<int> triangles)
        {
            if (quad.active == 0)
                return;

            int i0 = vertexRemap[quad.vertices.x];
            int i1 = vertexRemap[quad.vertices.y];
            int i2 = vertexRemap[quad.vertices.z];
            int i3 = vertexRemap[quad.vertices.w];

            if (i0 < 0 ||
                i1 < 0 ||
                i2 < 0 ||
                i3 < 0)
            {
                return;
            }

            if (quad.reversed == 0)
            {
                triangles.Add(i0);
                triangles.Add(i1);
                triangles.Add(i2);

                triangles.Add(i0);
                triangles.Add(i2);
                triangles.Add(i3);
            }
            else
            {
                triangles.Add(i0);
                triangles.Add(i2);
                triangles.Add(i1);

                triangles.Add(i0);
                triangles.Add(i3);
                triangles.Add(i2);
            }
        }

        public void RefreshNativeData(List<DensityTouchedColumn> touched = null)
        {
            if (touched != null) { nativeData.RefreshColumns(field, touched); return; }
            var replacement = new DensityNativeData(
                field,
                Allocator.Persistent
            );

            nativeData?.Dispose();

            nativeData = replacement;
        }

        public void PrintStatistics()
        {
            Debug.Log(
                "[DensityMeshPipeline] Hybrid Surface Nets / Dual Contouring Statistics\n" +

                $"Processed sections: {TotalSections}\n" +
                $"Dual Contouring sections: {DualSections}\n" +
                $"Skipped Dual Contouring: {TotalSections - DualSections}\n" +
                $"Sharp mask time (included in Jobs): {SharpMaskMilliseconds:F2} ms\n" +

                $"Density samples: {TotalDensitySamples}\n" +

                $"Processed cells: {TotalCells}\n" +

                $"Vertices: {TotalVertices}\n" +

                $"Triangles: {TotalTriangles}\n" +

                $"Total Jobs time: " +
                $"{TotalJobsMilliseconds:F2} ms\n" +

                $"Mesh assembly time: " +
                $"{TotalAssemblyMilliseconds:F2} ms\n" +

                $"Total pipeline time: " +
                $"{TotalJobsMilliseconds + TotalAssemblyMilliseconds:F2} ms"
            );
        }

        public void ResetStatistics()
        {
            TotalSections = 0;
            DualSections = 0;
            SharpMaskMilliseconds = 0;

            TotalDensitySamples = 0;
            TotalCells = 0;

            TotalVertices = 0;
            TotalTriangles = 0;

            TotalJobsMilliseconds = 0.0;
            TotalAssemblyMilliseconds = 0.0;
        }

        public void Dispose()
        {
            nativeData?.Dispose();
            nativeData = null;
        }
    }
}
