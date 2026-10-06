using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

public class FoliageManager
{

    private static readonly ProfilerMarker HandleViewerSubChunkChangedMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleViewerSubChunkChanged");
    private static readonly ProfilerMarker HandleSubChunkEnsureRuntimeMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.EnsureRuntime");
    private static readonly ProfilerMarker HandleSubChunkRangeChecksMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.RangeChecks");
    private static readonly ProfilerMarker HandleSubChunkClearInactiveMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.ClearInactive");
    private static readonly ProfilerMarker HandleSubChunkTreesMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.Trees");
    private static readonly ProfilerMarker HandleSubChunkBushesMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.Bushes");
    private static readonly ProfilerMarker HandleSubChunkRocksMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.Rocks");
    private static readonly ProfilerMarker HandleSubChunkFlowersMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.Flowers");
    private static readonly ProfilerMarker HandleSubChunkLilyPadsMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.LilyPads");
    private static readonly ProfilerMarker HandleSubChunkCattailsMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.Cattails");
    private static readonly ProfilerMarker HandleSubChunkCloverMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.Clover");
    private static readonly ProfilerMarker HandleSubChunkDandelionsMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.Dandelions");
    private static readonly ProfilerMarker HandleSubChunkSetVisibleMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.SetVisible");
    private static readonly ProfilerMarker DrawVisibleFoliageEveryFrameMarker = new ProfilerMarker("FS.Streaming.Foliage.DrawVisibleEveryFrame");
    private static readonly ProfilerMarker QueueFoliageManagementMarker = new ProfilerMarker("FS.Streaming.Foliage.QueueManagement");
    private static readonly ProfilerMarker ProcessFoliageManagementQueueMarker = new ProfilerMarker("FS.Streaming.Foliage.ProcessManagementQueue");
    private static readonly ProfilerMarker DrawFoliageOnlyMarker = new ProfilerMarker("FS.Streaming.Foliage.DrawOnly");
    private static readonly ProfilerMarker PruneStaleFoliageQueuesMarker = new ProfilerMarker("FS.Streaming.Foliage.PruneStaleQueues");
    private static readonly ProfilerMarker ProcessTreeRepresentationQueueMarker = new ProfilerMarker("FS.Streaming.Foliage.ProcessTreeRepresentationQueue");

    private readonly GrassSettings grassSettings;
    private readonly FlowerSettings flowerSettings;
    private readonly LilyPadSettings lilyPadSettings;
    private readonly CattailSettings cattailSettings;
    private readonly CloverSettings cloverSettings;
    private readonly DandelionSettings dandelionSettings;
    private readonly TreeSettings treeSettings;
    private readonly int chunkSize;
    private readonly float worldScale;

    private readonly Plane[] grassFrustum = new Plane[6];
    private readonly Vector4[] grassGpuPlanes = new Vector4[6];

    private Vector3 cloverViewerPosition;
    private bool hasCloverViewerPosition;
    private int observedCloverRadius,observedCloverPadding;

    private readonly List<TreeRepresentationWorkItem> pendingTreeRepresentationWork = new();
    private readonly HashSet<ChunkCoord> queuedTreeRepresentationWork = new();
    private readonly List<FoliageManagementWorkItem> pendingFoliageManagementWork = new();
    private readonly HashSet<ChunkCoord> queuedFoliageManagementWork = new();
    private readonly List<ChunkCoord> deferredFoliageManagementRetries = new();

    public FoliageManager(WorldGenerationConfiguration generation, WorldFoliageConfiguration settings, TreeRegistry treeRegistry = null)
    {
        var grassSettings = settings.Grass;
        var flowerSettings = settings.Flowers;
        var lilyPadSettings = settings.LilyPads;
        var cattailSettings = settings.Cattails;
        var cloverSettings = settings.Clover;
        var dandelionSettings = settings.Dandelions;
        var treeSettings = settings.Trees;
        var chunkSize = generation.ChunkSize;
        var worldScale = generation.WorldScale;
        var meshHeightMultiplier = generation.MeshHeightMultiplier;
        var waterSettings = generation.Water;
        var worldSeed = generation.Seed;
        this.grassSettings = grassSettings;
        this.flowerSettings = flowerSettings;
        this.lilyPadSettings = lilyPadSettings;
        this.cattailSettings = cattailSettings;
        this.cloverSettings = cloverSettings;
        this.dandelionSettings = dandelionSettings;
        this.treeSettings = treeSettings;
        this.chunkSize = chunkSize;
        this.worldScale = worldScale;

        placement = new FoliagePlacementService(generation, settings.Trees, treeRegistry);
        assets = new FoliageRenderAssets(settings);
        publicationScheduler = new FoliagePublicationScheduler(grassSettings, assets, IsFoliageBatchWorkStillWanted);
        discoveryScheduler = new FoliageDiscoveryScheduler(generation, settings, assets, placement, publicationScheduler, IsGroundFoliageGenerationStillWanted);
        grassStream = new GrassStream(grassSettings, cloverSettings, treeSettings, worldSeed, chunkSize, worldScale, meshHeightMultiplier, PrepareStreamingGrass, () => IsCloverSystemEnabled() && assets.HasCloverRenderAssets());
        observedCloverRadius=cloverSettings!=null?cloverSettings.activeRingRadius:0;
        observedCloverPadding=cloverSettings!=null?cloverSettings.preGenerationRingPadding:0;
    }

