
using System.Collections.Generic;
using UnityEngine;

namespace Unlimited.DensityTest
{
    public sealed class DensityMeshBuilder
    {
        private readonly DensitySampler sampler;

        public DensityMeshBuilder(DensitySampler sampler)
        {
            this.sampler = sampler;
        }

        public void EmitFaces(
            Vector3Int cell,
            Dictionary<Vector3Int, int> cellVertices,
            List<Vector3> vertices,
            List<int> triangles)
        {
            int x = cell.x;
            int y = cell.y;
            int z = cell.z;

            float d0 = sampler.ComputeDensityAtGridPoint(
                x, y, z
            );

            float dx = sampler.ComputeDensityAtGridPoint(
                x + 1, y, z
            );

            float dy = sampler.ComputeDensityAtGridPoint(
                x, y + 1, z
            );

            float dz = sampler.ComputeDensityAtGridPoint(
                x, y, z + 1
            );

            // X-axis edge.

            if ((d0 < 0f) != (dx < 0f))
            {
                EmitQuad(
                    new Vector3Int(x, y, z),
                    new Vector3Int(x, y - 1, z),
                    new Vector3Int(x, y - 1, z - 1),
                    new Vector3Int(x, y, z - 1),

                    d0 < 0f
                        ? Vector3.right
                        : Vector3.left,

                    cellVertices,
                    vertices,
                    triangles
                );
            }

            // Y-axis edge.

            if ((d0 < 0f) != (dy < 0f))
            {
                EmitQuad(
                    new Vector3Int(x, y, z),
                    new Vector3Int(x, y, z - 1),
                    new Vector3Int(x - 1, y, z - 1),
                    new Vector3Int(x - 1, y, z),

                    d0 < 0f
                        ? Vector3.up
                        : Vector3.down,

                    cellVertices,
                    vertices,
                    triangles
                );
            }

            // Z-axis edge.

            if ((d0 < 0f) != (dz < 0f))
            {
                EmitQuad(
                    new Vector3Int(x, y, z),
                    new Vector3Int(x - 1, y, z),
                    new Vector3Int(x - 1, y - 1, z),
                    new Vector3Int(x, y - 1, z),

                    d0 < 0f
                        ? Vector3.forward
                        : Vector3.back,

                    cellVertices,
                    vertices,
                    triangles
                );
            }
        }

        private void EmitQuad(
            Vector3Int a,
            Vector3Int b,
            Vector3Int c,
            Vector3Int d,
            Vector3 desiredNormal,
            Dictionary<Vector3Int, int> cellVertices,
            List<Vector3> vertices,
            List<int> triangles)
        {
            if (!cellVertices.TryGetValue(a, out int i0))
                return;

            if (!cellVertices.TryGetValue(b, out int i1))
                return;

            if (!cellVertices.TryGetValue(c, out int i2))
                return;

            if (!cellVertices.TryGetValue(d, out int i3))
                return;

            Vector3 p0 = vertices[i0];
            Vector3 p1 = vertices[i1];
            Vector3 p2 = vertices[i2];
            Vector3 p3 = vertices[i3];

            Vector3 normalA = Vector3.Cross(
                p1 - p0,
                p2 - p0
            );

            Vector3 normalB = Vector3.Cross(
                p2 - p0,
                p3 - p0
            );

            Vector3 normal = normalA + normalB;

            if (normal.sqrMagnitude < 0.000000000001f)
                return;

            bool reverse = Vector3.Dot(
                normal,
                desiredNormal
            ) < 0f;

            if (reverse)
            {
                triangles.Add(i0);
                triangles.Add(i2);
                triangles.Add(i1);

                triangles.Add(i0);
                triangles.Add(i3);
                triangles.Add(i2);
            }
            else
            {
                triangles.Add(i0);
                triangles.Add(i1);
                triangles.Add(i2);

                triangles.Add(i0);
                triangles.Add(i2);
                triangles.Add(i3);
            }
        }
    }
}
