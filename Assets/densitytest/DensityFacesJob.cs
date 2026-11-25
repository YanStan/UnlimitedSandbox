
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Unlimited.DensityTest
{
    public struct DensityQuad
    {
        public int4 vertices;

        public byte active;
        public byte reversed;
    }

    public struct DensityCellFaces
    {
        public DensityQuad xFace;
        public DensityQuad yFace;
        public DensityQuad zFace;
    }

    [BurstCompile(
        FloatPrecision.Standard,
        FloatMode.Strict
    )]
    public struct DensityFacesJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<float> Densities;

        [ReadOnly]
        public NativeArray<DensityCellVertex> CellVertices;

        [WriteOnly]
        public NativeArray<DensityCellFaces> Faces;

        public int3 DensityGridOrigin;
        public int3 DensityGridSize;

        public int3 CellGridOrigin;
        public int3 CellGridSize;

        public int3 FaceGridOrigin;
        public int3 FaceGridSize;

        public void Execute(int index)
        {
            int yzSize =
                FaceGridSize.y * FaceGridSize.z;

            int x = index / yzSize;
            int remainder = index % yzSize;

            int y = remainder / FaceGridSize.z;
            int z = remainder % FaceGridSize.z;

            int3 cell = new int3(
                x + FaceGridOrigin.x,
                y + FaceGridOrigin.y,
                z + FaceGridOrigin.z
            );

            Faces[index] = BuildFaces(cell);
        }

        private int DensityIndex(int3 point)
        {
            int3 local =
                point - DensityGridOrigin;

            return
                local.x *
                DensityGridSize.y *
                DensityGridSize.z +

                local.y *
                DensityGridSize.z +

                local.z;
        }

        private float GetDensity(int3 point)
        {
            return Densities[
                DensityIndex(point)
            ];
        }

        private bool TryGetCellIndex(
            int3 cell,
            out int index)
        {
            index = -1;

            int3 local =
                cell - CellGridOrigin;

            if (math.any(local < 0) ||
                math.any(local >= CellGridSize))
            {
                return false;
            }

            index =
                local.x *
                CellGridSize.y *
                CellGridSize.z +

                local.y *
                CellGridSize.z +

                local.z;

            return CellVertices[index].active != 0;
        }

        private DensityCellFaces BuildFaces(
            int3 cell)
        {
            DensityCellFaces result = default;

            if (!TryGetCellIndex(cell, out _))
                return result;

            int3 axisX = new int3(1, 0, 0);
            int3 axisY = new int3(0, 1, 0);
            int3 axisZ = new int3(0, 0, 1);

            float d0 = GetDensity(cell);

            bool solid = d0 < 0f;

            float dx = GetDensity(cell + axisX);
            float dy = GetDensity(cell + axisY);
            float dz = GetDensity(cell + axisZ);

            float direction = solid ? 1f : -1f;

            // Face perpendicular to the X axis.

            if (solid != (dx < 0f))
            {
                result.xFace = BuildQuad(
                    cell,
                    cell - axisY,
                    cell - axisY - axisZ,
                    cell - axisZ,

                    new float3(direction, 0f, 0f)
                );
            }

            // Face perpendicular to the Y axis.

            if (solid != (dy < 0f))
            {
                result.yFace = BuildQuad(
                    cell,
                    cell - axisZ,
                    cell - axisX - axisZ,
                    cell - axisX,

                    new float3(0f, direction, 0f)
                );
            }

            // Face perpendicular to the Z axis.

            if (solid != (dz < 0f))
            {
                result.zFace = BuildQuad(
                    cell,
                    cell - axisX,
                    cell - axisX - axisY,
                    cell - axisY,

                    new float3(0f, 0f, direction)
                );
            }

            return result;
        }

        private DensityQuad BuildQuad(
            int3 cell0,
            int3 cell1,
            int3 cell2,
            int3 cell3,
            float3 desiredNormal)
        {
            if (!TryGetCellIndex(cell0, out int i0) ||
                !TryGetCellIndex(cell1, out int i1) ||
                !TryGetCellIndex(cell2, out int i2) ||
                !TryGetCellIndex(cell3, out int i3))
            {
                return default;
            }

            // The cyclic cell order has a positive axis normal by construction.
            // Do not drop a whole quad or flip its winding based on noisy geometry.
            bool reversed = math.csum(desiredNormal) < 0f;

            return new DensityQuad
            {
                vertices = new int4(
                    i0, i1, i2, i3
                ),

                active = 1,

                reversed = reversed
                    ? (byte)1
                    : (byte)0
            };
        }
    }
}
