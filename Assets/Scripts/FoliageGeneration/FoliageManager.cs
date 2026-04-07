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

    public void HandleViewerSubChunkChanged(
        ChunkManager chunkManager,
        ChunkCoord viewerCoord,
        SubChunkCoord viewerGlobalSubChunk,
        List<ChunkCoord> orderedActiveCoords)
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
                {
                    runtime.FoliageRuntime.ClearCachedBatches();
                    runtime.FoliageRuntime.SetVisible(false);
                }

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
                    meshHeightMultiplier);
            }

            RebuildGrassMatricesForViewerSubChunk(runtime, record, viewerGlobalSubChunk);
            runtime.FoliageRuntime.SetVisible(true);
        }
    }

    public void DrawVisibleFoliageEveryFrame(
    ChunkManager chunkManager,
    ChunkCoord viewerCoord,
    SubChunkCoord viewerGlobalSubChunk,
    List<ChunkCoord> orderedActiveCoords)
    {
        for (int i = 0; i < orderedActiveCoords.Count; i++)
        {
            ChunkCoord coord = orderedActiveCoords[i];
            ChunkRecord record = chunkManager.GetChunkRecord(coord);
            ChunkRuntime runtime = chunkManager.GetChunkRuntime(record);

            if (record == null || runtime == null || runtime.FoliageRuntime == null)
                continue;

            bool shouldHaveGrass = IsWithinGrassRadius(viewerCoord, coord);

            if (!shouldHaveGrass || !HasRequiredTerrainData(record))
            {
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
                    meshHeightMultiplier);
            }

            bool needsInitialCacheBuild =
                !runtime.FoliageRuntime.HasValidRenderData();

            if (needsInitialCacheBuild)
            {
                RebuildGrassMatricesForViewerSubChunk(
                    runtime,
                    record,
                    viewerGlobalSubChunk);
            }

            runtime.FoliageRuntime.SetVisible(true);
            runtime.FoliageRuntime.Draw();
        }
    }

    private void RebuildGrassMatricesForViewerSubChunk(
        ChunkRuntime runtime,
        ChunkRecord record,
        SubChunkCoord viewerGlobalSubChunk)
    {
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;
        ChunkFoliageData data = record.FoliageData;

        if (foliageRuntime == null || data == null || data.grassInstancesBySubChunk == null)
            return;

        List<Matrix4x4> worldMatrices = new List<Matrix4x4>();
        Matrix4x4 chunkLocalToWorld = runtime.RootTransform.localToWorldMatrix;

        int subChunksPerChunk = data.subChunksPerChunk;

        for (int localSubX = 0; localSubX < subChunksPerChunk; localSubX++)
        {
            for (int localSubZ = 0; localSubZ < subChunksPerChunk; localSubZ++)
            {
                int targetGlobalSubX = record.ChunkCoord.x * subChunksPerChunk + localSubX;
                int targetGlobalSubZ = record.ChunkCoord.z * subChunksPerChunk + localSubZ;

                int dx = targetGlobalSubX - viewerGlobalSubChunk.x;
                int dz = targetGlobalSubZ - viewerGlobalSubChunk.z;
                int distSqr = dx * dx + dz * dz;

                float density = GetDensityForDistanceSqr(distSqr);

                List<FoliageInstanceData> subChunkInstances =
                    data.grassInstancesBySubChunk[localSubX, localSubZ];

                int totalCount = subChunkInstances.Count;
                int renderCount = Mathf.FloorToInt(totalCount * density);

                if (density > 0f && totalCount > 0)
                {
                    renderCount = Mathf.Clamp(renderCount, 1, totalCount);
                }

                for (int i = 0; i < renderCount; i++)
                {
                    FoliageInstanceData instance = subChunkInstances[i];

                    Matrix4x4 localMatrix = Matrix4x4.TRS(
                        instance.localPosition,
                        instance.localRotation,
                        instance.localScale);

                    Matrix4x4 worldMatrix = chunkLocalToWorld * localMatrix;
                    worldMatrices.Add(worldMatrix);
                }
            }
        }

        foliageRuntime.CacheMatrices(worldMatrices);
    }

    private float GetDensityForDistanceSqr(int distSqr)
    {
        if (distSqr <= 3 * 3)
            return grassSettings.densityRadius3;

        if (distSqr <= 6 * 6)
            return grassSettings.densityRadius6;

        if (distSqr <= 10 * 10)
            return grassSettings.densityRadius10;

        return grassSettings.densityBeyond10;
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