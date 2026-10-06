using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

public class ChunkManager : IChunkLookup
{
    public TreeRegistry Trees { get; }
    public TreeGameplayManager TreeGameplay { get; }
    private readonly TerrainHorizonShadowSystem terrainHorizonShadows;
    private const int FarTerrainLOD = 5;
    // The first leaf keeps the current 4x4 macro footprint. Larger leaves are
    // selected only when their complete footprint is beyond the preceding band.
    private const int MaxRuntimeCreationsPerFrame = 4;
    private const double RuntimeCreationBudgetMs = 0.35;
    private readonly Queue<ChunkCoord> pendingRuntimeCreations = new();
    private static readonly ProfilerMarker RuntimeCreationQueueMarker = new ProfilerMarker("FS.Streaming.RuntimeCreationQueue");
    private static readonly ProfilerMarker CreateRuntimeMarker = new ProfilerMarker("FS.Streaming.CreateOneChunkRuntime");
    private static readonly ProfilerMarker UpdateActiveChunksMarker = new ProfilerMarker("FS.Streaming.ChunkManager.UpdateActiveChunks");
    private static readonly ProfilerMarker ViewerSubChunkChangedMarker = new ProfilerMarker("FS.Streaming.Foliage.HandleViewerSubChunkChanged");
    private static readonly ProfilerMarker UpdateGrassStreamingMarker = new ProfilerMarker("FS.Streaming.Grass.UpdateStreaming");
    private static readonly ProfilerMarker UpdateDistantTreesMarker = new ProfilerMarker("FS.Streaming.DistantTrees.Update");
    private static readonly ProfilerMarker RebuildActiveChunkSetMarker = new ProfilerMarker("FS.Streaming.RebuildActiveChunkSet");
    private static readonly ProfilerMarker UpdateVisibleChunkContentMarker = new ProfilerMarker("FS.Streaming.UpdateVisibleChunkContent");
    private static readonly ProfilerMarker UpdateVisibleFarTerrainTilesMarker = new ProfilerMarker("FS.Streaming.UpdateVisibleFarTerrainTiles");
    private static readonly ProfilerMarker CalculateFrustumPlanesMarker = new ProfilerMarker("FS.Streaming.CalculateFrustumPlanes");
    private static readonly ProfilerMarker VisibleNormalChunkLoopMarker = new ProfilerMarker("FS.Streaming.VisibleNormalChunkLoop");
    private static readonly ProfilerMarker VisibleChunkFarTerrainPathMarker = new ProfilerMarker("FS.Streaming.VisibleChunk.FarTerrainPath");
    private static readonly ProfilerMarker VisibleChunkNearTerrainPathMarker = new ProfilerMarker("FS.Streaming.VisibleChunk.NearTerrainPath");
    private static readonly ProfilerMarker VisibleChunkColliderPathMarker = new ProfilerMarker("FS.Streaming.VisibleChunk.ColliderPath");
    private static readonly ProfilerMarker VisibleChunkRenderVisibilityMarker = new ProfilerMarker("FS.Streaming.VisibleChunk.RenderVisibility");
    private static readonly ProfilerMarker VisibleFarTileLoopMarker = new ProfilerMarker("FS.Streaming.VisibleFarTileLoop");
    private static readonly ProfilerMarker ProcessVisibleChunkContentQueueMarker = new ProfilerMarker("FS.Streaming.ProcessVisibleChunkContentQueue");
    private static readonly ProfilerMarker RefreshUrgentVisibleChunksMarker = new ProfilerMarker("FS.Streaming.RefreshUrgentVisibleChunks");
    private static readonly ProfilerMarker RefreshRenderVisibilityMarker = new ProfilerMarker("FS.Streaming.RefreshRenderVisibility");
    private static readonly ProfilerMarker ProcessFarTerrainTileContentQueueMarker = new ProfilerMarker("FS.Streaming.ProcessFarTerrainTileContentQueue");
    private static readonly ProfilerMarker EnsureFarTerrainRequestedMarker = new ProfilerMarker("FS.Streaming.EnsureFarTerrainRequested");
    private static readonly ProfilerMarker EnsureFarTerrainTileRequestedMarker = new ProfilerMarker("FS.Streaming.EnsureFarTerrainTileRequested");
    private static readonly ProfilerMarker EnsureTerrainDataRequestedMarker = new ProfilerMarker("FS.Streaming.EnsureTerrainDataRequested");
    private static readonly ProfilerMarker EnsureLodMeshRequestedMarker = new ProfilerMarker("FS.Streaming.EnsureLODMeshRequested");
    private static readonly ProfilerMarker EnsureColliderRequestedMarker = new ProfilerMarker("FS.Streaming.EnsureColliderRequested");
    private static readonly ProfilerMarker TryApplyFarTerrainMarker = new ProfilerMarker("FS.Streaming.TryApplyFarTerrain");
    private static readonly ProfilerMarker TryApplyFarTerrainTileMarker = new ProfilerMarker("FS.Streaming.TryApplyFarTerrainTile");
    private static readonly ProfilerMarker TryApplyLodMeshMarker = new ProfilerMarker("FS.Streaming.TryApplyLODMesh");
    private static readonly ProfilerMarker TryApplyColliderMarker = new ProfilerMarker("FS.Streaming.TryApplyCollider");
    private static readonly ProfilerMarker RemoveColliderMarker = new ProfilerMarker("FS.Streaming.RemoveCollider");

    private readonly int subChunksPerChunk = 10;

    private readonly int viewDistance;
    private readonly int colliderDistance;
    private readonly bool enableFarTerrain;
    private readonly int farTerrainMacroTileSize;
    private readonly int farTerrainHeightGridResolution;
    private readonly int farTerrainControlMapResolution;
    private readonly float farTerrainSkirtDepth;
    private readonly int chunkSize;
    private readonly int seed;
    private readonly Transform viewer;
    private readonly Camera viewerCamera;
    private readonly float sampleScale;
    private readonly float worldScale;
    private readonly int octaves;
    private readonly float persistence;
    private readonly float lacunarity;
    private readonly float meshHeightMultiplier;
    private readonly int urgentVisibleChunkRingRadius;
    private readonly int maxVisibleChunkContentUpdatesPerFrame;
    private readonly int maxRenderVisibilityChecksPerFrame;
    private readonly float foliageFrustumPaddingWorldUnits;
    private readonly float visibleChunkContentBudgetMsPerFrame;
    private readonly int maxFarTerrainTileContentUpdatesPerFrame;
    private readonly float farTerrainTileContentBudgetMsPerFrame;

    private readonly Dictionary<ChunkCoord, ChunkRecord> chunkRecords = new();
    private readonly Dictionary<ChunkCoord, ChunkRuntime> loadedChunks = new();
    private readonly Dictionary<FarTerrainPatchKey, FarTerrainTileRecord> farTerrainTileRecords = new();
    private readonly Dictionary<FarTerrainPatchKey, FarTerrainTileRuntime> loadedFarTerrainTiles = new();

    private HashSet<ChunkCoord> activeLastUpdate;
    private HashSet<ChunkCoord> activeThisUpdate;
    private HashSet<FarTerrainPatchKey> activeFarTilesLastUpdate;
    private HashSet<FarTerrainPatchKey> activeFarTilesThisUpdate;
    private readonly List<ChunkCoord> orderedActiveCoords;
    private readonly List<ChunkCoord> frustumVisibleCoords;
    private readonly List<FarTerrainPatchKey> orderedActiveFarTileCoords;
    private readonly Queue<ChunkCoord> pendingVisibleChunkContentWork;
    private readonly HashSet<ChunkCoord> queuedVisibleChunkContentCoords;
    private readonly List<ChunkCoord> deferredVisibleChunkContentRetries;
    private readonly Queue<FarTerrainPatchKey> pendingFarTerrainTileContentWork;
    private readonly HashSet<FarTerrainPatchKey> queuedFarTerrainTileContentCoords;
    private readonly List<FarTerrainPatchKey> deferredFarTerrainTileContentRetries;
    private readonly HashSet<ChunkCoord> frustumVisibleCoordSet;
    private readonly Plane[] frustumPlanes = new Plane[6];
    private int renderVisibilityCursor;

    private ChunkCoord lastUpdateViewerCoord = new ChunkCoord(int.MinValue, int.MinValue);
    private SubChunkCoord lastViewerGlobalSubChunk = new SubChunkCoord(int.MinValue, int.MinValue);

