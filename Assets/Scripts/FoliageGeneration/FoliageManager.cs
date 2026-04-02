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

    private Mesh grassMesh;
    private Material grassMaterial;

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

        ResolveGrassRenderAssets();
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

            if (!shouldHaveGrass || !HasRequiredTerrainData(record))
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

            CacheGrassMatricesIfNeeded(runtime, record);

            runtime.FoliageRuntime.SetVisible(true);
            runtime.FoliageRuntime.Draw();
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
        root.transform.SetParent(chunkRuntime.RootTransform, false);

        chunkRuntime.FoliageRuntime.root = root.transform;
        chunkRuntime.FoliageRuntime.grassMesh = grassMesh;
        chunkRuntime.FoliageRuntime.grassMaterial = grassMaterial;
        chunkRuntime.FoliageRuntime.SetVisible(false);
    }

    private void CacheGrassMatricesIfNeeded(ChunkRuntime runtime, ChunkRecord record)
    {
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;
        ChunkFoliageData data = record.FoliageData;

        if (foliageRuntime == null || data == null)
            return;

        if (foliageRuntime.cachedInstanceCount == data.grassInstances.Count)
            return;

        List<Matrix4x4> worldMatrices = new List<Matrix4x4>(data.grassInstances.Count);
        Matrix4x4 chunkLocalToWorld = runtime.RootTransform.localToWorldMatrix;

        for (int i = 0; i < data.grassInstances.Count; i++)
        {
            FoliageInstanceData instance = data.grassInstances[i];

            Matrix4x4 localMatrix = Matrix4x4.TRS(
                instance.localPosition,
                instance.localRotation,
                instance.localScale);

            Matrix4x4 worldMatrix = chunkLocalToWorld * localMatrix;
            worldMatrices.Add(worldMatrix);
        }

        foliageRuntime.CacheMatrices(worldMatrices);
    }

    private void ResolveGrassRenderAssets()
    {
        if (grassSettings.grassPrefab == null)
        {
            Debug.LogError("Grass prefab is missing.");
            return;
        }

        MeshFilter meshFilter = grassSettings.grassPrefab.GetComponentInChildren<MeshFilter>();
        MeshRenderer meshRenderer = grassSettings.grassPrefab.GetComponentInChildren<MeshRenderer>();

        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogError("Grass prefab missing MeshFilter or mesh.");
            return;
        }

        if (meshRenderer == null || meshRenderer.sharedMaterial == null)
        {
            Debug.LogError("Grass prefab missing MeshRenderer or material.");
            return;
        }

        grassMesh = meshFilter.sharedMesh;
        grassMaterial = meshRenderer.sharedMaterial;
    }
}