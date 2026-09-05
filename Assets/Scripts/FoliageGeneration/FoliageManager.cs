using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

public class FoliageManager
{
    private const string BillboardRenderFadeKeyword = "_BILLBOARD_RENDER_FADE_ON";

    private static readonly ProfilerMarker HandleViewerSubChunkChangedMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleViewerSubChunkChanged");
    private static readonly ProfilerMarker HandleSubChunkLoopMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunkLoop");
    private static readonly ProfilerMarker HandleSubChunkEnsureRuntimeMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.EnsureRuntime");
    private static readonly ProfilerMarker HandleSubChunkRangeChecksMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.RangeChecks");
    private static readonly ProfilerMarker HandleSubChunkClearInactiveMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.ClearInactive");
    private static readonly ProfilerMarker HandleSubChunkTreesMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.Trees");
    private static readonly ProfilerMarker HandleSubChunkBushesMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.Bushes");
    private static readonly ProfilerMarker HandleSubChunkRocksMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.Rocks");
    private static readonly ProfilerMarker HandleSubChunkFlowersMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.Flowers");
    private static readonly ProfilerMarker HandleSubChunkCloverMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.Clover");
    private static readonly ProfilerMarker HandleSubChunkDandelionsMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.Dandelions");
    private static readonly ProfilerMarker HandleSubChunkGrassMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.Grass");
    private static readonly ProfilerMarker HandleSubChunkBillboardGrassMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.BillboardGrass");
    private static readonly ProfilerMarker HandleSubChunkSetVisibleMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleSubChunk.SetVisible");
    private static readonly ProfilerMarker DrawVisibleFoliageEveryFrameMarker = new ProfilerMarker("FS.Streaming.Foliage.DrawVisibleEveryFrame");
    private static readonly ProfilerMarker QueueFoliageManagementMarker = new ProfilerMarker("FS.Streaming.Foliage.QueueManagement");
    private static readonly ProfilerMarker ProcessFoliageManagementQueueMarker = new ProfilerMarker("FS.Streaming.Foliage.ProcessManagementQueue");
    private static readonly ProfilerMarker DrawFoliageOnlyMarker = new ProfilerMarker("FS.Streaming.Foliage.DrawOnly");
    private static readonly ProfilerMarker PruneStaleFoliageQueuesMarker = new ProfilerMarker("FS.Streaming.Foliage.PruneStaleQueues");
    private static readonly ProfilerMarker CompleteActiveGrassJobsMarker = new ProfilerMarker("FS.Streaming.Foliage.CompleteActiveGrassJobs");
    private static readonly ProfilerMarker ProcessGroundFoliageGenerationMarker = new ProfilerMarker("FS.Streaming.Foliage.ProcessGroundGenerationQueue");
    private static readonly ProfilerMarker ProcessGrassSubChunkQueueMarker = new ProfilerMarker("FS.Streaming.Foliage.ProcessGrassSubChunkQueue");
    private static readonly ProfilerMarker ProcessFoliageBatchQueueMarker = new ProfilerMarker("FS.Streaming.Foliage.ProcessBatchQueue");
    private static readonly ProfilerMarker ProcessTreeRepresentationQueueMarker = new ProfilerMarker("FS.Streaming.Foliage.ProcessTreeRepresentationQueue");

    private readonly Transform foliageParent;
    private readonly GrassSettings grassSettings;
    private readonly FlowerSettings flowerSettings;
    private readonly CloverSettings cloverSettings;
    private readonly DandelionSettings dandelionSettings;
    private readonly TreeSettings treeSettings;
    private readonly int worldSeed;
    private readonly int chunkSize;
    private readonly float worldScale;
    private readonly float meshHeightMultiplier;

    private Mesh grassMesh;
    private Material grassMaterial;
    private int grassInstanceDataPropertyId;

    private Mesh billboardGrassMesh;
    private Material billboardGrassMaterial;
    private bool ownsBillboardGrassMaterial;

    private Mesh flowerMesh;
    private Material flowerMaterial;
    private int flowerPetalColorPropertyId;

    private CloverRenderData[] cloverRenderData;
    private int cloverInstanceDataPropertyId;

    private Mesh dandelionMesh;
    private Material dandelionMaterial;
    private int dandelionInstanceDataPropertyId;

    private TreeBillboardRenderData mapleTreeBillboard;
    private TreeBillboardRenderData sugarMapleTreeBillboard;
    private TreeBillboardRenderData birchAspenTreeBillboard;
    private TreeBillboardRenderData beechTreeBillboard;
    private TreeBillboardRenderData spruceTreeBillboard;
    private TreeBillboardRenderData whitePineTreeBillboard;
    private TreeBillboardRenderData oakTreeBillboard;
    private TreeBillboardRenderData fallbackTreeBillboard;
    private TreeBillboardRenderData grasslandMapleTreeBillboard;
    private TreeBillboardRenderData grasslandBirchAspenTreeBillboard;
    private TreeBillboardRenderData grasslandWhitePineTreeBillboard;
    private TreeBillboardRenderData grasslandOakTreeBillboard;
    private TreeBillboardRenderData grasslandWillowTreeBillboard;
    private TreeBillboardRenderData grasslandFallbackTreeBillboard;
    private readonly List<GrassSubChunkWorkItem> pendingGrassSubChunkWork = new();
    private readonly HashSet<GrassSubChunkWorkKey> queuedGrassSubChunks = new();
    private readonly List<ActiveGrassSubChunkGenerationWorkItem> activeGrassSubChunkGenerationWork = new();
    private readonly HashSet<GrassSubChunkWorkKey> activeGrassSubChunkGenerations = new();
    private readonly HashSet<ChunkCoord> dirtyGrassChunks = new();
    private readonly List<FoliageBatchWorkItem> pendingFoliageBatchWork = new();
    private readonly HashSet<FoliageBatchWorkKey> queuedFoliageBatchWork = new();
    private readonly List<TreeRepresentationWorkItem> pendingTreeRepresentationWork = new();
    private readonly HashSet<ChunkCoord> queuedTreeRepresentationWork = new();
    private readonly List<GroundFoliageGenerationWorkItem> pendingGroundFoliageGenerationWork = new();
    private readonly HashSet<GroundFoliageGenerationWorkKey> queuedGroundFoliageGenerationWork = new();
    private readonly List<FoliageManagementWorkItem> pendingFoliageManagementWork = new();
    private readonly HashSet<ChunkCoord> queuedFoliageManagementWork = new();
    private readonly List<ChunkCoord> deferredFoliageManagementRetries = new();
    private float lastObservedBillboardSpawnChance;
    private int lastObservedBillboardCellsPerAxis;
    private int lastObservedNearGrassPrecomputeChunkPadding;

    public FoliageManager(Transform foliageParent, GrassSettings grassSettings, FlowerSettings flowerSettings, CloverSettings cloverSettings, DandelionSettings dandelionSettings, TreeSettings treeSettings, int worldSeed,
        int chunkSize, float worldScale, float meshHeightMultiplier)
    {
        this.foliageParent = foliageParent;
        this.grassSettings = grassSettings;
        this.flowerSettings = flowerSettings;
        this.cloverSettings = cloverSettings;
        this.dandelionSettings = dandelionSettings;
        this.treeSettings = treeSettings;
        this.worldSeed = worldSeed;
        this.chunkSize = chunkSize;
        this.worldScale = worldScale;
        this.meshHeightMultiplier = meshHeightMultiplier;

        ResolveGrassRenderAssets();
        ResolveFlowerRenderAssets();
        ResolveCloverRenderAssets();
        ResolveDandelionRenderAssets();
        ResolveTreeRenderAssets();
        lastObservedBillboardSpawnChance = grassSettings.billboardSpawnChance;
        lastObservedBillboardCellsPerAxis = Mathf.Max(1, grassSettings.billboardCellsPerAxis);
        lastObservedNearGrassPrecomputeChunkPadding = Mathf.Max(0, grassSettings.nearGrassPrecomputeChunkPadding);
    }

    public void Dispose()
    {
        for (int i = 0; i < activeGrassSubChunkGenerationWork.Count; i++)
        {
            activeGrassSubChunkGenerationWork[i].GenerationJob.Dispose();
        }

        activeGrassSubChunkGenerationWork.Clear();
        activeGrassSubChunkGenerations.Clear();
        pendingGrassSubChunkWork.Clear();
        queuedGrassSubChunks.Clear();
        pendingGroundFoliageGenerationWork.Clear();
        queuedGroundFoliageGenerationWork.Clear();
        pendingFoliageBatchWork.Clear();
        queuedFoliageBatchWork.Clear();
        pendingTreeRepresentationWork.Clear();
        queuedTreeRepresentationWork.Clear();
        pendingFoliageManagementWork.Clear();
        queuedFoliageManagementWork.Clear();
        deferredFoliageManagementRetries.Clear();
        dirtyGrassChunks.Clear();
        DestroyOwnedBillboardGrassMaterial();
        RecordFoliageQueueSnapshot(0);
    }

