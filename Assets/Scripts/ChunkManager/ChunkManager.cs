using System.Collections.Generic;
using UnityEngine;

public class ChunkManager
{
    private const int FarTerrainLOD = 5;

    private readonly int subChunksPerChunk = 10;

    private readonly int viewDistance;
    private readonly int colliderDistance;
    private readonly bool enableFarTerrain;
    private readonly int farTerrainStartRing;
    private readonly int farTerrainMacroTileSize;
    private readonly int farTerrainHeightGridResolution;
    private readonly int farTerrainControlMapResolution;
    private readonly float farTerrainSkirtDepth;
    private readonly int chunkSize;
    private readonly int seed;
    private readonly Transform viewer;
    private readonly Camera viewerCamera;
    private readonly Transform chunkParent;
    private readonly float sampleScale;
    private readonly float worldScale;
    private readonly int octaves;
    private readonly float persistence;
    private readonly float lacunarity;
    private readonly float erosionStrength;
    private readonly float meshHeightMultiplier;
    private readonly Material terrainMaterial;
    private readonly Material waterMaterial;
    private readonly int maxActiveTerrainDataJobs;
    private readonly int maxActiveFarTerrainJobs;
    private readonly int maxActiveMeshJobs;
    private readonly int maxActiveColliderJobs;
    private readonly int maxTerrainDataResultsAppliedPerFrame;
    private readonly int maxFarTerrainResultsAppliedPerFrame;
    private readonly int maxLODMeshResultsAppliedPerFrame;
    private readonly int maxColliderResultsAppliedPerFrame;
    private readonly float completedRequestApplyBudgetMsPerFrame;
    private readonly float terrainDataApplyBudgetMsPerFrame;
    private readonly float farTerrainApplyBudgetMsPerFrame;
    private readonly float lodMeshApplyBudgetMsPerFrame;
    private readonly float colliderApplyBudgetMsPerFrame;

    private readonly Dictionary<ChunkCoord, ChunkRecord> chunkRecords = new();
    private readonly Dictionary<ChunkCoord, ChunkRuntime> loadedChunks = new();
    private readonly Dictionary<ChunkCoord, FarTerrainTileRecord> farTerrainTileRecords = new();
    private readonly Dictionary<ChunkCoord, FarTerrainTileRuntime> loadedFarTerrainTiles = new();

    private HashSet<ChunkCoord> activeLastUpdate;
    private HashSet<ChunkCoord> activeThisUpdate;
    private HashSet<ChunkCoord> activeFarTilesLastUpdate;
    private HashSet<ChunkCoord> activeFarTilesThisUpdate;
    private readonly List<ChunkCoord> orderedActiveCoords;
    private readonly List<ChunkCoord> frustumVisibleCoords;
    private readonly List<ChunkCoord> orderedActiveFarTileCoords;
    private readonly Plane[] frustumPlanes = new Plane[6];

    private ChunkCoord lastUpdateViewerCoord = new ChunkCoord(int.MinValue, int.MinValue);
    private SubChunkCoord lastViewerGlobalSubChunk = new SubChunkCoord(int.MinValue, int.MinValue);

    private readonly TerrainRequestManager terrainRequestManager;
    private readonly FoliageManager foliageManager;
    private readonly WorldFeatureGenerationSettings worldFeatureGenerationSettings;