    private readonly TerrainRuntimePool runtimePool;
    private readonly TerrainCoveragePolicy coveragePolicy;
    private readonly TerrainHandoffCoordinator handoffs;
    private readonly TerrainResultPublisher resultPublisher;
    private readonly TerrainRequestManager terrainRequestManager;
    private readonly FoliageManager foliageManager;
    private readonly AmbientLifeManager butterflyManager;
    private readonly AmbientLifeManager beeManager;
    private readonly DistantTreeManager distantTrees;
    private readonly LeafClusterSystem leafClusters;
    private readonly LeafClusterSystem ferns;
    private readonly WorldFeatureGenerationSettings worldFeatureGenerationSettings;

    public ChunkManager(WorldConfiguration configuration)
    {
        if (configuration == null) throw new System.ArgumentNullException(nameof(configuration));
        var content = configuration.Content;
        var coverage = configuration.Coverage;
        var foliage = configuration.Foliage;
        var generation = configuration.Generation;
        var rendering = configuration.Rendering;
        var scene = configuration.Scene;
        var workers = configuration.Workers;
        runtimePool = new TerrainRuntimePool(generation, rendering, scene.ChunkParent);
        coveragePolicy = new TerrainCoveragePolicy(coverage);
        handoffs = new TerrainHandoffCoordinator(coveragePolicy, loadedChunks, loadedFarTerrainTiles, ReleaseChunkRuntime, ReleaseFarTerrainTileRuntime);
        this.viewDistance = coverage.ViewDistance;
        this.colliderDistance = coverage.ColliderDistance;
        this.enableFarTerrain = coverage.EnableFarTerrain;
        this.farTerrainMacroTileSize = Mathf.Max(1, coverage.FarTerrainMacroTileSize);
        this.farTerrainHeightGridResolution = Mathf.Max(2, coverage.FarTerrainHeightGridResolution);
        this.farTerrainControlMapResolution = Mathf.Max(2, coverage.FarTerrainControlMapResolution);
        this.farTerrainSkirtDepth = Mathf.Max(0f, coverage.FarTerrainSkirtDepth);
        this.chunkSize = generation.ChunkSize;
        this.seed = generation.Seed;
        this.viewer = scene.Viewer;
        this.viewerCamera = scene.ViewerCamera;
        this.sampleScale = generation.SampleScale;
        this.worldScale = generation.WorldScale;
        this.octaves = generation.Octaves;
        this.persistence = generation.Persistence;
        this.lacunarity = generation.Lacunarity;
        this.meshHeightMultiplier = generation.MeshHeightMultiplier;
        leafClusters = new LeafClusterSystem(foliage.Leaves, generation.Seed, generation.ChunkSize, generation.WorldScale, generation.MeshHeightMultiplier, foliage.Grass);
        ferns = new LeafClusterSystem(foliage.Ferns ?? new FernSettings(), generation.Seed, generation.ChunkSize, generation.WorldScale, generation.MeshHeightMultiplier, foliage.Grass);
        terrainHorizonShadows = new TerrainHorizonShadowSystem(generation.ChunkSize, generation.Seed, generation.SampleScale, generation.WorldScale,
            generation.MeshHeightMultiplier, generation.Water, generation.MountainHorizontalScale, generation.Erosion, rendering.HorizonShadows);
        this.urgentVisibleChunkRingRadius = Mathf.Max(0, content.UrgentVisibleChunkRingRadius);
        this.maxVisibleChunkContentUpdatesPerFrame = Mathf.Max(1, content.MaxVisibleChunkUpdates);
        this.maxRenderVisibilityChecksPerFrame = Mathf.Max(1, content.MaxRenderVisibilityChecks);
        this.foliageFrustumPaddingWorldUnits = Mathf.Max(0f, content.FoliageFrustumPaddingChunks) * generation.ChunkSize * generation.WorldScale;
        this.visibleChunkContentBudgetMsPerFrame = Mathf.Max(0f, content.VisibleChunkMs);
        this.maxFarTerrainTileContentUpdatesPerFrame = Mathf.Max(1, content.MaxFarTileUpdates);
        this.farTerrainTileContentBudgetMsPerFrame = Mathf.Max(0f, content.FarTileMs);

        int maxChunks = ComputeMaxActiveChunkCount(coverage.ViewDistance);

        activeLastUpdate = new HashSet<ChunkCoord>(maxChunks);
        activeThisUpdate = new HashSet<ChunkCoord>(maxChunks);
        activeFarTilesLastUpdate = new HashSet<FarTerrainPatchKey>(maxChunks);
        activeFarTilesThisUpdate = new HashSet<FarTerrainPatchKey>(maxChunks);
        orderedActiveCoords = new List<ChunkCoord>(maxChunks);
        frustumVisibleCoords = new List<ChunkCoord>(maxChunks);
        orderedActiveFarTileCoords = new List<FarTerrainPatchKey>(maxChunks);
        pendingVisibleChunkContentWork = new Queue<ChunkCoord>(maxChunks);
        queuedVisibleChunkContentCoords = new HashSet<ChunkCoord>(maxChunks);
        deferredVisibleChunkContentRetries = new List<ChunkCoord>(maxChunks);
        pendingFarTerrainTileContentWork = new Queue<FarTerrainPatchKey>(maxChunks);
        queuedFarTerrainTileContentCoords = new HashSet<FarTerrainPatchKey>(maxChunks);
        deferredFarTerrainTileContentRetries = new List<FarTerrainPatchKey>(maxChunks);
        frustumVisibleCoordSet = new HashSet<ChunkCoord>(maxChunks);

        worldFeatureGenerationSettings = TreeGenerationSnapshot.Create(foliage.Trees);
        terrainRequestManager = new TerrainRequestManager(
            workers.TerrainData,
            workers.FarTerrain,
            workers.LodMesh,
            workers.Collider,
            generation.Water, generation.MountainHorizontalScale, generation.MountainSnowRenderCoverageGamma, generation.Erosion);
        resultPublisher = new TerrainResultPublisher(terrainRequestManager, configuration.Publication,
            GetChunkRecord, FindFarTerrainRecord, result => IsFarTerrainResultStillWanted(result, GetViewerChunkCoord()),
            QueueVisibleChunkContentWork, QueueFarTerrainTileContentWork);
        Trees = new TreeRegistry(generation.Seed, generation.ChunkSize * generation.WorldScale);
        if (foliage.Trees != null) TreeGameplay = new TreeGameplayManager(Trees, foliage.Trees, generation.ChunkSize * generation.WorldScale);
        foliageManager = new FoliageManager(configuration.Generation, configuration.Foliage, Trees);
        butterflyManager = new AmbientLifeManager(foliage.Butterflies, foliage.Flowers, generation.Seed, generation.ChunkSize, generation.WorldScale,
            generation.MeshHeightMultiplier, generation.Water.SurfaceY);
        if (foliage.Bees != null)
            beeManager = new AmbientLifeManager(foliage.Bees, foliage.Flowers, generation.Seed, generation.ChunkSize, generation.WorldScale,
                generation.MeshHeightMultiplier, generation.Water.SurfaceY);
        if (foliage.Trees != null)
            distantTrees = new DistantTreeManager(foliage.Trees, generation.Seed, generation.ChunkSize, generation.SampleScale, generation.Octaves, generation.Persistence,
                generation.Lacunarity, generation.WorldScale, generation.MeshHeightMultiplier, generation.Water.WaterLevel, generation.MountainHorizontalScale,
                worldFeatureGenerationSettings, generation.Erosion, Trees);
    }

    public void Dispose()
    {
        leafClusters.Dispose();
        ferns.Dispose();
        terrainHorizonShadows?.Dispose();
        butterflyManager?.Dispose();
        beeManager?.Dispose();
        distantTrees?.Dispose();
        TreeGameplay?.Dispose();
        foliageManager?.Dispose();
        foreach (var runtime in loadedChunks.Values)
            runtime.DestroyRuntime();
        loadedChunks.Clear();
        foreach (var runtime in loadedFarTerrainTiles.Values)
            runtime.DestroyRuntime();
        loadedFarTerrainTiles.Clear();
        runtimePool.Dispose();
        terrainRequestManager?.WaitForActiveRequestsToFinish();
        terrainRequestManager?.Dispose();
        foreach (var record in chunkRecords.Values)
            record.Dispose();
        Trees.Clear();
    }

