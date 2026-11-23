internal static class TerrainMeshTopology
{
    public static int[] BuildFineTriangles(int resolution)
    {
        int side = resolution + 1;
        int[] triangles = new int[resolution * resolution * 6];
        int ti = 0;

        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int i = z * side + x;

                triangles[ti++] = i;
                triangles[ti++] = i + side;
                triangles[ti++] = i + 1;

                triangles[ti++] = i + 1;
                triangles[ti++] = i + side;
                triangles[ti++] = i + side + 1;
            }
        }

        return triangles;
    }
}