    public ChunkManager(
        int viewDistance,
        int colliderDistance,
        bool enableFarTerrain,
        int farTerrainStartRing,
        int farTerrainMacroTileSize,
        int farTerrainHeightGridResolution,
        int farTerrainControlMapResolution,
        float farTerrainSkirtDepth,
        int chunkSize,
        int seed,
        Transform viewer,
        Camera viewerCamera,
        Transform chunkParent,
        Transform foliageParent,
        GrassSettings grassSettings,
        FlowerSettings flowerSettings,
        TreeSettings treeSettings,
        float sampleScale,
        float worldScale,
        int octaves,
        float persistence,
        float lacunarity,
        float erosionStrength,
        float meshHeightMultiplier,
        Material terrainMaterial,
        Material waterMaterial,
        int maxActiveTerrainDataJobs,
        int maxActiveFarTerrainJobs,
        int maxActiveMeshJobs,
        int maxActiveColliderJobs,
        int maxTerrainDataResultsAppliedPerFrame,
        int maxFarTerrainResultsAppliedPerFrame,
        int maxLODMeshResultsAppliedPerFrame,
        int maxColliderResultsAppliedPerFrame,
        float completedRequestApplyBudgetMsPerFrame,
        float terrainDataApplyBudgetMsPerFrame,
        float farTerrainApplyBudgetMsPerFrame,
        float lodMeshApplyBudgetMsPerFrame,
        float colliderApplyBudgetMsPerFrame)
    {
        this.viewDistance = viewDistance;
        this.colliderDistance = colliderDistance;
        this.enableFarTerrain = enableFarTerrain;
        this.farTerrainStartRing = Mathf.Max(1, farTerrainStartRing);
        this.farTerrainMacroTileSize = Mathf.Max(1, farTerrainMacroTileSize);
        this.farTerrainHeightGridResolution = Mathf.Max(2, farTerrainHeightGridResolution);
        this.farTerrainControlMapResolution = Mathf.Max(2, farTerrainControlMapResolution);
        this.farTerrainSkirtDepth = Mathf.Max(0f, farTerrainSkirtDepth);
        this.chunkSize = chunkSize;
        this.seed = seed;
        this.viewer = viewer;
        this.viewerCamera = viewerCamera;
        this.chunkParent = chunkParent;
        this.sampleScale = sampleScale;
        this.worldScale = worldScale;
        this.octaves = octaves;
        this.persistence = persistence;
        this.lacunarity = lacunarity;
        this.erosionStrength = erosionStrength;
        this.meshHeightMultiplier = meshHeightMultiplier;
        this.terrainMaterial = terrainMaterial;
        this.waterMaterial = waterMaterial;
        this.maxActiveTerrainDataJobs = Mathf.Max(1, maxActiveTerrainDataJobs);
        this.maxActiveFarTerrainJobs = Mathf.Max(1, maxActiveFarTerrainJobs);
        this.maxActiveMeshJobs = Mathf.Max(1, maxActiveMeshJobs);
        this.maxActiveColliderJobs = Mathf.Max(1, maxActiveColliderJobs);
        this.maxTerrainDataResultsAppliedPerFrame = Mathf.Max(1, maxTerrainDataResultsAppliedPerFrame);
        this.maxFarTerrainResultsAppliedPerFrame = Mathf.Max(1, maxFarTerrainResultsAppliedPerFrame);
        this.maxLODMeshResultsAppliedPerFrame = Mathf.Max(1, maxLODMeshResultsAppliedPerFrame);
        this.maxColliderResultsAppliedPerFrame = Mathf.Max(1, maxColliderResultsAppliedPerFrame);
        this.completedRequestApplyBudgetMsPerFrame = Mathf.Max(0f, completedRequestApplyBudgetMsPerFrame);
        this.terrainDataApplyBudgetMsPerFrame = Mathf.Max(0f, terrainDataApplyBudgetMsPerFrame);
        this.farTerrainApplyBudgetMsPerFrame = Mathf.Max(0f, farTerrainApplyBudgetMsPerFrame);
        this.lodMeshApplyBudgetMsPerFrame = Mathf.Max(0f, lodMeshApplyBudgetMsPerFrame);
        this.colliderApplyBudgetMsPerFrame = Mathf.Max(0f, colliderApplyBudgetMsPerFrame);

        int maxChunks = ComputeMaxActiveChunkCount(viewDistance);

        activeLastUpdate = new HashSet<ChunkCoord>(maxChunks);
        activeThisUpdate = new HashSet<ChunkCoord>(maxChunks);
        activeFarTilesLastUpdate = new HashSet<ChunkCoord>(maxChunks);
        activeFarTilesThisUpdate = new HashSet<ChunkCoord>(maxChunks);
        orderedActiveCoords = new List<ChunkCoord>(maxChunks);
        frustumVisibleCoords = new List<ChunkCoord>(maxChunks);
        orderedActiveFarTileCoords = new List<ChunkCoord>(maxChunks);

        worldFeatureGenerationSettings = BuildWorldFeatureGenerationSettings(treeSettings);
        terrainRequestManager = new TerrainRequestManager(
            this.maxActiveTerrainDataJobs,
            this.maxActiveFarTerrainJobs,
            this.maxActiveMeshJobs,
            this.maxActiveColliderJobs);
        foliageManager = new FoliageManager(
            foliageParent,
            grassSettings,
            flowerSettings,
            treeSettings,
            seed,
            chunkSize,
            worldScale,
            meshHeightMultiplier);
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
        ChunkRuntime runtime = null;
        bool hasRuntime = hasChunkRecord && loadedChunks.TryGetValue(coord, out runtime);
        bool hasFoliageRuntime = hasRuntime && runtime.FoliageRuntime != null && runtime.FoliageRuntime.IsCreated;

        BiomeType biome = default;
        SurfaceType surfaceType = default;
        float worldHeight = 0f;
        float slope = 0f;
        float moisture = 0f;
        float temperature = 0f;
        float riverMask = 0f;
        int gpuGrassInstanceCount = 0;
        int gpuFlowerInstanceCount = 0;
        int gpuTreeInstanceCount = 0;

        if (hasTerrainData && TryGetPaddedSampleIndices(coord, worldPosition, record, out int sampleX, out int sampleZ))
        {
            biome = record.BiomeMap[sampleX, sampleZ];
            surfaceType = record.SurfaceTypeMap[sampleX, sampleZ];
            worldHeight = record.HeightMap[sampleX, sampleZ] * meshHeightMultiplier * worldScale;
            slope = record.SlopeMap[sampleX, sampleZ];
            moisture = record.MoistureMap[sampleX, sampleZ];
            temperature = record.TemperatureMap[sampleX, sampleZ];
            riverMask = record.RiverMaskMap[sampleX, sampleZ];
        }

        if (hasFoliageRuntime)
        {
            gpuGrassInstanceCount = runtime.FoliageRuntime.GpuGrassInstanceCount;
            gpuFlowerInstanceCount = runtime.FoliageRuntime.GpuFlowerInstanceCount;
            gpuTreeInstanceCount = runtime.FoliageRuntime.GpuTreeInstanceCount;
        }

        return new WorldDebugInfo(
            worldPosition,
            coord,
            hasChunkRecord,
            hasTerrainData,
            hasRuntime,
            hasFoliageRuntime,
            biome,
            surfaceType,
            worldHeight,
            slope,
            moisture,
            temperature,
            riverMask,
            gpuGrassInstanceCount,
            gpuFlowerInstanceCount,
            gpuTreeInstanceCount);
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
            ChunkCoord tileCoord = orderedActiveFarTileCoords[i];
            if (!loadedFarTerrainTiles.TryGetValue(tileCoord, out FarTerrainTileRuntime runtime))
                continue;

            runtime.AccumulateRenderStats(ref stats);
        }

