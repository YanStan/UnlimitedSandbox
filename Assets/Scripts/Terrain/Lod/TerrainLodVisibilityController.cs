using System;
using System.Collections.Generic;
using UnityEngine;

internal sealed class TerrainLodVisibilityController
{
    private readonly Dictionary<Vector2Int, ChunkData> superChunks;
    private readonly Dictionary<Vector2Int, ChunkData> fastChunks;
    private readonly Dictionary<Vector2Int, ChunkData> fineChunks;
    private readonly int finePerFast;
    private readonly int fastPerSuper;
    private readonly Action<ChunkData, bool> setChunkVisible;

    public TerrainLodVisibilityController(
        Dictionary<Vector2Int, ChunkData> superChunks,
        Dictionary<Vector2Int, ChunkData> fastChunks,
        Dictionary<Vector2Int, ChunkData> fineChunks,
        int finePerFast,
        int fastPerSuper,
        Action<ChunkData, bool> setChunkVisible)
    {
        this.superChunks = superChunks;
        this.fastChunks = fastChunks;
        this.fineChunks = fineChunks;
        this.finePerFast = finePerFast;
        this.fastPerSuper = fastPerSuper;
        this.setChunkVisible = setChunkVisible;
    }

    // Fine разрешается генерировать только после того, как Fast существует,
    // а весь его Super-родитель уже заменён комплектом дочерних Fast.
    // Это гарантирует видимый этап Super -> Fast -> Fine.
    public bool CanGenerateFineForFast(Vector2Int fast)
    {
        if (!fastChunks.ContainsKey(fast))
            return false;

        Vector2Int super = TerrainLodCoordinates.GetParent(
            fast, fastPerSuper);

        return !superChunks.ContainsKey(super) ||
               HasAllFastChildren(super);
    }

    public void RefreshSuper(Vector2Int super, bool highSpeedMode)
    {
        if (!superChunks.TryGetValue(super, out ChunkData superData))
        {
            RefreshFastChildren(super, highSpeedMode);
            return;
        }

        if (highSpeedMode || !HasAllFastChildren(super))
        {
            setChunkVisible(superData, true);
            HideFastAndFineChildren(super);
            return;
        }

        setChunkVisible(superData, false);
        RefreshFastChildren(super, highSpeedMode);
    }

    public void RefreshFast(Vector2Int fast, bool highSpeedMode)
    {
        Vector2Int super = TerrainLodCoordinates.GetParent(fast, fastPerSuper);
        bool superExists = superChunks.ContainsKey(super);

        if ((highSpeedMode && superExists) ||
            (superExists && !HasAllFastChildren(super)))
        {
            HideFastAndFine(fast);
            return;
        }

        if (!fastChunks.TryGetValue(fast, out ChunkData fastData))
        {
            SetFineChildrenVisible(fast, !superExists);
            return;
        }

        if (HasAllFineChildren(fast))
        {
            SetFineChildrenVisible(fast, true);
            setChunkVisible(fastData, false);
            return;
        }

        SetFineChildrenVisible(fast, false);
        setChunkVisible(fastData, true);
    }

    public void ApplyHighSpeedVisibility()
    {
        foreach (Vector2Int super in new List<Vector2Int>(superChunks.Keys))
            RefreshSuper(super, true);

        foreach (Vector2Int fast in new List<Vector2Int>(fastChunks.Keys))
        {
            Vector2Int super = TerrainLodCoordinates.GetParent(
                fast, fastPerSuper);

            if (!superChunks.ContainsKey(super))
                RefreshFast(fast, true);
        }

        foreach (Vector2Int fine in new List<Vector2Int>(fineChunks.Keys))
        {
            Vector2Int fast = TerrainLodCoordinates.GetParent(
                fine, finePerFast);
            Vector2Int super = TerrainLodCoordinates.GetParent(
                fast, fastPerSuper);

            if (superChunks.ContainsKey(super))
                setChunkVisible(fineChunks[fine], false);
            else if (fastChunks.ContainsKey(fast))
                RefreshFast(fast, true);
            else
                setChunkVisible(fineChunks[fine], true);
        }

        Physics.SyncTransforms();
    }

    public void RefreshAllNormalVisibility()
    {
        foreach (Vector2Int super in new List<Vector2Int>(superChunks.Keys))
            RefreshSuper(super, false);

        foreach (Vector2Int fast in new List<Vector2Int>(fastChunks.Keys))
            RefreshFast(fast, false);

        foreach (Vector2Int fine in new List<Vector2Int>(fineChunks.Keys))
        {
            Vector2Int fast = TerrainLodCoordinates.GetParent(
                fine, finePerFast);

            if (fastChunks.ContainsKey(fast))
            {
                RefreshFast(fast, false);
                continue;
            }

            Vector2Int super = TerrainLodCoordinates.GetParent(
                fast, fastPerSuper);
            setChunkVisible(fineChunks[fine], !superChunks.ContainsKey(super));
        }

        Physics.SyncTransforms();
    }

    private bool HasAllFastChildren(Vector2Int super)
    {
        for (int z = 0; z < fastPerSuper; z++)
        {
            for (int x = 0; x < fastPerSuper; x++)
            {
                Vector2Int child = new Vector2Int(
                    super.x * fastPerSuper + x,
                    super.y * fastPerSuper + z);

                if (!fastChunks.ContainsKey(child))
                    return false;
            }
        }

        return true;
    }

    private bool HasAllFineChildren(Vector2Int fast)
    {
        for (int z = 0; z < finePerFast; z++)
        {
            for (int x = 0; x < finePerFast; x++)
            {
                Vector2Int child = new Vector2Int(
                    fast.x * finePerFast + x,
                    fast.y * finePerFast + z);

                if (!fineChunks.ContainsKey(child))
                    return false;
            }
        }

        return true;
    }

    private void HideFastAndFine(Vector2Int fast)
    {
        if (fastChunks.TryGetValue(fast, out ChunkData fastData))
            setChunkVisible(fastData, false);

        SetFineChildrenVisible(fast, false);
    }

    private void SetFineChildrenVisible(Vector2Int fast, bool visible)
    {
        for (int z = 0; z < finePerFast; z++)
        {
            for (int x = 0; x < finePerFast; x++)
            {
                Vector2Int fine = new Vector2Int(
                    fast.x * finePerFast + x,
                    fast.y * finePerFast + z);

                if (fineChunks.TryGetValue(fine, out ChunkData fineData))
                    setChunkVisible(fineData, visible);
            }
        }
    }

    private void RefreshFastChildren(Vector2Int super, bool highSpeedMode)
    {
        for (int z = 0; z < fastPerSuper; z++)
        {
            for (int x = 0; x < fastPerSuper; x++)
            {
                RefreshFast(
                    new Vector2Int(
                        super.x * fastPerSuper + x,
                        super.y * fastPerSuper + z),
                    highSpeedMode);
            }
        }
    }

    private void HideFastAndFineChildren(Vector2Int super)
    {
        for (int z = 0; z < fastPerSuper; z++)
        {
            for (int x = 0; x < fastPerSuper; x++)
            {
                HideFastAndFine(new Vector2Int(
                    super.x * fastPerSuper + x,
                    super.y * fastPerSuper + z));
            }
        }
    }
}