    private void RecordFoliageQueueSnapshot(int treeRepresentationWorkCount = -1)
    {
        TerrainGenerationProfiler.RecordFoliageQueueSnapshot(
            pendingGrassSubChunkWork.Count + activeGrassSubChunkGenerationWork.Count,
            queuedGrassSubChunks.Count + activeGrassSubChunkGenerations.Count,
            dirtyGrassChunks.Count,
            pendingFoliageBatchWork.Count,
            treeRepresentationWorkCount,
            pendingGroundFoliageGenerationWork.Count);
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
        List<ChunkCoord> orderedActiveCoords)
    {
        using (DrawVisibleFoliageEveryFrameMarker.Auto())
        {
            long stageStart = TerrainGenerationProfiler.GetTimestamp();
            long workBudgetStart = TerrainGenerationProfiler.GetTimestamp();
            float foregroundBudgetMs = Mathf.Max(0f, grassSettings.foregroundFoliageWorkBudgetMsPerFrame);

            PruneStaleFoliageQueues(chunkManager, viewerCoord, viewerGlobalSubChunk);
            EnqueueFoliageWorkForSettingsChanges(chunkManager, viewerCoord, orderedActiveCoords);
            CompleteActiveGrassSubChunkGenerationWork(
                chunkManager,
                viewerCoord,
                viewerGlobalSubChunk,
                workBudgetStart,
                foregroundBudgetMs);
            ProcessPendingFoliageManagementWork(chunkManager, viewerCoord, viewerGlobalSubChunk, workBudgetStart, foregroundBudgetMs);
            ProcessPendingGroundFoliageGenerationWork(chunkManager, viewerCoord, workBudgetStart, foregroundBudgetMs);
            ProcessPendingGrassSubChunkWork(chunkManager, viewerCoord, viewerGlobalSubChunk, workBudgetStart, foregroundBudgetMs);
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

    private void EnqueueFoliageWorkForSettingsChanges(
        ChunkManager chunkManager,
        ChunkCoord viewerCoord,
        List<ChunkCoord> orderedActiveCoords)
    {
        float currentSpawnChance = grassSettings.billboardSpawnChance;
        int currentCellsPerAxis = Mathf.Max(1, grassSettings.billboardCellsPerAxis);
        int currentNearGrassPrecomputeChunkPadding = Mathf.Max(0, grassSettings.nearGrassPrecomputeChunkPadding);

        bool billboardSettingsChanged =
            !Mathf.Approximately(currentSpawnChance, lastObservedBillboardSpawnChance) ||
            currentCellsPerAxis != lastObservedBillboardCellsPerAxis;
        bool nearGrassPrecomputeChanged =
            currentNearGrassPrecomputeChunkPadding != lastObservedNearGrassPrecomputeChunkPadding;

        if (!billboardSettingsChanged && !nearGrassPrecomputeChanged)
        {
            return;
        }

        lastObservedBillboardSpawnChance = currentSpawnChance;
        lastObservedBillboardCellsPerAxis = currentCellsPerAxis;
        lastObservedNearGrassPrecomputeChunkPadding = currentNearGrassPrecomputeChunkPadding;

        for (int i = 0; i < orderedActiveCoords.Count; i++)
        {
            ChunkCoord coord = orderedActiveCoords[i];

            if (nearGrassPrecomputeChanged && IsWithinNearGrassGenerationRange(viewerCoord, coord))
                EnqueueFoliageManagementWork(coord);

            if (!billboardSettingsChanged || !IsWithinBillboardGrass(viewerCoord, coord))
                continue;

            ChunkRecord record = chunkManager.GetChunkRecord(coord);
            ChunkRuntime runtime = chunkManager.GetChunkRuntime(record);

            if (record == null || runtime == null || runtime.FoliageRuntime == null || !HasRequiredTerrainData(record))
                continue;

            EnqueueFoliageBatchRebuild(record, FoliageBatchWorkType.BillboardGrass);
        }

        RecordFoliageQueueSnapshot();
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

        bool useNearGrass = IsWithinNearGrass(viewerCoord, coord);
        bool useBillboardGrass = IsWithinBillboardGrass(viewerCoord, coord);
        bool useFlowers = IsWithinFlowerRenderRange(viewerCoord, coord);
        bool useClover = IsWithinCloverRenderRange(viewerCoord, coord);
        bool useDandelions = IsWithinDandelionRenderRange(viewerCoord, coord);
        bool useTrees = IsWithinTreeRenderRange(viewerCoord, coord);

        if (!(useNearGrass || useBillboardGrass || useFlowers || useClover || useDandelions || useTrees))
            return;

        if (useNearGrass)
        {
            if (useClover && HasCloverRenderAssets())
                runtime.FoliageRuntime.DrawClover();

            if (IsCloverReadyForGrass(record, viewerCoord))
                runtime.FoliageRuntime.DrawGrass();
        }
        else if (useBillboardGrass)
        {
            runtime.FoliageRuntime.DrawBillboards();
        }

        if (useFlowers && HasFlowerRenderAssets())
            runtime.FoliageRuntime.DrawFlowers();

        if (useDandelions && HasDandelionRenderAssets())
            runtime.FoliageRuntime.DrawDandelions();

        DrawTreesForChunk(runtime, viewerCoord, coord);
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

        bool useNearGrass;
        bool preGenerateNearGrass;
        bool useBillboardGrass;
        bool useFlowers;
        bool useClover;
        bool preGenerateClover;
        bool useDandelions;
        bool useTrees;
        bool useBushes;
        bool useRocks;
        bool useFoliage;

        using (HandleSubChunkRangeChecksMarker.Auto())
        {
            useNearGrass = IsWithinNearGrass(viewerCoord, coord);
            preGenerateNearGrass = IsWithinNearGrassGenerationRange(viewerCoord, coord);
            useBillboardGrass = IsWithinBillboardGrass(viewerCoord, coord);
            useFlowers = IsWithinFlowerRenderRange(viewerCoord, coord);
            useClover = IsWithinCloverRenderRange(viewerCoord, coord);
            preGenerateClover = IsWithinCloverGenerationRange(viewerCoord, coord);
            useDandelions = IsWithinDandelionRenderRange(viewerCoord, coord);
            useTrees = IsWithinTreeRenderRange(viewerCoord, coord);
            useBushes = IsWithinBushRenderRange(viewerCoord, coord);
            useRocks = IsWithinRockRenderRange(viewerCoord, coord);
            useFoliage = useNearGrass || preGenerateNearGrass || useBillboardGrass || useFlowers || useClover || preGenerateClover || useDandelions || useTrees || useBushes || useRocks;
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
                runtime.FoliageRuntime.ClearTreeRepresentation(
                    ShouldRetainTreeGameObjectsForReuse(viewerCoord, coord));
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

        bool cloverReadyForGrass = true;

        using (HandleSubChunkFlowersMarker.Auto())
        {
            if (useFlowers && HasFlowerRenderAssets())
            {
                if (record.FoliageData == null || !record.FoliageData.flowersGenerated)
                {
                    EnqueueGroundFoliageGeneration(record, GroundFoliageGenerationType.Flower);
                }
                else if (!runtime.FoliageRuntime.HasValidFlowerRenderData())
                {
                    EnqueueFoliageBatchRebuild(record, FoliageBatchWorkType.Flower);
                }
            }
            else
            {
                runtime.FoliageRuntime.ClearFlowerBatches();
            }
        }

        using (HandleSubChunkCloverMarker.Auto())
        {
            if (useClover && HasCloverRenderAssets())
            {
                if (record.FoliageData == null || !record.FoliageData.cloverGenerated)
                {
                    EnqueueGroundFoliageGeneration(record, GroundFoliageGenerationType.Clover);
                }
                else if (!runtime.FoliageRuntime.HasValidCloverRenderData())
                {
                    EnqueueFoliageBatchRebuild(record, FoliageBatchWorkType.Clover);
                }
            }
            else
            {
                runtime.FoliageRuntime.ClearCloverBatches();
            }

            if (preGenerateClover && HasCloverRenderAssets() &&
                (record.FoliageData == null || !record.FoliageData.cloverGenerated))
            {
                EnqueueGroundFoliageGeneration(record, GroundFoliageGenerationType.Clover);
            }
        }

        using (HandleSubChunkDandelionsMarker.Auto())
        {
            if (useDandelions && HasDandelionRenderAssets())
            {
                if (record.FoliageData == null || !record.FoliageData.dandelionsGenerated)
                {
                    EnqueueGroundFoliageGeneration(record, GroundFoliageGenerationType.Dandelion);
                }
                else if (!runtime.FoliageRuntime.HasValidDandelionRenderData())
                {
                    EnqueueFoliageBatchRebuild(record, FoliageBatchWorkType.Dandelion);
                }
            }
            else
            {
                runtime.FoliageRuntime.ClearDandelionBatches();
            }
        }

        using (HandleSubChunkGrassMarker.Auto())
        {
            if (useNearGrass || preGenerateNearGrass)
            {
                EnsureRocksGenerated(record);
                cloverReadyForGrass = IsCloverReadyForGrass(record, viewerCoord);
                if (cloverReadyForGrass)
                {
                    EnsureNearGrassCloverInfluenceState(record, runtime, ShouldApplyCloverInfluenceToGrass(viewerCoord, record.ChunkCoord));
                    EnqueueMissingGrassSubChunks(record, viewerGlobalSubChunk);
                    if (useNearGrass)
                        EnqueueFoliageBatchRebuild(record, FoliageBatchWorkType.NearGrass);
                }
                else
                {
                    EnqueueGroundFoliageGeneration(record, GroundFoliageGenerationType.Clover);
                    runtime.FoliageRuntime.ClearGrassBatches();
                }
            }
        }

        using (HandleSubChunkBillboardGrassMarker.Auto())
        {
            if (!useNearGrass && useBillboardGrass)
            {
                if (record.FoliageData == null || !record.FoliageData.billboardGenerated)
                {
                    EnsureRocksGenerated(record);

                    long billboardGenerationStart = TerrainGenerationProfiler.GetTimestamp();
                    FoliageGenerator.GenerateBillboardGrassForChunk(
                        record,
                        grassSettings,
                        cloverSettings,
                        treeSettings,
                        worldSeed,
                        chunkSize,
                        worldScale,
                        meshHeightMultiplier);
                    TerrainGenerationProfiler.Record(
                        TerrainGenerationProfileStage.FoliageBillboardGrassGeneration,
                        billboardGenerationStart);
                }

                if (!runtime.FoliageRuntime.HasValidBillboardRenderData())
                    EnqueueFoliageBatchRebuild(record, FoliageBatchWorkType.BillboardGrass);
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
            useNearGrass,
            preGenerateNearGrass,
            useBillboardGrass,
            useFlowers,
            useClover,
            useDandelions,
            useTrees,
            useBushes,
            useRocks,
            cloverReadyForGrass,
            viewerGlobalSubChunk);
    }

    private bool ShouldRetryFoliageManagement(
        ChunkRecord record,
        ChunkRuntime runtime,
        ChunkCoord viewerCoord,
        bool useNearGrass,
        bool preGenerateNearGrass,
        bool useBillboardGrass,
        bool useFlowers,
        bool useClover,
        bool useDandelions,
        bool useTrees,
        bool useBushes,
        bool useRocks,
        bool cloverReadyForGrass,
        SubChunkCoord viewerGlobalSubChunk)
    {
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;

        if (useNearGrass || preGenerateNearGrass)
        {
            if (!cloverReadyForGrass)
                return true;

            if (useNearGrass && !foliageRuntime.HasValidGrassRenderData())
                return true;

            if (HasMissingDesiredGrassSubChunks(record, viewerGlobalSubChunk))
                return true;
        }

        if (!useNearGrass &&
            useBillboardGrass &&
            !foliageRuntime.HasValidBillboardRenderData())
        {
            return true;
        }

        if (useFlowers &&
            HasFlowerRenderAssets() &&
            !foliageRuntime.HasValidFlowerRenderData())
        {
            return true;
        }

        if (useClover &&
            HasCloverRenderAssets() &&
            !foliageRuntime.HasValidCloverRenderData())
        {
            return true;
        }

        if (useDandelions &&
            HasDandelionRenderAssets() &&
            !foliageRuntime.HasValidDandelionRenderData())
        {
            return true;
        }

        if (useTrees &&
            !foliageRuntime.HasCurrentTreeRepresentation(GetTreeRepresentationMode(viewerCoord, record.ChunkCoord)))
        {
            return true;
        }

        if (useBushes && !foliageRuntime.HasCurrentBushRepresentation())
            return true;

        if (useRocks && !foliageRuntime.HasCurrentRockRepresentation())
            return true;

        return false;
    }

    private bool HasMissingDesiredGrassSubChunks(
        ChunkRecord record,
        SubChunkCoord viewerGlobalSubChunk)
    {
        ChunkFoliageData data = record.FoliageData;
        if (data == null ||
            data.nearGrassInstancesBySubChunk == null ||
            data.nearGrassSubChunkGenerated == null)
        {
            return true;
        }

        int subChunksPerChunk = Mathf.Max(1, data.subChunksPerChunk);
        int activeSubChunkRadius = GetActiveGrassSubChunkRadius(subChunksPerChunk);

        for (int localSubX = 0; localSubX < subChunksPerChunk; localSubX++)
        {
            for (int localSubZ = 0; localSubZ < subChunksPerChunk; localSubZ++)
            {
                if (!IsGrassSubChunkDesired(
                        record.ChunkCoord,
                        localSubX,
                        localSubZ,
                        viewerGlobalSubChunk,
                        subChunksPerChunk,
                        activeSubChunkRadius))
                {
                    continue;
                }

                if (!data.IsNearGrassSubChunkGenerated(localSubX, localSubZ))
                    return true;
            }
        }

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

            bool useNearGrass = IsWithinNearGrass(viewerCoord, coord);
            bool useBillboardGrass = IsWithinBillboardGrass(viewerCoord, coord);
            bool useFlowers = IsWithinFlowerRenderRange(viewerCoord, coord);
            bool useClover = IsWithinCloverRenderRange(viewerCoord, coord);
            bool preGenerateClover = IsWithinCloverGenerationRange(viewerCoord, coord);
            bool useDandelions = IsWithinDandelionRenderRange(viewerCoord, coord);
            bool useTrees = IsWithinTreeRenderRange(viewerCoord, coord);
            bool useBushes = IsWithinBushRenderRange(viewerCoord, coord);
            bool useRocks = IsWithinRockRenderRange(viewerCoord, coord);
            bool useFoliage = useNearGrass || useBillboardGrass || useFlowers || useClover || preGenerateClover || useDandelions || useTrees || useBushes || useRocks;

            if (!HasRequiredTerrainData(record) || !useFoliage)
                continue;

            if (useNearGrass)
            {
                runtime.FoliageRuntime.AccumulateGrassRenderStats(ref stats);
            }
            else if (useBillboardGrass)
            {
                runtime.FoliageRuntime.AccumulateBillboardGrassRenderStats(ref stats);
            }

            if (useFlowers && HasFlowerRenderAssets())
                runtime.FoliageRuntime.AccumulateFlowerRenderStats(ref stats);

            if (useClover && HasCloverRenderAssets())
                runtime.FoliageRuntime.AccumulateCloverRenderStats(ref stats);

            if (useDandelions && HasDandelionRenderAssets())
                runtime.FoliageRuntime.AccumulateDandelionRenderStats(ref stats);

            if (useTrees)
            {
                FoliageRepresentationMode mode = GetTreeRepresentationMode(viewerCoord, coord);

                if (mode == FoliageRepresentationMode.GPUInstancedBillboard)
                    runtime.FoliageRuntime.AccumulateTreeBillboardRenderStats(ref stats);
                else if (mode == FoliageRepresentationMode.GameObjectWithCollision)
                    runtime.FoliageRuntime.AccumulateTreeGameObjectRenderStats(ref stats);
            }

            if (useBushes)
                runtime.FoliageRuntime.AccumulateBushGameObjectRenderStats(ref stats);

            if (useRocks)
                runtime.FoliageRuntime.AccumulateRockGameObjectRenderStats(ref stats);
        }
    }

    private void EnsureTreesGenerated(ChunkRecord record)
    {
        if (record.FoliageData == null || !record.FoliageData.treeCubesGenerated)
        {
            long stageStart = TerrainGenerationProfiler.GetTimestamp();
            FoliageGenerator.GenerateTreeCubesForChunk(
                record,
                treeSettings,
                worldSeed,
                chunkSize,
                worldScale,
                meshHeightMultiplier);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageTreeGeneration,
                stageStart);
        }
    }

    private void EnsureFlowersGenerated(ChunkRecord record)
    {
        if (!IsFlowerSystemEnabled())
            return;

        if (record.FoliageData == null || !record.FoliageData.treeCubesGenerated)
        {
            EnsureTreesGenerated(record);
        }

        if (record.FoliageData == null || !record.FoliageData.flowersGenerated)
        {
            long stageStart = TerrainGenerationProfiler.GetTimestamp();
            FoliageGenerator.GenerateFlowersForChunk(
                record,
                flowerSettings,
                worldSeed,
                chunkSize,
                worldScale,
                meshHeightMultiplier);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageFlowerGeneration,
                stageStart);
        }
    }

    private void EnsureCloverGenerated(ChunkRecord record)
    {
        if (!IsCloverSystemEnabled())
            return;

        if (record.FoliageData == null || !record.FoliageData.treeCubesGenerated)
            EnsureTreesGenerated(record);

        if (record.FoliageData == null || !record.FoliageData.bushesGenerated)
            EnsureBushesGenerated(record);

        if (record.FoliageData == null || !record.FoliageData.rocksGenerated)
            EnsureRocksGenerated(record);

        if (record.FoliageData == null || !record.FoliageData.cloverGenerated)
        {
            long stageStart = TerrainGenerationProfiler.GetTimestamp();
            FoliageGenerator.GenerateCloverForChunk(
                record,
                cloverSettings,
                GetCloverRenderAssetCount(),
                worldSeed,
                chunkSize,
                worldScale,
                meshHeightMultiplier);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageCloverGeneration,
                stageStart);
        }
    }

