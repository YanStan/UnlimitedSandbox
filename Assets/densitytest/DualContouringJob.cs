using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Unlimited.DensityTest
{
    // One vertex per active cell. Samples require a one-point gradient halo.
    [BurstCompile]
    public struct DualContouringJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> Samples;
        public int3 SampleOrigin, SampleSize, CellOrigin, CellSize;
        [WriteOnly] public NativeArray<float3> Vertices;
        [WriteOnly] public NativeArray<byte> Active;
        public void Execute(int index)
        {
            int3 p = new int3(index / (CellSize.y * CellSize.z), index / CellSize.z % CellSize.y, index % CellSize.z);
            int3 cell = CellOrigin + p;
            var hits = new FixedList512Bytes<DensityHermiteSample>();
            DensityHermite.Collect(Samples, SampleOrigin, SampleSize, cell, ref hits);
            bool ok = DensityQef.Solve(in hits, out float3 vertex);
            Active[index] = (byte)(ok ? 1 : 0);
            Vertices[index] = (float3)p + vertex;
        }
    }

    // Adapter for the production pipeline: world-local positions and existing quad ownership.
    [BurstCompile(FloatPrecision.Standard, FloatMode.Strict)]
    public struct DualContouringPipelineJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> Densities;
        [ReadOnly] public NativeArray<byte> SharpMask;
        public NativeArray<DensityCellVertex> CellVertices;
        public int3 DensityGridOrigin, DensityGridSize, CellGridOrigin, CellGridSize;
        public int WorldWidth, WorldDepth;
        public void Execute(int index)
        {
            if (SharpMask[index] == 0 || CellVertices[index].active == 0) return;
            int3 p = new int3(index / (CellGridSize.y * CellGridSize.z),
                index / CellGridSize.z % CellGridSize.y, index % CellGridSize.z);
            int3 cell = CellGridOrigin + p;
            var hits = new FixedList512Bytes<DensityHermiteSample>();
            DensityHermite.Collect(Densities, DensityGridOrigin, DensityGridSize, cell, ref hits);
            if (!DensityQef.Solve(in hits, out float3 vertex))
            {
                // Keep the valid Surface Nets vertex if QEF fails.
                return;
            }
            // Keep vertices slightly inside their own cells to avoid coincident QEF corners.
            vertex = math.clamp(vertex, new float3(0.015f), new float3(0.985f));
            CellVertices[index] = new DensityCellVertex
            {
                active = 1,
                position = (float3)cell + vertex - new float3(WorldWidth * 0.5f, 0f, WorldDepth * 0.5f)
            };
        }
    }

    // Emit interior quads around sign-changing primal edges. Boundary ownership
    // and neighbour cells will be handled by the mesh pipeline integration.
    [BurstCompile]
    public struct DualContouringFacesJob : IJob
    {
        [ReadOnly] public NativeArray<float> Samples;
        [ReadOnly] public NativeArray<byte> Active;
        public int3 SampleOrigin, SampleSize, CellOrigin, CellSize;
        public NativeList<int> Indices;
        private int Index(int3 p) => (p.x * CellSize.y + p.y) * CellSize.z + p.z;
        private float Sample(int3 p)
        {
            p += CellOrigin - SampleOrigin;
            return Samples[(p.x * SampleSize.y + p.y) * SampleSize.z + p.z];
        }
        public void Execute()
        {
            Indices.Clear();
            for (int x = 0; x < CellSize.x; x++)
            for (int y = 0; y < CellSize.y; y++)
            for (int z = 0; z < CellSize.z; z++)
            {
                int3 p = new int3(x,y,z);
                for (int axis = 0; axis < 3; axis++)
                {
                    int3 e = axis == 0 ? new int3(1,0,0) : axis == 1 ? new int3(0,1,0) : new int3(0,0,1);
                    int3 u = axis == 0 ? new int3(0,1,0) : axis == 1 ? new int3(0,0,1) : new int3(1,0,0);
                    int3 v = axis == 0 ? new int3(0,0,1) : axis == 1 ? new int3(1,0,0) : new int3(0,1,0);
                    if (math.any(p-u-v < 0)) continue;
                    bool solid = Sample(p) < 0f;
                    if (solid == (Sample(p+e) < 0f)) continue;
                    int a=Index(p), b=Index(p-u), c=Index(p-u-v), d=Index(p-v);
                    if (Active[a]==0 || Active[b]==0 || Active[c]==0 || Active[d]==0) continue;
                    Indices.Add(a); Indices.Add(solid ? b : d); Indices.Add(c);
                    Indices.Add(a); Indices.Add(c); Indices.Add(solid ? d : b);
                }
            }
        }
    }
}