    public bool TryGetDistantTreeSurface(ChunkCoord coord, out ChunkRuntime runtime,
        out float[,] heights, out Vector2 origin, out float size)
    {
        loadedChunks.TryGetValue(coord, out runtime);
        heights = null;
        origin = new Vector2(coord.x * chunkSize * worldScale, coord.z * chunkSize * worldScale);
        size = chunkSize * worldScale;
        if (runtime != null && runtime.IsVisible)
        {
            if (runtime.CurrentLOD < 0) return false;
            if (runtime.CurrentLOD == FarTerrainLOD)
                heights = runtime.ChunkRecord.FarTreeHeightGrid;
            return true;
        }
        if (coveragePolicy.TryGetFarTerrainPatch(GetViewerChunkCoord(), coord, out FarTerrainPatchKey patch) &&
            loadedFarTerrainTiles.TryGetValue(patch, out var far) && far.IsVisible &&
            farTerrainTileRecords.TryGetValue(patch, out var record) && record.HasTerrain)
        {
            heights = record.FarTreeHeightGrid;
            size *= patch.SizeInChunks;
            origin = new Vector2(patch.Origin.x * patch.SizeInChunks * chunkSize * worldScale,
                patch.Origin.z * patch.SizeInChunks * chunkSize * worldScale);
            return heights != null;
        }
        return false;
    }

    public ChunkCoord GetViewerChunkCoord()
    {
        return GetChunkCoordFromWorldPosition(viewer.position);
    }

    public WorldDebugInfo GetDebugInfoAtWorldPosition(Vector3 worldPosition)
    {
        ChunkCoord coord = GetChunkCoordFromWorldPosition(worldPosition);
        chunkRecords.TryGetValue(coord, out ChunkRecord record);

        bool hasChunkRecord = record != null;
        bool hasTerrainData = hasChunkRecord && record.HasTerrainData;

        BiomeType biome = default;
        SurfaceType surfaceType = default;
        float worldHeight = 0f;
        float slope = 0f;
        float moisture = 0f;
        float temperature = 0f;
        float riverMask = 0f;

        float forestMembership = -1f;
        if (hasTerrainData && TryGetPaddedSampleIndices(coord, worldPosition, record, out int sampleX, out int sampleZ))
        {
            biome = record.BiomeMap[sampleX, sampleZ];
            byte membership = record.WorldFeaturePlan?.ForestMembershipMap[sampleX,sampleZ] ?? 0;
            if (membership != 0) forestMembership = BiomeTransitionPolicy.ForestWeight(membership, biome);
            surfaceType = record.SurfaceTypeMap[sampleX, sampleZ];

            worldHeight = record.HeightMap[sampleX, sampleZ] * meshHeightMultiplier * worldScale;
            slope = record.SlopeMap[sampleX, sampleZ];
            moisture = record.MoistureMap[sampleX, sampleZ];
            temperature = record.TemperatureMap[sampleX, sampleZ];
            riverMask = record.RiverMaskMap[sampleX, sampleZ];
        }

        return new WorldDebugInfo(
            worldPosition,
            coord,
            hasChunkRecord,
            hasTerrainData,
            biome,
            surfaceType,
            worldHeight,
            slope,
            moisture,
            temperature,
            riverMask, forestMembership);
    }

    public WorldRenderStatsDebugInfo GetVisibleRenderStatsDebugInfo()
    {
        WorldRenderStatsDebugInfo stats = new WorldRenderStatsDebugInfo();
        ChunkCoord viewerCoord = GetViewerChunkCoord();

        for (int i = 0; i < frustumVisibleCoords.Count; i++)
        {
            ChunkCoord coord = frustumVisibleCoords[i];
            if (!loadedChunks.TryGetValue(coord, out ChunkRuntime runtime))
                continue;

            runtime.AccumulateRenderStats(ref stats);
        }

        for (int i = 0; i < orderedActiveFarTileCoords.Count; i++)
        {
            FarTerrainPatchKey patch = orderedActiveFarTileCoords[i];
            if (!loadedFarTerrainTiles.TryGetValue(patch, out FarTerrainTileRuntime runtime))
                continue;

            runtime.AccumulateRenderStats(ref stats);
        }

        foliageManager.AccumulateVisibleFoliageRenderStats(
            this,
            viewerCoord,
            frustumVisibleCoords,
            ref stats);
        if (distantTrees != null)
        {
            stats.TreeBillboards = distantTrees.RenderStats;
            stats.TreeMeshes = distantTrees.MeshRenderStats;
        }

        return stats;
    }

    private ChunkCoord GetChunkCoordFromWorldPosition(Vector3 worldPosition)
    {
        float safeWorldScale = Mathf.Max(0.0001f, worldScale);
        float chunkWorldSize = chunkSize * safeWorldScale;

        int cx = Mathf.FloorToInt(worldPosition.x / chunkWorldSize);
        int cz = Mathf.FloorToInt(worldPosition.z / chunkWorldSize);

        return new ChunkCoord(cx, cz);
    }

    private bool TryGetPaddedSampleIndices(
        ChunkCoord coord,
        Vector3 worldPosition,
        ChunkRecord record,
        out int sampleX,
        out int sampleZ)
    {
        sampleX = 0;
        sampleZ = 0;

        if (record == null || !record.HasTerrainData)
            return false;

        int width = record.HeightMap.GetLength(0);
        int height = record.HeightMap.GetLength(1);

        if (width == 0 || height == 0)
            return false;

        float safeWorldScale = Mathf.Max(0.0001f, worldScale);
        float chunkWorldSize = chunkSize * safeWorldScale;
        float chunkMinWorldX = coord.x * chunkWorldSize;
        float chunkMinWorldZ = coord.z * chunkWorldSize;

        float localTerrainX = (worldPosition.x - chunkMinWorldX) / safeWorldScale;
        float localTerrainZ = (worldPosition.z - chunkMinWorldZ) / safeWorldScale;

        int localVertexX = Mathf.Clamp(Mathf.RoundToInt(localTerrainX), 0, chunkSize);
        int localVertexZ = Mathf.Clamp(Mathf.RoundToInt(localTerrainZ), 0, chunkSize);

        sampleX = Mathf.Clamp(localVertexX + 1, 0, width - 1);
        sampleZ = Mathf.Clamp(localVertexZ + 1, 0, height - 1);
        return true;
    }

    public SubChunkCoord GetViewerGlobalSubChunkCoord()
    {
        ChunkCoord viewerChunk = GetViewerChunkCoord();

        float chunkWorldSize = chunkSize * worldScale;
        float subChunkWorldSize = chunkWorldSize / subChunksPerChunk;

        float chunkMinWorldX = viewerChunk.x * chunkWorldSize;
        float chunkMinWorldZ = viewerChunk.z * chunkWorldSize;

        float localWorldX = viewer.position.x - chunkMinWorldX;
        float localWorldZ = viewer.position.z - chunkMinWorldZ;

        int localSubChunkX = Mathf.Clamp(
            Mathf.FloorToInt(localWorldX / subChunkWorldSize),
            0,
            subChunksPerChunk - 1);

        int localSubChunkZ = Mathf.Clamp(
            Mathf.FloorToInt(localWorldZ / subChunkWorldSize),
            0,
            subChunksPerChunk - 1);

        int globalSubChunkX = viewerChunk.x * subChunksPerChunk + localSubChunkX;
        int globalSubChunkZ = viewerChunk.z * subChunksPerChunk + localSubChunkZ;

        return new SubChunkCoord(globalSubChunkX, globalSubChunkZ);
    }

    public int GetSubChunksPerChunk()
    {
        return subChunksPerChunk;
    }

    public float GetChunkWorldSize()
    {
        return chunkSize * worldScale;
    }

    public float GetSubChunkWorldSize()
    {
        return (chunkSize * worldScale) / subChunksPerChunk;
    }