    private void EnsureDandelionsGenerated(ChunkRecord record)
    {
        if (!IsDandelionSystemEnabled())
            return;

        if (record.FoliageData == null || !record.FoliageData.treeCubesGenerated)
            EnsureTreesGenerated(record);

        if (record.FoliageData == null || !record.FoliageData.bushesGenerated)
            EnsureBushesGenerated(record);

        if (record.FoliageData == null || !record.FoliageData.rocksGenerated)
            EnsureRocksGenerated(record);

        if (record.FoliageData == null || !record.FoliageData.dandelionsGenerated)
        {
            long stageStart = TerrainGenerationProfiler.GetTimestamp();
            FoliageGenerator.GenerateDandelionsForChunk(
                record,
                dandelionSettings,
                worldSeed,
                chunkSize,
                worldScale,
                meshHeightMultiplier);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageDandelionGeneration,
                stageStart);
        }
    }

    private void EnsureBushesGenerated(ChunkRecord record)
    {
        if (record.FoliageData == null || !record.FoliageData.bushesGenerated)
        {
            long stageStart = TerrainGenerationProfiler.GetTimestamp();
            FoliageGenerator.GenerateBushesForChunk(
                record,
                treeSettings,
                worldSeed,
                chunkSize,
                worldScale,
                meshHeightMultiplier);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageBushGeneration,
                stageStart);
        }
    }

    private void EnsureRocksGenerated(ChunkRecord record)
    {
        if (record.FoliageData == null || !record.FoliageData.rocksGenerated)
        {
            long stageStart = TerrainGenerationProfiler.GetTimestamp();
            FoliageGenerator.GenerateRocksForChunk(
                record,
                treeSettings,
                worldSeed,
                chunkSize,
                worldScale,
                meshHeightMultiplier);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageRockGeneration,
                stageStart);
        }
    }

    private void RebuildTreeRepresentationIfNeeded(
        ChunkRuntime runtime,
        ChunkRecord record,
        ChunkCoord viewerCoord)
    {
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;

        FoliageRepresentationMode mode = GetTreeRepresentationMode(viewerCoord, record.ChunkCoord);
        if (foliageRuntime.HasCurrentTreeRepresentation(mode))
            return;

        foliageRuntime.ClearTreeBillboardMatrices();

        if (mode == FoliageRepresentationMode.GameObjectWithCollision)
        {
            long stageStart = TerrainGenerationProfiler.GetTimestamp();
            foliageRuntime.RebuildTreeGameObjects(
                record.FoliageData.treeCubeInstances,
                runtime.RootTransform,
                treeSettings.castTreeShadows,
                treeSettings.receiveTreeShadows);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageTreeGameObjectRebuild,
                stageStart);
        }
        else if (mode == FoliageRepresentationMode.GPUInstancedBillboard)
        {
            if (ShouldRetainTreeGameObjectsForReuse(viewerCoord, record.ChunkCoord))
                foliageRuntime.ReleaseTreeGameObjectsToPool();
            else
                foliageRuntime.ClearTreeGameObjects();

            RebuildTreeBillboardMatrices(runtime, record);
        }

        foliageRuntime.SetCurrentTreeRepresentation(mode);
    }

    private void RebuildBushGameObjectsIfNeeded(ChunkRuntime runtime, ChunkRecord record)
    {
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;

        if (foliageRuntime == null || foliageRuntime.HasCurrentBushRepresentation())
            return;

        long stageStart = TerrainGenerationProfiler.GetTimestamp();
        foliageRuntime.RebuildBushGameObjects(
            record.FoliageData.bushInstances,
            runtime.RootTransform);
        TerrainGenerationProfiler.Record(
            TerrainGenerationProfileStage.FoliageBushGameObjectRebuild,
            stageStart);
    }

    private void RebuildRockGameObjectsIfNeeded(ChunkRuntime runtime, ChunkRecord record)
    {
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;

        if (foliageRuntime == null || foliageRuntime.HasCurrentRockRepresentation())
            return;

        long stageStart = TerrainGenerationProfiler.GetTimestamp();
        foliageRuntime.RebuildRockGameObjects(
            record.FoliageData.rockInstances,
            runtime.RootTransform);
        TerrainGenerationProfiler.Record(
            TerrainGenerationProfileStage.FoliageRockGameObjectRebuild,
            stageStart);
    }

    private void DrawTreesForChunk(
        ChunkRuntime runtime,
        ChunkCoord viewerCoord,
        ChunkCoord chunkCoord)
    {
        if (runtime.FoliageRuntime == null)
            return;

        if (!IsWithinTreeRenderRange(viewerCoord, chunkCoord))
            return;

        FoliageRepresentationMode mode = GetTreeRepresentationMode(viewerCoord, chunkCoord);

        if (mode == FoliageRepresentationMode.GPUInstancedBillboard)
        {
            runtime.FoliageRuntime.DrawTreeBillboards(treeSettings.castTreeShadows);
        }
    }

    private void RebuildTreeBillboardMatrices(ChunkRuntime runtime, ChunkRecord record)
    {
        long stageStart = TerrainGenerationProfiler.GetTimestamp();
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;
        ChunkFoliageData data = record.FoliageData;

        List<Matrix4x4> mapleWorldMatrices = new List<Matrix4x4>();
        List<Vector4> mapleLeafTints = new List<Vector4>();
        List<Matrix4x4> sugarMapleWorldMatrices = new List<Matrix4x4>();
        List<Vector4> sugarMapleLeafTints = new List<Vector4>();
        List<Matrix4x4> birchAspenWorldMatrices = new List<Matrix4x4>();
        List<Vector4> birchAspenLeafTints = new List<Vector4>();
        List<Matrix4x4> beechWorldMatrices = new List<Matrix4x4>();
        List<Vector4> beechLeafTints = new List<Vector4>();
        List<Matrix4x4> spruceWorldMatrices = new List<Matrix4x4>();
        List<Vector4> spruceLeafTints = new List<Vector4>();
        List<Matrix4x4> whitePineWorldMatrices = new List<Matrix4x4>();
        List<Vector4> whitePineLeafTints = new List<Vector4>();
        List<Matrix4x4> oakWorldMatrices = new List<Matrix4x4>();
        List<Vector4> oakLeafTints = new List<Vector4>();
        List<Matrix4x4> grasslandMapleWorldMatrices = new List<Matrix4x4>();
        List<Vector4> grasslandMapleLeafTints = new List<Vector4>();
        List<Matrix4x4> grasslandBirchAspenWorldMatrices = new List<Matrix4x4>();
        List<Vector4> grasslandBirchAspenLeafTints = new List<Vector4>();
        List<Matrix4x4> grasslandWhitePineWorldMatrices = new List<Matrix4x4>();
        List<Vector4> grasslandWhitePineLeafTints = new List<Vector4>();
        List<Matrix4x4> grasslandOakWorldMatrices = new List<Matrix4x4>();
        List<Vector4> grasslandOakLeafTints = new List<Vector4>();
        List<Matrix4x4> grasslandWillowWorldMatrices = new List<Matrix4x4>();
        List<Vector4> grasslandWillowLeafTints = new List<Vector4>();
        Matrix4x4 chunkLocalToWorld = runtime.RootTransform.localToWorldMatrix;

        int instanceCount = data.treeCubeInstances.Count;
        if (instanceCount > 0)
        {
            NativeArray<TreeBillboardRenderSourceData> sources =
                new NativeArray<TreeBillboardRenderSourceData>(instanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            NativeArray<float4x4> nativeMatrices =
                new NativeArray<float4x4>(instanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            NativeArray<float4> nativeLeafTints =
                new NativeArray<float4>(instanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            try
            {
                for (int i = 0; i < instanceCount; i++)
                {
                    TreeInstanceData instance = data.treeCubeInstances[i];
                    Vector4 leafTint = Color32ToLinearVector4(instance.leafTint);

                    sources[i] = new TreeBillboardRenderSourceData
                    {
                        localPosition = new float3(
                            instance.localPosition.x,
                            instance.localPosition.y,
                            instance.localPosition.z),
                        localRotation = new quaternion(
                            instance.localRotation.x,
                            instance.localRotation.y,
                            instance.localRotation.z,
                            instance.localRotation.w),
                        localScale = new float3(
                            instance.localScale.x,
                            instance.localScale.y,
                            instance.localScale.z),
                        leafTint = new float4(leafTint.x, leafTint.y, leafTint.z, leafTint.w)
                    };
                }

                TreeBillboardRenderBatchBuildJob job = new TreeBillboardRenderBatchBuildJob
                {
                    sources = sources,
                    chunkLocalToWorld = ToFloat4x4(chunkLocalToWorld),
                    matrices = nativeMatrices,
                    leafTints = nativeLeafTints
                };

                JobHandle handle = job.Schedule(instanceCount, 64);
                handle.Complete();

                for (int i = 0; i < instanceCount; i++)
                {
                    AddTreeBillboardMatrix(
                        data.treeCubeInstances[i].variant,
                        ToMatrix4x4(nativeMatrices[i]),
                        ToVector4(nativeLeafTints[i]),
                        mapleWorldMatrices,
                        mapleLeafTints,
                        sugarMapleWorldMatrices,
                        sugarMapleLeafTints,
                        birchAspenWorldMatrices,
                        birchAspenLeafTints,
                        beechWorldMatrices,
                        beechLeafTints,
                        spruceWorldMatrices,
                        spruceLeafTints,
                        whitePineWorldMatrices,
                        whitePineLeafTints,
                        oakWorldMatrices,
                        oakLeafTints,
                        grasslandMapleWorldMatrices,
                        grasslandMapleLeafTints,
                        grasslandBirchAspenWorldMatrices,
                        grasslandBirchAspenLeafTints,
                        grasslandWhitePineWorldMatrices,
                        grasslandWhitePineLeafTints,
                        grasslandOakWorldMatrices,
                        grasslandOakLeafTints,
                        grasslandWillowWorldMatrices,
                        grasslandWillowLeafTints);
                }
            }
            finally
            {
                if (sources.IsCreated)
                    sources.Dispose();
                if (nativeMatrices.IsCreated)
                    nativeMatrices.Dispose();
                if (nativeLeafTints.IsCreated)
                    nativeLeafTints.Dispose();
            }
        }

        foliageRuntime.CacheTreeBillboardMatrices(
            mapleWorldMatrices,
            mapleLeafTints,
            sugarMapleWorldMatrices,
            sugarMapleLeafTints,
            birchAspenWorldMatrices,
            birchAspenLeafTints,
            beechWorldMatrices,
            beechLeafTints,
            spruceWorldMatrices,
            spruceLeafTints,
            whitePineWorldMatrices,
            whitePineLeafTints,
            oakWorldMatrices,
            oakLeafTints,
            grasslandMapleWorldMatrices,
            grasslandMapleLeafTints,
            grasslandBirchAspenWorldMatrices,
            grasslandBirchAspenLeafTints,
            grasslandWhitePineWorldMatrices,
            grasslandWhitePineLeafTints,
            grasslandOakWorldMatrices,
            grasslandOakLeafTints,
            grasslandWillowWorldMatrices,
            grasslandWillowLeafTints);
        TerrainGenerationProfiler.Record(
            TerrainGenerationProfileStage.FoliageTreeBillboardBatchBuild,
            stageStart);
    }

    private void AddTreeBillboardMatrix(
        WorldFeatureVariant variant,
        Matrix4x4 worldMatrix,
        Vector4 leafTint,
        List<Matrix4x4> mapleWorldMatrices,
        List<Vector4> mapleLeafTints,
        List<Matrix4x4> sugarMapleWorldMatrices,
        List<Vector4> sugarMapleLeafTints,
        List<Matrix4x4> birchAspenWorldMatrices,
        List<Vector4> birchAspenLeafTints,
        List<Matrix4x4> beechWorldMatrices,
        List<Vector4> beechLeafTints,
        List<Matrix4x4> spruceWorldMatrices,
        List<Vector4> spruceLeafTints,
        List<Matrix4x4> whitePineWorldMatrices,
        List<Vector4> whitePineLeafTints,
        List<Matrix4x4> oakWorldMatrices,
        List<Vector4> oakLeafTints,
        List<Matrix4x4> grasslandMapleWorldMatrices,
        List<Vector4> grasslandMapleLeafTints,
        List<Matrix4x4> grasslandBirchAspenWorldMatrices,
        List<Vector4> grasslandBirchAspenLeafTints,
        List<Matrix4x4> grasslandWhitePineWorldMatrices,
        List<Vector4> grasslandWhitePineLeafTints,
        List<Matrix4x4> grasslandOakWorldMatrices,
        List<Vector4> grasslandOakLeafTints,
        List<Matrix4x4> grasslandWillowWorldMatrices,
        List<Vector4> grasslandWillowLeafTints)
    {
        switch (variant)
        {
            case WorldFeatureVariant.SugarMapleTree:
                sugarMapleWorldMatrices.Add(worldMatrix);
                sugarMapleLeafTints.Add(leafTint);
                break;
            case WorldFeatureVariant.BirchAspenTree:
                birchAspenWorldMatrices.Add(worldMatrix);
                birchAspenLeafTints.Add(leafTint);
                break;
            case WorldFeatureVariant.BeechTree:
                beechWorldMatrices.Add(worldMatrix);
                beechLeafTints.Add(leafTint);
                break;
            case WorldFeatureVariant.SpruceTree:
                spruceWorldMatrices.Add(worldMatrix);
                spruceLeafTints.Add(leafTint);
                break;
            case WorldFeatureVariant.WhitePineTree:
                whitePineWorldMatrices.Add(worldMatrix);
                whitePineLeafTints.Add(leafTint);
                break;
            case WorldFeatureVariant.OakTree:
                oakWorldMatrices.Add(worldMatrix);
                oakLeafTints.Add(leafTint);
                break;
            case WorldFeatureVariant.GrasslandMapleTree:
                grasslandMapleWorldMatrices.Add(worldMatrix);
                grasslandMapleLeafTints.Add(leafTint);
                break;
            case WorldFeatureVariant.GrasslandBirchAspenTree:
                grasslandBirchAspenWorldMatrices.Add(worldMatrix);
                grasslandBirchAspenLeafTints.Add(leafTint);
                break;
            case WorldFeatureVariant.GrasslandWhitePineTree:
                grasslandWhitePineWorldMatrices.Add(worldMatrix);
                grasslandWhitePineLeafTints.Add(leafTint);
                break;
            case WorldFeatureVariant.GrasslandOakTree:
                grasslandOakWorldMatrices.Add(worldMatrix);
                grasslandOakLeafTints.Add(leafTint);
                break;
            case WorldFeatureVariant.GrasslandWillowTree:
                grasslandWillowWorldMatrices.Add(worldMatrix);
                grasslandWillowLeafTints.Add(leafTint);
                break;
            default:
                mapleWorldMatrices.Add(worldMatrix);
                mapleLeafTints.Add(leafTint);
                break;
        }
    }

    private void EnqueueMissingGrassSubChunks(
        ChunkRecord record,
        SubChunkCoord viewerGlobalSubChunk)
    {
        long stageStart = TerrainGenerationProfiler.GetTimestamp();

        if (!HasRequiredTerrainData(record))
            return;

        ChunkFoliageData data = EnsureGrassSubChunkStorage(record);
        int subChunksPerChunk = data.subChunksPerChunk;
        int activeSubChunkRadius = GetActiveGrassSubChunkRadius(subChunksPerChunk);
        int chunkGlobalSubX = record.ChunkCoord.x * subChunksPerChunk;
        int chunkGlobalSubZ = record.ChunkCoord.z * subChunksPerChunk;
        int viewerLocalSubX = viewerGlobalSubChunk.x - chunkGlobalSubX;
        int viewerLocalSubZ = viewerGlobalSubChunk.z - chunkGlobalSubZ;
        int maxQueuedGrassWork = Mathf.Max(1, grassSettings.maxQueuedGrassSubChunkWork);

        for (int radius = 0; radius <= activeSubChunkRadius; radius++)
        {
            int minSubX = Mathf.Max(0, viewerLocalSubX - radius);
            int maxSubX = Mathf.Min(subChunksPerChunk - 1, viewerLocalSubX + radius);
            int minSubZ = Mathf.Max(0, viewerLocalSubZ - radius);
            int maxSubZ = Mathf.Min(subChunksPerChunk - 1, viewerLocalSubZ + radius);

            for (int localSubX = minSubX; localSubX <= maxSubX; localSubX++)
            {
                for (int localSubZ = minSubZ; localSubZ <= maxSubZ; localSubZ++)
                {
                    int localDx = Mathf.Abs(localSubX - viewerLocalSubX);
                    int localDz = Mathf.Abs(localSubZ - viewerLocalSubZ);
                    if (Mathf.Max(localDx, localDz) != radius)
                        continue;

                    if (!IsGrassSubChunkDesired(
                            record.ChunkCoord,
                            localSubX,
                            localSubZ,
                            viewerGlobalSubChunk,
                            subChunksPerChunk,
                            activeSubChunkRadius))
                        continue;

                    if (data.IsNearGrassSubChunkGenerated(localSubX, localSubZ))
                        continue;

                    if (pendingGrassSubChunkWork.Count + activeGrassSubChunkGenerationWork.Count >= maxQueuedGrassWork)
                    {
                        TerrainGenerationProfiler.Record(
                            TerrainGenerationProfileStage.FoliageGrassSubChunkEnqueue,
                            stageStart);
                        RecordFoliageQueueSnapshot();
                        return;
                    }

                    GrassSubChunkWorkKey key = new GrassSubChunkWorkKey(record.ChunkCoord, localSubX, localSubZ);
                    if (activeGrassSubChunkGenerations.Contains(key))
                        continue;

                    if (!queuedGrassSubChunks.Add(key))
                        continue;

                    pendingGrassSubChunkWork.Add(new GrassSubChunkWorkItem(key));
                }
            }
        }

        TerrainGenerationProfiler.Record(
            TerrainGenerationProfileStage.FoliageGrassSubChunkEnqueue,
            stageStart);
        RecordFoliageQueueSnapshot();
    }

    private void CompleteActiveGrassSubChunkGenerationWork(
        ChunkManager chunkManager,
        ChunkCoord viewerCoord,
        SubChunkCoord viewerGlobalSubChunk,
        long sharedBudgetStart,
        float sharedBudgetMs)
    {
        using var completeActiveGrassJobsScope = CompleteActiveGrassJobsMarker.Auto();

        for (int i = activeGrassSubChunkGenerationWork.Count - 1; i >= 0; i--)
        {
            if (sharedBudgetMs > 0f &&
                TerrainGenerationProfiler.GetElapsedMilliseconds(sharedBudgetStart) >= sharedBudgetMs)
            {
                break;
            }

            ActiveGrassSubChunkGenerationWorkItem workItem = activeGrassSubChunkGenerationWork[i];
            if (!workItem.GenerationJob.IsCompleted)
                continue;

            activeGrassSubChunkGenerationWork.RemoveAt(i);
            activeGrassSubChunkGenerations.Remove(workItem.Key);

            ChunkRecord record = chunkManager.GetChunkRecord(workItem.Key.ChunkCoord);
            bool shouldUseCloverInfluence =
                ShouldApplyCloverInfluenceToGrass(viewerCoord, workItem.Key.ChunkCoord);
            if (workItem.UsesCloverInfluence == shouldUseCloverInfluence &&
                IsGrassSubChunkWorkStillWanted(record, viewerCoord, viewerGlobalSubChunk, workItem.Key))
            {
                workItem.GenerationJob.CompleteAndApply();
                dirtyGrassChunks.Add(workItem.Key.ChunkCoord);
            }
            else
            {
                workItem.GenerationJob.Dispose();
            }
        }

        EnqueueDirtyGrassBatchRebuilds(chunkManager, viewerCoord);
    }

    private void EnqueueDirtyGrassBatchRebuilds(ChunkManager chunkManager, ChunkCoord viewerCoord)
    {
        foreach (ChunkCoord chunkCoord in dirtyGrassChunks)
        {
            ChunkRecord record = chunkManager.GetChunkRecord(chunkCoord);
            ChunkRuntime runtime = chunkManager.GetChunkRuntime(record);

            if (record == null || runtime == null || runtime.FoliageRuntime == null)
                continue;

            if (IsWithinNearGrass(viewerCoord, chunkCoord))
            {
                EnqueueFoliageBatchRebuild(record, FoliageBatchWorkType.NearGrass);
            }
        }

        dirtyGrassChunks.Clear();
    }

    private void ProcessPendingGrassSubChunkWork(
        ChunkManager chunkManager,
        ChunkCoord viewerCoord,
        SubChunkCoord viewerGlobalSubChunk,
        long sharedBudgetStart,
        float sharedBudgetMs)
    {
        using var processGrassSubChunkQueueScope = ProcessGrassSubChunkQueueMarker.Auto();

        int maxGenerations = Mathf.Max(1, grassSettings.maxSubChunkGenerationsPerFrame);
        float budgetMs = Mathf.Max(0f, grassSettings.subChunkGenerationBudgetMsPerFrame);
        long frameStart = TerrainGenerationProfiler.GetTimestamp();
        int generatedCount = 0;

        while (pendingGrassSubChunkWork.Count > 0 && generatedCount < maxGenerations)
        {
            if (!HasFoliageWorkBudgetRemaining(frameStart, budgetMs, sharedBudgetStart, sharedBudgetMs))
                break;

            GrassSubChunkWorkItem workItem = PopNearestGrassSubChunkWork(viewerGlobalSubChunk);
            queuedGrassSubChunks.Remove(workItem.Key);

            if (!IsWithinNearGrassGenerationRange(viewerCoord, workItem.Key.ChunkCoord))
                continue;

            ChunkRecord record = chunkManager.GetChunkRecord(workItem.Key.ChunkCoord);
            ChunkRuntime runtime = chunkManager.GetChunkRuntime(record);
            if (record == null || !HasRequiredTerrainData(record))
                continue;

            if (!IsCloverReadyForGrass(record, viewerCoord))
            {
                EnqueueGroundFoliageGeneration(record, GroundFoliageGenerationType.Clover);
                continue;
            }

            ChunkFoliageData data = EnsureGrassSubChunkStorage(record);
            int activeSubChunkRadius = GetActiveGrassSubChunkRadius(data.subChunksPerChunk);
            if (!IsGrassSubChunkDesired(
                    record.ChunkCoord,
                    workItem.Key.LocalSubChunkX,
                    workItem.Key.LocalSubChunkZ,
                    viewerGlobalSubChunk,
                    data.subChunksPerChunk,
                    activeSubChunkRadius))
            {
                continue;
            }

            bool applyCloverInfluence = ShouldApplyCloverInfluenceToGrass(viewerCoord, record.ChunkCoord);
            EnsureNearGrassCloverInfluenceState(record, runtime, applyCloverInfluence);

            if (data.IsNearGrassSubChunkGenerated(workItem.Key.LocalSubChunkX, workItem.Key.LocalSubChunkZ))
                continue;

            EnsureTreesGenerated(record);
            EnsureRocksGenerated(record);

            long discoveryStart = TerrainGenerationProfiler.GetTimestamp();
            bool scheduled = FoliageGenerator.TryScheduleGrassForSubChunk(
                record,
                grassSettings,
                cloverSettings,
                treeSettings,
                worldSeed,
                chunkSize,
                worldScale,
                meshHeightMultiplier,
                workItem.Key.LocalSubChunkX,
                workItem.Key.LocalSubChunkZ,
                applyCloverInfluence,
                out FoliageGenerator.GrassSubChunkGenerationJob generationJob);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageGrassSubChunkDiscovery,
                discoveryStart);

            if (scheduled)
            {
                activeGrassSubChunkGenerations.Add(workItem.Key);
                activeGrassSubChunkGenerationWork.Add(
                    new ActiveGrassSubChunkGenerationWorkItem(workItem.Key, generationJob, applyCloverInfluence));
            }
            else
            {
                dirtyGrassChunks.Add(record.ChunkCoord);
            }

            generatedCount++;

            if (!HasFoliageWorkBudgetRemaining(frameStart, budgetMs, sharedBudgetStart, sharedBudgetMs))
                break;
        }

        EnqueueDirtyGrassBatchRebuilds(chunkManager, viewerCoord);
        RecordFoliageQueueSnapshot();
    }

    private void EnqueueGroundFoliageGeneration(ChunkRecord record, GroundFoliageGenerationType generationType)
    {
        if (record == null)
            return;

        GroundFoliageGenerationWorkKey key = new GroundFoliageGenerationWorkKey(record.ChunkCoord, generationType);
        if (!queuedGroundFoliageGenerationWork.Add(key))
            return;

        if (pendingGroundFoliageGenerationWork.Count >= Mathf.Max(1, grassSettings.maxQueuedGroundFoliageGenerationWork))
        {
            queuedGroundFoliageGenerationWork.Remove(key);
            return;
        }

        pendingGroundFoliageGenerationWork.Add(new GroundFoliageGenerationWorkItem(key));
        RecordFoliageQueueSnapshot();
    }

    private void ProcessPendingGroundFoliageGenerationWork(
        ChunkManager chunkManager,
        ChunkCoord viewerCoord,
        long sharedBudgetStart,
        float sharedBudgetMs)
    {
        using var processGroundFoliageGenerationScope = ProcessGroundFoliageGenerationMarker.Auto();

        int maxGenerations = Mathf.Max(1, grassSettings.maxGroundFoliageGenerationsPerFrame);
        float budgetMs = Mathf.Max(0f, grassSettings.groundFoliageGenerationBudgetMsPerFrame);
        long frameStart = TerrainGenerationProfiler.GetTimestamp();
        int generationCount = 0;

        while (pendingGroundFoliageGenerationWork.Count > 0 && generationCount < maxGenerations)
        {
            if (!HasFoliageWorkBudgetRemaining(frameStart, budgetMs, sharedBudgetStart, sharedBudgetMs))
                break;

            GroundFoliageGenerationWorkItem workItem = PopNearestGroundFoliageGenerationWork(viewerCoord);
            queuedGroundFoliageGenerationWork.Remove(workItem.Key);

            ChunkRecord record = chunkManager.GetChunkRecord(workItem.Key.ChunkCoord);
            ChunkRuntime runtime = chunkManager.GetChunkRuntime(record);

            if (record == null || runtime == null || runtime.FoliageRuntime == null || !HasRequiredTerrainData(record))
                continue;

            if (!IsGroundFoliageGenerationStillWanted(record, viewerCoord, workItem.Key.GenerationType))
                continue;

            switch (workItem.Key.GenerationType)
            {
                case GroundFoliageGenerationType.Flower:
                    EnsureFlowersGenerated(record);
                    if (record.FoliageData != null && record.FoliageData.flowersGenerated)
                        EnqueueFoliageBatchRebuild(record, FoliageBatchWorkType.Flower);
                    generationCount++;
                    break;
                case GroundFoliageGenerationType.Clover:
                    EnsureCloverGenerated(record);
                    if (record.FoliageData != null && record.FoliageData.cloverGenerated)
                    {
                        record.FoliageData.ClearNearGrass();
                        runtime.FoliageRuntime.ClearGrassBatches();
                        if (IsWithinCloverRenderRange(viewerCoord, record.ChunkCoord))
                            EnqueueFoliageBatchRebuild(record, FoliageBatchWorkType.Clover);
                    }

                    generationCount++;
                    break;
                case GroundFoliageGenerationType.Dandelion:
                    EnsureDandelionsGenerated(record);
                    if (record.FoliageData != null && record.FoliageData.dandelionsGenerated)
                        EnqueueFoliageBatchRebuild(record, FoliageBatchWorkType.Dandelion);
                    generationCount++;
                    break;
            }

            if (!HasFoliageWorkBudgetRemaining(frameStart, budgetMs, sharedBudgetStart, sharedBudgetMs))
                break;
        }

        RecordFoliageQueueSnapshot();
    }

    private void EnqueueFoliageBatchRebuild(ChunkRecord record, FoliageBatchWorkType workType)
    {
        if (record == null)
            return;

        FoliageBatchWorkKey key = new FoliageBatchWorkKey(record.ChunkCoord, workType);
        if (!queuedFoliageBatchWork.Add(key))
            return;

        if (pendingFoliageBatchWork.Count >= Mathf.Max(1, grassSettings.maxQueuedRenderBatchWork))
        {
            queuedFoliageBatchWork.Remove(key);
            return;
        }

        pendingFoliageBatchWork.Add(new FoliageBatchWorkItem(key));
        RecordFoliageQueueSnapshot();
    }

    private void EnqueueTreeRepresentationRebuildIfNeeded(
        ChunkRuntime runtime,
        ChunkRecord record,
        ChunkCoord viewerCoord)
    {
        if (runtime == null || record == null || runtime.FoliageRuntime == null)
            return;

        FoliageRepresentationMode mode = GetTreeRepresentationMode(viewerCoord, record.ChunkCoord);
        if (runtime.FoliageRuntime.HasCurrentTreeRepresentation(mode))
            return;

        EnqueueTreeRepresentationRebuild(record);
    }

    private void EnqueueTreeRepresentationRebuild(ChunkRecord record)
    {
        if (record == null || !queuedTreeRepresentationWork.Add(record.ChunkCoord))
            return;

        pendingTreeRepresentationWork.Add(new TreeRepresentationWorkItem(record.ChunkCoord));
        RecordFoliageQueueSnapshot(pendingTreeRepresentationWork.Count);
    }

    private void ProcessPendingFoliageBatchWork(
        ChunkManager chunkManager,
        ChunkCoord viewerCoord,
        SubChunkCoord viewerGlobalSubChunk,
        long sharedBudgetStart,
        float sharedBudgetMs)
    {
        using var processFoliageBatchQueueScope = ProcessFoliageBatchQueueMarker.Auto();

        int maxRebuilds = Mathf.Max(1, grassSettings.maxRenderBatchRebuildsPerFrame);
        float budgetMs = Mathf.Max(0f, grassSettings.renderBatchRebuildBudgetMsPerFrame);
        long frameStart = TerrainGenerationProfiler.GetTimestamp();
        int rebuildCount = 0;

        while (pendingFoliageBatchWork.Count > 0 && rebuildCount < maxRebuilds)
        {
            if (!HasFoliageWorkBudgetRemaining(frameStart, budgetMs, sharedBudgetStart, sharedBudgetMs))
                break;

            FoliageBatchWorkItem workItem = PopNearestFoliageBatchWork(viewerCoord);
            queuedFoliageBatchWork.Remove(workItem.Key);

            ChunkRecord record = chunkManager.GetChunkRecord(workItem.Key.ChunkCoord);
            ChunkRuntime runtime = chunkManager.GetChunkRuntime(record);

            if (record == null || runtime == null || runtime.FoliageRuntime == null || !HasRequiredTerrainData(record))
                continue;

            if (!IsFoliageBatchWorkStillWanted(record, viewerCoord, workItem.Key.WorkType))
                continue;

            switch (workItem.Key.WorkType)
            {
                case FoliageBatchWorkType.NearGrass:
                    RebuildGrassMatricesForViewerSubChunk(runtime, record, viewerGlobalSubChunk);
                    rebuildCount++;
                    break;
                case FoliageBatchWorkType.BillboardGrass:
                    RebuildBillboardMatrices(runtime, record, viewerCoord);
                    rebuildCount++;
                    break;
                case FoliageBatchWorkType.Flower:
                    RebuildFlowerBatches(runtime, record);
                    rebuildCount++;
                    break;
                case FoliageBatchWorkType.Clover:
                    RebuildCloverBatches(runtime, record);
                    rebuildCount++;
                    break;
                case FoliageBatchWorkType.Dandelion:
                    RebuildDandelionBatches(runtime, record);
                    rebuildCount++;
                    break;
            }

            if (!HasFoliageWorkBudgetRemaining(frameStart, budgetMs, sharedBudgetStart, sharedBudgetMs))
                break;
        }

        RecordFoliageQueueSnapshot();
    }

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
                runtime.FoliageRuntime.ClearTreeRepresentation(
                    ShouldRetainTreeGameObjectsForReuse(viewerCoord, workItem.ChunkCoord));
                continue;
            }

            FoliageRepresentationMode mode = GetTreeRepresentationMode(viewerCoord, workItem.ChunkCoord);
            if (runtime.FoliageRuntime.HasCurrentTreeRepresentation(mode))
                continue;

            EnsureTreesGenerated(record);
            RebuildTreeRepresentationIfNeeded(runtime, record, viewerCoord);
            rebuildCount++;

            if (!HasFoliageWorkBudgetRemaining(frameStart, budgetMs, sharedBudgetStart, sharedBudgetMs))
                break;
        }

        RecordFoliageQueueSnapshot(pendingTreeRepresentationWork.Count);
    }

    private static bool HasFoliageWorkBudgetRemaining(
        long localBudgetStart,
        float localBudgetMs,
        long sharedBudgetStart,
        float sharedBudgetMs)
    {
        return (localBudgetMs <= 0f ||
                TerrainGenerationProfiler.GetElapsedMilliseconds(localBudgetStart) < localBudgetMs) &&
               (sharedBudgetMs <= 0f ||
                TerrainGenerationProfiler.GetElapsedMilliseconds(sharedBudgetStart) < sharedBudgetMs);
    }

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

        queuedGrassSubChunks.Clear();
        for (int i = pendingGrassSubChunkWork.Count - 1; i >= 0; i--)
        {
            GrassSubChunkWorkItem workItem = pendingGrassSubChunkWork[i];
            ChunkRecord record = chunkManager.GetChunkRecord(workItem.Key.ChunkCoord);

            if (!IsGrassSubChunkWorkStillWanted(record, viewerCoord, viewerGlobalSubChunk, workItem.Key) ||
                !queuedGrassSubChunks.Add(workItem.Key))
            {
                pendingGrassSubChunkWork.RemoveAt(i);
            }
        }

        queuedGroundFoliageGenerationWork.Clear();
        for (int i = pendingGroundFoliageGenerationWork.Count - 1; i >= 0; i--)
        {
            GroundFoliageGenerationWorkItem workItem = pendingGroundFoliageGenerationWork[i];
            ChunkRecord record = chunkManager.GetChunkRecord(workItem.Key.ChunkCoord);

            if (record == null ||
                !HasRequiredTerrainData(record) ||
                !IsGroundFoliageGenerationStillWanted(record, viewerCoord, workItem.Key.GenerationType) ||
                !queuedGroundFoliageGenerationWork.Add(workItem.Key))
            {
                pendingGroundFoliageGenerationWork.RemoveAt(i);
            }
        }

        queuedFoliageBatchWork.Clear();
        for (int i = pendingFoliageBatchWork.Count - 1; i >= 0; i--)
        {
            FoliageBatchWorkItem workItem = pendingFoliageBatchWork[i];
            ChunkRecord record = chunkManager.GetChunkRecord(workItem.Key.ChunkCoord);
            ChunkRuntime runtime = chunkManager.GetChunkRuntime(record);

            if (record == null ||
                runtime == null ||
                runtime.FoliageRuntime == null ||
                !HasRequiredTerrainData(record) ||
                !IsFoliageBatchWorkStillWanted(record, viewerCoord, workItem.Key.WorkType) ||
                !queuedFoliageBatchWork.Add(workItem.Key))
            {
                pendingFoliageBatchWork.RemoveAt(i);
            }
        }

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
                runtime.FoliageRuntime.HasCurrentTreeRepresentation(GetTreeRepresentationMode(viewerCoord, workItem.ChunkCoord)) ||
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

        return IsWithinNearGrass(viewerCoord, coord) ||
               IsWithinNearGrassGenerationRange(viewerCoord, coord) ||
               IsWithinBillboardGrass(viewerCoord, coord) ||
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
            int distance = GetChunkRingDistance(viewerCoord, candidate.ChunkCoord);

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = i;
        }

        FoliageManagementWorkItem result = pendingFoliageManagementWork[bestIndex];
        pendingFoliageManagementWork.RemoveAt(bestIndex);
        return result;
    }

    private GrassSubChunkWorkItem PopNearestGrassSubChunkWork(SubChunkCoord viewerGlobalSubChunk)
    {
        int bestIndex = 0;
        int bestDistance = int.MaxValue;
        int subChunksPerChunk = Mathf.Max(1, grassSettings.subChunksPerChunk);

        for (int i = 0; i < pendingGrassSubChunkWork.Count; i++)
        {
            GrassSubChunkWorkItem candidate = pendingGrassSubChunkWork[i];
            int globalSubX = candidate.Key.ChunkCoord.x * subChunksPerChunk + candidate.Key.LocalSubChunkX;
            int globalSubZ = candidate.Key.ChunkCoord.z * subChunksPerChunk + candidate.Key.LocalSubChunkZ;
            int dx = globalSubX - viewerGlobalSubChunk.x;
            int dz = globalSubZ - viewerGlobalSubChunk.z;
            int distance = dx * dx + dz * dz;

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = i;
        }

        GrassSubChunkWorkItem result = pendingGrassSubChunkWork[bestIndex];
        pendingGrassSubChunkWork.RemoveAt(bestIndex);
        return result;
    }

    private TreeRepresentationWorkItem PopNearestTreeRepresentationWork(ChunkCoord viewerCoord)
    {
        int bestIndex = 0;
        int bestDistance = int.MaxValue;

        for (int i = 0; i < pendingTreeRepresentationWork.Count; i++)
        {
            TreeRepresentationWorkItem candidate = pendingTreeRepresentationWork[i];
            int distance = GetChunkRingDistance(viewerCoord, candidate.ChunkCoord);

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = i;
        }

        TreeRepresentationWorkItem result = pendingTreeRepresentationWork[bestIndex];
        pendingTreeRepresentationWork.RemoveAt(bestIndex);
        return result;
    }

    private GroundFoliageGenerationWorkItem PopNearestGroundFoliageGenerationWork(ChunkCoord viewerCoord)
    {
        int bestIndex = 0;
        int bestDistance = int.MaxValue;
        GroundFoliageGenerationType bestType = GroundFoliageGenerationType.Flower;

        for (int i = 0; i < pendingGroundFoliageGenerationWork.Count; i++)
        {
            GroundFoliageGenerationWorkItem candidate = pendingGroundFoliageGenerationWork[i];
            int distance = GetChunkRingDistance(viewerCoord, candidate.Key.ChunkCoord);
            bool preferCloverTie = distance == bestDistance &&
                                   candidate.Key.GenerationType == GroundFoliageGenerationType.Clover &&
                                   bestType != GroundFoliageGenerationType.Clover;

            if (distance > bestDistance || (distance == bestDistance && !preferCloverTie))
                continue;

            bestDistance = distance;
            bestType = candidate.Key.GenerationType;
            bestIndex = i;
        }

        GroundFoliageGenerationWorkItem result = pendingGroundFoliageGenerationWork[bestIndex];
        pendingGroundFoliageGenerationWork.RemoveAt(bestIndex);
        return result;
    }

    private FoliageBatchWorkItem PopNearestFoliageBatchWork(ChunkCoord viewerCoord)
    {
        int bestIndex = 0;
        int bestDistance = int.MaxValue;

        for (int i = 0; i < pendingFoliageBatchWork.Count; i++)
        {
            FoliageBatchWorkItem candidate = pendingFoliageBatchWork[i];
            int distance = GetChunkRingDistance(viewerCoord, candidate.Key.ChunkCoord);

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = i;
        }

        FoliageBatchWorkItem result = pendingFoliageBatchWork[bestIndex];
        pendingFoliageBatchWork.RemoveAt(bestIndex);
        return result;
    }

    private bool IsFoliageBatchWorkStillWanted(
        ChunkRecord record,
        ChunkCoord viewerCoord,
        FoliageBatchWorkType workType)
    {
        switch (workType)
        {
            case FoliageBatchWorkType.NearGrass:
                return IsWithinNearGrass(viewerCoord, record.ChunkCoord) &&
                       IsCloverReadyForGrass(record, viewerCoord);
            case FoliageBatchWorkType.BillboardGrass:
                return IsWithinBillboardGrass(viewerCoord, record.ChunkCoord);
            case FoliageBatchWorkType.Flower:
                return IsWithinFlowerRenderRange(viewerCoord, record.ChunkCoord) &&
                       HasFlowerRenderAssets();
            case FoliageBatchWorkType.Clover:
                return IsWithinCloverRenderRange(viewerCoord, record.ChunkCoord) &&
                       HasCloverRenderAssets();
            case FoliageBatchWorkType.Dandelion:
                return IsWithinDandelionRenderRange(viewerCoord, record.ChunkCoord) &&
                       HasDandelionRenderAssets();
            default:
                return false;
        }
    }

    private bool IsGrassSubChunkWorkStillWanted(
        ChunkRecord record,
        ChunkCoord viewerCoord,
        SubChunkCoord viewerGlobalSubChunk,
        GrassSubChunkWorkKey key)
    {
        if (record == null ||
            !HasRequiredTerrainData(record) ||
            !IsWithinNearGrassGenerationRange(viewerCoord, key.ChunkCoord) ||
            !IsCloverReadyForGrass(record, viewerCoord))
        {
            return false;
        }

        ChunkFoliageData data = record.FoliageData;
        if (data == null ||
            data.nearGrassInstancesBySubChunk == null ||
            data.nearGrassSubChunkGenerated == null)
        {
            return true;
        }

        int subChunksPerChunk = Mathf.Max(1, data.subChunksPerChunk);
        if (key.LocalSubChunkX < 0 ||
            key.LocalSubChunkZ < 0 ||
            key.LocalSubChunkX >= subChunksPerChunk ||
            key.LocalSubChunkZ >= subChunksPerChunk)
        {
            return false;
        }

        int activeSubChunkRadius = GetActiveGrassSubChunkRadius(subChunksPerChunk);
        return IsGrassSubChunkDesired(
                   key.ChunkCoord,
                   key.LocalSubChunkX,
                   key.LocalSubChunkZ,
                   viewerGlobalSubChunk,
                   subChunksPerChunk,
                   activeSubChunkRadius) &&
               !data.IsNearGrassSubChunkGenerated(key.LocalSubChunkX, key.LocalSubChunkZ);
    }

    private bool IsGroundFoliageGenerationStillWanted(
        ChunkRecord record,
        ChunkCoord viewerCoord,
        GroundFoliageGenerationType generationType)
    {
        switch (generationType)
        {
            case GroundFoliageGenerationType.Flower:
                return IsFlowerSystemEnabled() &&
                       HasFlowerRenderAssets() &&
                       IsWithinFlowerRenderRange(viewerCoord, record.ChunkCoord) &&
                       (record.FoliageData == null || !record.FoliageData.flowersGenerated);
            case GroundFoliageGenerationType.Clover:
                return IsCloverSystemEnabled() &&
                       HasCloverRenderAssets() &&
                       IsWithinCloverGenerationRange(viewerCoord, record.ChunkCoord) &&
                       (record.FoliageData == null || !record.FoliageData.cloverGenerated);
            case GroundFoliageGenerationType.Dandelion:
                return IsDandelionSystemEnabled() &&
                       HasDandelionRenderAssets() &&
                       IsWithinDandelionRenderRange(viewerCoord, record.ChunkCoord) &&
                       (record.FoliageData == null || !record.FoliageData.dandelionsGenerated);
            default:
                return false;
        }
    }

    private bool IsCloverReadyForGrass(ChunkRecord record, ChunkCoord viewerCoord)
    {
        if (!IsCloverSystemEnabled() || !HasCloverRenderAssets())
            return true;

        if (!ShouldApplyCloverInfluenceToGrass(viewerCoord, record.ChunkCoord))
            return true;

        return record.FoliageData != null && record.FoliageData.cloverGenerated;
    }

    private bool ShouldApplyCloverInfluenceToGrass(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        return IsCloverSystemEnabled() &&
               HasCloverRenderAssets() &&
               IsWithinNearGrassGenerationRange(viewerCoord, targetCoord) &&
               IsWithinCloverGenerationRange(viewerCoord, targetCoord);
    }

    private int GetNearGrassGenerationRingRadius()
    {
        return Mathf.Max(0, grassSettings.activeRingRadius) +
               Mathf.Max(0, grassSettings.nearGrassPrecomputeChunkPadding);
    }

    private void EnsureNearGrassCloverInfluenceState(
        ChunkRecord record,
        ChunkRuntime runtime,
        bool shouldUseCloverInfluence)
    {
        if (record == null || record.FoliageData == null)
            return;

        ChunkFoliageData data = record.FoliageData;
        if (!data.HasAnyNearGrassSubChunkGenerated() ||
            data.nearGrassUsesCloverInfluence == shouldUseCloverInfluence)
        {
            return;
        }

        data.ClearNearGrass();
        if (runtime != null && runtime.FoliageRuntime != null)
            runtime.FoliageRuntime.ClearGrassBatches();
    }

    private ChunkFoliageData EnsureGrassSubChunkStorage(ChunkRecord record)
    {
        if (record.FoliageData == null)
        {
            record.FoliageData = new ChunkFoliageData();
        }

        int subChunksPerChunk = Mathf.Max(1, grassSettings.subChunksPerChunk);
        if (record.FoliageData.nearGrassInstancesBySubChunk == null ||
            record.FoliageData.nearGrassSubChunkGenerated == null ||
            record.FoliageData.subChunksPerChunk != subChunksPerChunk)
        {
            record.FoliageData.InitializeNearGrass(subChunksPerChunk);
        }

        return record.FoliageData;
    }

    private int GetActiveGrassSubChunkRadius(int subChunksPerChunk)
    {
        if (grassSettings.activeSubChunkRadius > 0)
            return grassSettings.activeSubChunkRadius;

        return Mathf.Max(1, (GetNearGrassGenerationRingRadius() + 1) * subChunksPerChunk);
    }

    private static bool IsGrassSubChunkDesired(
        ChunkCoord chunkCoord,
        int localSubX,
        int localSubZ,
        SubChunkCoord viewerGlobalSubChunk,
        int subChunksPerChunk,
        int activeSubChunkRadius)
    {
        subChunksPerChunk = Mathf.Max(1, subChunksPerChunk);
        int globalSubX = chunkCoord.x * subChunksPerChunk + localSubX;
        int globalSubZ = chunkCoord.z * subChunksPerChunk + localSubZ;
        int dx = Mathf.Abs(globalSubX - viewerGlobalSubChunk.x);
        int dz = Mathf.Abs(globalSubZ - viewerGlobalSubChunk.z);
        return dx <= activeSubChunkRadius && dz <= activeSubChunkRadius;
    }

    private void RebuildFlowerBatches(ChunkRuntime runtime, ChunkRecord record)
    {
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;
        ChunkFoliageData data = record.FoliageData;

        if (foliageRuntime == null || data == null || data.flowerInstances == null)
            return;

        long stageStart = TerrainGenerationProfiler.GetTimestamp();
        int instanceCount = data.flowerInstances.Count;

        if (instanceCount == 0)
        {
            foliageRuntime.CacheFlowerBatches(Array.Empty<Matrix4x4>(), Array.Empty<Vector4>());
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageFlowerBatchBuild,
                stageStart);
            return;
        }

        Matrix4x4 chunkLocalToWorld = runtime.RootTransform.localToWorldMatrix;
        NativeArray<FlowerRenderSourceData> sources =
            new NativeArray<FlowerRenderSourceData>(instanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<float4x4> nativeMatrices =
            new NativeArray<float4x4>(instanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<float4> nativePetalColors =
            new NativeArray<float4>(instanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        try
        {
            for (int i = 0; i < instanceCount; i++)
            {
                FlowerInstanceData instance = data.flowerInstances[i];
                Vector4 petalColor = Color32ToVector4(instance.petalColor);

                sources[i] = new FlowerRenderSourceData
                {
                    localPosition = new float3(
                        instance.localPosition.x,
                        instance.localPosition.y,
                        instance.localPosition.z),
                    localRotation = new quaternion(
                        instance.localRotation.x,
                        instance.localRotation.y,
                        instance.localRotation.z,
                        instance.localRotation.w),
                    localScale = new float3(
                        instance.localScale.x,
                        instance.localScale.y,
                        instance.localScale.z),
                    petalColor = new float4(petalColor.x, petalColor.y, petalColor.z, petalColor.w)
                };
            }

            FlowerRenderBatchBuildJob job = new FlowerRenderBatchBuildJob
            {
                sources = sources,
                chunkLocalToWorld = ToFloat4x4(chunkLocalToWorld),
                matrices = nativeMatrices,
                petalColors = nativePetalColors
            };

            JobHandle handle = job.Schedule(instanceCount, 64);
            handle.Complete();

            Matrix4x4[] worldMatrices = new Matrix4x4[instanceCount];
            Vector4[] petalColors = new Vector4[instanceCount];

            for (int i = 0; i < instanceCount; i++)
            {
                worldMatrices[i] = ToMatrix4x4(nativeMatrices[i]);
                petalColors[i] = ToVector4(nativePetalColors[i]);
            }

            foliageRuntime.CacheFlowerBatches(worldMatrices, petalColors);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageFlowerBatchBuild,
                stageStart);
        }
        finally
        {
            if (sources.IsCreated)
                sources.Dispose();
            if (nativeMatrices.IsCreated)
                nativeMatrices.Dispose();
            if (nativePetalColors.IsCreated)
                nativePetalColors.Dispose();
        }
    }

    private void RebuildCloverBatches(ChunkRuntime runtime, ChunkRecord record)
    {
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;
        ChunkFoliageData data = record.FoliageData;

        if (foliageRuntime == null || data == null || data.cloverInstances == null)
            return;

        long stageStart = TerrainGenerationProfiler.GetTimestamp();
        int prefabCount = GetCloverRenderAssetCount();
        int instanceCount = data.cloverInstances.Count;

        if (prefabCount == 0 || instanceCount == 0)
        {
            foliageRuntime.CacheCloverBatches(
                Array.Empty<List<Matrix4x4>>(),
                Array.Empty<List<Vector4>>());
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageCloverBatchBuild,
                stageStart);
            return;
        }

        Matrix4x4 chunkLocalToWorld = runtime.RootTransform.localToWorldMatrix;
        NativeArray<CloverRenderSourceData> sources =
            new NativeArray<CloverRenderSourceData>(instanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<float4x4> nativeMatrices =
            new NativeArray<float4x4>(instanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<float4> nativeInstanceData =
            new NativeArray<float4>(instanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        try
        {
            for (int i = 0; i < instanceCount; i++)
            {
                CloverInstanceData instance = data.cloverInstances[i];
                sources[i] = new CloverRenderSourceData
                {
                    localPosition = new float3(
                        instance.localPosition.x,
                        instance.localPosition.y,
                        instance.localPosition.z),
                    localRotation = new quaternion(
                        instance.localRotation.x,
                        instance.localRotation.y,
                        instance.localRotation.z,
                        instance.localRotation.w),
                    localScale = new float3(
                        instance.localScale.x,
                        instance.localScale.y,
                        instance.localScale.z),
                    selectionRank = instance.selectionRank
                };
            }

            CloverRenderBatchBuildJob job = new CloverRenderBatchBuildJob
            {
                sources = sources,
                chunkLocalToWorld = ToFloat4x4(chunkLocalToWorld),
                matrices = nativeMatrices,
                instanceData = nativeInstanceData
            };

            JobHandle handle = job.Schedule(instanceCount, 64);
            handle.Complete();

            List<Matrix4x4>[] worldMatricesByPrefab = new List<Matrix4x4>[prefabCount];
            List<Vector4>[] instanceDataByPrefab = new List<Vector4>[prefabCount];

            for (int i = 0; i < prefabCount; i++)
            {
                worldMatricesByPrefab[i] = new List<Matrix4x4>();
                instanceDataByPrefab[i] = new List<Vector4>();
            }

            for (int i = 0; i < instanceCount; i++)
            {
                CloverInstanceData instance = data.cloverInstances[i];
                int prefabIndex = Mathf.Clamp(instance.prefabIndex, 0, prefabCount - 1);
                worldMatricesByPrefab[prefabIndex].Add(ToMatrix4x4(nativeMatrices[i]));
                instanceDataByPrefab[prefabIndex].Add(ToVector4(nativeInstanceData[i]));
            }

            foliageRuntime.CacheCloverBatches(worldMatricesByPrefab, instanceDataByPrefab);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageCloverBatchBuild,
                stageStart);
        }
        finally
        {
            if (sources.IsCreated)
                sources.Dispose();
            if (nativeMatrices.IsCreated)
                nativeMatrices.Dispose();
            if (nativeInstanceData.IsCreated)
                nativeInstanceData.Dispose();
        }
    }

    private void RebuildDandelionBatches(ChunkRuntime runtime, ChunkRecord record)
    {
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;
        ChunkFoliageData data = record.FoliageData;

        if (foliageRuntime == null || data == null || data.dandelionInstances == null)
            return;

        long stageStart = TerrainGenerationProfiler.GetTimestamp();
        int instanceCount = data.dandelionInstances.Count;

        if (instanceCount == 0)
        {
            foliageRuntime.CacheDandelionBatches(Array.Empty<Matrix4x4>(), Array.Empty<Vector4>());
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageDandelionBatchBuild,
                stageStart);
            return;
        }

        Matrix4x4 chunkLocalToWorld = runtime.RootTransform.localToWorldMatrix;
        NativeArray<CloverRenderSourceData> sources =
            new NativeArray<CloverRenderSourceData>(instanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<float4x4> nativeMatrices =
            new NativeArray<float4x4>(instanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<float4> nativeInstanceData =
            new NativeArray<float4>(instanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        try
        {
            for (int i = 0; i < instanceCount; i++)
            {
                DandelionInstanceData instance = data.dandelionInstances[i];
                sources[i] = new CloverRenderSourceData
                {
                    localPosition = new float3(
                        instance.localPosition.x,
                        instance.localPosition.y,
                        instance.localPosition.z),
                    localRotation = new quaternion(
                        instance.localRotation.x,
                        instance.localRotation.y,
                        instance.localRotation.z,
                        instance.localRotation.w),
                    localScale = new float3(
                        instance.localScale.x,
                        instance.localScale.y,
                        instance.localScale.z),
                    selectionRank = instance.selectionRank
                };
            }

            CloverRenderBatchBuildJob job = new CloverRenderBatchBuildJob
            {
                sources = sources,
                chunkLocalToWorld = ToFloat4x4(chunkLocalToWorld),
                matrices = nativeMatrices,
                instanceData = nativeInstanceData
            };

            JobHandle handle = job.Schedule(instanceCount, 64);
            handle.Complete();

            Matrix4x4[] worldMatrices = new Matrix4x4[instanceCount];
            Vector4[] instanceData = new Vector4[instanceCount];

            for (int i = 0; i < instanceCount; i++)
            {
                worldMatrices[i] = ToMatrix4x4(nativeMatrices[i]);
                instanceData[i] = ToVector4(nativeInstanceData[i]);
            }

            foliageRuntime.CacheDandelionBatches(worldMatrices, instanceData);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageDandelionBatchBuild,
                stageStart);
        }
        finally
        {
            if (sources.IsCreated)
                sources.Dispose();
            if (nativeMatrices.IsCreated)
                nativeMatrices.Dispose();
            if (nativeInstanceData.IsCreated)
                nativeInstanceData.Dispose();
        }
    }

    private void RebuildGrassMatricesForViewerSubChunk(
        ChunkRuntime runtime,
        ChunkRecord record,
        SubChunkCoord viewerGlobalSubChunk)
    {
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;
        ChunkFoliageData data = record.FoliageData;

        if (foliageRuntime == null || data == null || data.nearGrassInstancesBySubChunk == null)
            return;

        long stageStart = TerrainGenerationProfiler.GetTimestamp();
        Matrix4x4 chunkLocalToWorld = runtime.RootTransform.localToWorldMatrix;

        int subChunksPerChunk = data.subChunksPerChunk;
        int selectedInstanceCount = 0;

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
                    data.nearGrassInstancesBySubChunk[localSubX, localSubZ];

                int totalCount = subChunkInstances.Count;
                int renderCount = Mathf.FloorToInt(totalCount * density);

                if (density > 0f && totalCount > 0)
                {
                    renderCount = Mathf.Clamp(renderCount, 1, totalCount);
                }

                selectedInstanceCount += renderCount;
            }
        }

        if (selectedInstanceCount == 0)
        {
            foliageRuntime.CacheGrassMatrices(Array.Empty<Matrix4x4>(), Array.Empty<Vector4>());
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageGrassRenderBatchBuild,
                stageStart);
            return;
        }

        NativeArray<GrassRenderSourceData> sources =
            new NativeArray<GrassRenderSourceData>(selectedInstanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<float4x4> nativeMatrices =
            new NativeArray<float4x4>(selectedInstanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<float4> nativeInstanceData =
            new NativeArray<float4>(selectedInstanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        try
        {
            int sourceIndex = 0;

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
                        data.nearGrassInstancesBySubChunk[localSubX, localSubZ];

                    int totalCount = subChunkInstances.Count;
                    int renderCount = Mathf.FloorToInt(totalCount * density);

                    if (density > 0f && totalCount > 0)
                    {
                        renderCount = Mathf.Clamp(renderCount, 1, totalCount);
                    }

                    for (int i = 0; i < renderCount; i++)
                    {
                        FoliageInstanceData instance = subChunkInstances[i];
                        sources[sourceIndex] = new GrassRenderSourceData
                        {
                            localPosition = new float3(
                                instance.localPosition.x,
                                instance.localPosition.y,
                                instance.localPosition.z),
                            localRotation = new quaternion(
                                instance.localRotation.x,
                                instance.localRotation.y,
                                instance.localRotation.z,
                                instance.localRotation.w),
                            localScale = new float3(
                                instance.localScale.x,
                                instance.localScale.y,
                                instance.localScale.z),
                            selectionRank = instance.selectionRank,
                            forestBlend = instance.forestBlend
                        };
                        sourceIndex++;
                    }
                }
            }

            GrassRenderBatchBuildJob job = new GrassRenderBatchBuildJob
            {
                sources = sources,
                chunkLocalToWorld = ToFloat4x4(chunkLocalToWorld),
                matrices = nativeMatrices,
                instanceData = nativeInstanceData
            };

            JobHandle handle = job.Schedule(selectedInstanceCount, 64);
            handle.Complete();

            Matrix4x4[] worldMatrices = new Matrix4x4[selectedInstanceCount];
            Vector4[] instanceData = new Vector4[selectedInstanceCount];

            for (int i = 0; i < selectedInstanceCount; i++)
            {
                worldMatrices[i] = ToMatrix4x4(nativeMatrices[i]);
                float4 nativeData = nativeInstanceData[i];
                instanceData[i] = new Vector4(nativeData.x, nativeData.y, nativeData.z, nativeData.w);
            }

            foliageRuntime.CacheGrassMatrices(worldMatrices, instanceData);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageGrassRenderBatchBuild,
                stageStart);
        }
        finally
        {
            if (sources.IsCreated)
                sources.Dispose();
            if (nativeMatrices.IsCreated)
                nativeMatrices.Dispose();
            if (nativeInstanceData.IsCreated)
                nativeInstanceData.Dispose();
        }
    }

    private static float4x4 ToFloat4x4(Matrix4x4 matrix)
    {
        return new float4x4(
            new float4(matrix.m00, matrix.m10, matrix.m20, matrix.m30),
            new float4(matrix.m01, matrix.m11, matrix.m21, matrix.m31),
            new float4(matrix.m02, matrix.m12, matrix.m22, matrix.m32),
            new float4(matrix.m03, matrix.m13, matrix.m23, matrix.m33));
    }

    private static Matrix4x4 ToMatrix4x4(float4x4 matrix)
    {
        Matrix4x4 result = new Matrix4x4();
        result.m00 = matrix.c0.x;
        result.m10 = matrix.c0.y;
        result.m20 = matrix.c0.z;
        result.m30 = matrix.c0.w;
        result.m01 = matrix.c1.x;
        result.m11 = matrix.c1.y;
        result.m21 = matrix.c1.z;
        result.m31 = matrix.c1.w;
        result.m02 = matrix.c2.x;
        result.m12 = matrix.c2.y;
        result.m22 = matrix.c2.z;
        result.m32 = matrix.c2.w;
        result.m03 = matrix.c3.x;
        result.m13 = matrix.c3.y;
        result.m23 = matrix.c3.z;
        result.m33 = matrix.c3.w;
        return result;
    }

    private static Vector4 ToVector4(float4 value)
    {
        return new Vector4(value.x, value.y, value.z, value.w);
    }

    private struct GrassRenderSourceData
    {
        public float3 localPosition;
        public quaternion localRotation;
        public float3 localScale;
        public uint selectionRank;
        public float forestBlend;
    }

    [BurstCompile]
    private struct GrassRenderBatchBuildJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<GrassRenderSourceData> sources;
        public float4x4 chunkLocalToWorld;
        [WriteOnly] public NativeArray<float4x4> matrices;
        [WriteOnly] public NativeArray<float4> instanceData;

        public void Execute(int index)
        {
            GrassRenderSourceData source = sources[index];
            matrices[index] = math.mul(
                chunkLocalToWorld,
                float4x4.TRS(source.localPosition, source.localRotation, source.localScale));
            instanceData[index] = new float4(
                source.forestBlend,
                SelectionRankToUnitPhase(source.selectionRank),
                0f,
                0f);
        }

        private static float SelectionRankToUnitPhase(uint selectionRank)
        {
            const float inv24Bit = 1f / 16777216f;
            return (selectionRank & 0x00FFFFFFu) * inv24Bit;
        }
    }

    private struct BillboardGrassRenderSourceData
    {
        public float3 localPosition;
        public quaternion localRotation;
        public float3 localScale;
        public uint selectionRank;
        public float forestBlend;
    }

    [BurstCompile]
    private struct BillboardGrassRenderBatchBuildJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<BillboardGrassRenderSourceData> sources;
        public float4x4 chunkLocalToWorld;
        public float scaleMultiplier;
        [WriteOnly] public NativeArray<float4x4> matrices;
        [WriteOnly] public NativeArray<float4> instanceData;

        public void Execute(int index)
        {
            BillboardGrassRenderSourceData source = sources[index];
            matrices[index] = math.mul(
                chunkLocalToWorld,
                float4x4.TRS(source.localPosition, source.localRotation, source.localScale * scaleMultiplier));
            instanceData[index] = new float4(
                source.forestBlend,
                SelectionRankToUnitPhase(source.selectionRank),
                0f,
                0f);
        }

        private static float SelectionRankToUnitPhase(uint selectionRank)
        {
            const float inv24Bit = 1f / 16777216f;
            return (selectionRank & 0x00FFFFFFu) * inv24Bit;
        }
    }

    private struct FlowerRenderSourceData
    {
        public float3 localPosition;
        public quaternion localRotation;
        public float3 localScale;
        public float4 petalColor;
    }

    private struct CloverRenderSourceData
    {
        public float3 localPosition;
        public quaternion localRotation;
        public float3 localScale;
        public uint selectionRank;
    }

    [BurstCompile]
    private struct FlowerRenderBatchBuildJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<FlowerRenderSourceData> sources;
        public float4x4 chunkLocalToWorld;
        [WriteOnly] public NativeArray<float4x4> matrices;
        [WriteOnly] public NativeArray<float4> petalColors;

        public void Execute(int index)
        {
            FlowerRenderSourceData source = sources[index];
            matrices[index] = math.mul(
                chunkLocalToWorld,
                float4x4.TRS(source.localPosition, source.localRotation, source.localScale));
            petalColors[index] = source.petalColor;
        }
    }

    [BurstCompile]
    private struct CloverRenderBatchBuildJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<CloverRenderSourceData> sources;
        public float4x4 chunkLocalToWorld;
        [WriteOnly] public NativeArray<float4x4> matrices;
        [WriteOnly] public NativeArray<float4> instanceData;

        public void Execute(int index)
        {
            CloverRenderSourceData source = sources[index];
            matrices[index] = math.mul(
                chunkLocalToWorld,
                float4x4.TRS(source.localPosition, source.localRotation, source.localScale));

            float phase = SelectionRankToUnitPhase(source.selectionRank);
            float colorSeed = math.frac(phase * 37.618034f);
            instanceData[index] = new float4(phase, colorSeed, 0f, 0f);
        }

        private static float SelectionRankToUnitPhase(uint selectionRank)
        {
            const float inv24Bit = 1f / 16777216f;
            return (selectionRank & 0x00FFFFFFu) * inv24Bit;
        }
    }

    private struct TreeBillboardRenderSourceData
    {
        public float3 localPosition;
        public quaternion localRotation;
        public float3 localScale;
        public float4 leafTint;
    }

    [BurstCompile]
    private struct TreeBillboardRenderBatchBuildJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<TreeBillboardRenderSourceData> sources;
        public float4x4 chunkLocalToWorld;
        [WriteOnly] public NativeArray<float4x4> matrices;
        [WriteOnly] public NativeArray<float4> leafTints;

        public void Execute(int index)
        {
            TreeBillboardRenderSourceData source = sources[index];
            matrices[index] = math.mul(
                chunkLocalToWorld,
                float4x4.TRS(source.localPosition, source.localRotation, source.localScale));
            leafTints[index] = source.leafTint;
        }
    }

    private void RebuildBillboardMatrices(
        ChunkRuntime runtime,
        ChunkRecord record,
        ChunkCoord viewerCoord)
    {
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;
        ChunkFoliageData data = record.FoliageData;

        if (foliageRuntime == null || data == null || data.billboardGrassInstances == null)
            return;

        long stageStart = TerrainGenerationProfiler.GetTimestamp();
        Matrix4x4 chunkLocalToWorld = runtime.RootTransform.localToWorldMatrix;

        int chunkRing = GetChunkRingDistance(viewerCoord, record.ChunkCoord);
        float densityMultiplier = Mathf.Clamp01(grassSettings.billboardSpawnChance) *
                                  GetBillboardDensityMultiplierForChunkRing(chunkRing);
        float scaleMultiplier = 1f;

        int cellsPerAxis = Mathf.Max(1, grassSettings.billboardCellsPerAxis);
        float cellSize = (float)chunkSize / cellsPerAxis;

        List<BillboardFoliageInstanceData>[,] cellBuckets =
            new List<BillboardFoliageInstanceData>[cellsPerAxis, cellsPerAxis];

        for (int x = 0; x < cellsPerAxis; x++)
        {
            for (int z = 0; z < cellsPerAxis; z++)
            {
                cellBuckets[x, z] = new List<BillboardFoliageInstanceData>();
            }
        }

        for (int i = 0; i < data.billboardGrassInstances.Count; i++)
        {
            BillboardFoliageInstanceData instance = data.billboardGrassInstances[i];

            float localX = (instance.localPosition.x / worldScale) + chunkSize / 2f;
            float localZ = (instance.localPosition.z / worldScale) + chunkSize / 2f;

            int cellX = Mathf.Clamp(Mathf.FloorToInt(localX / cellSize), 0, cellsPerAxis - 1);
            int cellZ = Mathf.Clamp(Mathf.FloorToInt(localZ / cellSize), 0, cellsPerAxis - 1);

            cellBuckets[cellX, cellZ].Add(instance);
        }

        List<BillboardFoliageInstanceData> selectedInstances = new List<BillboardFoliageInstanceData>();

        for (int cellX = 0; cellX < cellsPerAxis; cellX++)
        {
            for (int cellZ = 0; cellZ < cellsPerAxis; cellZ++)
            {
                List<BillboardFoliageInstanceData> bucket = cellBuckets[cellX, cellZ];
                int totalCount = bucket.Count;

                if (totalCount == 0)
                    continue;

                bucket.Sort((a, b) => a.selectionRank.CompareTo(b.selectionRank));
                int renderCount = GetBillboardGrassRenderCount(
                    totalCount,
                    densityMultiplier,
                    record.ChunkCoord,
                    cellX,
                    cellZ);

                for (int i = 0; i < renderCount; i++)
                {
                    BillboardFoliageInstanceData instance = bucket[i];
                    selectedInstances.Add(instance);
                }
            }
        }

        int selectedCount = selectedInstances.Count;
        if (selectedCount == 0)
        {
            foliageRuntime.CacheBillboardMatrices(Array.Empty<Matrix4x4>(), Array.Empty<Vector4>());
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageBillboardGrassBatchBuild,
                stageStart);
            return;
        }

        NativeArray<BillboardGrassRenderSourceData> sources =
            new NativeArray<BillboardGrassRenderSourceData>(selectedCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<float4x4> nativeMatrices =
            new NativeArray<float4x4>(selectedCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<float4> nativeInstanceData =
            new NativeArray<float4>(selectedCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        try
        {
            for (int i = 0; i < selectedCount; i++)
            {
                BillboardFoliageInstanceData instance = selectedInstances[i];
                sources[i] = new BillboardGrassRenderSourceData
                {
                    localPosition = new float3(
                        instance.localPosition.x,
                        instance.localPosition.y,
                        instance.localPosition.z),
                    localRotation = new quaternion(
                        instance.localRotation.x,
                        instance.localRotation.y,
                        instance.localRotation.z,
                        instance.localRotation.w),
                    localScale = new float3(
                        instance.localScale.x,
                        instance.localScale.y,
                        instance.localScale.z),
                    selectionRank = instance.selectionRank,
                    forestBlend = instance.forestBlend
                };
            }

            BillboardGrassRenderBatchBuildJob job = new BillboardGrassRenderBatchBuildJob
            {
                sources = sources,
                chunkLocalToWorld = ToFloat4x4(chunkLocalToWorld),
                scaleMultiplier = scaleMultiplier,
                matrices = nativeMatrices,
                instanceData = nativeInstanceData
            };

            JobHandle handle = job.Schedule(selectedCount, 64);
            handle.Complete();

            Matrix4x4[] worldMatrices = new Matrix4x4[selectedCount];
            Vector4[] instanceData = new Vector4[selectedCount];

            for (int i = 0; i < selectedCount; i++)
            {
                worldMatrices[i] = ToMatrix4x4(nativeMatrices[i]);
                instanceData[i] = ToVector4(nativeInstanceData[i]);
            }

            foliageRuntime.CacheBillboardMatrices(worldMatrices, instanceData);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageBillboardGrassBatchBuild,
                stageStart);
        }
        finally
        {
            if (sources.IsCreated)
                sources.Dispose();
            if (nativeMatrices.IsCreated)
                nativeMatrices.Dispose();
            if (nativeInstanceData.IsCreated)
                nativeInstanceData.Dispose();
        }
    }

    private FoliageRepresentationMode GetTreeRepresentationMode(
        ChunkCoord viewerCoord,
        ChunkCoord targetCoord)
    {
        int ring = GetChunkRingDistance(viewerCoord, targetCoord);

        if (ring <= treeSettings.gameObjectTreeChunkRingRadius)
            return FoliageRepresentationMode.GameObjectWithCollision;

        return FoliageRepresentationMode.GPUInstancedBillboard;
    }

    private bool IsWithinTreeRenderRange(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        int ring = GetChunkRingDistance(viewerCoord, targetCoord);
        return ring <= treeSettings.gameObjectTreeChunkRingRadius ||
               IsWithinBillboardTreeRenderRange(ring);
    }

    private bool ShouldRetainTreeGameObjectsForReuse(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        int extraRings = treeSettings != null
            ? Mathf.Max(0, treeSettings.treeGameObjectWarmRetainExtraRings)
            : 0;

        if (extraRings == 0)
            return false;

        int ring = GetChunkRingDistance(viewerCoord, targetCoord);
        return ring <= treeSettings.gameObjectTreeChunkRingRadius + extraRings;
    }

    private bool IsWithinBillboardTreeRenderRange(int ring)
    {
        int billboardStartRing = Mathf.Max(
            treeSettings.gameObjectTreeChunkRingRadius + 1,
            treeSettings.billboardTreeChunkStartRingRadius);

        return ring >= billboardStartRing &&
               ring <= treeSettings.billboardTreeChunkRingRadius;
    }

    private bool IsWithinBushRenderRange(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        int ring = GetChunkRingDistance(viewerCoord, targetCoord);
        return ring <= treeSettings.gameObjectBushChunkRingRadius;
    }

    private bool IsWithinRockRenderRange(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        int ring = GetChunkRingDistance(viewerCoord, targetCoord);
        return ring <= treeSettings.gameObjectRockChunkRingRadius;
    }

    private int GetChunkRingDistance(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        int dx = Mathf.Abs(targetCoord.x - viewerCoord.x);
        int dz = Mathf.Abs(targetCoord.z - viewerCoord.z);
        return Mathf.Max(dx, dz);
    }

    private float GetBillboardDensityMultiplierForChunkRing(int chunkRing)
    {
        if (chunkRing <= 2)
            return 1f;

        return 1f / (chunkRing - 1);
    }

    private int GetBillboardGrassRenderCount(
        int totalCount,
        float densityMultiplier,
        ChunkCoord chunkCoord,
        int cellX,
        int cellZ)
    {
        if (totalCount <= 0)
            return 0;

        float exactCount = totalCount * Mathf.Clamp01(densityMultiplier);
        int renderCount = Mathf.FloorToInt(exactCount);
        float fractionalCount = exactCount - renderCount;

        if (renderCount < totalCount &&
            fractionalCount > 0f &&
            Hash01(Hash6(worldSeed, chunkCoord.x, chunkCoord.z, cellX, cellZ, 1301)) < fractionalCount)
        {
            renderCount++;
        }

        return Mathf.Clamp(renderCount, 0, totalCount);
    }

    private static int Hash6(int v0, int v1, int v2, int v3, int v4, int v5)
    {
        unchecked
        {
            uint hash = 2166136261u;
            hash = MixHash(hash, v0);
            hash = MixHash(hash, v1);
            hash = MixHash(hash, v2);
            hash = MixHash(hash, v3);
            hash = MixHash(hash, v4);
            hash = MixHash(hash, v5);
            return (int)hash;
        }
    }

    private static uint MixHash(uint hash, int value)
    {
        unchecked
        {
            hash ^= (uint)value;
            hash *= 16777619u;
            hash ^= hash >> 13;
            hash *= 1274126177u;
            hash ^= hash >> 16;
            return hash;
        }
    }

    private static float Hash01(int hash)
    {
        unchecked
        {
            uint value = (uint)hash;

            value ^= value >> 17;
            value *= 0xed5ad4bbu;
            value ^= value >> 11;
            value *= 0xac4c1b51u;
            value ^= value >> 15;
            value *= 0x31848babu;
            value ^= value >> 14;

            return value / 4294967295f;
        }
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

    private bool IsWithinNearGrass(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        int dx = Mathf.Abs(targetCoord.x - viewerCoord.x);
        int dz = Mathf.Abs(targetCoord.z - viewerCoord.z);
        return dx <= grassSettings.activeRingRadius && dz <= grassSettings.activeRingRadius;
    }

    private bool IsWithinNearGrassGenerationRange(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        int generationRingRadius = GetNearGrassGenerationRingRadius();
        int dx = Mathf.Abs(targetCoord.x - viewerCoord.x);
        int dz = Mathf.Abs(targetCoord.z - viewerCoord.z);
        return dx <= generationRingRadius && dz <= generationRingRadius;
    }

    private bool IsWithinFlowerRenderRange(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        if (!IsFlowerSystemEnabled())
            return false;

        int dx = Mathf.Abs(targetCoord.x - viewerCoord.x);
        int dz = Mathf.Abs(targetCoord.z - viewerCoord.z);
        return dx <= flowerSettings.activeRingRadius && dz <= flowerSettings.activeRingRadius;
    }

    private bool IsWithinCloverRenderRange(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        if (!IsCloverSystemEnabled())
            return false;

        int activeRingRadius = Mathf.Min(
            Mathf.Max(0, cloverSettings.activeRingRadius),
            Mathf.Max(0, grassSettings.activeRingRadius));
        int dx = Mathf.Abs(targetCoord.x - viewerCoord.x);
        int dz = Mathf.Abs(targetCoord.z - viewerCoord.z);
        return dx <= activeRingRadius && dz <= activeRingRadius;
    }

    private bool IsWithinCloverGenerationRange(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        if (!IsCloverSystemEnabled())
            return false;

        int activeRingRadius = Mathf.Min(
            Mathf.Max(0, cloverSettings.activeRingRadius),
            Mathf.Max(0, grassSettings.activeRingRadius));
        int preGenerationPadding = Mathf.Max(0, cloverSettings.preGenerationRingPadding);
        int generationRingRadius = activeRingRadius + preGenerationPadding;
        int dx = Mathf.Abs(targetCoord.x - viewerCoord.x);
        int dz = Mathf.Abs(targetCoord.z - viewerCoord.z);
        return dx <= generationRingRadius && dz <= generationRingRadius;
    }

    private bool IsWithinDandelionRenderRange(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        if (!IsDandelionSystemEnabled())
            return false;

        int activeRingRadius = Mathf.Max(0, dandelionSettings.activeRingRadius);
        int dx = Mathf.Abs(targetCoord.x - viewerCoord.x);
        int dz = Mathf.Abs(targetCoord.z - viewerCoord.z);
        return dx <= activeRingRadius && dz <= activeRingRadius;
    }

    private bool IsWithinBillboardGrass(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        int absDx = Mathf.Abs(targetCoord.x - viewerCoord.x);
        int absDz = Mathf.Abs(targetCoord.z - viewerCoord.z);

        bool insideNearSquare =
            absDx <= grassSettings.activeRingRadius &&
            absDz <= grassSettings.activeRingRadius;

        if (insideNearSquare)
            return false;

        int dx = targetCoord.x - viewerCoord.x;
        int dz = targetCoord.z - viewerCoord.z;
        int distSqr = dx * dx + dz * dz;

        int billboardRangeSqr = grassSettings.billboardRingRadius * grassSettings.billboardRingRadius;
        return distSqr <= billboardRangeSqr;
    }

    private bool HasRequiredTerrainData(ChunkRecord record)
    {
        return record.HeightMap != null &&
               record.SurfaceTypeMap != null &&
               record.BiomeMap != null;
    }

    private bool IsFlowerSystemEnabled()
    {
        return flowerSettings != null && flowerSettings.enableFlowers;
    }

    private bool IsCloverSystemEnabled()
    {
        return cloverSettings != null && cloverSettings.enableClover;
    }

    private bool IsDandelionSystemEnabled()
    {
        return dandelionSettings != null && dandelionSettings.enableDandelions;
    }

    private bool HasFlowerRenderAssets()
    {
        return flowerMesh != null && flowerMaterial != null;
    }

    private bool HasCloverRenderAssets()
    {
        if (cloverRenderData == null)
            return false;

        for (int i = 0; i < cloverRenderData.Length; i++)
        {
            if (cloverRenderData[i].mesh != null && cloverRenderData[i].material != null)
                return true;
        }

        return false;
    }

    private bool HasDandelionRenderAssets()
    {
        return dandelionMesh != null && dandelionMaterial != null;
    }

    private int GetCloverRenderAssetCount()
    {
        return cloverRenderData != null ? cloverRenderData.Length : 0;
    }

    private static Vector4 Color32ToVector4(Color32 color)
    {
        const float inv255 = 1f / 255f;
        return new Vector4(
            color.r * inv255,
            color.g * inv255,
            color.b * inv255,
            color.a * inv255);
    }

    private static Vector4 Color32ToLinearVector4(Color32 color)
    {
        Color linearColor = ((Color)color).linear;
        return new Vector4(linearColor.r, linearColor.g, linearColor.b, linearColor.a);
    }

    private readonly struct FoliageManagementWorkItem
    {
        public readonly ChunkCoord ChunkCoord;

        public FoliageManagementWorkItem(ChunkCoord chunkCoord)
        {
            ChunkCoord = chunkCoord;
        }
    }

    private readonly struct GrassSubChunkWorkItem
    {
        public readonly GrassSubChunkWorkKey Key;

        public GrassSubChunkWorkItem(GrassSubChunkWorkKey key)
        {
            Key = key;
        }
    }

    private readonly struct ActiveGrassSubChunkGenerationWorkItem
    {
        public readonly GrassSubChunkWorkKey Key;
        public readonly FoliageGenerator.GrassSubChunkGenerationJob GenerationJob;
        public readonly bool UsesCloverInfluence;

        public ActiveGrassSubChunkGenerationWorkItem(
            GrassSubChunkWorkKey key,
            FoliageGenerator.GrassSubChunkGenerationJob generationJob,
            bool usesCloverInfluence)
        {
            Key = key;
            GenerationJob = generationJob;
            UsesCloverInfluence = usesCloverInfluence;
        }
    }

    private readonly struct GrassSubChunkWorkKey : IEquatable<GrassSubChunkWorkKey>
    {
        public readonly ChunkCoord ChunkCoord;
        public readonly int LocalSubChunkX;
        public readonly int LocalSubChunkZ;

        public GrassSubChunkWorkKey(ChunkCoord chunkCoord, int localSubChunkX, int localSubChunkZ)
        {
            ChunkCoord = chunkCoord;
            LocalSubChunkX = localSubChunkX;
            LocalSubChunkZ = localSubChunkZ;
        }

        public bool Equals(GrassSubChunkWorkKey other)
        {
            return ChunkCoord == other.ChunkCoord &&
                   LocalSubChunkX == other.LocalSubChunkX &&
                   LocalSubChunkZ == other.LocalSubChunkZ;
        }

        public override bool Equals(object obj)
        {
            return obj is GrassSubChunkWorkKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ChunkCoord, LocalSubChunkX, LocalSubChunkZ);
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

    private enum GroundFoliageGenerationType
    {
        Flower,
        Clover,
        Dandelion
    }

    private readonly struct GroundFoliageGenerationWorkItem
    {
        public readonly GroundFoliageGenerationWorkKey Key;

        public GroundFoliageGenerationWorkItem(GroundFoliageGenerationWorkKey key)
        {
            Key = key;
        }
    }

    private readonly struct GroundFoliageGenerationWorkKey : IEquatable<GroundFoliageGenerationWorkKey>
    {
        public readonly ChunkCoord ChunkCoord;
        public readonly GroundFoliageGenerationType GenerationType;

        public GroundFoliageGenerationWorkKey(ChunkCoord chunkCoord, GroundFoliageGenerationType generationType)
        {
            ChunkCoord = chunkCoord;
            GenerationType = generationType;
        }

        public bool Equals(GroundFoliageGenerationWorkKey other)
        {
            return ChunkCoord == other.ChunkCoord &&
                   GenerationType == other.GenerationType;
        }

        public override bool Equals(object obj)
        {
            return obj is GroundFoliageGenerationWorkKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ChunkCoord, GenerationType);
        }
    }

    private enum FoliageBatchWorkType
    {
        NearGrass,
        BillboardGrass,
        Flower,
        Clover,
        Dandelion
    }

    private readonly struct FoliageBatchWorkItem
    {
        public readonly FoliageBatchWorkKey Key;

        public FoliageBatchWorkItem(FoliageBatchWorkKey key)
        {
            Key = key;
        }
    }

    private readonly struct FoliageBatchWorkKey : IEquatable<FoliageBatchWorkKey>
    {
        public readonly ChunkCoord ChunkCoord;
        public readonly FoliageBatchWorkType WorkType;

        public FoliageBatchWorkKey(ChunkCoord chunkCoord, FoliageBatchWorkType workType)
        {
            ChunkCoord = chunkCoord;
            WorkType = workType;
        }

        public bool Equals(FoliageBatchWorkKey other)
        {
            return ChunkCoord == other.ChunkCoord &&
                   WorkType == other.WorkType;
        }

        public override bool Equals(object obj)
        {
            return obj is FoliageBatchWorkKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ChunkCoord, WorkType);
        }
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
        chunkRuntime.FoliageRuntime.receiveGrassShadows = grassSettings.receiveGrassShadows;
        chunkRuntime.FoliageRuntime.grassInstanceDataPropertyId = grassInstanceDataPropertyId;
        chunkRuntime.FoliageRuntime.forestDarkGrassColor = grassSettings.forestDarkGrassColor;
        chunkRuntime.FoliageRuntime.forestMidGrassColor = grassSettings.forestMidGrassColor;
        chunkRuntime.FoliageRuntime.forestLightGrassColor = grassSettings.forestLightGrassColor;

        chunkRuntime.FoliageRuntime.billboardMesh = billboardGrassMesh;
        chunkRuntime.FoliageRuntime.billboardMaterial = billboardGrassMaterial;
        chunkRuntime.FoliageRuntime.enableBillboardGrassRenderFade = grassSettings.enableBillboardRenderFade;
        chunkRuntime.FoliageRuntime.billboardGrassRenderFadeDuration = grassSettings.billboardRenderFadeDuration;
        chunkRuntime.FoliageRuntime.billboardGrassFadeDitherPixelSize = grassSettings.billboardFadeDitherPixelSize;

        chunkRuntime.FoliageRuntime.flowerMesh = flowerMesh;
        chunkRuntime.FoliageRuntime.flowerMaterial = flowerMaterial;
        chunkRuntime.FoliageRuntime.flowerPetalColorPropertyId = flowerPetalColorPropertyId;

        chunkRuntime.FoliageRuntime.cloverRenderData = cloverRenderData;
        chunkRuntime.FoliageRuntime.receiveCloverShadows = cloverSettings != null && cloverSettings.receiveCloverShadows;
        chunkRuntime.FoliageRuntime.cloverInstanceDataPropertyId = cloverInstanceDataPropertyId;

        chunkRuntime.FoliageRuntime.dandelionMesh = dandelionMesh;
        chunkRuntime.FoliageRuntime.dandelionMaterial = dandelionMaterial;
        chunkRuntime.FoliageRuntime.receiveDandelionShadows = dandelionSettings != null && dandelionSettings.receiveDandelionShadows;
        chunkRuntime.FoliageRuntime.dandelionInstanceDataPropertyId = dandelionInstanceDataPropertyId;

        chunkRuntime.FoliageRuntime.mapleTreePrefab = treeSettings.mapleTreePrefab;
        chunkRuntime.FoliageRuntime.sugarMapleTreePrefab = treeSettings.sugarMapleTreePrefab;
        chunkRuntime.FoliageRuntime.birchAspenTreePrefab = treeSettings.birchAspenTreePrefab;
        chunkRuntime.FoliageRuntime.beechTreePrefab = treeSettings.beechTreePrefab;
        chunkRuntime.FoliageRuntime.spruceTreePrefab = treeSettings.spruceTreePrefab;
        chunkRuntime.FoliageRuntime.whitePineTreePrefab = treeSettings.whitePineTreePrefab;
        chunkRuntime.FoliageRuntime.oakTreePrefab = treeSettings.oakTreePrefab;
        chunkRuntime.FoliageRuntime.fallbackTreePrefab = treeSettings.treeLOD0GameObjectPrefab;
        chunkRuntime.FoliageRuntime.grasslandMapleTreePrefab = treeSettings.grasslandMapleTreePrefab;
        chunkRuntime.FoliageRuntime.grasslandBirchAspenTreePrefab = treeSettings.grasslandBirchAspenTreePrefab;
        chunkRuntime.FoliageRuntime.grasslandWhitePineTreePrefab = treeSettings.grasslandWhitePineTreePrefab;
        chunkRuntime.FoliageRuntime.grasslandOakTreePrefab = treeSettings.grasslandOakTreePrefab;
        chunkRuntime.FoliageRuntime.grasslandWillowTreePrefab = treeSettings.grasslandWillowTreePrefab;
        chunkRuntime.FoliageRuntime.grasslandFallbackTreePrefab = treeSettings.grasslandTreeFallbackPrefab;
        chunkRuntime.FoliageRuntime.blueberryBushPrefab = treeSettings.blueberryBushPrefab;
        chunkRuntime.FoliageRuntime.raspberryBushPrefab = treeSettings.raspberryBushPrefab;
        chunkRuntime.FoliageRuntime.strawberryBushPrefab = treeSettings.strawberryBushPrefab;
        chunkRuntime.FoliageRuntime.blackberryBushPrefab = treeSettings.blackberryBushPrefab;
        chunkRuntime.FoliageRuntime.fallbackBushPrefab = treeSettings.fallbackBushPrefab;
        chunkRuntime.FoliageRuntime.forestRockPrefabs = treeSettings.forestRockPrefabs;
        chunkRuntime.FoliageRuntime.forestRockFallbackPrefab = treeSettings.forestRockFallbackPrefab;
        chunkRuntime.FoliageRuntime.grasslandRockPrefabs = treeSettings.grasslandRockPrefabs;
        chunkRuntime.FoliageRuntime.grasslandRockFallbackPrefab = treeSettings.grasslandRockFallbackPrefab;
        chunkRuntime.FoliageRuntime.grasslandLargeRockPrefabs = treeSettings.grasslandLargeRockPrefabs;
        chunkRuntime.FoliageRuntime.grasslandLargeRockFallbackPrefab = treeSettings.grasslandLargeRockFallbackPrefab;
        chunkRuntime.FoliageRuntime.mapleTreeBillboard = mapleTreeBillboard;
        chunkRuntime.FoliageRuntime.sugarMapleTreeBillboard = sugarMapleTreeBillboard;
        chunkRuntime.FoliageRuntime.birchAspenTreeBillboard = birchAspenTreeBillboard;
        chunkRuntime.FoliageRuntime.beechTreeBillboard = beechTreeBillboard;
        chunkRuntime.FoliageRuntime.spruceTreeBillboard = spruceTreeBillboard;
        chunkRuntime.FoliageRuntime.whitePineTreeBillboard = whitePineTreeBillboard;
        chunkRuntime.FoliageRuntime.oakTreeBillboard = oakTreeBillboard;
        chunkRuntime.FoliageRuntime.fallbackTreeBillboard = fallbackTreeBillboard;
        chunkRuntime.FoliageRuntime.grasslandMapleTreeBillboard = grasslandMapleTreeBillboard;
        chunkRuntime.FoliageRuntime.grasslandBirchAspenTreeBillboard = grasslandBirchAspenTreeBillboard;
        chunkRuntime.FoliageRuntime.grasslandWhitePineTreeBillboard = grasslandWhitePineTreeBillboard;
        chunkRuntime.FoliageRuntime.grasslandOakTreeBillboard = grasslandOakTreeBillboard;
        chunkRuntime.FoliageRuntime.grasslandWillowTreeBillboard = grasslandWillowTreeBillboard;
        chunkRuntime.FoliageRuntime.grasslandFallbackTreeBillboard = grasslandFallbackTreeBillboard;

        chunkRuntime.FoliageRuntime.SetRenderVisible(chunkRuntime.IsFoliageRenderVisible);
        chunkRuntime.FoliageRuntime.SetShadowCasterVisible(chunkRuntime.IsFoliageShadowCasterVisible);
        chunkRuntime.FoliageRuntime.SetVisible(false);
    }

    private void ResolveGrassRenderAssets()
    {
        string instanceDataPropertyName = string.IsNullOrEmpty(grassSettings.grassInstanceDataPropertyName)
            ? "_GrassInstanceData"
            : grassSettings.grassInstanceDataPropertyName;

        grassInstanceDataPropertyId = Shader.PropertyToID(instanceDataPropertyName);

        if (grassSettings.grassPrefab == null)
        {
            Debug.LogError("Grass prefab is missing.");
        }
        else
        {
            MeshFilter meshFilter = grassSettings.grassPrefab.GetComponentInChildren<MeshFilter>();
            MeshRenderer meshRenderer = grassSettings.grassPrefab.GetComponentInChildren<MeshRenderer>();

            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                Debug.LogError("Grass prefab missing MeshFilter or mesh.");
            }
            else
            {
                grassMesh = meshFilter.sharedMesh;
            }

            if (meshRenderer == null || meshRenderer.sharedMaterial == null)
            {
                Debug.LogError("Grass prefab missing MeshRenderer or material.");
            }
            else
            {
                grassMaterial = meshRenderer.sharedMaterial;
                grassMaterial.enableInstancing = true;
            }
        }

        if (grassSettings.billboardGrassPrefab == null)
        {
            Debug.LogError("Billboard grass prefab is missing.");
        }
        else
        {
            MeshFilter meshFilter = grassSettings.billboardGrassPrefab.GetComponentInChildren<MeshFilter>();
            MeshRenderer meshRenderer = grassSettings.billboardGrassPrefab.GetComponentInChildren<MeshRenderer>();

            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                Debug.LogError("Billboard grass prefab missing MeshFilter or mesh.");
            }
            else
            {
                billboardGrassMesh = meshFilter.sharedMesh;
            }

            if (meshRenderer == null || meshRenderer.sharedMaterial == null)
            {
                Debug.LogError("Billboard grass prefab missing MeshRenderer or material.");
            }
            else
            {
                if (grassSettings.enableBillboardRenderFade)
                {
                    billboardGrassMaterial = CreateBillboardGrassRenderFadeMaterial(meshRenderer.sharedMaterial);
                }
                else
                {
                    DestroyOwnedBillboardGrassMaterial();
                    billboardGrassMaterial = meshRenderer.sharedMaterial;
                }

                billboardGrassMaterial.enableInstancing = true;
            }
        }
    }

    private Material CreateBillboardGrassRenderFadeMaterial(Material sourceMaterial)
    {
        DestroyOwnedBillboardGrassMaterial();

        Material fadeMaterial = new Material(sourceMaterial)
        {
            name = $"{sourceMaterial.name} (Billboard Render Fade)"
        };
        fadeMaterial.EnableKeyword(BillboardRenderFadeKeyword);
        fadeMaterial.SetFloat("_RenderFadeEnabled", 1f);
        fadeMaterial.SetFloat("_RenderFadeProgress", 1f);
        fadeMaterial.enableInstancing = true;

        ownsBillboardGrassMaterial = true;
        return fadeMaterial;
    }

    private void DestroyOwnedBillboardGrassMaterial()
    {
        if (!ownsBillboardGrassMaterial || billboardGrassMaterial == null)
            return;

        UnityEngine.Object.Destroy(billboardGrassMaterial);
        billboardGrassMaterial = null;
        ownsBillboardGrassMaterial = false;
    }

    private void ResolveFlowerRenderAssets()
    {
        if (!IsFlowerSystemEnabled())
            return;

        string petalColorPropertyName = string.IsNullOrEmpty(flowerSettings.flowerPetalColorPropertyName)
            ? "_FlowerPetalColor"
            : flowerSettings.flowerPetalColorPropertyName;

        flowerPetalColorPropertyId = Shader.PropertyToID(petalColorPropertyName);

        if (flowerSettings.flowerPrefab == null)
        {
            Debug.LogWarning("Flower prefab is missing. Flowers will not render until one is assigned.");
            return;
        }

        MeshFilter meshFilter = flowerSettings.flowerPrefab.GetComponentInChildren<MeshFilter>();
        MeshRenderer meshRenderer = flowerSettings.flowerPrefab.GetComponentInChildren<MeshRenderer>();

        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogError("Flower prefab missing MeshFilter or mesh.");
        }
        else
        {
            flowerMesh = meshFilter.sharedMesh;
        }

        if (meshRenderer == null || meshRenderer.sharedMaterial == null)
        {
            Debug.LogError("Flower prefab missing MeshRenderer or material.");
        }
        else
        {
            flowerMaterial = meshRenderer.sharedMaterial;
            flowerMaterial.enableInstancing = true;
        }
    }

    private void ResolveCloverRenderAssets()
    {
        string instanceDataPropertyName = cloverSettings == null ||
                                          string.IsNullOrEmpty(cloverSettings.cloverInstanceDataPropertyName)
            ? "_CloverInstanceData"
            : cloverSettings.cloverInstanceDataPropertyName;

        cloverInstanceDataPropertyId = Shader.PropertyToID(instanceDataPropertyName);

        if (!IsCloverSystemEnabled())
        {
            cloverRenderData = Array.Empty<CloverRenderData>();
            return;
        }

        List<GameObject> prefabs = new List<GameObject>();
        if (cloverSettings.cloverClumpPrefabs != null)
        {
            for (int i = 0; i < cloverSettings.cloverClumpPrefabs.Length; i++)
            {
                if (cloverSettings.cloverClumpPrefabs[i] != null)
                    prefabs.Add(cloverSettings.cloverClumpPrefabs[i]);
            }
        }

        if (prefabs.Count == 0 && cloverSettings.cloverClumpPrefab != null)
            prefabs.Add(cloverSettings.cloverClumpPrefab);

        if (prefabs.Count == 0)
        {
            Debug.LogWarning("Clover is enabled but no clover clump prefab is assigned.");
            cloverRenderData = Array.Empty<CloverRenderData>();
            return;
        }

        cloverRenderData = new CloverRenderData[prefabs.Count];
        for (int i = 0; i < prefabs.Count; i++)
        {
            cloverRenderData[i] = ResolveCloverRenderData(prefabs[i], $"clover clump prefab {i}");
        }
    }

    private CloverRenderData ResolveCloverRenderData(GameObject prefab, string label)
    {
        if (prefab == null)
            return new CloverRenderData(null, null);

        MeshFilter meshFilter = prefab.GetComponentInChildren<MeshFilter>();
        MeshRenderer meshRenderer = prefab.GetComponentInChildren<MeshRenderer>();

        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogError($"{label} must have a MeshFilter with a mesh.");
            return new CloverRenderData(null, null);
        }

        if (meshRenderer == null || meshRenderer.sharedMaterial == null)
        {
            Debug.LogError($"{label} must have a MeshRenderer with one shared material.");
            return new CloverRenderData(null, null);
        }

        meshRenderer.sharedMaterial.enableInstancing = true;
        return new CloverRenderData(meshFilter.sharedMesh, meshRenderer.sharedMaterial);
    }

    private void ResolveDandelionRenderAssets()
    {
        string instanceDataPropertyName = dandelionSettings == null ||
                                          string.IsNullOrEmpty(dandelionSettings.dandelionInstanceDataPropertyName)
            ? "_DandelionInstanceData"
            : dandelionSettings.dandelionInstanceDataPropertyName;

        dandelionInstanceDataPropertyId = Shader.PropertyToID(instanceDataPropertyName);

        if (!IsDandelionSystemEnabled())
            return;

        if (dandelionSettings.dandelionPrefab == null)
        {
            Debug.LogWarning("Dandelions are enabled but no dandelion prefab is assigned.");
            return;
        }

        CloverRenderData renderData = ResolveCloverRenderData(dandelionSettings.dandelionPrefab, "dandelion prefab");
        dandelionMesh = renderData.mesh;
        dandelionMaterial = renderData.material;
    }

    private void ResolveTreeRenderAssets()
    {
        WarnMissingTreePrefab(treeSettings.mapleTreePrefab, "Maple");
        WarnMissingTreePrefab(treeSettings.sugarMapleTreePrefab, "Sugar maple");
        WarnMissingTreePrefab(treeSettings.birchAspenTreePrefab, "Birch/aspen");
        WarnMissingTreePrefab(treeSettings.beechTreePrefab, "Beech");
        WarnMissingTreePrefab(treeSettings.spruceTreePrefab, "Spruce");
        WarnMissingTreePrefab(treeSettings.whitePineTreePrefab, "White pine");
        WarnMissingTreePrefab(treeSettings.oakTreePrefab, "Oak");
        WarnMissingGrasslandTreePrefab(treeSettings.grasslandMapleTreePrefab, "Grassland maple");
        WarnMissingGrasslandTreePrefab(treeSettings.grasslandBirchAspenTreePrefab, "Grassland birch/aspen");
        WarnMissingGrasslandTreePrefab(treeSettings.grasslandWhitePineTreePrefab, "Grassland white pine");
        WarnMissingGrasslandTreePrefab(treeSettings.grasslandOakTreePrefab, "Grassland oak");
        WarnMissingGrasslandTreePrefab(treeSettings.grasslandWillowTreePrefab, "Grassland willow");
        WarnMissingBushPrefab(treeSettings.blueberryBushPrefab, "Blueberry");
        WarnMissingBushPrefab(treeSettings.raspberryBushPrefab, "Raspberry");
        WarnMissingBushPrefab(treeSettings.strawberryBushPrefab, "Strawberry");
        WarnMissingBushPrefab(treeSettings.blackberryBushPrefab, "Blackberry");

        fallbackTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.treeBillboardPrefab,
            "fallback tree billboard");

        grasslandFallbackTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.grasslandTreeBillboardFallbackPrefab,
            "grassland fallback tree billboard");

        mapleTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.mapleTreeBillboardPrefab,
            "maple tree billboard");

        sugarMapleTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.sugarMapleTreeBillboardPrefab,
            "sugar maple tree billboard");

        birchAspenTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.birchAspenTreeBillboardPrefab,
            "birch/aspen tree billboard");

        beechTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.beechTreeBillboardPrefab,
            "beech tree billboard");

        spruceTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.spruceTreeBillboardPrefab,
            "spruce tree billboard");

        whitePineTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.whitePineTreeBillboardPrefab,
            "white pine tree billboard");

        oakTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.oakTreeBillboardPrefab,
            "oak tree billboard");

        grasslandMapleTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.grasslandMapleTreeBillboardPrefab,
            "grassland maple tree billboard");

        grasslandBirchAspenTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.grasslandBirchAspenTreeBillboardPrefab,
            "grassland birch/aspen tree billboard");

        grasslandWhitePineTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.grasslandWhitePineTreeBillboardPrefab,
            "grassland white pine tree billboard");

        grasslandOakTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.grasslandOakTreeBillboardPrefab,
            "grassland oak tree billboard");

        grasslandWillowTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.grasslandWillowTreeBillboardPrefab,
            "grassland willow tree billboard");

        if (grasslandFallbackTreeBillboard.mesh == null)
            grasslandFallbackTreeBillboard = fallbackTreeBillboard;

        if (mapleTreeBillboard.mesh == null)
            mapleTreeBillboard = fallbackTreeBillboard;

        if (sugarMapleTreeBillboard.mesh == null)
            sugarMapleTreeBillboard = fallbackTreeBillboard;

        if (birchAspenTreeBillboard.mesh == null)
            birchAspenTreeBillboard = fallbackTreeBillboard;

        if (beechTreeBillboard.mesh == null)
            beechTreeBillboard = fallbackTreeBillboard;

        if (spruceTreeBillboard.mesh == null)
            spruceTreeBillboard = fallbackTreeBillboard;

        if (whitePineTreeBillboard.mesh == null)
            whitePineTreeBillboard = fallbackTreeBillboard;

        if (oakTreeBillboard.mesh == null)
            oakTreeBillboard = fallbackTreeBillboard;

        if (grasslandMapleTreeBillboard.mesh == null)
            grasslandMapleTreeBillboard = grasslandFallbackTreeBillboard;

        if (grasslandBirchAspenTreeBillboard.mesh == null)
            grasslandBirchAspenTreeBillboard = grasslandFallbackTreeBillboard;

        if (grasslandWhitePineTreeBillboard.mesh == null)
            grasslandWhitePineTreeBillboard = grasslandFallbackTreeBillboard;

        if (grasslandOakTreeBillboard.mesh == null)
            grasslandOakTreeBillboard = grasslandFallbackTreeBillboard;

        if (grasslandWillowTreeBillboard.mesh == null)
            grasslandWillowTreeBillboard = grasslandFallbackTreeBillboard;
    }

    private void WarnMissingTreePrefab(GameObject prefab, string label)
    {
        if (prefab == null && treeSettings.treeLOD0GameObjectPrefab == null)
        {
            Debug.LogWarning($"{label} tree prefab is missing and no fallback tree prefab is assigned.");
        }
    }

    private void WarnMissingGrasslandTreePrefab(GameObject prefab, string label)
    {
        if (prefab == null &&
            treeSettings.grasslandTreeFallbackPrefab == null &&
            treeSettings.treeLOD0GameObjectPrefab == null)
        {
            Debug.LogWarning($"{label} tree prefab is missing and no grassland or general fallback tree prefab is assigned.");
        }
    }

    private void WarnMissingBushPrefab(GameObject prefab, string label)
    {
        if (prefab == null && treeSettings.fallbackBushPrefab == null)
        {
            Debug.LogWarning($"{label} bush prefab is missing and no fallback bush prefab is assigned.");
        }
    }

    private TreeBillboardRenderData ResolveTreeBillboardRenderData(GameObject prefab, string label)
    {
        if (prefab == null)
            return new TreeBillboardRenderData(null, null);

        MeshFilter meshFilter = prefab.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = prefab.GetComponent<MeshRenderer>();

        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogError($"{label} prefab must have a MeshFilter with a mesh on the root.");
            return new TreeBillboardRenderData(null, null);
        }

        if (meshRenderer == null || meshRenderer.sharedMaterial == null)
        {
            Debug.LogError($"{label} prefab must have a MeshRenderer with one shared material on the root.");
            return new TreeBillboardRenderData(null, null);
        }

        meshRenderer.sharedMaterial.enableInstancing = true;
        return new TreeBillboardRenderData(meshFilter.sharedMesh, meshRenderer.sharedMaterial);
    }
}
