using System;
using Unity.Profiling;
using UnityEngine;

/// <summary>Main-thread completed-result consumption and mesh/texture publication, independent of visibility updates.</summary>
public sealed class TerrainResultPublisher
{
    private readonly TerrainRequestManager terrainRequestManager;
    private readonly TerrainPublicationBudget budget;
    private readonly WorldStreamingBudget streamingBudget;
    private readonly Func<int> frameIndex;
    private int submissionFrame;
    private bool hasSubmissionFrame;
    private int submittedControlMapSets;
    public int SubmittedControlMapSets => submittedControlMapSets;
    private bool dataFirst;
    private readonly Func<ChunkCoord, ChunkRecord> findNormal;
    private readonly Func<FarTerrainPatchKey, FarTerrainTileRecord> findFar;
    private readonly Func<FarTerrainRequestResult, bool> stillWanted;
    private readonly Action<ChunkCoord> wakeNormal;
    private readonly Action<FarTerrainPatchKey> wakeFar;
    public TerrainResultPublisher(TerrainRequestManager requests, TerrainPublicationBudget budget,
        Func<ChunkCoord, ChunkRecord> findNormal, Func<FarTerrainPatchKey, FarTerrainTileRecord> findFar,
        Func<FarTerrainRequestResult, bool> stillWanted, Action<ChunkCoord> wakeNormal, Action<FarTerrainPatchKey> wakeFar, WorldStreamingBudget streamingBudget = null,
        Func<int> frameIndex = null)
    {
        terrainRequestManager = requests; this.budget = budget.Sanitized();
        this.frameIndex = frameIndex ?? (() => Time.frameCount);
        this.findNormal = findNormal; this.findFar = findFar; this.stillWanted = stillWanted;
        this.wakeNormal = wakeNormal; this.wakeFar = wakeFar; this.streamingBudget = streamingBudget;
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
    private static readonly ProfilerMarker CreateTerrainMeshMarker = new("FS.Streaming.Publication.CreateTerrainMesh");
    private static readonly ProfilerMarker CreateWaterMeshMarker = new("FS.Streaming.Publication.CreateWaterMesh");
    private static readonly ProfilerMarker CreateColliderMeshMarker = new("FS.Streaming.Publication.CreateColliderMesh");
    private static readonly ProfilerMarker CreateControlMapsMarker = new("FS.Streaming.Publication.CreateControlMaps");
    private static readonly ProfilerMarker UploadControlMapMarker = new("FS.Streaming.Publication.UploadOneControlMap");
    private static readonly ProfilerMarker DeferControlMapsMarker = new("FS.Streaming.Publication.DeferControlMapsForSubmissionLimit");
    public void Update()
    {
        using var streamingScope = streamingBudget?.Measure(WorldStreamingPhase.Results);
        using var processCompletedRequestsScope = ProcessCompletedRequestsMarker.Auto();
        int frame = frameIndex();
        if (!hasSubmissionFrame || submissionFrame != frame)
        {
            hasSubmissionFrame = true; submissionFrame = frame; submittedControlMapSets = 0;
        }
        long totalStart = TerrainGenerationProfiler.GetTimestamp();
        bool processedAnyRequest = false;
        ApplyFailures();

        TerrainGenerationProfiler.RecordQueueSnapshot(
            terrainRequestManager.ActiveTerrainDataJobCount,
            terrainRequestManager.ActiveFarTerrainJobCount,
            terrainRequestManager.ActiveMeshJobCount,
            terrainRequestManager.ActiveColliderJobCount,
            terrainRequestManager.CompletedTerrainDataResultCount,
            terrainRequestManager.CompletedFarTerrainResultCount,
            terrainRequestManager.CompletedMeshResultCount,
            terrainRequestManager.CompletedColliderResultCount);

        ApplyColliders(totalStart, ref processedAnyRequest);
        dataFirst = !dataFirst;
        if (dataFirst)
        {
            ApplyTerrainData(totalStart, ref processedAnyRequest);
            ApplyLodMeshes(totalStart, ref processedAnyRequest);
        }
        else
        {
            ApplyLodMeshes(totalStart, ref processedAnyRequest);
            ApplyTerrainData(totalStart, ref processedAnyRequest);
        }
        ApplyFarTerrain(totalStart, ref processedAnyRequest);

        if (processedAnyRequest)
        {
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.MainProcessCompletedRequestsTotal,
                totalStart);
        }
    }