    public void UpdateActiveChunks()
    {
        using (UpdateActiveChunksMarker.Auto())
        {
        resultPublisher.Update();

        ChunkCoord viewerCoord = GetViewerChunkCoord();
        SubChunkCoord viewerGlobalSubChunk = GetViewerGlobalSubChunkCoord();

        bool viewerChunkChanged = viewerCoord != lastUpdateViewerCoord;
        bool viewerSubChunkChanged = viewerGlobalSubChunk != lastViewerGlobalSubChunk;

        if (viewerChunkChanged)
        {
            RebuildActiveChunkSet(viewerCoord);
            lastUpdateViewerCoord = viewerCoord;
        }

        ProcessRuntimeCreationQueue();
        UpdateVisibleChunkContent(viewerCoord);

        // Optional horizon work never delays uploading a terrain mesh. Dispatch only after
        // player-proximate generation/collision has settled; one shadow worker at a time.
        terrainHorizonShadows.Update(viewer.position,
            terrainRequestManager.ActiveTerrainDataJobCount == 0 &&
            terrainRequestManager.ActiveMeshJobCount == 0 &&
            terrainRequestManager.ActiveColliderJobCount == 0);

        long foliageStart = TerrainGenerationProfiler.GetTimestamp();

        if (viewerChunkChanged || viewerSubChunkChanged)
        {
            using (ViewerSubChunkChangedMarker.Auto())
            {
            foliageManager.HandleViewerSubChunkChanged(
                this,
                viewerCoord,
                viewerGlobalSubChunk,
                orderedActiveCoords,
                viewerChunkChanged);
            }
        }

        foliageManager.DrawVisibleFoliageEveryFrame(
            this,
            viewerCoord,
            viewerGlobalSubChunk,
            frustumVisibleCoords,
            viewerCamera,
            viewer.position);

        using (UpdateGrassStreamingMarker.Auto())
        {
            foliageManager.UpdateGrassStreaming(this, orderedActiveCoords, viewer.position, viewerCamera);
        }

        using (UpdateDistantTreesMarker.Auto())
        {
            distantTrees?.Update(this, viewer.position, viewerCamera, viewDistance);
        }
        TreeGameplay?.Update(viewer.position);
        butterflyManager?.Update(this, viewerCoord, viewerCamera, Time.deltaTime);
        beeManager?.Update(this, viewerCoord, viewerCamera, Time.deltaTime);
        leafClusters.Update(this, orderedActiveCoords, viewer.position, viewerCamera);
        ferns.Update(this, orderedActiveCoords, viewer.position, viewerCamera);
        TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.FoliageTotal, foliageStart);

        lastViewerGlobalSubChunk = viewerGlobalSubChunk;
        }
    }

    private void RebuildActiveChunkSet(ChunkCoord viewerCoord)
    {
        using (RebuildActiveChunkSetMarker.Auto())
        {
        activeThisUpdate.Clear();
        activeFarTilesThisUpdate.Clear();
        orderedActiveCoords.Clear();
        orderedActiveFarTileCoords.Clear();
        pendingVisibleChunkContentWork.Clear();
        queuedVisibleChunkContentCoords.Clear();
        pendingFarTerrainTileContentWork.Clear();
        queuedFarTerrainTileContentCoords.Clear();

        int sqrViewRadius = viewDistance * viewDistance;

        for (int x = -viewDistance; x <= viewDistance; x++)
        {
            for (int z = -viewDistance; z <= viewDistance; z++)
            {
                int sqrDistance = x * x + z * z;
                if (sqrDistance > sqrViewRadius)
                    continue;

                ChunkCoord targetCoord = new ChunkCoord(viewerCoord.x + x, viewerCoord.z + z);

                if (coveragePolicy.TryGetFarTerrainPatch(viewerCoord, targetCoord, out FarTerrainPatchKey farPatch))
                {
                    if (activeFarTilesThisUpdate.Add(farPatch))
                        orderedActiveFarTileCoords.Add(farPatch);

                    continue;
                }

                activeThisUpdate.Add(targetCoord);
                orderedActiveCoords.Add(targetCoord);
            }
        }

        SortOrderedActiveCoords(viewerCoord);
        SortOrderedActiveFarTileCoords(viewerCoord);

        // Rebuild from current priorities so a direction change cannot leave stale
        // requests ahead of the chunks nearest the viewer.
        pendingRuntimeCreations.Clear();
        foreach (ChunkCoord targetCoord in orderedActiveCoords)
        {
            ChunkRecord record = GetOrCreateChunkRecord(targetCoord);
            if (loadedChunks.TryGetValue(targetCoord, out ChunkRuntime runtime))
            {
                if (!runtime.IsVisible)
                    runtime.SetVisible(true);
            }
            else
                pendingRuntimeCreations.Enqueue(targetCoord);

            if (IsUrgentVisibleChunk(viewerCoord, targetCoord))
                EnsureTerrainVisualRequested(record, viewerCoord, targetCoord);
            else
                QueueVisibleChunkContentWork(targetCoord);
        }

        foreach (FarTerrainPatchKey farPatch in orderedActiveFarTileCoords)
        {
            GetOrCreateFarTerrainTileRecord(farPatch);
            QueueFarTerrainTileContentWork(farPatch);
        }

        foreach (ChunkCoord coord in activeLastUpdate)
        {
            if (!activeThisUpdate.Contains(coord))
            {
                if (loadedChunks.TryGetValue(coord, out ChunkRuntime runtime))
                {
                    // Retain outgoing terrain until its macro replacement is attached.
                    runtime.SetRenderVisible(true);
                    runtime.SetFoliageRenderVisible(false);
                    runtime.SetFoliageShadowCasterVisible(false);
                    runtime.RemoveCollider();
                    RemoveFrustumVisibleCoord(coord);
                }
            }
        }

        // Outgoing macro tiles remain until CompleteTerrainHandoffs observes
        // attached replacement chunks, including their water meshes.

        var temp = activeLastUpdate;
        activeLastUpdate = activeThisUpdate;
        activeThisUpdate = temp;

        var farTemp = activeFarTilesLastUpdate;
        activeFarTilesLastUpdate = activeFarTilesThisUpdate;
        activeFarTilesThisUpdate = farTemp;
        // Reconcile from loaded runtimes on each active-set change. This includes
        // older handoffs still waiting and excludes chunks that have re-entered.
        handoffs.Reconcile(activeLastUpdate);
        renderVisibilityCursor = 0;
        }
    }

    private void ProcessRuntimeCreationQueue()
    {
        using (RuntimeCreationQueueMarker.Auto())
        {
            long start = TerrainGenerationProfiler.GetTimestamp();
            int created = 0;
            while (pendingRuntimeCreations.Count > 0 && created < MaxRuntimeCreationsPerFrame)
            {
                ChunkCoord coord = pendingRuntimeCreations.Dequeue();
                if (!activeLastUpdate.Contains(coord) || loadedChunks.ContainsKey(coord))
                    continue;

                using (CreateRuntimeMarker.Auto())
                {
                    ChunkRuntime runtime = GetOrCreateChunkRuntime(GetOrCreateChunkRecord(coord));
                    runtime.SetVisible(true);
                }
                // The content queue may already have visited this coordinate while
                // its runtime was pending. Creation must wake it again.
                QueueVisibleChunkContentWork(coord);
                created++;
                // One constructor is indivisible; stop before starting another.
                if (TerrainGenerationProfiler.GetElapsedMilliseconds(start) >= RuntimeCreationBudgetMs)
                    break;
            }
        }
    }

    private void UpdateVisibleChunkContent(ChunkCoord viewerCoord)
    {
        using (UpdateVisibleChunkContentMarker.Auto())
        {
        int sqrColliderRadius = colliderDistance * colliderDistance;

        if (viewerCamera != null)
        {
            using (CalculateFrustumPlanesMarker.Auto())
            {
                GeometryUtility.CalculateFrustumPlanes(viewerCamera, frustumPlanes);
            }
        }

        long budgetStart = TerrainGenerationProfiler.GetTimestamp();
        RefreshUrgentVisibleChunks(viewerCoord, sqrColliderRadius, budgetStart);
        ProcessVisibleChunkContentQueue(viewerCoord, sqrColliderRadius, budgetStart);
        RefreshRenderVisibility(viewerCoord, budgetStart);

        UpdateVisibleFarTerrainTiles(viewerCoord);
        CompleteTerrainHandoffs();
        }
    }

    private void CompleteTerrainHandoffs() => handoffs.Update(GetViewerChunkCoord(), activeLastUpdate, activeFarTilesLastUpdate);

    private void RefreshUrgentVisibleChunks(
        ChunkCoord viewerCoord,
        int sqrColliderRadius,
        long budgetStart)
    {
        using (RefreshUrgentVisibleChunksMarker.Auto())
        {
            for (int i = 0; i < orderedActiveCoords.Count; i++)
            {
                ChunkCoord coord = orderedActiveCoords[i];
                if (!IsUrgentVisibleChunk(viewerCoord, coord))
                    break;

                ProcessVisibleChunkContent(coord, viewerCoord, sqrColliderRadius);
                if (!HasVisibleChunkContentBudgetRemaining(budgetStart))
                    break;
            }
        }
    }

    private void ProcessVisibleChunkContentQueue(
        ChunkCoord viewerCoord,
        int sqrColliderRadius,
        long budgetStart)
    {
        using (ProcessVisibleChunkContentQueueMarker.Auto())
        {
            deferredVisibleChunkContentRetries.Clear();
            int processedCount = 0;
            while (pendingVisibleChunkContentWork.Count > 0 &&
                   processedCount < maxVisibleChunkContentUpdatesPerFrame &&
                   HasVisibleChunkContentBudgetRemaining(budgetStart))
            {
                ChunkCoord coord = pendingVisibleChunkContentWork.Dequeue();
                queuedVisibleChunkContentCoords.Remove(coord);
                processedCount++;

                if (!activeLastUpdate.Contains(coord))
                    continue;

                if (IsUrgentVisibleChunk(viewerCoord, coord))
                    continue;

                ProcessVisibleChunkContentStep(coord, viewerCoord, sqrColliderRadius);
                if (ChunkNeedsVisibleContentWork(coord, viewerCoord, sqrColliderRadius))
                    deferredVisibleChunkContentRetries.Add(coord);
            }

            for (int i = 0; i < deferredVisibleChunkContentRetries.Count; i++)
                QueueVisibleChunkContentWork(deferredVisibleChunkContentRetries[i]);

            deferredVisibleChunkContentRetries.Clear();
        }
    }

    private void ProcessVisibleChunkContent(
        ChunkCoord coord,
        ChunkCoord viewerCoord,
        int sqrColliderRadius)
    {
        using (VisibleNormalChunkLoopMarker.Auto())
        {
            if (!loadedChunks.TryGetValue(coord, out ChunkRuntime runtime))
            {
                RemoveFrustumVisibleCoord(coord);
                return;
            }

            if (!chunkRecords.TryGetValue(coord, out ChunkRecord record))
            {
                RemoveFrustumVisibleCoord(coord);
                return;
            }

            int dx = coord.x - viewerCoord.x;
            int dz = coord.z - viewerCoord.z;
            int sqrDistance = dx * dx + dz * dz;
            bool useFarTerrain = coveragePolicy.ShouldUseFarTerrain(viewerCoord, coord);

            if (useFarTerrain)
            {
                using (VisibleChunkFarTerrainPathMarker.Auto())
                {
                    EnsureFarTerrainRequested(record);
                    TryApplyFarTerrain(record, runtime);
                }
            }
            else
            {
                using (VisibleChunkNearTerrainPathMarker.Auto())
                {
                    EnsureTerrainDataRequested(record);

                    int lod = ChunkRingLODPolicy.GetLOD(viewerCoord, coord);

                    EnsureLODMeshRequested(record, lod);
                    TryApplyLODMesh(record, runtime, lod);

                    if (!record.HasTerrainData)
                        TryApplyFarTerrain(record, runtime);
                }
            }

            using (VisibleChunkColliderPathMarker.Auto())
            {
                bool colliderDesired = sqrDistance <= sqrColliderRadius;
                record.ColliderDesired = colliderDesired;

                if (colliderDesired && !useFarTerrain)
                {
                    EnsureColliderRequested(record);
                    TryApplyCollider(record, runtime);
                }
                else if (runtime.HasCollider())
                {
                    using (RemoveColliderMarker.Auto())
                    {
                        runtime.RemoveCollider();
                        record.ClearColliderMesh();
                    }
                }
            }

            UpdateChunkRenderVisibility(coord, runtime);
        }
    }

    private void ProcessVisibleChunkContentStep(
        ChunkCoord coord,
        ChunkCoord viewerCoord,
        int sqrColliderRadius)
    {
        using (VisibleNormalChunkLoopMarker.Auto())
        {
            if (!loadedChunks.TryGetValue(coord, out ChunkRuntime runtime))
            {
                RemoveFrustumVisibleCoord(coord);
                return;
            }

            if (!chunkRecords.TryGetValue(coord, out ChunkRecord record))
            {
                RemoveFrustumVisibleCoord(coord);
                return;
            }

            int dx = coord.x - viewerCoord.x;
            int dz = coord.z - viewerCoord.z;
            int sqrDistance = dx * dx + dz * dz;
            bool useFarTerrain = coveragePolicy.ShouldUseFarTerrain(viewerCoord, coord);

            if (useFarTerrain)
            {
                using (VisibleChunkFarTerrainPathMarker.Auto())
                {
                    if (!record.HasFarTerrain)
                    {
                        EnsureFarTerrainRequested(record);
                    }
                    else if (!runtime.IsShowingLOD(FarTerrainLOD))
                    {
                        TryApplyFarTerrain(record, runtime);
                    }
                }

                UpdateChunkRenderVisibility(coord, runtime);
                return;
            }

            using (VisibleChunkNearTerrainPathMarker.Auto())
            {
                if (!record.HasTerrainData)
                {
                    EnsureTerrainDataRequested(record);
                    UpdateChunkRenderVisibility(coord, runtime);
                    return;
                }

                int lod = ChunkRingLODPolicy.GetLOD(viewerCoord, coord);
                if (!record.TryGetLODTerrainMesh(lod, out _))
                {
                    EnsureLODMeshRequested(record, lod);
                    UpdateChunkRenderVisibility(coord, runtime);
                    return;
                }

                if (!runtime.IsShowingLOD(lod))
                {
                    TryApplyLODMesh(record, runtime, lod);
                    UpdateChunkRenderVisibility(coord, runtime);
                    return;
                }
            }

            using (VisibleChunkColliderPathMarker.Auto())
            {
                bool colliderDesired = sqrDistance <= sqrColliderRadius;
                record.ColliderDesired = colliderDesired;

                if (colliderDesired)
                {
                    if (!record.ColliderReady)
                    {
                        EnsureColliderRequested(record);
                    }
                    else if (!runtime.HasCollider())
                    {
                        TryApplyCollider(record, runtime);
                    }
                }
                else if (runtime.HasCollider())
                {
                    using (RemoveColliderMarker.Auto())
                    {
                        runtime.RemoveCollider();
                        record.ClearColliderMesh();
                    }
                }
            }

            UpdateChunkRenderVisibility(coord, runtime);
        }
    }

    private void RefreshRenderVisibility(ChunkCoord viewerCoord, long budgetStart)
    {
        using (RefreshRenderVisibilityMarker.Auto())
        {
            if (orderedActiveCoords.Count == 0)
                return;

            int checksThisFrame = Mathf.Min(maxRenderVisibilityChecksPerFrame, orderedActiveCoords.Count);
            for (int checkedCount = 0;
                 checkedCount < checksThisFrame && HasVisibleChunkContentBudgetRemaining(budgetStart);
                 checkedCount++)
            {
                if (renderVisibilityCursor >= orderedActiveCoords.Count)
                    renderVisibilityCursor = 0;

                ChunkCoord coord = orderedActiveCoords[renderVisibilityCursor];
                renderVisibilityCursor++;

                if (IsUrgentVisibleChunk(viewerCoord, coord))
                    continue;

                if (loadedChunks.TryGetValue(coord, out ChunkRuntime runtime))
                    UpdateChunkRenderVisibility(coord, runtime);
                else
                    RemoveFrustumVisibleCoord(coord);
            }
        }
    }

    private void UpdateChunkRenderVisibility(ChunkCoord coord, ChunkRuntime runtime)
    {
        using (VisibleChunkRenderVisibilityMarker.Auto())
        {
            if (!runtime.IsVisible)
                runtime.SetVisible(true);

            bool renderVisible = viewerCamera == null || IsChunkInFrustum(coord);
            bool foliageShadowCasterVisible = viewerCamera == null || IsChunkInFrustum(coord, foliageFrustumPaddingWorldUnits);
            runtime.SetRenderVisible(renderVisible);
            runtime.SetFoliageRenderVisible(renderVisible);
            runtime.SetFoliageShadowCasterVisible(foliageShadowCasterVisible);

            if (renderVisible)
                AddFrustumVisibleCoord(coord);
            else
                RemoveFrustumVisibleCoord(coord);
        }
    }

    private void QueueVisibleChunkContentWork(ChunkCoord coord)
    {
        if (queuedVisibleChunkContentCoords.Add(coord))
            pendingVisibleChunkContentWork.Enqueue(coord);
    }

    private bool ChunkNeedsVisibleContentWork(
        ChunkCoord coord,
        ChunkCoord viewerCoord,
        int sqrColliderRadius)
    {
        if (!activeLastUpdate.Contains(coord))
            return false;

        if (!loadedChunks.TryGetValue(coord, out ChunkRuntime runtime))
            return false;

        if (!chunkRecords.TryGetValue(coord, out ChunkRecord record))
            return false;

        bool useFarTerrain = coveragePolicy.ShouldUseFarTerrain(viewerCoord, coord);
        if (useFarTerrain)
        {
            if (record.HasFarTerrain)
                return !runtime.IsShowingLOD(FarTerrainLOD);

            return !record.IsFarTerrainRequestInFlight;
        }

        if (!record.HasTerrainData)
            return !record.IsTerrainDataRequestInFlight;

        int lod = ChunkRingLODPolicy.GetLOD(viewerCoord, coord);
        if (!record.TryGetLODTerrainMesh(lod, out _))
            return !record.IsMeshRequestInFlight(lod);

        if (!runtime.IsShowingLOD(lod))
            return true;

        int dx = coord.x - viewerCoord.x;
        int dz = coord.z - viewerCoord.z;
        bool colliderDesired = dx * dx + dz * dz <= sqrColliderRadius;

        if (colliderDesired)
        {
            if (!record.ColliderReady)
                return !record.ColliderRequestInFlight;

            return !runtime.HasCollider();
        }

        return runtime.HasCollider();
    }

    private void AddFrustumVisibleCoord(ChunkCoord coord)
    {
        if (frustumVisibleCoordSet.Add(coord))
            frustumVisibleCoords.Add(coord);
    }

    private void RemoveFrustumVisibleCoord(ChunkCoord coord)
    {
        if (!frustumVisibleCoordSet.Remove(coord))
            return;

        frustumVisibleCoords.Remove(coord);
    }

    private bool IsUrgentVisibleChunk(ChunkCoord viewerCoord, ChunkCoord coord)
    {
        return TerrainCoveragePolicy.GetChunkRingDistance(viewerCoord, coord) <= urgentVisibleChunkRingRadius;
    }

    private bool HasVisibleChunkContentBudgetRemaining(long budgetStart)
    {
        return visibleChunkContentBudgetMsPerFrame <= 0f ||
               TerrainGenerationProfiler.GetElapsedMilliseconds(budgetStart) < visibleChunkContentBudgetMsPerFrame;
    }

    private void UpdateVisibleFarTerrainTiles(ChunkCoord viewerCoord)
    {
        using (UpdateVisibleFarTerrainTilesMarker.Auto())
        {
            long budgetStart = TerrainGenerationProfiler.GetTimestamp();
            ProcessFarTerrainTileContentQueue(viewerCoord, budgetStart);
        }
    }

    private void ProcessFarTerrainTileContentQueue(ChunkCoord viewerCoord, long budgetStart)
    {
        using (ProcessFarTerrainTileContentQueueMarker.Auto())
        using (VisibleFarTileLoopMarker.Auto())
        {
            deferredFarTerrainTileContentRetries.Clear();
            int processedCount = 0;

            while (pendingFarTerrainTileContentWork.Count > 0 &&
                   processedCount < maxFarTerrainTileContentUpdatesPerFrame &&
                   HasFarTerrainTileContentBudgetRemaining(budgetStart))
            {
                FarTerrainPatchKey farPatch = pendingFarTerrainTileContentWork.Dequeue();
                queuedFarTerrainTileContentCoords.Remove(farPatch);
                processedCount++;

                if (!activeFarTilesLastUpdate.Contains(farPatch))
                    continue;

                FarTerrainTileRecord record = GetOrCreateFarTerrainTileRecord(farPatch);
                FarTerrainTileRuntime runtime = GetOrCreateFarTerrainTileRuntime(record);

                EnsureFarTerrainTileRequested(record);
                TryApplyFarTerrainTile(record, runtime);

                if (!runtime.IsVisible)
                    runtime.SetVisible(true);

                runtime.SetRenderVisible(true);

                if (FarTerrainTileNeedsContentWork(farPatch))
                    deferredFarTerrainTileContentRetries.Add(farPatch);
            }

            for (int i = 0; i < deferredFarTerrainTileContentRetries.Count; i++)
                QueueFarTerrainTileContentWork(deferredFarTerrainTileContentRetries[i]);

            deferredFarTerrainTileContentRetries.Clear();
        }
    }

    private void QueueFarTerrainTileContentWork(FarTerrainPatchKey farPatch)
    {
        if (queuedFarTerrainTileContentCoords.Add(farPatch))
            pendingFarTerrainTileContentWork.Enqueue(farPatch);
    }

    private bool FarTerrainTileNeedsContentWork(FarTerrainPatchKey farPatch)
    {
        if (!activeFarTilesLastUpdate.Contains(farPatch))
            return false;

        if (!farTerrainTileRecords.TryGetValue(farPatch, out FarTerrainTileRecord record))
            return true;

        if (!record.HasTerrain)
            return !record.IsRequestInFlight;

        if (!loadedFarTerrainTiles.TryGetValue(farPatch, out FarTerrainTileRuntime runtime))
            return true;

        return record.TryGetTerrainMesh(out Mesh terrainMesh) && !runtime.IsShowingMesh(terrainMesh);
    }

    private bool HasFarTerrainTileContentBudgetRemaining(long budgetStart)
    {
        return farTerrainTileContentBudgetMsPerFrame <= 0f ||
               TerrainGenerationProfiler.GetElapsedMilliseconds(budgetStart) < farTerrainTileContentBudgetMsPerFrame;
    }

    private bool IsChunkInFrustum(ChunkCoord coord)
    {
        return IsChunkInFrustum(coord, 0f);
    }

    private bool IsChunkInFrustum(ChunkCoord coord, float paddingWorldUnits)
    {
        Bounds bounds = GetChunkWorldBounds(coord);
        if (paddingWorldUnits > 0f)
            bounds.Expand(new Vector3(paddingWorldUnits * 2f, 0f, paddingWorldUnits * 2f));

        return GeometryUtility.TestPlanesAABB(frustumPlanes, bounds);
    }

    private bool IsFarTerrainTileInFrustum(FarTerrainPatchKey farPatch)
    {
        Bounds bounds = GetFarTerrainTileWorldBounds(farPatch);
        return GeometryUtility.TestPlanesAABB(frustumPlanes, bounds);
    }

    private Bounds GetChunkWorldBounds(ChunkCoord coord)
    {
        float chunkWorldSize = chunkSize * worldScale;

        float centerX = coord.x * chunkWorldSize + chunkWorldSize * 0.5f;
        float centerZ = coord.z * chunkWorldSize + chunkWorldSize * 0.5f;

        float boundsHeight = Mathf.Max(200f, meshHeightMultiplier * 2f + 100f);

        Vector3 center = new Vector3(
            centerX,
            boundsHeight * 0.5f,
            centerZ
        );

        Vector3 size = new Vector3(
            chunkWorldSize,
            boundsHeight,
            chunkWorldSize
        );

        return new Bounds(center, size);
    }

    private Bounds GetFarTerrainTileWorldBounds(FarTerrainPatchKey farPatch)
    {
        float tileWorldSize = chunkSize * farPatch.SizeInChunks * worldScale;

        float centerX = farPatch.Origin.x * tileWorldSize + tileWorldSize * 0.5f;
        float centerZ = farPatch.Origin.z * tileWorldSize + tileWorldSize * 0.5f;

        float boundsHeight = Mathf.Max(200f, meshHeightMultiplier * 2f + 100f);

        Vector3 center = new Vector3(
            centerX,
            boundsHeight * 0.5f,
            centerZ);

        Vector3 size = new Vector3(
            tileWorldSize,
            boundsHeight,
            tileWorldSize);

        return new Bounds(center, size);
    }

    private FarTerrainTileRecord FindFarTerrainRecord(FarTerrainPatchKey key)
    {
        farTerrainTileRecords.TryGetValue(key, out var record); return record;
    }

    public ChunkRecord GetChunkRecord(ChunkCoord coord)
    {
        chunkRecords.TryGetValue(coord, out ChunkRecord record);
        return record;
    }

    public bool HasVisibleWater(Plane[] frustum, int cameraMask)
    {
        int waterLayer = LayerMask.NameToLayer("Water");
        if (waterLayer < 0 || (cameraMask & (1 << waterLayer)) == 0) return false;
        foreach (var runtime in loadedChunks.Values)
            if (runtime.TryGetVisibleWaterBounds(out Bounds bounds) && GeometryUtility.TestPlanesAABB(frustum, bounds)) return true;
        foreach (var runtime in loadedFarTerrainTiles.Values)
            if (runtime.TryGetVisibleWaterBounds(out Bounds bounds) && GeometryUtility.TestPlanesAABB(frustum, bounds)) return true;
        return false;
    }

    public ChunkRuntime GetChunkRuntime(ChunkRecord record)
    {
        if (record == null)
            return null;

        loadedChunks.TryGetValue(record.ChunkCoord, out ChunkRuntime runtime);
        return runtime;
    }

    private ChunkRecord GetOrCreateChunkRecord(ChunkCoord coord)
    {
        if (!chunkRecords.TryGetValue(coord, out ChunkRecord record))
        {
            record = new ChunkRecord(coord);
            chunkRecords.Add(coord, record);
        }

        return record;
    }

    private ChunkRuntime GetOrCreateChunkRuntime(ChunkRecord record)
    {
        ChunkCoord coord = record.ChunkCoord;

        if (!loadedChunks.TryGetValue(coord, out ChunkRuntime runtime))
        {
            runtime = runtimePool.Acquire(record);

            loadedChunks.Add(coord, runtime);
        }

        return runtime;
    }

    private void ReleaseChunkRuntime(ChunkCoord coord, ChunkRuntime runtime)
    { runtimePool.Release(runtime); RemoveFrustumVisibleCoord(coord); }

    private FarTerrainTileRecord GetOrCreateFarTerrainTileRecord(FarTerrainPatchKey farPatch)
    {
        if (!farTerrainTileRecords.TryGetValue(farPatch, out FarTerrainTileRecord record))
        {
            record = new FarTerrainTileRecord(farPatch);
            farTerrainTileRecords.Add(farPatch, record);
        }

        return record;
    }

    private FarTerrainTileRuntime GetOrCreateFarTerrainTileRuntime(FarTerrainTileRecord record)
    {
        FarTerrainPatchKey patch = record.PatchKey;

        if (!loadedFarTerrainTiles.TryGetValue(patch, out FarTerrainTileRuntime runtime))
        {
            runtime = runtimePool.Acquire(record);

            loadedFarTerrainTiles.Add(patch, runtime);
        }

        return runtime;
    }

    private void ReleaseFarTerrainTileRuntime(FarTerrainTileRuntime runtime) => runtimePool.Release(runtime);

    private void EnsureTerrainVisualRequested(ChunkRecord record, ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        if (coveragePolicy.ShouldUseFarTerrain(viewerCoord, targetCoord))
        {
            EnsureFarTerrainRequested(record);
        }
        else
        {
            EnsureTerrainDataRequested(record);
        }
    }

    private void EnsureFarTerrainRequested(ChunkRecord record)
    {
        using var ensureFarTerrainRequestedScope = EnsureFarTerrainRequestedMarker.Auto();

        if (record.HasFarTerrain)
            return;

        if (record.IsFarTerrainRequestInFlight)
            return;

        if (ShouldDeferFarTerrainWork())
            return;

        int requestVersion = record.BeginFarTerrainRequest();

        bool submitted = terrainRequestManager.RequestFarTerrainData(
            record.ChunkCoord,
            requestVersion,
            chunkSize,
            seed,
            sampleScale,
            meshHeightMultiplier,
            worldScale,
            farTerrainHeightGridResolution,
            farTerrainControlMapResolution,
            farTerrainSkirtDepth,
            climateOctaves: octaves,
            climatePersistence: persistence,
            climateLacunarity: lacunarity
        );

        if (!submitted)
        {
            record.CancelFarTerrainRequest(requestVersion);
        }
    }

    private void EnsureFarTerrainTileRequested(FarTerrainTileRecord record)
    {
        using var ensureFarTerrainTileRequestedScope = EnsureFarTerrainTileRequestedMarker.Auto();

        if (record.HasTerrain)
            return;

        if (record.IsRequestInFlight)
            return;

        if (ShouldDeferFarTerrainWork())
            return;

        int requestVersion = record.BeginRequest();
        int tileChunkSize = chunkSize * record.SizeInChunks;
        int tileHeightGridResolution = GetFarPatchHeightGridResolution(record.SizeInChunks);
        int tileControlMapResolution = GetFarPatchControlMapResolution();

        bool submitted = terrainRequestManager.RequestFarTerrainData(
            record.TileCoord,
            requestVersion,
            tileChunkSize,
            seed,
            sampleScale,
            meshHeightMultiplier,
            worldScale,
            tileHeightGridResolution,
            tileControlMapResolution,
            farTerrainSkirtDepth,
            true,
            record.SizeInChunks,
            octaves,
            persistence,
            lacunarity);

        if (!submitted)
            record.CancelRequest(requestVersion);
    }

    private int GetFarPatchHeightGridResolution(int patchSizeInChunks)
    {
        // Give the nearest two far patch sizes more geometry where mountain
        // ridges are still prominent. Keep the remaining grids unchanged.
        return patchSizeInChunks <= farTerrainMacroTileSize * 2 ||
               patchSizeInChunks >= TerrainCoveragePolicy.FarTerrainMaxPatchSizeInChunks ? 65 : 33;
    }

    private int GetFarPatchControlMapResolution()
    {
        // Preserve the former 4x4 macro-tile control-map density at every
        // quadtree level. Geometry coarsens with distance; material masks remain
        // stable enough to avoid broad, obvious biome transitions.
        return Mathf.Clamp((farTerrainControlMapResolution - 1) * farTerrainMacroTileSize + 1, 2, 128);
    }

    private void EnsureTerrainDataRequested(ChunkRecord record)
    {
        using var ensureTerrainDataRequestedScope = EnsureTerrainDataRequestedMarker.Auto();

        if (record.HasTerrainData)
            return;

        if (record.IsTerrainDataRequestInFlight)
            return;

        int requestVersion = record.BeginTerrainDataRequest();

        bool submitted = terrainRequestManager.RequestTerrainData(
            record.ChunkCoord,
            requestVersion,
            chunkSize,
            seed,
            sampleScale,
            octaves,
            persistence,
            lacunarity,
            worldFeatureGenerationSettings,
            meshHeightMultiplier
        );

        if (!submitted)
        {
            record.CancelTerrainDataRequest(requestVersion);
        }
    }

    private void TryApplyFarTerrain(ChunkRecord record, ChunkRuntime runtime)
    {
        using var tryApplyFarTerrainScope = TryApplyFarTerrainMarker.Auto();

        if (!record.TryGetFarTerrainMesh(out Mesh terrainMesh))
            return;

        if (runtime.IsShowingLOD(FarTerrainLOD))
            return;

        runtime.SetControlMaps(record.FarTerrainControlMapData);
        runtime.SetTerrainHorizon(terrainHorizonShadows, record.ChunkCoord.x * chunkSize,
            record.ChunkCoord.z * chunkSize, chunkSize, record.FarTreeHeightGrid, 0);
        runtime.SetMeshes(terrainMesh, record.FarTerrainWaterMesh, FarTerrainLOD);
    }

    private void TryApplyFarTerrainTile(FarTerrainTileRecord record, FarTerrainTileRuntime runtime)
    {
        using var tryApplyFarTerrainTileScope = TryApplyFarTerrainTileMarker.Auto();

        if (!record.TryGetTerrainMesh(out Mesh terrainMesh))
            return;

        if (runtime.IsShowingMesh(terrainMesh))
            return;

        runtime.SetControlMaps(record.ControlMapData);
        int tileSize = chunkSize * record.SizeInChunks;
        runtime.SetTerrainHorizon(terrainHorizonShadows, record.TileCoord.x * tileSize,
            record.TileCoord.z * tileSize, tileSize, record.FarTreeHeightGrid, 0);
        runtime.SetMesh(terrainMesh, record.WaterMesh);
    }

    private void EnsureColliderRequested(ChunkRecord record)
    {
        using var ensureColliderRequestedScope = EnsureColliderRequestedMarker.Auto();

        if (!record.HasTerrainData)
            return;

        if (record.ColliderReady)
            return;

        if (record.ColliderRequestInFlight)
            return;

        int requestVersion = record.BeginColliderRequest();

        bool submitted = terrainRequestManager.RequestColliderMesh(
            record.ChunkCoord,
            requestVersion,
            record.HeightMap,
            meshHeightMultiplier,
            worldScale
        );

        if (!submitted)
            record.CancelColliderRequest(requestVersion);
    }

    private void TryApplyCollider(ChunkRecord record, ChunkRuntime runtime)
    {
        using var tryApplyColliderScope = TryApplyColliderMarker.Auto();

        if (!record.TryGetColliderMesh(out Mesh colliderMesh))
            return;

        if (!runtime.HasCollider())
        {
            runtime.ApplyCollider(colliderMesh);
        }
    }

    private void EnsureLODMeshRequested(ChunkRecord record, int lod)
    {
        using var ensureLodMeshRequestedScope = EnsureLodMeshRequestedMarker.Auto();

        if (!record.HasTerrainData)
            return;

        if (record.TryGetLODTerrainMesh(lod, out _))
            return;

        if (record.IsMeshRequestInFlight(lod))
            return;

        int stepIncrement = 1 << lod;
        int requestVersion = record.BeginMeshRequest(lod);

        bool submitted = terrainRequestManager.RequestLODMesh(
            record.ChunkCoord,
            lod,
            requestVersion,
            record.NativeData,
            meshHeightMultiplier,
            stepIncrement,
            worldScale
        );

        if (!submitted)
            record.CancelMeshRequest(lod, requestVersion);
    }

    private void TryApplyLODMesh(ChunkRecord record, ChunkRuntime runtime, int lod)
    {
        using var tryApplyLodMeshScope = TryApplyLodMeshMarker.Auto();

        if (!record.TryGetLODTerrainMesh(lod, out Mesh terrainMesh))
            return;

        if (!runtime.IsShowingLOD(lod))
        {
            Mesh waterMesh = null;

            record.TryGetLODWaterMesh(lod, out waterMesh);

            runtime.SetControlMaps(record.ControlMapData);
            runtime.SetTerrainHorizon(terrainHorizonShadows, record.ChunkCoord.x * chunkSize,
                record.ChunkCoord.z * chunkSize, chunkSize, record.HeightMap, 1);
            runtime.SetMeshes(terrainMesh, waterMesh, lod);
        }
    }

    private bool ShouldDeferFarTerrainWork()
    {
        return terrainRequestManager.ActiveTerrainDataJobCount > 0 ||
               terrainRequestManager.ActiveMeshJobCount > 0 ||
               terrainRequestManager.ActiveColliderJobCount > 0 ||
               resultPublisher.HasHigherPriorityCompletedTerrainResults();
    }

    private bool IsFarTerrainResultStillWanted(
        FarTerrainRequestResult result,
        ChunkCoord viewerCoord)
    {
        if (result.IsMacroTile)
        {
            FarTerrainPatchKey patch = new FarTerrainPatchKey(result.ChunkCoord, result.PatchSizeInChunks);
            if (!farTerrainTileRecords.TryGetValue(patch, out FarTerrainTileRecord tileRecord))
                return false;

            if (!tileRecord.IsRequestCurrent(result.RequestVersion))
                return false;

            if (!IsFarTerrainTileWanted(viewerCoord, patch))
            {
                tileRecord.CancelRequest(result.RequestVersion);
                return false;
            }

            return true;
        }

        if (!chunkRecords.TryGetValue(result.ChunkCoord, out ChunkRecord record))
            return false;

        if (!record.IsFarTerrainRequestCurrent(result.RequestVersion))
            return false;

        if (!IsChunkWithinViewDistance(viewerCoord, result.ChunkCoord) ||
            !coveragePolicy.ShouldUseFarTerrain(viewerCoord, result.ChunkCoord))
        {
            record.CancelFarTerrainRequest(result.RequestVersion);
            return false;
        }

        return true;
    }

    private bool IsChunkWithinViewDistance(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        int dx = targetCoord.x - viewerCoord.x;
        int dz = targetCoord.z - viewerCoord.z;
        return dx * dx + dz * dz <= viewDistance * viewDistance;
    }

    private bool IsFarTerrainTileWanted(ChunkCoord viewerCoord, FarTerrainPatchKey patch)
    {
        if (!enableFarTerrain || !activeFarTilesLastUpdate.Contains(patch))
            return false;

        int originX = patch.Origin.x * patch.SizeInChunks;
        int originZ = patch.Origin.z * patch.SizeInChunks;
        int maxX = originX + patch.SizeInChunks - 1;
        int maxZ = originZ + patch.SizeInChunks - 1;
        int sqrViewRadius = viewDistance * viewDistance;

        for (int x = originX; x <= maxX; x++)
        {
            for (int z = originZ; z <= maxZ; z++)
            {
                int dx = x - viewerCoord.x;
                int dz = z - viewerCoord.z;
                if (dx * dx + dz * dz <= sqrViewRadius)
                    return true;
            }
        }

        return false;
    }

    private void SortOrderedActiveCoords(ChunkCoord viewerCoord)
    {
        Vector2 forward = new Vector2(viewer.forward.x, viewer.forward.z).normalized;

        orderedActiveCoords.Sort((a, b) =>
        {
            int adx = a.x - viewerCoord.x;
            int adz = a.z - viewerCoord.z;
            int bdx = b.x - viewerCoord.x;
            int bdz = b.z - viewerCoord.z;

            int aRing = Mathf.Max(Mathf.Abs(adx), Mathf.Abs(adz));
            int bRing = Mathf.Max(Mathf.Abs(bdx), Mathf.Abs(bdz));

            int ringCompare = aRing.CompareTo(bRing);
            if (ringCompare != 0)
                return ringCompare;

            int aSqrDist = adx * adx + adz * adz;
            int bSqrDist = bdx * bdx + bdz * bdz;

            float aDot = aSqrDist == 0 ? 2f : Vector2.Dot(forward, new Vector2(adx, adz).normalized);
            float bDot = bSqrDist == 0 ? 2f : Vector2.Dot(forward, new Vector2(bdx, bdz).normalized);

            int dotCompare = bDot.CompareTo(aDot);
            if (dotCompare != 0)
                return dotCompare;

            return aSqrDist.CompareTo(bSqrDist);
        });
    }

    private void SortOrderedActiveFarTileCoords(ChunkCoord viewerCoord)
    {
        orderedActiveFarTileCoords.Sort((a, b) =>
        {
            int aDistance = GetFarTerrainTileDistanceSqr(viewerCoord, a);
            int bDistance = GetFarTerrainTileDistanceSqr(viewerCoord, b);
            return aDistance.CompareTo(bDistance);
        });
    }

    private int GetFarTerrainTileDistanceSqr(ChunkCoord viewerCoord, FarTerrainPatchKey patch)
    {
        int originX = patch.Origin.x * patch.SizeInChunks;
        int originZ = patch.Origin.z * patch.SizeInChunks;
        int centerX = originX + patch.SizeInChunks / 2;
        int centerZ = originZ + patch.SizeInChunks / 2;
        int dx = centerX - viewerCoord.x;
        int dz = centerZ - viewerCoord.z;
        return dx * dx + dz * dz;
    }

    private static int ComputeMaxActiveChunkCount(int viewDistance)
    {
        int count = 0;
        int sqrViewRadius = viewDistance * viewDistance;

        for (int x = -viewDistance; x <= viewDistance; x++)
        {
            for (int z = -viewDistance; z <= viewDistance; z++)
            {
                if (x * x + z * z <= sqrViewRadius)
                    count++;
            }
        }

        return count;
    }

}
