using System;
using Unity.Profiling;
using UnityEngine;

/// <summary>Main-thread completed-result consumption and mesh/texture publication, independent of visibility updates.</summary>
public sealed class TerrainResultPublisher
{
    private readonly TerrainRequestManager terrainRequestManager;
    private readonly TerrainPublicationBudget budget;
    private readonly Func<ChunkCoord, ChunkRecord> findNormal;
    private readonly Func<FarTerrainPatchKey, FarTerrainTileRecord> findFar;
    private readonly Func<FarTerrainRequestResult, bool> stillWanted;
    private readonly Action<ChunkCoord> wakeNormal;
    private readonly Action<FarTerrainPatchKey> wakeFar;
    public TerrainResultPublisher(TerrainRequestManager requests, TerrainPublicationBudget budget,
        Func<ChunkCoord, ChunkRecord> findNormal, Func<FarTerrainPatchKey, FarTerrainTileRecord> findFar,
        Func<FarTerrainRequestResult, bool> stillWanted, Action<ChunkCoord> wakeNormal, Action<FarTerrainPatchKey> wakeFar)
    {
        terrainRequestManager = requests; this.budget = budget.Sanitized();
        this.findNormal = findNormal; this.findFar = findFar; this.stillWanted = stillWanted;
        this.wakeNormal = wakeNormal; this.wakeFar = wakeFar;
    }
    private static readonly ProfilerMarker ProcessCompletedRequestsMarker = new ProfilerMarker("FS.Streaming.ProcessCompletedRequests");
    private static readonly ProfilerMarker ApplyTerrainDataResultsMarker = new ProfilerMarker("FS.Streaming.ApplyTerrainDataResults");
    private static readonly ProfilerMarker ApplyTerrainDataResultMarker = new ProfilerMarker("FS.Streaming.ApplyTerrainDataResult");
    private static readonly ProfilerMarker ApplyLodMeshResultsMarker = new ProfilerMarker("FS.Streaming.ApplyLODMeshResults");
    private static readonly ProfilerMarker ApplyLodMeshResultMarker = new ProfilerMarker("FS.Streaming.ApplyLODMeshResult");
    private static readonly ProfilerMarker ApplyColliderResultsMarker = new ProfilerMarker("FS.Streaming.ApplyColliderResults");
    private static readonly ProfilerMarker ApplyColliderResultMarker = new ProfilerMarker("FS.Streaming.ApplyColliderResult");
    private static readonly ProfilerMarker ApplyFarTerrainResultsMarker = new ProfilerMarker("FS.Streaming.ApplyFarTerrainResults");
    private static readonly ProfilerMarker ApplyFarTerrainResultMarker = new ProfilerMarker("FS.Streaming.ApplyFarTerrainResult");
    public void Update()
    {
        using var processCompletedRequestsScope = ProcessCompletedRequestsMarker.Auto();
        long totalStart = TerrainGenerationProfiler.GetTimestamp();
        long categoryStart = totalStart;
        bool processedAnyRequest = false;

        TerrainGenerationProfiler.RecordQueueSnapshot(
            terrainRequestManager.ActiveTerrainDataJobCount,
            terrainRequestManager.ActiveFarTerrainJobCount,
            terrainRequestManager.ActiveMeshJobCount,
            terrainRequestManager.ActiveColliderJobCount,
            terrainRequestManager.CompletedTerrainDataResultCount,
            terrainRequestManager.CompletedFarTerrainResultCount,
            terrainRequestManager.CompletedMeshResultCount,
            terrainRequestManager.CompletedColliderResultCount);

        using (ApplyTerrainDataResultsMarker.Auto())
        {
            int terrainDataResultsApplied = 0;
            while (CanApplyMoreResults(
                       terrainDataResultsApplied,
                       budget.TerrainDataCount,
                       totalStart,
                       budget.TotalMs,
                       categoryStart,
                       budget.TerrainDataMs) &&
                   terrainRequestManager.TryDequeueTerrainDataResult(out TerrainDataRequestResult terrainResult))
            {
                using (terrainResult)
                {
                    ChunkRecord record = findNormal(terrainResult.ChunkCoord);
                    if (record == null)
                        continue;

                    using (ApplyTerrainDataResultMarker.Auto())
                    {
                        processedAnyRequest = true;
                        terrainDataResultsApplied++;
                        long stageStart = TerrainGenerationProfiler.GetTimestamp();
                        Texture2D[] controlMaps = CreateControlMapTextures(terrainResult.ControlMapsRawData);
                        TerrainGenerationProfiler.Record(
                            TerrainGenerationProfileStage.MainTerrainControlMapTextureCreate,
                            stageStart);

                        bool completed = record.TryCompleteTerrainDataRequest(
                            terrainResult.RequestVersion,
                            terrainResult.HeightMap,
                            terrainResult.SlopeMap,
                            terrainResult.MoistureMap,
                            terrainResult.TemperatureMap,
                            terrainResult.BiomeMap,
                            terrainResult.SurfaceTypeMap,
                            terrainResult.WaterStateMap,
                            terrainResult.GroundCoverMap,
                            terrainResult.WorldFeaturePlan,
                            terrainResult.RiverMaskMap,
                            controlMaps,
                            terrainResult.NativeData);

                        if (completed)
                        {
                            terrainResult.TransferNativeOwnership();
                            wakeNormal(record.ChunkCoord);
                        }
                    }
                }
            }
        }

        categoryStart = TerrainGenerationProfiler.GetTimestamp();
        using (ApplyLodMeshResultsMarker.Auto())
        {
            int lodMeshResultsApplied = 0;
            while (CanApplyMoreResults(
                       lodMeshResultsApplied,
                       budget.LodMeshCount,
                       totalStart,
                       budget.TotalMs,
                       categoryStart,
                       budget.LodMeshMs) &&
                   terrainRequestManager.TryDequeueMeshResult(out MeshRequestResult meshResult))
            {
                ChunkRecord record = findNormal(meshResult.ChunkCoord);
                    if (record == null)
                    continue;

                using (ApplyLodMeshResultMarker.Auto())
                {
                    processedAnyRequest = true;
                    lodMeshResultsApplied++;
                    long stageStart = TerrainGenerationProfiler.GetTimestamp();
                    Mesh terrainMesh = meshResult.TerrainMeshData.CreateMesh();
                    TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.MainLODTerrainMeshCreate, stageStart);

                    stageStart = TerrainGenerationProfiler.GetTimestamp();
                    Mesh waterMesh = meshResult.WaterMeshData.CreateMesh();
                    TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.MainWaterMeshCreate, stageStart);

                    bool completed = record.TryCompleteMeshRequest(
                        meshResult.LOD,
                        meshResult.RequestVersion,
                        terrainMesh,
                        waterMesh
                    );

                    if (completed)
                        wakeNormal(record.ChunkCoord);
                    else
                    {
                        DestroyLODMeshAssets(terrainMesh, waterMesh);
                    }
                }
            }
        }

        categoryStart = TerrainGenerationProfiler.GetTimestamp();
        using (ApplyColliderResultsMarker.Auto())
        {
            int colliderResultsApplied = 0;
            while (CanApplyMoreResults(
                       colliderResultsApplied,
                       budget.ColliderCount,
                       totalStart,
                       budget.TotalMs,
                       categoryStart,
                       budget.ColliderMs) &&
                   terrainRequestManager.TryDequeueColliderResult(out ColliderRequestResult colliderResult))
            {
                ChunkRecord record = findNormal(colliderResult.ChunkCoord);
                    if (record == null)
                    continue;

                using (ApplyColliderResultMarker.Auto())
                {
                    processedAnyRequest = true;
                    colliderResultsApplied++;
                    long stageStart = TerrainGenerationProfiler.GetTimestamp();
                    Mesh colliderMesh = colliderResult.ColliderMeshData.CreateMesh();
                    TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.MainColliderMeshCreate, stageStart);

                    bool completed = record.TryCompleteColliderRequest(
                        colliderResult.RequestVersion,
                        colliderMesh
                    );

                    if (completed)
                        wakeNormal(record.ChunkCoord);
                }
            }
        }

        categoryStart = TerrainGenerationProfiler.GetTimestamp();

        using (ApplyFarTerrainResultsMarker.Auto())
        {
            int farTerrainResultsApplied = 0;
            while (!HasHigherPriorityCompletedTerrainResults() &&
                   CanApplyMoreResults(
                       farTerrainResultsApplied,
                       budget.FarTerrainCount,
                       totalStart,
                       budget.TotalMs,
                       categoryStart,
                       budget.FarTerrainMs) &&
                   terrainRequestManager.TryDequeueFarTerrainResult(out FarTerrainRequestResult farTerrainResult))
            {
                if (!stillWanted(farTerrainResult))
                    continue;

                using (ApplyFarTerrainResultMarker.Auto())
                {
                    processedAnyRequest = true;
                    farTerrainResultsApplied++;
                    long stageStart = TerrainGenerationProfiler.GetTimestamp();
                    Texture2D[] controlMaps = CreateControlMapTextures(farTerrainResult.ControlMapsRawData);
                    TerrainGenerationProfiler.Record(
                        TerrainGenerationProfileStage.MainFarControlMapTextureCreate,
                        stageStart);

                    stageStart = TerrainGenerationProfiler.GetTimestamp();
                    Mesh terrainMesh = farTerrainResult.TerrainMeshData.CreateMesh();
                    TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.MainFarTerrainMeshCreate, stageStart);

                    Mesh waterMesh = farTerrainResult.WaterMeshData != null && farTerrainResult.WaterMeshData.VertexCount > 0
                        ? farTerrainResult.WaterMeshData.CreateMesh() : null;
                    bool accepted = TryCompleteFarTerrainResult(farTerrainResult, terrainMesh, controlMaps, waterMesh);
                    if (!accepted)
                    {
                        DestroyFarTerrainAssets(terrainMesh, controlMaps, waterMesh);
                    }
                    else if (farTerrainResult.IsMacroTile)
                    {
                        wakeFar(new FarTerrainPatchKey(
                            farTerrainResult.ChunkCoord, farTerrainResult.PatchSizeInChunks));
                    }
                    else
                    {
                        wakeNormal(farTerrainResult.ChunkCoord);
                    }
                }
            }
        }

        if (processedAnyRequest)
        {
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.MainProcessCompletedRequestsTotal,
                totalStart);
        }
    }

    public bool HasHigherPriorityCompletedTerrainResults()
    {
        return terrainRequestManager.CompletedTerrainDataResultCount > 0 ||
               terrainRequestManager.CompletedMeshResultCount > 0 ||
               terrainRequestManager.CompletedColliderResultCount > 0;
    }

    private bool TryCompleteFarTerrainResult(FarTerrainRequestResult result, Mesh terrainMesh, Texture2D[] maps, Mesh waterMesh)
    {
        if (result.IsMacroTile)
        {
            var tile = findFar(new FarTerrainPatchKey(result.ChunkCoord, result.PatchSizeInChunks));
            return tile != null && tile.TryCompleteRequest(result.RequestVersion, terrainMesh, maps, waterMesh, result.HeightGrid);
        }
        var record = findNormal(result.ChunkCoord);
        return record != null && record.TryCompleteFarTerrainRequest(result.RequestVersion, terrainMesh, maps, waterMesh, result.HeightGrid);
    }

    private static void DestroyLODMeshAssets(Mesh terrainMesh, Mesh waterMesh)
    {
        if (terrainMesh != null)
            UnityEngine.Object.Destroy(terrainMesh);

        if (waterMesh != null)
            UnityEngine.Object.Destroy(waterMesh);
    }

    private static void DestroyFarTerrainAssets(Mesh terrainMesh, Texture2D[] controlMaps, Mesh waterMesh)
    {
        if (terrainMesh != null)
            UnityEngine.Object.Destroy(terrainMesh);

        if (waterMesh != null)
            UnityEngine.Object.Destroy(waterMesh);

        if (controlMaps == null)
            return;

        for (int i = 0; i < controlMaps.Length; i++)
        {
            if (controlMaps[i] != null)
                UnityEngine.Object.Destroy(controlMaps[i]);
        }
    }

    private static bool CanApplyMoreResults(
        int appliedCount,
        int maxCount,
        long frameStart,
        float frameBudgetMs,
        long categoryStart,
        float categoryBudgetMs)
    {
        if (appliedCount >= maxCount)
            return false;

        if (frameBudgetMs > 0f && TerrainGenerationProfiler.GetElapsedMilliseconds(frameStart) >= frameBudgetMs)
            return false;

        if (categoryBudgetMs > 0f && TerrainGenerationProfiler.GetElapsedMilliseconds(categoryStart) >= categoryBudgetMs)
            return false;

        return true;
    }

    private Texture2D[] CreateControlMapTextures(ControlMapPixelData rawData)
    {
        if (rawData == null || rawData.Maps == null || rawData.Maps.Length == 0)
            return null;

        Texture2D[] textures = new Texture2D[rawData.Maps.Length];

        for (int i = 0; i < rawData.Maps.Length; i++)
        {
            Texture2D tex = new Texture2D(rawData.Width, rawData.Height, TextureFormat.RGBA32, false, true);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels32(rawData.Maps[i]);
            tex.Apply(false, false);
            textures[i] = tex;
        }

        return textures;
    }
}
