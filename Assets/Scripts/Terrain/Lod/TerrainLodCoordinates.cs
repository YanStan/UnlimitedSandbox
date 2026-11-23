using UnityEngine;

internal static class TerrainLodCoordinates
{
    public static Vector2Int GetParent(Vector2Int child, int childrenPerSide)
    {
        return new Vector2Int(
            Mathf.FloorToInt((float)child.x / childrenPerSide),
            Mathf.FloorToInt((float)child.y / childrenPerSide));
    }
}