    private readonly FoliageDiscoveryScheduler discoveryScheduler;
    private readonly FoliagePublicationScheduler publicationScheduler;
    private readonly FoliagePlacementService placement;
    private readonly FoliageRenderAssets assets;
    private readonly GrassStream grassStream;
    public void UpdateGrassStreaming(ChunkManager manager, List<ChunkCoord> activeCoords, Vector3 viewer, Camera camera)
    {
        RefreshCloverRangeWork(activeCoords);
        grassStream.Update(manager, activeCoords, viewer, camera, camera != null ? grassGpuPlanes : null,
            assets.GrassMesh, assets.GrassMaterial, assets.BillboardGrassMesh, assets.BillboardGrassMaterial,
            assets.ForestGrassMesh, assets.ForestGrassMaterial, assets.ForestFarGrassMesh, assets.ForestFarGrassMaterial);
    }
    private void RefreshCloverRangeWork(List<ChunkCoord> activeCoords)
    {
        if(cloverSettings==null || (observedCloverRadius==cloverSettings.activeRingRadius &&
            observedCloverPadding==cloverSettings.preGenerationRingPadding))return;
        observedCloverRadius=cloverSettings.activeRingRadius;observedCloverPadding=cloverSettings.preGenerationRingPadding;
        foreach(var coord in activeCoords)EnqueueFoliageManagementWork(coord);
    }
    private void PrepareStreamingGrass(ChunkRecord record)
    {
        EnsureTreesGenerated(record); EnsureBushesGenerated(record); EnsureRocksGenerated(record);
        if (IsCloverSystemEnabled() && assets.HasCloverRenderAssets() && !record.FoliageData.cloverGenerated)
            EnqueueGroundFoliageGeneration(record, FoliagePublicationKind.Clover);
    }

    public void Dispose()
    {
        grassStream.Dispose();
        discoveryScheduler.Dispose();
        publicationScheduler.Dispose();
        pendingTreeRepresentationWork.Clear(); queuedTreeRepresentationWork.Clear();
        pendingFoliageManagementWork.Clear(); queuedFoliageManagementWork.Clear();
        deferredFoliageManagementRetries.Clear(); RecordFoliageQueueSnapshot(0);
    }

    private void RecordFoliageQueueSnapshot(int treeRepresentationWorkCount = -1)
    {
        TerrainGenerationProfiler.RecordFoliageQueueSnapshot(0, 0, 0,
            publicationScheduler.PendingCount + publicationScheduler.ActiveCount, treeRepresentationWorkCount,
            discoveryScheduler.PendingCount + discoveryScheduler.ActiveCount);
    }