        foliageManager.AccumulateVisibleFoliageRenderStats(
            this,
            viewerCoord,
            frustumVisibleCoords,
            ref stats);

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
        ProcessCompletedRequests();

        ChunkCoord viewerCoord = GetViewerChunkCoord();
        SubChunkCoord viewerGlobalSubChunk = GetViewerGlobalSubChunkCoord();

        bool viewerChunkChanged = viewerCoord != lastUpdateViewerCoord;
        bool viewerSubChunkChanged = viewerGlobalSubChunk != lastViewerGlobalSubChunk;

        if (viewerChunkChanged)
        {
            RebuildActiveChunkSet(viewerCoord);
            lastUpdateViewerCoord = viewerCoord;
        }

        UpdateVisibleChunkContent(viewerCoord);

        long foliageStart = TerrainGenerationProfiler.GetTimestamp();

        if (viewerChunkChanged || viewerSubChunkChanged)
        {
            foliageManager.HandleViewerSubChunkChanged(
                this,
                viewerCoord,
                viewerGlobalSubChunk,
                orderedActiveCoords,
                viewerChunkChanged);
        }

        foliageManager.DrawVisibleFoliageEveryFrame(
            this,
            viewerCoord,
            viewerGlobalSubChunk,
            frustumVisibleCoords);

        TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.FoliageTotal, foliageStart);

        lastViewerGlobalSubChunk = viewerGlobalSubChunk;
    }

    private void RebuildActiveChunkSet(ChunkCoord viewerCoord)
    {
        activeThisUpdate.Clear();
        activeFarTilesThisUpdate.Clear();
        orderedActiveCoords.Clear();
        orderedActiveFarTileCoords.Clear();

        int sqrViewRadius = viewDistance * viewDistance;

        for (int x = -viewDistance; x <= viewDistance; x++)
        {
            for (int z = -viewDistance; z <= viewDistance; z++)
            {
                int sqrDistance = x * x + z * z;
                if (sqrDistance > sqrViewRadius)
                    continue;

                ChunkCoord targetCoord = new ChunkCoord(viewerCoord.x + x, viewerCoord.z + z);

                if (ShouldUseMacroFarTerrain(viewerCoord, targetCoord, out ChunkCoord farTileCoord))
                {
                    if (activeFarTilesThisUpdate.Add(farTileCoord))
                        orderedActiveFarTileCoords.Add(farTileCoord);

                    continue;
                }

                activeThisUpdate.Add(targetCoord);
                orderedActiveCoords.Add(targetCoord);
            }
        }

        SortOrderedActiveCoords(viewerCoord);
        SortOrderedActiveFarTileCoords(viewerCoord);

        foreach (ChunkCoord targetCoord in orderedActiveCoords)
        {
            ChunkRecord record = GetOrCreateChunkRecord(targetCoord);
            ChunkRuntime runtime = GetOrCreateChunkRuntime(record);

            if (!runtime.IsVisible)
                runtime.SetVisible(true);

            EnsureTerrainVisualRequested(record, viewerCoord, targetCoord);
        }

        foreach (ChunkCoord farTileCoord in orderedActiveFarTileCoords)
        {
            FarTerrainTileRecord record = GetOrCreateFarTerrainTileRecord(farTileCoord);
            FarTerrainTileRuntime runtime = GetOrCreateFarTerrainTileRuntime(record);

            if (!runtime.IsVisible)
                runtime.SetVisible(true);

            EnsureFarTerrainTileRequested(record);
        }

        foreach (ChunkCoord coord in activeLastUpdate)
        {
            if (!activeThisUpdate.Contains(coord))
            {
                if (loadedChunks.TryGetValue(coord, out ChunkRuntime runtime))
                {
                    runtime.DestroyRuntime();
                    loadedChunks.Remove(coord);
                }
            }
        }

        foreach (ChunkCoord farTileCoord in activeFarTilesLastUpdate)
        {
            if (!activeFarTilesThisUpdate.Contains(farTileCoord))
            {
                if (loadedFarTerrainTiles.TryGetValue(farTileCoord, out FarTerrainTileRuntime runtime))
                {
                    runtime.DestroyRuntime();
                    loadedFarTerrainTiles.Remove(farTileCoord);
                }
            }
        }

        var temp = activeLastUpdate;
        activeLastUpdate = activeThisUpdate;
        activeThisUpdate = temp;

        temp = activeFarTilesLastUpdate;
        activeFarTilesLastUpdate = activeFarTilesThisUpdate;
        activeFarTilesThisUpdate = temp;
    }

    private void UpdateVisibleChunkContent(ChunkCoord viewerCoord)
    {
        int sqrColliderRadius = colliderDistance * colliderDistance;

        frustumVisibleCoords.Clear();

        if (viewerCamera != null)
        {
            GeometryUtility.CalculateFrustumPlanes(viewerCamera, frustumPlanes);
        }

        foreach (ChunkCoord coord in orderedActiveCoords)
        {
            if (!loadedChunks.TryGetValue(coord, out ChunkRuntime runtime))
                continue;

            if (!chunkRecords.TryGetValue(coord, out ChunkRecord record))
                continue;

            int dx = coord.x - viewerCoord.x;
            int dz = coord.z - viewerCoord.z;
            int sqrDistance = dx * dx + dz * dz;
            bool useFarTerrain = ShouldUseFarTerrain(viewerCoord, coord);

            if (useFarTerrain)
            {
                EnsureFarTerrainRequested(record);
                TryApplyFarTerrain(record, runtime);
            }
            else
            {
                EnsureTerrainDataRequested(record);

                int lod = ChunkRingLODPolicy.GetLOD(viewerCoord, coord);

                EnsureLODMeshRequested(record, lod);
                TryApplyLODMesh(record, runtime, lod);

                if (!record.HasTerrainData)
                    TryApplyFarTerrain(record, runtime);
            }

            bool colliderDesired = sqrDistance <= sqrColliderRadius;
            record.ColliderDesired = colliderDesired;

            if (colliderDesired && !useFarTerrain)
            {
                EnsureColliderRequested(record);
                TryApplyCollider(record, runtime);
            }
            else if (runtime.HasCollider())
            {
                runtime.RemoveCollider();
                record.ClearColliderMesh();
            }

            if (!runtime.IsVisible)
                runtime.SetVisible(true);

            bool renderVisible = viewerCamera == null || IsChunkInFrustum(coord);
            runtime.SetRenderVisible(renderVisible);

            if (renderVisible)
            {
                frustumVisibleCoords.Add(coord);
            }
        }

        UpdateVisibleFarTerrainTiles(viewerCoord);
    }

    private void UpdateVisibleFarTerrainTiles(ChunkCoord viewerCoord)
    {
        foreach (ChunkCoord farTileCoord in orderedActiveFarTileCoords)
        {
            if (!farTerrainTileRecords.TryGetValue(farTileCoord, out FarTerrainTileRecord record))
                continue;

            if (!loadedFarTerrainTiles.TryGetValue(farTileCoord, out FarTerrainTileRuntime runtime))
                continue;

            EnsureFarTerrainTileRequested(record);
            TryApplyFarTerrainTile(record, runtime);

            if (!runtime.IsVisible)
                runtime.SetVisible(true);

            bool renderVisible = viewerCamera == null || IsFarTerrainTileInFrustum(farTileCoord);
            runtime.SetRenderVisible(renderVisible);
        }
    }

    private bool IsChunkInFrustum(ChunkCoord coord)
    {
        Bounds bounds = GetChunkWorldBounds(coord);
        return GeometryUtility.TestPlanesAABB(frustumPlanes, bounds);
    }

    private bool IsFarTerrainTileInFrustum(ChunkCoord farTileCoord)
    {
        Bounds bounds = GetFarTerrainTileWorldBounds(farTileCoord);
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

    private Bounds GetFarTerrainTileWorldBounds(ChunkCoord farTileCoord)
    {
        float tileWorldSize = chunkSize * farTerrainMacroTileSize * worldScale;

        float centerX = farTileCoord.x * tileWorldSize + tileWorldSize * 0.5f;
        float centerZ = farTileCoord.z * tileWorldSize + tileWorldSize * 0.5f;

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

    public ChunkRecord GetChunkRecord(ChunkCoord coord)
    {
        chunkRecords.TryGetValue(coord, out ChunkRecord record);
        return record;
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
            runtime = new ChunkRuntime(record, chunkSize, worldScale, chunkParent, terrainMaterial, waterMaterial);
            loadedChunks.Add(coord, runtime);
        }

        return runtime;
    }

    private FarTerrainTileRecord GetOrCreateFarTerrainTileRecord(ChunkCoord tileCoord)
    {
        if (!farTerrainTileRecords.TryGetValue(tileCoord, out FarTerrainTileRecord record))
        {
            record = new FarTerrainTileRecord(tileCoord);
            farTerrainTileRecords.Add(tileCoord, record);
        }

        return record;
    }

    private FarTerrainTileRuntime GetOrCreateFarTerrainTileRuntime(FarTerrainTileRecord record)
    {
        ChunkCoord tileCoord = record.TileCoord;

        if (!loadedFarTerrainTiles.TryGetValue(tileCoord, out FarTerrainTileRuntime runtime))
        {
            runtime = new FarTerrainTileRuntime(
                record,
                chunkSize * farTerrainMacroTileSize,
                worldScale,
                chunkParent,
                terrainMaterial);
            loadedFarTerrainTiles.Add(tileCoord, runtime);
        }

        return runtime;
    }

    private void EnsureTerrainVisualRequested(ChunkRecord record, ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        if (ShouldUseFarTerrain(viewerCoord, targetCoord))
        {
            EnsureFarTerrainRequested(record);
        }
        else
        {
            EnsureTerrainDataRequested(record);
        }
    }

    private bool ShouldUseFarTerrain(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        if (!enableFarTerrain)
            return false;

        int ring = GetChunkRingDistance(viewerCoord, targetCoord);
        return ring >= farTerrainStartRing;
    }

    private bool ShouldUseMacroFarTerrain(
        ChunkCoord viewerCoord,
        ChunkCoord targetCoord,
        out ChunkCoord farTileCoord)
    {
        farTileCoord = default;

        if (!ShouldUseFarTerrain(viewerCoord, targetCoord))
            return false;

        if (farTerrainMacroTileSize <= 1)
        {
            farTileCoord = targetCoord;
            return true;
        }

        farTileCoord = GetFarTerrainTileCoord(targetCoord);
        return IsFarTerrainTileFullyFar(viewerCoord, farTileCoord);
    }

    private ChunkCoord GetFarTerrainTileCoord(ChunkCoord chunkCoord)
    {
        return new ChunkCoord(
            FloorDiv(chunkCoord.x, farTerrainMacroTileSize),
            FloorDiv(chunkCoord.z, farTerrainMacroTileSize));
    }

    private bool IsFarTerrainTileFullyFar(ChunkCoord viewerCoord, ChunkCoord farTileCoord)
    {
        int originX = farTileCoord.x * farTerrainMacroTileSize;
        int originZ = farTileCoord.z * farTerrainMacroTileSize;
        int maxX = originX + farTerrainMacroTileSize - 1;
        int maxZ = originZ + farTerrainMacroTileSize - 1;

        int closestX = Mathf.Clamp(viewerCoord.x, originX, maxX);
        int closestZ = Mathf.Clamp(viewerCoord.z, originZ, maxZ);
        int minRing = Mathf.Max(
            Mathf.Abs(closestX - viewerCoord.x),
            Mathf.Abs(closestZ - viewerCoord.z));

        return minRing >= farTerrainStartRing;
    }

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;

        if (remainder != 0 && ((remainder > 0) != (divisor > 0)))
            quotient--;

        return quotient;
    }

    private int GetChunkRingDistance(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        int dx = Mathf.Abs(targetCoord.x - viewerCoord.x);
        int dz = Mathf.Abs(targetCoord.z - viewerCoord.z);
        return Mathf.Max(dx, dz);
    }

    private void EnsureFarTerrainRequested(ChunkRecord record)
    {
        if (record.HasFarTerrain)
            return;

        if (record.IsFarTerrainRequestInFlight)
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
            farTerrainSkirtDepth
        );

        if (!submitted)
        {
            record.CancelFarTerrainRequest(requestVersion);
        }
    }

    private void EnsureFarTerrainTileRequested(FarTerrainTileRecord record)
    {
        if (record.HasTerrain)
            return;

        if (record.IsRequestInFlight)
            return;

        int requestVersion = record.BeginRequest();
        int tileChunkSize = chunkSize * farTerrainMacroTileSize;
        int tileHeightGridResolution = ScaleFarTerrainResolutionForMacroTile(farTerrainHeightGridResolution);
        int tileControlMapResolution = ScaleFarTerrainResolutionForMacroTile(farTerrainControlMapResolution);

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
            true);

        if (!submitted)
            record.CancelRequest(requestVersion);
    }

    private int ScaleFarTerrainResolutionForMacroTile(int baseResolution)
    {
        if (farTerrainMacroTileSize <= 1)
            return baseResolution;

        return Mathf.Max(2, (baseResolution - 1) * farTerrainMacroTileSize + 1);
    }

    private void EnsureTerrainDataRequested(ChunkRecord record)
    {
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
            erosionStrength,
            worldFeatureGenerationSettings
        );

        if (!submitted)
        {
            record.CancelTerrainDataRequest(requestVersion);
        }
    }

    private static WorldFeatureGenerationSettings BuildWorldFeatureGenerationSettings(TreeSettings treeSettings)
    {
        WorldFeatureGenerationSettings settings = WorldFeatureGenerationSettings.Default;

        if (treeSettings == null)
            return settings;

        settings.forestRockPrefabCount =
            treeSettings.forestRockPrefabs != null ? treeSettings.forestRockPrefabs.Length : 0;
        settings.maxForestRocksPerChunk = Mathf.Max(0, treeSettings.maxForestRocksPerChunk);
        settings.forestRockUniformScaleRange = treeSettings.forestRockUniformScaleRange;
        settings.forestRockPitchRange = treeSettings.forestRockPitchRange;

        return settings;
    }

    private void TryApplyFarTerrain(ChunkRecord record, ChunkRuntime runtime)
    {
        if (!record.TryGetFarTerrainMesh(out Mesh terrainMesh))
            return;

        if (runtime.IsShowingLOD(FarTerrainLOD))
            return;

        runtime.SetControlMaps(record.FarTerrainControlMapData);
        runtime.SetMeshes(terrainMesh, null, null, FarTerrainLOD);
    }

    private void TryApplyFarTerrainTile(FarTerrainTileRecord record, FarTerrainTileRuntime runtime)
    {
        if (!record.TryGetTerrainMesh(out Mesh terrainMesh))
            return;

        if (runtime.IsShowingMesh(terrainMesh))
            return;

        runtime.SetControlMaps(record.ControlMapData);
        runtime.SetMesh(terrainMesh);
    }

    private void EnsureColliderRequested(ChunkRecord record)
    {
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
        if (!record.TryGetColliderMesh(out Mesh colliderMesh))
            return;

        if (!runtime.HasCollider())
        {
            runtime.ApplyCollider(colliderMesh);
        }
    }

    private void EnsureLODMeshRequested(ChunkRecord record, int lod)
    {
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
            record.HeightMap,
            record.BiomeMap,
            record.SurfaceTypeMap,
            record.WaterStateMap,
            meshHeightMultiplier,
            stepIncrement,
            worldScale,
            record.RiverMaskMap
        );

        if (!submitted)
            record.CancelMeshRequest(lod, requestVersion);
    }

    private void TryApplyLODMesh(ChunkRecord record, ChunkRuntime runtime, int lod)
    {
        if (!record.TryGetLODTerrainMesh(lod, out Mesh terrainMesh))
            return;

        if (!runtime.IsShowingLOD(lod))
        {
            Mesh lakeMesh = null;
            Mesh riverMesh = null;

            record.TryGetLODLakeMesh(lod, out lakeMesh);
            record.TryGetLODRiverMesh(lod, out riverMesh);

            runtime.SetControlMaps(record.ControlMapData);
            runtime.SetMeshes(terrainMesh, lakeMesh, riverMesh, lod);
        }
    }

    private void ProcessCompletedRequests()
    {
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

        int terrainDataResultsApplied = 0;
        while (CanApplyMoreResults(
                   terrainDataResultsApplied,
                   maxTerrainDataResultsAppliedPerFrame,
                   totalStart,
                   completedRequestApplyBudgetMsPerFrame,
                   categoryStart,
                   terrainDataApplyBudgetMsPerFrame) &&
               terrainRequestManager.TryDequeueTerrainDataResult(out TerrainDataRequestResult terrainResult))
        {
            if (!chunkRecords.TryGetValue(terrainResult.ChunkCoord, out ChunkRecord record))
                continue;

            processedAnyRequest = true;
            terrainDataResultsApplied++;
            long stageStart = TerrainGenerationProfiler.GetTimestamp();
            Texture2D[] controlMaps = CreateControlMapTextures(terrainResult.ControlMapsRawData);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.MainTerrainControlMapTextureCreate,
                stageStart);

            record.TryCompleteTerrainDataRequest(
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
                controlMaps
            );
        }

        int farTerrainResultsApplied = 0;
        categoryStart = TerrainGenerationProfiler.GetTimestamp();
        while (CanApplyMoreResults(
                   farTerrainResultsApplied,
                   maxFarTerrainResultsAppliedPerFrame,
                   totalStart,
                   completedRequestApplyBudgetMsPerFrame,
                   categoryStart,
                   farTerrainApplyBudgetMsPerFrame) &&
               terrainRequestManager.TryDequeueFarTerrainResult(out FarTerrainRequestResult farTerrainResult))
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

            if (farTerrainResult.IsMacroTile &&
                farTerrainTileRecords.TryGetValue(farTerrainResult.ChunkCoord, out FarTerrainTileRecord tileRecord))
            {
                tileRecord.TryCompleteRequest(
                    farTerrainResult.RequestVersion,
                    terrainMesh,
                    controlMaps);
            }
            else if (chunkRecords.TryGetValue(farTerrainResult.ChunkCoord, out ChunkRecord record))
            {
                record.TryCompleteFarTerrainRequest(
                    farTerrainResult.RequestVersion,
                    terrainMesh,
                    controlMaps);
            }
        }

        int lodMeshResultsApplied = 0;
        categoryStart = TerrainGenerationProfiler.GetTimestamp();
        while (CanApplyMoreResults(
                   lodMeshResultsApplied,
                   maxLODMeshResultsAppliedPerFrame,
                   totalStart,
                   completedRequestApplyBudgetMsPerFrame,
                   categoryStart,
                   lodMeshApplyBudgetMsPerFrame) &&
               terrainRequestManager.TryDequeueMeshResult(out MeshRequestResult meshResult))
        {
            if (!chunkRecords.TryGetValue(meshResult.ChunkCoord, out ChunkRecord record))
                continue;

            processedAnyRequest = true;
            lodMeshResultsApplied++;
            long stageStart = TerrainGenerationProfiler.GetTimestamp();
            Mesh terrainMesh = meshResult.TerrainMeshData.CreateMesh();
            TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.MainLODTerrainMeshCreate, stageStart);

            stageStart = TerrainGenerationProfiler.GetTimestamp();
            Mesh lakeMesh = meshResult.LakeMeshData.CreateMesh();
            TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.MainLakeMeshCreate, stageStart);

            stageStart = TerrainGenerationProfiler.GetTimestamp();
            Mesh riverMesh = meshResult.RiverMeshData.CreateMesh();
            TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.MainRiverMeshCreate, stageStart);

            record.TryCompleteMeshRequest(
                meshResult.LOD,
                meshResult.RequestVersion,
                terrainMesh,
                lakeMesh,
                riverMesh
            );
        }

        int colliderResultsApplied = 0;
        categoryStart = TerrainGenerationProfiler.GetTimestamp();
        while (CanApplyMoreResults(
                   colliderResultsApplied,
                   maxColliderResultsAppliedPerFrame,
                   totalStart,
                   completedRequestApplyBudgetMsPerFrame,
                   categoryStart,
                   colliderApplyBudgetMsPerFrame) &&
               terrainRequestManager.TryDequeueColliderResult(out ColliderRequestResult colliderResult))
        {
            if (!chunkRecords.TryGetValue(colliderResult.ChunkCoord, out ChunkRecord record))
                continue;

            processedAnyRequest = true;
            colliderResultsApplied++;
            long stageStart = TerrainGenerationProfiler.GetTimestamp();
            Mesh colliderMesh = colliderResult.ColliderMeshData.CreateMesh();
            TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.MainColliderMeshCreate, stageStart);

            record.TryCompleteColliderRequest(
                colliderResult.RequestVersion,
                colliderMesh
            );
        }

        if (processedAnyRequest)
        {
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.MainProcessCompletedRequestsTotal,
                totalStart);
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

    private int GetFarTerrainTileDistanceSqr(ChunkCoord viewerCoord, ChunkCoord farTileCoord)
    {
        int originX = farTileCoord.x * farTerrainMacroTileSize;
        int originZ = farTileCoord.z * farTerrainMacroTileSize;
        int centerX = originX + farTerrainMacroTileSize / 2;
        int centerZ = originZ + farTerrainMacroTileSize / 2;
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

    private Texture2D[] CreateControlMapTextures(ControlMapPixelData rawData)
    {
        if (rawData == null || rawData.Maps == null || rawData.Maps.Length == 0)
            return null;

        Texture2D[] textures = new Texture2D[rawData.Maps.Length];

        for (int i = 0; i < rawData.Maps.Length; i++)
        {
            Texture2D tex = new Texture2D(rawData.Width, rawData.Height, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels32(rawData.Maps[i]);
            tex.Apply(false, false);
            textures[i] = tex;
        }

        return textures;
    }
}
