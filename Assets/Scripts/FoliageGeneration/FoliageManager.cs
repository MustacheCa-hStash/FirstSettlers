using System.Collections.Generic;
using UnityEngine;

public class FoliageManager
{
    private readonly Transform foliageParent;
    private readonly GrassSettings grassSettings;
    private readonly int worldSeed;
    private readonly int chunkSize;
    private readonly float worldScale;
    private readonly float meshHeightMultiplier;

    public FoliageManager(
        Transform foliageParent,
        GrassSettings grassSettings,
        int worldSeed,
        int chunkSize,
        float worldScale,
        float meshHeightMultiplier)
    {
        this.foliageParent = foliageParent;
        this.grassSettings = grassSettings;
        this.worldSeed = worldSeed;
        this.chunkSize = chunkSize;
        this.worldScale = worldScale;
        this.meshHeightMultiplier = meshHeightMultiplier;
    }

    public void UpdateVisibleFoliage(ChunkManager chunkManager, ChunkCoord viewerCoord, List<ChunkCoord> orderedActiveCoords)
    {
        for (int i = 0; i < orderedActiveCoords.Count; i++)
        {
            ChunkCoord coord = orderedActiveCoords[i];
            ChunkRecord record = chunkManager.GetChunkRecord(coord);
            ChunkRuntime runtime = chunkManager.GetChunkRuntime(record);

            if (record == null || runtime == null)
                continue;

            bool shouldHaveGrass = IsWithinGrassRadius(viewerCoord, coord);

            EnsureFoliageRuntimeExists(runtime, record);

            if (!shouldHaveGrass)
            {
                if (runtime.FoliageRuntime != null)
                    runtime.FoliageRuntime.SetVisible(false);

                continue;
            }

            if (!HasRequiredTerrainData(record))
            {
                if (runtime.FoliageRuntime != null)
                    runtime.FoliageRuntime.SetVisible(false);

                continue;
            }

            if (record.FoliageData == null || !record.FoliageData.grassGenerated)
            {
                FoliageGenerator.GenerateGrassForChunk(
                    record,
                    grassSettings,
                    worldSeed,
                    chunkSize,
                    worldScale,
                    meshHeightMultiplier
                );
            }

            SpawnGrassIfNeeded(runtime, record);
            runtime.FoliageRuntime.SetVisible(true);
        }
    }

    private bool IsWithinGrassRadius(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        int dx = Mathf.Abs(targetCoord.x - viewerCoord.x);
        int dz = Mathf.Abs(targetCoord.z - viewerCoord.z);
        return dx <= grassSettings.activeRingRadius && dz <= grassSettings.activeRingRadius;
    }

    private bool HasRequiredTerrainData(ChunkRecord record)
    {
        return record.HeightMap != null && record.SurfaceTypeMap != null;
    }

    private void EnsureFoliageRuntimeExists(ChunkRuntime chunkRuntime, ChunkRecord record)
    {
        if (chunkRuntime.FoliageRuntime != null && chunkRuntime.FoliageRuntime.IsCreated)
            return;

        chunkRuntime.FoliageRuntime = new ChunkFoliageRuntime();

        GameObject root = new GameObject($"Foliage_{record.ChunkCoord.x}_{record.ChunkCoord.z}");

        if (chunkRuntime.RootTransform != null)
            root.transform.SetParent(chunkRuntime.RootTransform, false);
        else if (foliageParent != null)
            root.transform.SetParent(foliageParent, false);

        chunkRuntime.FoliageRuntime.root = root.transform;
        chunkRuntime.FoliageRuntime.SetVisible(false);
    }

    private void SpawnGrassIfNeeded(ChunkRuntime runtime, ChunkRecord record)
    {
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;
        ChunkFoliageData data = record.FoliageData;

        if (foliageRuntime == null || data == null)
            return;

        if (foliageRuntime.grassObjects.Count == data.grassInstances.Count && foliageRuntime.grassObjects.Count > 0)
            return;

        foliageRuntime.ClearSpawnedObjects();

        for (int i = 0; i < data.grassInstances.Count; i++)
        {
            FoliageInstanceData instance = data.grassInstances[i];

            GameObject grassObject = Object.Instantiate(grassSettings.grassPrefab, foliageRuntime.root);
            grassObject.transform.localPosition = instance.localPosition;
            grassObject.transform.localRotation = instance.localRotation;
            grassObject.transform.localScale = instance.localScale;

            foliageRuntime.grassObjects.Add(grassObject);
        }
    }
}