    private void ApplyFailures()
    {
        // Workers report failure as data; Unity objects and record state remain main-thread owned.
        for (int count = 0; count < 8 && (streamingBudget == null || streamingBudget.HasRemaining) &&
            terrainRequestManager.TryDequeueFailure(out var failure); count++)
        {
            if (failure.Macro)
            {
                var patch = new FarTerrainPatchKey(failure.Coord, failure.PatchSize);
                var tile = findFar(patch);
                if (tile?.IsRequestCurrent(failure.Version) != true) continue;
                tile.CancelRequest(failure.Version); wakeFar(patch); continue;
            }
            var record = findNormal(failure.Coord);
            if (record == null) continue;
            switch (failure.Kind)
            {
                case TerrainRequestKind.Data:
                    if (!record.IsTerrainDataRequestCurrent(failure.Version)) continue;
                    record.CancelTerrainDataRequest(failure.Version); break;
                case TerrainRequestKind.Far:
                    if (!record.IsFarTerrainRequestCurrent(failure.Version)) continue;
                    record.CancelFarTerrainRequest(failure.Version); break;
                case TerrainRequestKind.Lod:
                    if (!record.IsMeshRequestCurrent(failure.Lod, failure.Version)) continue;
                    record.CancelMeshRequest(failure.Lod, failure.Version); break;
                case TerrainRequestKind.Collider:
                    if (!record.IsColliderRequestCurrent(failure.Version)) continue;
                    record.CancelColliderRequest(failure.Version); break;
            }
            wakeNormal(failure.Coord);
        }
    }

    private void ApplyTerrainData(long totalStart, ref bool processedAnyRequest)
    {
        long categoryStart = TerrainGenerationProfiler.GetTimestamp();
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
                   terrainRequestManager.TryPeekTerrainDataResult(out var next))
            {
                // Only the main thread consumes/retires these queues. Inspect before dequeue so a
                // quota deferral needs neither a second holding queue nor an ownership transfer.
                ChunkRecord record = findNormal(next.ChunkCoord);
                bool current = record != null && record.IsTerrainDataRequestCurrent(next.RequestVersion);
                if (current && !CanSubmitControlMaps(next.ControlMapsRawData)) break;
                if (!terrainRequestManager.TryDequeueTerrainDataResult(out TerrainDataRequestResult terrainResult)) break;
                using (terrainResult)
                {
                    terrainDataResultsApplied++;
                    if (!current)
                        continue;

                    using (ApplyTerrainDataResultMarker.Auto())
                    {
                        processedAnyRequest = true;

                        Texture2D[] controlMaps = null;
                        bool completed = false;
                        try
                        {
                            long stageStart = TerrainGenerationProfiler.GetTimestamp();
                            controlMaps = CreateControlMapTextures(terrainResult.ControlMapsRawData);
                            TerrainGenerationProfiler.Record(
                                TerrainGenerationProfileStage.MainTerrainControlMapTextureCreate,
                                stageStart);

                            completed = record.TryCompleteTerrainDataRequest(
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
                        catch
                        {
                            if (!completed) record.CancelTerrainDataRequest(terrainResult.RequestVersion);
                            throw;
                        }
                        finally { if (!completed) DestroyFarTerrainAssets(null, controlMaps, null); }
                    }
                }
            }
        }
    }