    public void HandleViewerSubChunkChanged(
        ChunkManager chunkManager,
        ChunkCoord viewerCoord,
        SubChunkCoord viewerGlobalSubChunk,
        List<ChunkCoord> orderedActiveCoords,
        bool viewerChunkChanged)
    {
        using (HandleViewerSubChunkChangedMarker.Auto())
        {
            long stageStart = TerrainGenerationProfiler.GetTimestamp();

            using (QueueFoliageManagementMarker.Auto())
            {
                for (int i = 0; i < orderedActiveCoords.Count; i++)
                {
                    EnqueueFoliageManagementWork(orderedActiveCoords[i]);
                }
            }

            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageHandleSubChunkChanged,
                stageStart);
        }
    }

    public void DrawVisibleFoliageEveryFrame(
        ChunkManager chunkManager,
        ChunkCoord viewerCoord,
        SubChunkCoord viewerGlobalSubChunk,
        List<ChunkCoord> orderedActiveCoords,
        Camera renderCamera = null,Vector3? viewerPosition = null)
    {
        using (DrawVisibleFoliageEveryFrameMarker.Auto())
        {
            cloverViewerPosition=viewerPosition ?? (renderCamera!=null?renderCamera.transform.position:
                new Vector3((viewerCoord.x+.5f)*chunkSize*worldScale,0,(viewerCoord.z+.5f)*chunkSize*worldScale));
            hasCloverViewerPosition=true;
            if (renderCamera != null && grassSettings.gpuIndirectRendering)
            {
                GeometryUtility.CalculateFrustumPlanes(renderCamera, grassFrustum);
                for (int i = 0; i < 6; i++)
                    grassGpuPlanes[i] = new Vector4(grassFrustum[i].normal.x, grassFrustum[i].normal.y,
                        grassFrustum[i].normal.z, grassFrustum[i].distance);
            }
            long stageStart = TerrainGenerationProfiler.GetTimestamp();
            long workBudgetStart = TerrainGenerationProfiler.GetTimestamp();
            float foregroundBudgetMs = Mathf.Max(0f, grassSettings.foregroundFoliageWorkBudgetMsPerFrame);

            PruneStaleFoliageQueues(chunkManager, viewerCoord, viewerGlobalSubChunk);
            ProcessPendingFoliageManagementWork(chunkManager, viewerCoord, viewerGlobalSubChunk, workBudgetStart, foregroundBudgetMs);
            ProcessPendingGroundFoliageGenerationWork(chunkManager, viewerCoord, workBudgetStart, foregroundBudgetMs);

            ProcessPendingFoliageBatchWork(chunkManager, viewerCoord, viewerGlobalSubChunk, workBudgetStart, foregroundBudgetMs);
            ProcessPendingTreeRepresentationWork(chunkManager, viewerCoord, workBudgetStart, foregroundBudgetMs);

            using (DrawFoliageOnlyMarker.Auto())
            {
                for (int i = 0; i < orderedActiveCoords.Count; i++)
                {
                    ChunkCoord coord = orderedActiveCoords[i];
                    ChunkRecord record = chunkManager.GetChunkRecord(coord);
                    ChunkRuntime runtime = chunkManager.GetChunkRuntime(record);

                    DrawFoliageForChunk(record, runtime, viewerCoord, coord);
                }
            }

            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageDrawVisibleEveryFrame,
                stageStart);
        }
    }

    private void DrawFoliageForChunk(
        ChunkRecord record,
        ChunkRuntime runtime,
        ChunkCoord viewerCoord,
        ChunkCoord coord)
    {
        if (record == null ||
            runtime == null ||
            runtime.FoliageRuntime == null ||
            !HasRequiredTerrainData(record))
        {
            return;
        }

        bool useFlowers = IsWithinFlowerRenderRange(viewerCoord, coord);
        bool useLilyPads = IsWithinLilyPadRenderRange(viewerCoord, coord);
        bool useCattails = IsWithinCattailRenderRange(viewerCoord, coord);
        bool useClover = IsWithinCloverRenderRange(viewerCoord, coord);
        bool useDandelions = IsWithinDandelionRenderRange(viewerCoord, coord);
        bool useTrees = IsWithinTreeRenderRange(viewerCoord, coord);

        if (!(useFlowers || useLilyPads || useCattails || useClover || useDandelions || useTrees))
            return;

        if (useClover && assets.HasCloverRenderAssets())
            runtime.FoliageRuntime.DrawClover(GetCloverViewer(viewerCoord),
                CloverStreamingPolicy.RenderDistance(cloverSettings,chunkSize,worldScale),
                CloverStreamingPolicy.FadeWidth(cloverSettings,chunkSize,worldScale));

        if (useFlowers && assets.HasFlowerRenderAssets())
            runtime.FoliageRuntime.DrawFlowers();

        if (useLilyPads && assets.HasLilyPadRenderAssets())
            runtime.FoliageRuntime.DrawLilyPads();

        if (useCattails && assets.HasCattailRenderAssets())
            runtime.FoliageRuntime.DrawCattails();

        if (useDandelions && assets.HasDandelionRenderAssets())
            runtime.FoliageRuntime.DrawDandelions();

    }

    private void EnqueueFoliageManagementWork(ChunkCoord coord)
    {
        if (!queuedFoliageManagementWork.Add(coord))
            return;

        if (pendingFoliageManagementWork.Count >= Mathf.Max(1, grassSettings.maxQueuedFoliageManagementWork))
        {
            queuedFoliageManagementWork.Remove(coord);
            return;
        }

        pendingFoliageManagementWork.Add(new FoliageManagementWorkItem(coord));
    }

    private void ProcessPendingFoliageManagementWork(
        ChunkManager chunkManager,
        ChunkCoord viewerCoord,
        SubChunkCoord viewerGlobalSubChunk,
        long sharedBudgetStart,
        float sharedBudgetMs)
    {
        using var processFoliageManagementQueueScope = ProcessFoliageManagementQueueMarker.Auto();

        int maxChunks = Mathf.Max(1, grassSettings.maxFoliageManagementChunksPerFrame);
        float budgetMs = Mathf.Max(0f, grassSettings.foliageManagementBudgetMsPerFrame);
        long frameStart = TerrainGenerationProfiler.GetTimestamp();
        int managedCount = 0;

        deferredFoliageManagementRetries.Clear();

        while (pendingFoliageManagementWork.Count > 0 && managedCount < maxChunks)
        {
            if (!HasFoliageWorkBudgetRemaining(frameStart, budgetMs, sharedBudgetStart, sharedBudgetMs))
                break;

            FoliageManagementWorkItem workItem = PopNearestFoliageManagementWork(viewerCoord);
            queuedFoliageManagementWork.Remove(workItem.ChunkCoord);

            bool shouldRetry = ManageFoliageForChunk(
                chunkManager,
                viewerCoord,
                viewerGlobalSubChunk,
                workItem.ChunkCoord);

            if (shouldRetry)
                deferredFoliageManagementRetries.Add(workItem.ChunkCoord);

            managedCount++;

            if (!HasFoliageWorkBudgetRemaining(frameStart, budgetMs, sharedBudgetStart, sharedBudgetMs))
                break;
        }

        for (int i = 0; i < deferredFoliageManagementRetries.Count; i++)
        {
            EnqueueFoliageManagementWork(deferredFoliageManagementRetries[i]);
        }

        deferredFoliageManagementRetries.Clear();
        RecordFoliageQueueSnapshot(pendingTreeRepresentationWork.Count);
    }

    private bool ManageFoliageForChunk(
        ChunkManager chunkManager,
        ChunkCoord viewerCoord,
        SubChunkCoord viewerGlobalSubChunk,
        ChunkCoord coord)
    {
        ChunkRecord record = chunkManager.GetChunkRecord(coord);
        ChunkRuntime runtime = chunkManager.GetChunkRuntime(record);

        if (record == null || runtime == null)
            return false;

        using (HandleSubChunkEnsureRuntimeMarker.Auto())
        {
            EnsureFoliageRuntimeExists(runtime, record);
        }

        bool useFlowers;
        bool useLilyPads;
        bool useCattails;
        bool useClover;
        bool preGenerateClover;
        bool useDandelions;
        bool useTrees;
        bool useBushes;
        bool useRocks;
        bool useFoliage;

        using (HandleSubChunkRangeChecksMarker.Auto())
        {
            useFlowers = IsWithinFlowerRenderRange(viewerCoord, coord);
            useLilyPads = IsWithinLilyPadRenderRange(viewerCoord, coord);
            useCattails = IsWithinCattailRenderRange(viewerCoord, coord);
            useClover = IsWithinCloverRenderRange(viewerCoord, coord);
            preGenerateClover = IsWithinCloverGenerationRange(viewerCoord, coord);
            useDandelions = IsWithinDandelionRenderRange(viewerCoord, coord);
            useTrees = IsWithinTreeRenderRange(viewerCoord, coord);
            useBushes = IsWithinBushRenderRange(viewerCoord, coord);
            useRocks = IsWithinRockRenderRange(viewerCoord, coord);
            useFoliage = useFlowers || useLilyPads || useCattails || useClover || preGenerateClover || useDandelions || useTrees || useBushes || useRocks;
        }

        if (!HasRequiredTerrainData(record))
        {
            using (HandleSubChunkClearInactiveMarker.Auto())
            {
                runtime.FoliageRuntime.SetVisible(false);
            }

            return useFoliage;
        }

        if (!useFoliage)
        {
            using (HandleSubChunkClearInactiveMarker.Auto())
            {
                runtime.FoliageRuntime.ClearCachedBatches();
                runtime.FoliageRuntime.SetVisible(false);
            }

            return false;
        }

        using (HandleSubChunkTreesMarker.Auto())
        {
            if (useTrees)
            {
                EnqueueTreeRepresentationRebuildIfNeeded(runtime, record, viewerCoord);
            }
            else
            {
                runtime.FoliageRuntime.TreePlacementReady = false;
            }
        }

        using (HandleSubChunkBushesMarker.Auto())
        {
            if (useBushes)
            {
                EnsureBushesGenerated(record);
                RebuildBushGameObjectsIfNeeded(runtime, record);
            }
            else
            {
                runtime.FoliageRuntime.ClearBushGameObjects();
            }
        }

        using (HandleSubChunkRocksMarker.Auto())
        {
            if (useRocks)
            {
                EnsureRocksGenerated(record);
                RebuildRockGameObjectsIfNeeded(runtime, record);
            }
            else
            {
                runtime.FoliageRuntime.ClearRockGameObjects();
            }
        }

        using (HandleSubChunkFlowersMarker.Auto())
        {
            if (useFlowers && assets.HasFlowerRenderAssets())
            {
                if (record.FoliageData == null || !record.FoliageData.flowersGenerated)
                {
                    EnqueueGroundFoliageGeneration(record, FoliagePublicationKind.Flower);
                }
                else if (!runtime.FoliageRuntime.HasValidFlowerRenderData())
                {
                    EnqueueFoliageBatchRebuild(record, FoliagePublicationKind.Flower);
                }
            }
            else
            {
                runtime.FoliageRuntime.ClearFlowerBatches();
            }
        }

        using (HandleSubChunkCloverMarker.Auto())
        {
            if ((useClover || preGenerateClover) && assets.HasCloverRenderAssets())
            {
                if (record.FoliageData == null || !record.FoliageData.cloverGenerated)
                {
                    EnqueueGroundFoliageGeneration(record, FoliagePublicationKind.Clover);
                }
                else if (!runtime.FoliageRuntime.HasValidCloverRenderData())
                {
                    EnqueueFoliageBatchRebuild(record, FoliagePublicationKind.Clover);
                }
            }
            else
            {
                runtime.FoliageRuntime.ClearCloverBatches();
            }
        }

        using (HandleSubChunkLilyPadsMarker.Auto())
        {
            if (useLilyPads && assets.HasLilyPadRenderAssets())
            {
                if (record.FoliageData == null || !record.FoliageData.lilyPadsGenerated)
                    EnqueueGroundFoliageGeneration(record, FoliagePublicationKind.LilyPad);
                else if (!runtime.FoliageRuntime.HasValidLilyPadRenderData())
                    EnqueueFoliageBatchRebuild(record, FoliagePublicationKind.LilyPad);
            }
            else
            {
                runtime.FoliageRuntime.ClearLilyPadBatches();
            }
        }

        using (HandleSubChunkCattailsMarker.Auto())
        {
            if (useCattails && assets.HasCattailRenderAssets())
            {
                if (record.FoliageData == null || !record.FoliageData.cattailsGenerated)
                    EnqueueGroundFoliageGeneration(record, FoliagePublicationKind.Cattail);
                else if (!runtime.FoliageRuntime.HasValidCattailRenderData())
                    EnqueueFoliageBatchRebuild(record, FoliagePublicationKind.Cattail);
            }
            else
            {
                runtime.FoliageRuntime.ClearCattailBatches();
            }
        }

        using (HandleSubChunkDandelionsMarker.Auto())
        {
            if (useDandelions && assets.HasDandelionRenderAssets())
            {
                if (record.FoliageData == null || !record.FoliageData.dandelionsGenerated)
                {
                    EnqueueGroundFoliageGeneration(record, FoliagePublicationKind.Dandelion);
                }
                else if (!runtime.FoliageRuntime.HasValidDandelionRenderData())
                {
                    EnqueueFoliageBatchRebuild(record, FoliagePublicationKind.Dandelion);
                }
            }
            else
            {
                runtime.FoliageRuntime.ClearDandelionBatches();
            }
        }

        using (HandleSubChunkSetVisibleMarker.Auto())
        {
            runtime.FoliageRuntime.SetVisible(true);
        }

        return ShouldRetryFoliageManagement(
            record,
            runtime,
            viewerCoord,
            useFlowers,
            useLilyPads,
            useCattails,
            useClover,
            useDandelions,
            useTrees,
            useBushes,
            useRocks,
            viewerGlobalSubChunk);
    }

    private bool ShouldRetryFoliageManagement(
        ChunkRecord record,
        ChunkRuntime runtime,
        ChunkCoord viewerCoord,
        bool useFlowers,
        bool useLilyPads,
        bool useCattails,
        bool useClover,
        bool useDandelions,
        bool useTrees,
        bool useBushes,
        bool useRocks,
        SubChunkCoord viewerGlobalSubChunk)
    {
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;

        if (useFlowers &&
            assets.HasFlowerRenderAssets() &&
            !foliageRuntime.HasValidFlowerRenderData())
        {
            return true;
        }

        if (useLilyPads && assets.HasLilyPadRenderAssets() && !foliageRuntime.HasValidLilyPadRenderData())
            return true;

        if (useCattails && assets.HasCattailRenderAssets() && !foliageRuntime.HasValidCattailRenderData())
            return true;

        if ((useClover || IsWithinCloverGenerationRange(viewerCoord,record.ChunkCoord)) &&
            assets.HasCloverRenderAssets() &&
            !foliageRuntime.HasValidCloverRenderData())
        {
            return true;
        }

        if (useDandelions &&
            assets.HasDandelionRenderAssets() &&
            !foliageRuntime.HasValidDandelionRenderData())
        {
            return true;
        }

        if (useTrees &&
            !foliageRuntime.TreePlacementReady)
        {
            return true;
        }

        if (useBushes && !foliageRuntime.HasCurrentBushRepresentation())
            return true;

        if (useRocks && !foliageRuntime.HasCurrentRockRepresentation())
            return true;

        return false;
    }

    public void AccumulateVisibleFoliageRenderStats(
        ChunkManager chunkManager,
        ChunkCoord viewerCoord,
        List<ChunkCoord> orderedActiveCoords,
        ref WorldRenderStatsDebugInfo stats)
    {
        for (int i = 0; i < orderedActiveCoords.Count; i++)
        {
            ChunkCoord coord = orderedActiveCoords[i];
            ChunkRecord record = chunkManager.GetChunkRecord(coord);
            ChunkRuntime runtime = chunkManager.GetChunkRuntime(record);

            if (record == null || runtime == null || runtime.FoliageRuntime == null || !runtime.IsFoliageRenderVisible)
                continue;

            bool useFlowers = IsWithinFlowerRenderRange(viewerCoord, coord);
            bool useLilyPads = IsWithinLilyPadRenderRange(viewerCoord, coord);
            bool useCattails = IsWithinCattailRenderRange(viewerCoord, coord);
            bool useClover = IsWithinCloverRenderRange(viewerCoord, coord);
            bool preGenerateClover = IsWithinCloverGenerationRange(viewerCoord, coord);
            bool useDandelions = IsWithinDandelionRenderRange(viewerCoord, coord);
            bool useTrees = IsWithinTreeRenderRange(viewerCoord, coord);
            bool useBushes = IsWithinBushRenderRange(viewerCoord, coord);
            bool useRocks = IsWithinRockRenderRange(viewerCoord, coord);
            bool useFoliage = useFlowers || useLilyPads || useCattails || useClover || preGenerateClover || useDandelions || useTrees || useBushes || useRocks;

            if (!HasRequiredTerrainData(record) || !useFoliage)
                continue;

            if (useFlowers && assets.HasFlowerRenderAssets())
                runtime.FoliageRuntime.AccumulateFlowerRenderStats(ref stats);

            if (useLilyPads && assets.HasLilyPadRenderAssets())
                runtime.FoliageRuntime.AccumulateLilyPadRenderStats(ref stats);

            if (useCattails && assets.HasCattailRenderAssets())
                runtime.FoliageRuntime.AccumulateCattailRenderStats(ref stats);

            if (useClover && assets.HasCloverRenderAssets())
                runtime.FoliageRuntime.AccumulateCloverRenderStats(ref stats);

            if (useDandelions && assets.HasDandelionRenderAssets())
                runtime.FoliageRuntime.AccumulateDandelionRenderStats(ref stats);

            if (useBushes)
                runtime.FoliageRuntime.AccumulateBushGameObjectRenderStats(ref stats);

            if (useRocks)
                runtime.FoliageRuntime.AccumulateRockGameObjectRenderStats(ref stats);
        }
    }

    private void EnsureTreesGenerated(ChunkRecord record) => placement.EnsureTreesGenerated(record);

    private void EnsureBushesGenerated(ChunkRecord record) => placement.EnsureBushesGenerated(record);

    private void EnsureRocksGenerated(ChunkRecord record) => placement.EnsureRocksGenerated(record);

    private void RebuildTreeRepresentationIfNeeded(ChunkRuntime runtime, ChunkRecord record, ChunkCoord viewerCoord)
    {
        runtime.FoliageRuntime.TreePlacementReady = true;
    }

    private void RebuildBushGameObjectsIfNeeded(ChunkRuntime runtime, ChunkRecord record)
    {
        if (!runtime.FoliageRuntime.HasCurrentBushRepresentation())
            EnqueueFoliageBatchRebuild(record, FoliagePublicationKind.Bush);
    }

    private void RebuildRockGameObjectsIfNeeded(ChunkRuntime runtime, ChunkRecord record)
    {
        if (!runtime.FoliageRuntime.HasCurrentRockRepresentation())
            EnqueueFoliageBatchRebuild(record, FoliagePublicationKind.Rock);
    }

    private void EnqueueGroundFoliageGeneration(ChunkRecord record, FoliagePublicationKind kind) => discoveryScheduler.Enqueue(record, kind);

    private void ProcessPendingGroundFoliageGenerationWork(ChunkManager manager, ChunkCoord viewer,
        long sharedStart, float sharedMs) => discoveryScheduler.Update(manager, viewer, sharedStart, sharedMs);

    private void EnqueueFoliageBatchRebuild(ChunkRecord record, FoliagePublicationKind kind) => publicationScheduler.Enqueue(record, kind);

    private void EnqueueTreeRepresentationRebuildIfNeeded(ChunkRuntime runtime, ChunkRecord record, ChunkCoord viewerCoord)
    {
        if (!runtime.FoliageRuntime.TreePlacementReady) EnqueueTreeRepresentationRebuild(record);
    }

    private void EnqueueTreeRepresentationRebuild(ChunkRecord record)
    {
        if (record == null || !queuedTreeRepresentationWork.Add(record.ChunkCoord))
            return;

        pendingTreeRepresentationWork.Add(new TreeRepresentationWorkItem(record.ChunkCoord));
        RecordFoliageQueueSnapshot(pendingTreeRepresentationWork.Count);
    }

    private void ProcessPendingFoliageBatchWork(ChunkManager manager, ChunkCoord viewer, SubChunkCoord subChunk,
        long sharedStart, float sharedMs) => publicationScheduler.Update(manager, viewer, sharedStart, sharedMs);

    private void ProcessPendingTreeRepresentationWork(
        ChunkManager chunkManager,
        ChunkCoord viewerCoord,
        long sharedBudgetStart,
        float sharedBudgetMs)
    {
        using var processTreeRepresentationQueueScope = ProcessTreeRepresentationQueueMarker.Auto();

        int maxRebuilds = Mathf.Max(1, treeSettings.maxTreeRepresentationRebuildsPerFrame);
        float budgetMs = Mathf.Max(0f, treeSettings.treeRepresentationRebuildBudgetMsPerFrame);
        long frameStart = TerrainGenerationProfiler.GetTimestamp();
        int rebuildCount = 0;

        while (pendingTreeRepresentationWork.Count > 0 && rebuildCount < maxRebuilds)
        {
            if (!HasFoliageWorkBudgetRemaining(frameStart, budgetMs, sharedBudgetStart, sharedBudgetMs))
                break;

            TreeRepresentationWorkItem workItem = PopNearestTreeRepresentationWork(viewerCoord);
            queuedTreeRepresentationWork.Remove(workItem.ChunkCoord);

            ChunkRecord record = chunkManager.GetChunkRecord(workItem.ChunkCoord);
            ChunkRuntime runtime = chunkManager.GetChunkRuntime(record);

            if (record == null || runtime == null || runtime.FoliageRuntime == null || !HasRequiredTerrainData(record))
                continue;

            if (!IsWithinTreeRenderRange(viewerCoord, workItem.ChunkCoord))
            {
                runtime.FoliageRuntime.TreePlacementReady = false;
                continue;
            }

            if (runtime.FoliageRuntime.TreePlacementReady)
                continue;

            EnsureTreesGenerated(record);
            RebuildTreeRepresentationIfNeeded(runtime, record, viewerCoord);
            rebuildCount++;

            if (!HasFoliageWorkBudgetRemaining(frameStart, budgetMs, sharedBudgetStart, sharedBudgetMs))
                break;
        }

        RecordFoliageQueueSnapshot(pendingTreeRepresentationWork.Count);
    }

    private static bool HasFoliageWorkBudgetRemaining(long localStart, float localMs, long sharedStart, float sharedMs) =>
        FoliageWorkBudget.HasRemaining(localStart, localMs, sharedStart, sharedMs);

    private void PruneStaleFoliageQueues(
        ChunkManager chunkManager,
        ChunkCoord viewerCoord,
        SubChunkCoord viewerGlobalSubChunk)
    {
        using var pruneStaleFoliageQueuesScope = PruneStaleFoliageQueuesMarker.Auto();

        queuedFoliageManagementWork.Clear();
        for (int i = pendingFoliageManagementWork.Count - 1; i >= 0; i--)
        {
            FoliageManagementWorkItem workItem = pendingFoliageManagementWork[i];
            ChunkRecord record = chunkManager.GetChunkRecord(workItem.ChunkCoord);

            if (!IsFoliageManagementWorkStillWanted(record, viewerCoord, workItem.ChunkCoord) ||
                !queuedFoliageManagementWork.Add(workItem.ChunkCoord))
            {
                pendingFoliageManagementWork.RemoveAt(i);
            }
        }

        discoveryScheduler.Prune(chunkManager, viewerCoord);

        publicationScheduler.Prune(chunkManager, viewerCoord);

        queuedTreeRepresentationWork.Clear();
        for (int i = pendingTreeRepresentationWork.Count - 1; i >= 0; i--)
        {
            TreeRepresentationWorkItem workItem = pendingTreeRepresentationWork[i];
            ChunkRecord record = chunkManager.GetChunkRecord(workItem.ChunkCoord);
            ChunkRuntime runtime = chunkManager.GetChunkRuntime(record);

            if (record == null ||
                runtime == null ||
                runtime.FoliageRuntime == null ||
                !HasRequiredTerrainData(record) ||
                !IsWithinTreeRenderRange(viewerCoord, workItem.ChunkCoord) ||
                runtime.FoliageRuntime.TreePlacementReady ||
                !queuedTreeRepresentationWork.Add(workItem.ChunkCoord))
            {
                pendingTreeRepresentationWork.RemoveAt(i);
            }
        }

        RecordFoliageQueueSnapshot(pendingTreeRepresentationWork.Count);
    }

    private bool IsFoliageManagementWorkStillWanted(
        ChunkRecord record,
        ChunkCoord viewerCoord,
        ChunkCoord coord)
    {
        if (record == null)
            return false;

        return IsWithinLilyPadRenderRange(viewerCoord, coord) ||
               IsWithinCattailRenderRange(viewerCoord, coord) ||
               IsWithinFlowerRenderRange(viewerCoord, coord) ||
               IsWithinCloverRenderRange(viewerCoord, coord) ||
               IsWithinCloverGenerationRange(viewerCoord, coord) ||
               IsWithinDandelionRenderRange(viewerCoord, coord) ||
               IsWithinTreeRenderRange(viewerCoord, coord) ||
               IsWithinBushRenderRange(viewerCoord, coord) ||
               IsWithinRockRenderRange(viewerCoord, coord);
    }

    private FoliageManagementWorkItem PopNearestFoliageManagementWork(ChunkCoord viewerCoord)
    {
        int bestIndex = 0;
        int bestDistance = int.MaxValue;

        for (int i = 0; i < pendingFoliageManagementWork.Count; i++)
        {
            FoliageManagementWorkItem candidate = pendingFoliageManagementWork[i];
            int distance = GetChunkRadialRing(viewerCoord, candidate.ChunkCoord);

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = i;
        }

        FoliageManagementWorkItem result = pendingFoliageManagementWork[bestIndex];
        pendingFoliageManagementWork.RemoveAt(bestIndex);
        return result;
    }

    private TreeRepresentationWorkItem PopNearestTreeRepresentationWork(ChunkCoord viewerCoord)
    {
        int bestIndex = 0;
        int bestDistance = int.MaxValue;

        for (int i = 0; i < pendingTreeRepresentationWork.Count; i++)
        {
            TreeRepresentationWorkItem candidate = pendingTreeRepresentationWork[i];
            int distance = GetChunkRadialRing(viewerCoord, candidate.ChunkCoord);

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = i;
        }

        TreeRepresentationWorkItem result = pendingTreeRepresentationWork[bestIndex];
        pendingTreeRepresentationWork.RemoveAt(bestIndex);
        return result;
    }

    private bool IsFoliageBatchWorkStillWanted(
        ChunkRecord record,
        ChunkCoord viewerCoord,
        FoliagePublicationKind workType)
    {
        switch (workType)
        {
            case FoliagePublicationKind.Flower:
                return IsWithinFlowerRenderRange(viewerCoord, record.ChunkCoord) &&
                       assets.HasFlowerRenderAssets();
            case FoliagePublicationKind.LilyPad:
                return IsWithinLilyPadRenderRange(viewerCoord, record.ChunkCoord) &&
                       assets.HasLilyPadRenderAssets();
            case FoliagePublicationKind.Cattail:
                return IsWithinCattailRenderRange(viewerCoord, record.ChunkCoord) &&
                       assets.HasCattailRenderAssets();
            case FoliagePublicationKind.Clover:
                return IsWithinCloverGenerationRange(viewerCoord, record.ChunkCoord) &&
                       assets.HasCloverRenderAssets();
            case FoliagePublicationKind.Dandelion:
                return IsWithinDandelionRenderRange(viewerCoord, record.ChunkCoord) &&
                       assets.HasDandelionRenderAssets();
            case FoliagePublicationKind.Bush:
                return IsWithinBushRenderRange(viewerCoord, record.ChunkCoord);
            case FoliagePublicationKind.Rock:
                return IsWithinRockRenderRange(viewerCoord, record.ChunkCoord);
            default:
                return false;
        }
    }

    private bool IsGroundFoliageGenerationStillWanted(
        ChunkRecord record,
        ChunkCoord viewerCoord,
        FoliagePublicationKind generationType)
    {
        switch (generationType)
        {
            case FoliagePublicationKind.Flower:
                return IsFlowerSystemEnabled() &&
                       assets.HasFlowerRenderAssets() &&
                       IsWithinFlowerRenderRange(viewerCoord, record.ChunkCoord) &&
                       (record.FoliageData == null || !record.FoliageData.flowersGenerated);
            case FoliagePublicationKind.LilyPad:
                return IsLilyPadSystemEnabled() &&
                       assets.HasLilyPadRenderAssets() &&
                       record.WaterStateMap != null &&
                       IsWithinLilyPadRenderRange(viewerCoord, record.ChunkCoord) &&
                       (record.FoliageData == null || !record.FoliageData.lilyPadsGenerated);
            case FoliagePublicationKind.Cattail:
                return IsCattailSystemEnabled() &&
                       assets.HasCattailRenderAssets() &&
                       record.WaterStateMap != null && record.SurfaceTypeMap != null &&
                       IsWithinCattailRenderRange(viewerCoord, record.ChunkCoord) &&
                       (record.FoliageData == null || !record.FoliageData.cattailsGenerated);
            case FoliagePublicationKind.Clover:
                return IsCloverSystemEnabled() &&
                       assets.HasCloverRenderAssets() &&
                       // Grass also requests exclusions beyond clover's own rendering radius.
                       (record.FoliageData == null || !record.FoliageData.cloverGenerated);
            case FoliagePublicationKind.Dandelion:
                return IsDandelionSystemEnabled() &&
                       assets.HasDandelionRenderAssets() &&
                       IsWithinDandelionRenderRange(viewerCoord, record.ChunkCoord) &&
                       (record.FoliageData == null || !record.FoliageData.dandelionsGenerated);
            default:
                return false;
        }
    }

    private bool IsWithinTreeRenderRange(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        int ring = GetChunkRadialRing(viewerCoord, targetCoord);
        return ring <= treeSettings.gameObjectTreeChunkRingRadius + 2;
    }

    private bool IsWithinBushRenderRange(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        int ring = GetChunkRadialRing(viewerCoord, targetCoord);
        return ring <= treeSettings.gameObjectBushChunkRingRadius;
    }

    private bool IsWithinRockRenderRange(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        int ring = GetChunkRadialRing(viewerCoord, targetCoord);
        return ring <= treeSettings.gameObjectRockChunkRingRadius;
    }

    public static int GetChunkRadialRing(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        int dx = Mathf.Abs(targetCoord.x - viewerCoord.x);
        int dz = Mathf.Abs(targetCoord.z - viewerCoord.z);
        return Mathf.CeilToInt(Mathf.Sqrt((float)dx * dx + (float)dz * dz));
    }

    // Both render paths use these exact prefix counts. No independent GPU rank/hash
    // conversion: ties, minimum-one behavior and billboard rounding stay identical.

    private bool IsWithinFlowerRenderRange(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        if (!IsFlowerSystemEnabled())
            return false;

        return IsWithinChunkRadius(viewerCoord, targetCoord, flowerSettings.activeRingRadius);
    }

    private bool IsWithinLilyPadRenderRange(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        return IsLilyPadSystemEnabled() &&
               IsWithinChunkRadius(viewerCoord, targetCoord, lilyPadSettings.activeRingRadius);
    }

    private bool IsWithinCattailRenderRange(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        return IsCattailSystemEnabled() &&
               IsWithinChunkRadius(viewerCoord, targetCoord, cattailSettings.activeRingRadius);
    }

    private bool IsWithinCloverRenderRange(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        if (!IsCloverSystemEnabled())
            return false;

        return CloverStreamingPolicy.WithinRange(GetCloverViewer(viewerCoord),targetCoord,chunkSize,worldScale,
            CloverStreamingPolicy.RenderDistance(cloverSettings,chunkSize,worldScale)+CloverGeometryMargin);
    }

    private bool IsWithinCloverGenerationRange(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        if (!IsCloverSystemEnabled())
            return false;

        float radius=CloverStreamingPolicy.RenderDistance(cloverSettings,chunkSize,worldScale)+CloverGeometryMargin+
            Mathf.Max(0,cloverSettings.preGenerationRingPadding)*Mathf.Max(.001f,chunkSize*worldScale);
        return CloverStreamingPolicy.WithinRange(GetCloverViewer(viewerCoord),targetCoord,chunkSize,worldScale,radius);
    }
    private Vector3 GetCloverViewer(ChunkCoord viewerCoord) => hasCloverViewerPosition?cloverViewerPosition:
        new Vector3((viewerCoord.x+.5f)*chunkSize*worldScale,0,(viewerCoord.z+.5f)*chunkSize*worldScale);
    private float CloverGeometryMargin => assets.CloverMeshRadius*Mathf.Max(1,Mathf.Max(cloverSettings.uniformScaleRange.x,cloverSettings.uniformScaleRange.y));

    private bool IsWithinDandelionRenderRange(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        if (!IsDandelionSystemEnabled())
            return false;

        int activeRingRadius = Mathf.Max(0, dandelionSettings.activeRingRadius);
        return IsWithinChunkRadius(viewerCoord, targetCoord, activeRingRadius);
    }

    // Circular membership of whole logical chunks. Terrain LOD rings are independent.
    public static bool IsWithinChunkRadius(ChunkCoord viewerCoord, ChunkCoord targetCoord, int radius)
    {
        if (radius < 0) return false;
        double dx = (double)targetCoord.x - viewerCoord.x;
        double dz = (double)targetCoord.z - viewerCoord.z;
        return dx * dx + dz * dz <= (double)radius * radius;
    }

    private bool HasRequiredTerrainData(ChunkRecord record) => FoliageWorkBudget.HasTerrainInputs(record);

    private bool IsFlowerSystemEnabled()
    {
        return flowerSettings != null && flowerSettings.enableFlowers;
    }

    private bool IsLilyPadSystemEnabled()
    {
        return lilyPadSettings != null && lilyPadSettings.enableLilyPads;
    }

    private bool IsCattailSystemEnabled()
    {
        return cattailSettings != null && cattailSettings.enableCattails;
    }

    private bool IsCloverSystemEnabled()
    {
        return cloverSettings != null && cloverSettings.enableClover;
    }

    private bool IsDandelionSystemEnabled()
    {
        return dandelionSettings != null && dandelionSettings.enableDandelions;
    }

    private readonly struct FoliageManagementWorkItem
    {
        public readonly ChunkCoord ChunkCoord;

        public FoliageManagementWorkItem(ChunkCoord chunkCoord)
        {
            ChunkCoord = chunkCoord;
        }
    }

    private readonly struct TreeRepresentationWorkItem
    {
        public readonly ChunkCoord ChunkCoord;

        public TreeRepresentationWorkItem(ChunkCoord chunkCoord)
        {
            ChunkCoord = chunkCoord;
        }
    }

    private void EnsureFoliageRuntimeExists(ChunkRuntime chunkRuntime, ChunkRecord record)
    {
        if (chunkRuntime.FoliageRuntime != null && chunkRuntime.FoliageRuntime.IsCreated) return;
        var runtime = new ChunkFoliageRuntime();
        var root = new GameObject($"Foliage_{record.ChunkCoord.x}_{record.ChunkCoord.z}");
        root.transform.SetParent(chunkRuntime.RootTransform, false); runtime.root = root.transform;
        assets.ApplyTo(runtime); chunkRuntime.FoliageRuntime = runtime;
        runtime.SetRenderVisible(chunkRuntime.IsFoliageRenderVisible);
        runtime.SetShadowCasterVisible(chunkRuntime.IsFoliageShadowCasterVisible); runtime.SetVisible(false);
    }

}