    private void ApplyLodMeshes(long totalStart, ref bool processedAnyRequest)
    {
        long categoryStart = TerrainGenerationProfiler.GetTimestamp();
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
                lodMeshResultsApplied++;
                ChunkRecord record = findNormal(meshResult.ChunkCoord);
                if (record == null || !record.IsMeshRequestCurrent(meshResult.LOD, meshResult.RequestVersion))
                    continue;

                using (ApplyLodMeshResultMarker.Auto())
                {
                    processedAnyRequest = true;

                    Mesh terrainMesh = null, waterMesh = null;
                    bool completed = false;
                    try
                    {
                        long stageStart = TerrainGenerationProfiler.GetTimestamp();
                        using (CreateTerrainMeshMarker.Auto()) terrainMesh = meshResult.TerrainMeshData.CreateMesh();
                        TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.MainLODTerrainMeshCreate, stageStart);

                        stageStart = TerrainGenerationProfiler.GetTimestamp();
                        using (CreateWaterMeshMarker.Auto()) waterMesh = meshResult.WaterMeshData.CreateMesh();
                        TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.MainWaterMeshCreate, stageStart);

                        completed = record.TryCompleteMeshRequest(
                            meshResult.LOD,
                            meshResult.RequestVersion,
                            terrainMesh,
                            waterMesh
                        );

                        if (completed)
                        {
                            record.RefreshDisplayedLOD(meshResult.LOD);
                            wakeNormal(record.ChunkCoord);
                        }
                    }
                    catch
                    {
                        if (!completed) record.CancelMeshRequest(meshResult.LOD, meshResult.RequestVersion);
                        throw;
                    }
                    finally { if (!completed) DestroyLODMeshAssets(terrainMesh, waterMesh); }
                }
            }
        }
    }

    private void ApplyColliders(long totalStart, ref bool processedAnyRequest)
    {
        long categoryStart = TerrainGenerationProfiler.GetTimestamp();
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
                colliderResultsApplied++;
                ChunkRecord record = findNormal(colliderResult.ChunkCoord);
                if (record == null || !record.IsColliderRequestCurrent(colliderResult.RequestVersion))
                    continue;

                using (ApplyColliderResultMarker.Auto())
                {
                    processedAnyRequest = true;

                    Mesh colliderMesh = null;
                    bool completed = false;
                    try
                    {
                        long stageStart = TerrainGenerationProfiler.GetTimestamp();
                        using (CreateColliderMeshMarker.Auto()) colliderMesh = colliderResult.ColliderMeshData.CreateMesh();
                        TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.MainColliderMeshCreate, stageStart);

                        completed = record.TryCompleteColliderRequest(
                            colliderResult.RequestVersion,
                            colliderMesh
                        );

                        if (completed)
                            wakeNormal(record.ChunkCoord);
                    }
                    catch
                    {
                        if (!completed) record.CancelColliderRequest(colliderResult.RequestVersion);
                        throw;
                    }
                    finally { if (!completed) TerrainAssetOwner.Destroy(colliderMesh); }
                }
            }
        }
    }

    private void ApplyFarTerrain(long totalStart, ref bool processedAnyRequest)
    {
        long categoryStart = TerrainGenerationProfiler.GetTimestamp();
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
                   terrainRequestManager.TryPeekFarTerrainResult(out var next))
            {
                bool wanted = stillWanted(next);
                if (wanted && !CanSubmitControlMaps(next.ControlMapsRawData)) break;
                if (!terrainRequestManager.TryDequeueFarTerrainResult(out FarTerrainRequestResult farTerrainResult)) break;
                farTerrainResultsApplied++;
                if (!wanted)
                    continue;

                using (ApplyFarTerrainResultMarker.Auto())
                {
                    processedAnyRequest = true;
                    Texture2D[] controlMaps = null;
                    Mesh terrainMesh = null, waterMesh = null;
                    bool accepted = false;
                    try
                    {
                        long stageStart = TerrainGenerationProfiler.GetTimestamp();
                        controlMaps = CreateControlMapTextures(farTerrainResult.ControlMapsRawData);
                        TerrainGenerationProfiler.Record(
                            TerrainGenerationProfileStage.MainFarControlMapTextureCreate,
                            stageStart);

                        stageStart = TerrainGenerationProfiler.GetTimestamp();
                        using (CreateTerrainMeshMarker.Auto()) terrainMesh = farTerrainResult.TerrainMeshData.CreateMesh();
                        TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.MainFarTerrainMeshCreate, stageStart);

                        if (farTerrainResult.WaterMeshData != null && farTerrainResult.WaterMeshData.VertexCount > 0)
                            using (CreateWaterMeshMarker.Auto()) waterMesh = farTerrainResult.WaterMeshData.CreateMesh();
                        accepted = TryCompleteFarTerrainResult(farTerrainResult, terrainMesh, controlMaps, waterMesh);
                        if (accepted && farTerrainResult.IsMacroTile)
                        {
                            wakeFar(new FarTerrainPatchKey(
                                farTerrainResult.ChunkCoord, farTerrainResult.PatchSizeInChunks));
                        }
                        else if (accepted)
                        {
                            wakeNormal(farTerrainResult.ChunkCoord);
                        }
                    }
                    catch
                    {
                        if (!accepted)
                        {
                            if (farTerrainResult.IsMacroTile)
                                findFar(new FarTerrainPatchKey(farTerrainResult.ChunkCoord, farTerrainResult.PatchSizeInChunks))?.CancelRequest(farTerrainResult.RequestVersion);
                            else findNormal(farTerrainResult.ChunkCoord)?.CancelFarTerrainRequest(farTerrainResult.RequestVersion);
                        }
                        throw;
                    }
                    finally { if (!accepted) DestroyFarTerrainAssets(terrainMesh, controlMaps, waterMesh); }
                }
            }
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
            TerrainAssetOwner.Destroy(terrainMesh);

        if (waterMesh != null)
            TerrainAssetOwner.Destroy(waterMesh);
    }

    private static void DestroyFarTerrainAssets(Mesh terrainMesh, Texture2D[] controlMaps, Mesh waterMesh)
    {
        if (terrainMesh != null)
            TerrainAssetOwner.Destroy(terrainMesh);

        if (waterMesh != null)
            TerrainAssetOwner.Destroy(waterMesh);

        if (controlMaps == null)
            return;

        for (int i = 0; i < controlMaps.Length; i++)
        {
            if (controlMaps[i] != null)
                TerrainAssetOwner.Destroy(controlMaps[i]);
        }
    }

    private bool CanApplyMoreResults(
        int appliedCount,
        int maxCount,
        long frameStart,
        float frameBudgetMs,
        long categoryStart,
        float categoryBudgetMs)
    {
        if (streamingBudget != null && !streamingBudget.HasRemaining) return false;
        if (appliedCount >= maxCount)
            return false;

        if (frameBudgetMs > 0f && TerrainGenerationProfiler.GetElapsedMilliseconds(frameStart) >= frameBudgetMs)
            return false;

        if (categoryBudgetMs > 0f && TerrainGenerationProfiler.GetElapsedMilliseconds(categoryStart) >= categoryBudgetMs)
            return false;

        return true;
    }

    private bool CanSubmitControlMaps(ControlMapPixelData rawData)
    {
        // Empty and obsolete results don't spend a texture-submission slot.
        if (rawData?.Maps == null || rawData.Maps.Length == 0 ||
            submittedControlMapSets < budget.ControlMapSetsPerFrame) return true;
        using (DeferControlMapsMarker.Auto()) return false;
    }

    private Texture2D[] CreateControlMapTextures(ControlMapPixelData rawData)
    {
        using var controlMapsScope = CreateControlMapsMarker.Auto();
        if (rawData == null || rawData.Maps == null || rawData.Maps.Length == 0)
            return null;

        // Reserve before creating the first texture. Failed/partial attempts still consume the
        // slot: cleanup doesn't undo graphics commands already submitted in this frame.
        submittedControlMapSets++;
        Texture2D[] textures = new Texture2D[rawData.Maps.Length];

        try
        {
            for (int i = 0; i < rawData.Maps.Length; i++)
            {
                using var uploadScope = UploadControlMapMarker.Auto();
                if (rawData.Maps[i] == null || rawData.Maps[i].Length != rawData.Width * rawData.Height)
                    throw new ArgumentException("Control-map pixel buffer does not match its dimensions.", nameof(rawData));
                Texture2D tex = new Texture2D(rawData.Width, rawData.Height, TextureFormat.RGBA32, false, true);
                textures[i] = tex;
                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.SetPixels32(rawData.Maps[i]);
                tex.Apply(false, false);
            }

            return textures;
        }
        catch { DestroyFarTerrainAssets(null, textures, null); throw; }
    }
}
