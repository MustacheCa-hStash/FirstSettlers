using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using Unity.Profiling;
using UnityEngine;

public class TerrainRequestManager : System.IDisposable
{
    private static readonly ProfilerMarker TerrainDataWorkerMarker = new ProfilerMarker("FS.Streaming.Worker.TerrainDataRequest");
    private static readonly ProfilerMarker FarTerrainWorkerMarker = new ProfilerMarker("FS.Streaming.Worker.FarTerrainRequest");
    private static readonly ProfilerMarker LodMeshWorkerMarker = new ProfilerMarker("FS.Streaming.Worker.LODMeshRequest");
    private static readonly ProfilerMarker ColliderWorkerMarker = new ProfilerMarker("FS.Streaming.Worker.ColliderRequest");

    private readonly Queue<TerrainDataRequestResult> completedTerrainDataResults = new Queue<TerrainDataRequestResult>();
    private readonly Queue<FarTerrainRequestResult> completedFarTerrainResults = new Queue<FarTerrainRequestResult>();
    private readonly Queue<MeshRequestResult> completedMeshResults = new Queue<MeshRequestResult>();
    private readonly Queue<ColliderRequestResult> completedColliderResults = new();
    private readonly Queue<TerrainRequestFailure> failedRequests = new();
    private readonly object failuresLock = new();

    private readonly object terrainDataResultsLock = new object();
    private volatile bool disposed;
    private readonly object farTerrainResultsLock = new object();
    private readonly object meshResultsLock = new object();
    private readonly object colliderResultsLock = new();

    private static int activeTerrainDataJobs;
    private static int activeFarTerrainJobs;
    private static int activeMeshJobs;
    private static int activeColliderJobs;

    private readonly int maxActiveTerrainDataJobs;
    private readonly int maxActiveFarTerrainJobs;
    private readonly int maxActiveMeshJobs;
    private readonly int maxActiveColliderJobs;
    private readonly TerrainWaterSettings waterSettings;
    private readonly float mountainHorizontalScale;
    private readonly WorldErosionSettings erosion;
    private readonly float mountainSnowRenderCoverageGamma;
    private readonly TerrainControlMapScratchPool controlMapScratchPool;

    public int CompletedTerrainDataResultCount
    {
        get
        {
            lock (terrainDataResultsLock)
                return completedTerrainDataResults.Count;
        }
    }

    public int CompletedFarTerrainResultCount
    {
        get
        {
            lock (farTerrainResultsLock)
                return completedFarTerrainResults.Count;
        }
    }

    public int CompletedMeshResultCount
    {
        get
        {
            lock (meshResultsLock)
                return completedMeshResults.Count;
        }
    }

    public int CompletedColliderResultCount
    {
        get
        {
            lock (colliderResultsLock)
                return completedColliderResults.Count;
        }
    }

    public int ActiveTerrainDataJobCount => Interlocked.CompareExchange(ref activeTerrainDataJobs, 0, 0);
    public int ActiveFarTerrainJobCount => Interlocked.CompareExchange(ref activeFarTerrainJobs, 0, 0);
    public int ActiveMeshJobCount => Interlocked.CompareExchange(ref activeMeshJobs, 0, 0);
    public int ActiveColliderJobCount => Interlocked.CompareExchange(ref activeColliderJobs, 0, 0);

    /// <summary>Sculpt extraction shares the ordinary mesh allowance; no independent worker pool.</summary>
    public bool TryRunSculpt(System.Action work)
    {
        if (work == null) throw new System.ArgumentNullException(nameof(work));
        if (disposed || !TryAcquire(ref activeMeshJobs,maxActiveMeshJobs)) return false;
        return QueueWorker(_ => { try { work(); } finally { Interlocked.Decrement(ref activeMeshJobs); } },ref activeMeshJobs);
    }
    public System.IDisposable TryAcquireSculptCooking()
    {
        if(disposed||!TryAcquire(ref activeColliderJobs,maxActiveColliderJobs))return null;
        return new SculptCookingLease();
    }
    private sealed class SculptCookingLease : System.IDisposable
    {
        private int active=1;
        public void Dispose(){if(Interlocked.Exchange(ref active,0)==1)Interlocked.Decrement(ref activeColliderJobs);}
    }
    private static bool TryAcquire(ref int count,int limit)
    {
        int seen=Interlocked.CompareExchange(ref count,0,0);
        while(seen<limit)
        {int actual=Interlocked.CompareExchange(ref count,seen+1,seen);if(actual==seen)return true;seen=actual;}
        return false;
    }

    public TerrainRequestManager(
        int maxActiveTerrainDataJobs,
        int maxActiveFarTerrainJobs,
        int maxActiveMeshJobs,
        int maxActiveColliderJobs,
        TerrainWaterSettings waterSettings,
        float mountainHorizontalScale = 1f,
        float mountainSnowRenderCoverageGamma = MountainSnow.DefaultRenderCoverageGamma, WorldErosionSettings erosion = default)
    {
        this.waterSettings = waterSettings;
        this.erosion = erosion.Sanitized();
        this.mountainHorizontalScale = HeightMapGenerator.SanitizeMountainHorizontalScale(mountainHorizontalScale);
        this.mountainSnowRenderCoverageGamma = MountainSnow.SanitizeRenderCoverageGamma(mountainSnowRenderCoverageGamma);
        this.maxActiveTerrainDataJobs = Mathf.Max(1, maxActiveTerrainDataJobs);
        this.maxActiveFarTerrainJobs = Mathf.Max(1, maxActiveFarTerrainJobs);
        this.maxActiveMeshJobs = Mathf.Max(1, maxActiveMeshJobs);
        this.maxActiveColliderJobs = Mathf.Max(1, maxActiveColliderJobs);
        controlMapScratchPool = new TerrainControlMapScratchPool(this.maxActiveTerrainDataJobs);
    }

    public bool RequestTerrainData(
        ChunkCoord chunkCoord,
        int requestVersion,
        int chunkSize,
        int seed,
        float sampleScale,
        int octaves,
        float persistence,
        float lacunarity,
        WorldFeatureGenerationSettings worldFeatureGenerationSettings,
        float meshHeightMultiplier = 200f)
    {
        if (disposed || CompletedTerrainDataResultCount >= Mathf.Max(2, maxActiveTerrainDataJobs * 2) ||
            Interlocked.CompareExchange(ref activeTerrainDataJobs, 0, 0) >= maxActiveTerrainDataJobs)
            return false;

        Interlocked.Increment(ref activeTerrainDataJobs);

        return QueueWorker(_ =>
        {
            NativeArray<float> generatedHeights = default;
            NativeArray<float> generatedSlopes = default;
            NativeArray<float> generatedRiverMasks = default;
            NativeArray<float> generatedMoistures = default;
            NativeArray<float> generatedTemperatures = default;
            try
            {
                using var terrainDataWorkerScope = TerrainDataWorkerMarker.Auto();
                long totalStart = TerrainGenerationProfiler.GetTimestamp();
                long stageStart = TerrainGenerationProfiler.GetTimestamp();
                HeightFieldResult heightField = HeightMapGenerator.GenerateTerrainHeightFieldForRequest(
                    chunkSize,
                    seed,
                    sampleScale,
                    chunkCoord,
                    waterSettings.WaterLevel,
                    mountainHorizontalScale,
                    meshHeightMultiplier, erosion,
                    out generatedHeights, out generatedSlopes, out generatedRiverMasks
                );
                TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.TerrainHeightField, stageStart);

                float[,] finalHeightMap = heightField.HeightMap;
                float[,] slopeMap = heightField.SlopeMap;
                float[,] mountainMaskMap = heightField.MountainMaskMap;
                float[,] riverMaskMap = heightField.RiverMaskMap;

                stageStart = TerrainGenerationProfiler.GetTimestamp();
                float[,] moistureMap = ClimateGenerator.GenerateTerrainMoistureMapForRequest(chunkSize, seed, sampleScale,
                    octaves, persistence, lacunarity, chunkCoord, out generatedMoistures);
                TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.TerrainClimateMoisture, stageStart);

                stageStart = TerrainGenerationProfiler.GetTimestamp();
                float[,] temperatureMap = ClimateGenerator.GenerateTerrainTemperatureMapForRequest(chunkSize, seed,
                    sampleScale, octaves, persistence, lacunarity, chunkCoord, out generatedTemperatures);
                TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.TerrainClimateTemperature, stageStart);

                NativeArray<float> nativeHeights = default;
                NativeArray<float> nativeMoistures = default;
                NativeArray<float> nativeTemperatures = default;
                NativeArray<float> nativeSlopes = default;
                NativeArray<float> nativeMountainMasks = default;
                NativeArray<float> nativeRiverMasks = default;
                NativeArray<BiomeType> nativeBiomes = default;
                NativeArray<SurfaceType> nativeSurfaces = default;
                NativeArray<WaterState> nativeWaterStates = default;
                NativeArray<float> nativeCanopyDensities = default;
                NativeArray<float> nativeLocalMoistureAdjustments = default;
                NativeArray<float> nativeClearings = default;
                NativeArray<float> nativeRockInfluences = default;
                NativeArray<float> nativeTreeLitterBalances = default;
                NativeArray<float> nativeDampShades = default;
                NativeArray<float> nativeOrganicFloorIntents = default;
                NativeArray<GroundCoverType> nativeGroundCovers = default;
                ChunkRecord.NativeTerrainData retainedNativeData = null;

                try
                {
                    int unusedMapWidth;
                    int unusedMapHeight;
                    int mapWidth = finalHeightMap.GetLength(0);
                    int mapHeight = finalHeightMap.GetLength(1);
                    nativeHeights = generatedHeights; generatedHeights = default;
                    nativeMoistures = generatedMoistures; generatedMoistures = default;
                    nativeTemperatures = generatedTemperatures; generatedTemperatures = default;
                    nativeSlopes = generatedSlopes; generatedSlopes = default;
                    // A ThreadPool request has no four-frame deadline. Scratch buffers remain
                    // explicitly owned here and are released after their last consuming job.
                    nativeMountainMasks = TerrainMapNativeUtility.CopyFloatMapToNative(mountainMaskMap, Allocator.Persistent, out unusedMapWidth, out unusedMapHeight);
                    nativeRiverMasks = generatedRiverMasks; generatedRiverMasks = default;

                    stageStart = TerrainGenerationProfiler.GetTimestamp();
                    BiomeType[,] biomeMap = BiomeMapGenerator.GenerateBiomeMap(
                        nativeHeights,
                        nativeMoistures,
                        nativeTemperatures,
                        nativeSlopes,
                        nativeMountainMasks,
                        nativeRiverMasks,
                        mapWidth,
                        mapHeight,
                        waterSettings.WaterLevel,
                        Allocator.Persistent,
                        out nativeBiomes);
                    TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.TerrainBiomeMap, stageStart);
                    nativeMountainMasks.Dispose();

                    stageStart = TerrainGenerationProfiler.GetTimestamp();
                    SurfaceType[,] surfaceTypeMap = SurfaceMapGenerator.GenerateSurfaceTypeMap(
                        nativeHeights,
                        nativeSlopes,
                        nativeRiverMasks,
                        nativeBiomes,
                        mapWidth,
                        mapHeight,
                        waterSettings.WaterLevel,
                        Allocator.Persistent,
                        out nativeSurfaces);
                    TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.TerrainSurfaceMap, stageStart);

                    stageStart = TerrainGenerationProfiler.GetTimestamp();
                    WaterState[,] waterStateMap = WaterStateMapGenerator.GenerateWaterStateMap(
                        nativeHeights,
                        nativeRiverMasks,
                        mapWidth,
                        mapHeight,
                        waterSettings.WaterLevel,
                        Allocator.Persistent,
                        out nativeWaterStates);
                    TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.TerrainWaterStateMap, stageStart);

                    stageStart = TerrainGenerationProfiler.GetTimestamp();
                    using var treeHabitats = worldFeatureGenerationSettings.treeDistribution.Enabled
                        ? new TreeHabitatSampler(seed,sampleScale,octaves,persistence,lacunarity,meshHeightMultiplier,
                            waterSettings.WaterLevel,mountainHorizontalScale,erosion) : null;
                    WorldFeaturePlan worldFeaturePlan = WorldFeaturePlanGenerator.Generate(
                        chunkCoord,
                        chunkSize,
                        seed,
                        biomeMap,
                        surfaceTypeMap,
                        moistureMap,
                        temperatureMap,
                        slopeMap,
                        riverMaskMap,
                        worldFeatureGenerationSettings,
                        finalHeightMap,
                        waterSettings.WaterLevel, mountainMaskMap,
                        treeHabitatSampler: treeHabitats == null ? null : treeHabitats.Sample);
                    TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.TerrainWorldFeaturePlan, stageStart);

                    nativeCanopyDensities = TerrainMapNativeUtility.CopyFloatMapToNative(worldFeaturePlan.CanopyDensityMap, Allocator.Persistent, out unusedMapWidth, out unusedMapHeight);
                    nativeLocalMoistureAdjustments = TerrainMapNativeUtility.CopyFloatMapToNative(worldFeaturePlan.LocalMoistureAdjustmentMap, Allocator.Persistent, out unusedMapWidth, out unusedMapHeight);
                    nativeClearings = TerrainMapNativeUtility.CopyFloatMapToNative(worldFeaturePlan.ForestStructure.ClearingMap, Allocator.Persistent, out unusedMapWidth, out unusedMapHeight);
                    nativeRockInfluences = TerrainMapNativeUtility.CopyFloatMapToNative(worldFeaturePlan.ForestStructure.RockInfluenceMap, Allocator.Persistent, out unusedMapWidth, out unusedMapHeight);
                    nativeTreeLitterBalances = TerrainMapNativeUtility.CopyFloatMapToNative(worldFeaturePlan.ForestStructure.TreeLitterBalanceMap, Allocator.Persistent, out unusedMapWidth, out unusedMapHeight);
                    nativeDampShades = TerrainMapNativeUtility.CopyFloatMapToNative(worldFeaturePlan.ForestStructure.DampShadeMap, Allocator.Persistent, out unusedMapWidth, out unusedMapHeight);
                    nativeOrganicFloorIntents = TerrainMapNativeUtility.CopyFloatMapToNative(worldFeaturePlan.ForestStructure.OrganicFloorIntentMap, Allocator.Persistent, out unusedMapWidth, out unusedMapHeight);

                    stageStart = TerrainGenerationProfiler.GetTimestamp();
                    GroundCoverType[,] groundCoverMap = GroundCoverMapGenerator.GenerateGroundCoverMap(
                        nativeBiomes,
                        nativeSurfaces,
                        nativeMoistures,
                        nativeLocalMoistureAdjustments,
                        nativeSlopes,
                        nativeRiverMasks,
                        nativeCanopyDensities,
                        nativeClearings,
                        nativeRockInfluences,
                        nativeTreeLitterBalances,
                        nativeDampShades,
                        nativeOrganicFloorIntents,
                        mapWidth,
                        mapHeight,
                        chunkSize,
                        seed,
                        chunkCoord,
                        Allocator.Persistent,
                        out nativeGroundCovers);
                    TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.TerrainGroundCoverMap, stageStart);
                    nativeCanopyDensities.Dispose();
                    nativeLocalMoistureAdjustments.Dispose();
                    nativeClearings.Dispose();
                    nativeRockInfluences.Dispose();
                    nativeTreeLitterBalances.Dispose();
                    nativeDampShades.Dispose();
                    nativeOrganicFloorIntents.Dispose();

                    stageStart = TerrainGenerationProfiler.GetTimestamp();
                    var mountainSnow = MountainSnow.Generate(finalHeightMap, mountainMaskMap, riverMaskMap,
                        temperatureMap, moistureMap, chunkSize, chunkCoord, seed, sampleScale, waterSettings, mountainHorizontalScale, erosion);
                    ControlMapPixelData controlMapRawData = TerrainControlMapBuilder.BuildRaw(
                        surfaceTypeMap,
                        groundCoverMap,
                        mountainSnow,
                        mountainSnowRenderCoverageGamma,
                        worldFeaturePlan.ForestStructure, biomeMap, worldFeaturePlan.ForestMembershipMap, controlMapScratchPool);
                    TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.TerrainControlMapBuild, stageStart);

                    retainedNativeData = new ChunkRecord.NativeTerrainData(
                        finalHeightMap, slopeMap, biomeMap, surfaceTypeMap, waterStateMap, groundCoverMap, riverMaskMap,
                        nativeHeights, nativeSlopes, nativeBiomes, nativeSurfaces, nativeWaterStates,
                        nativeGroundCovers, nativeRiverMasks, worldFeaturePlan.ForestStructure.FloorEcologyMap, worldFeaturePlan.ForestMembershipMap);
                    nativeHeights = default;
                    nativeSlopes = default;
                    nativeBiomes = default;
                    nativeSurfaces = default;
                    nativeWaterStates = default;
                    nativeGroundCovers = default;
                    nativeRiverMasks = default;

                    TerrainDataRequestResult result = new TerrainDataRequestResult(chunkCoord, requestVersion,
                        finalHeightMap, slopeMap, moistureMap, temperatureMap, biomeMap,
                        surfaceTypeMap, waterStateMap, groundCoverMap, worldFeaturePlan, riverMaskMap,
                        controlMapRawData, retainedNativeData);
                    TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.TerrainDataTotal, totalStart);

                    lock (terrainDataResultsLock)
                    {
                        if (disposed) result.Dispose();
                        else completedTerrainDataResults.Enqueue(result);
                    }
                    retainedNativeData = null;
                }
                finally
                {
                    retainedNativeData?.Dispose();
                    if (nativeHeights.IsCreated) nativeHeights.Dispose();
                    if (nativeMoistures.IsCreated) nativeMoistures.Dispose();
                    if (nativeTemperatures.IsCreated) nativeTemperatures.Dispose();
                    if (nativeSlopes.IsCreated) nativeSlopes.Dispose();
                    if (nativeMountainMasks.IsCreated) nativeMountainMasks.Dispose();
                    if (nativeRiverMasks.IsCreated) nativeRiverMasks.Dispose();
                    if (nativeBiomes.IsCreated) nativeBiomes.Dispose();
                    if (nativeSurfaces.IsCreated) nativeSurfaces.Dispose();
                    if (nativeWaterStates.IsCreated) nativeWaterStates.Dispose();
                    if (nativeCanopyDensities.IsCreated) nativeCanopyDensities.Dispose();
                    if (nativeLocalMoistureAdjustments.IsCreated) nativeLocalMoistureAdjustments.Dispose();
                    if (nativeClearings.IsCreated) nativeClearings.Dispose();
                    if (nativeRockInfluences.IsCreated) nativeRockInfluences.Dispose();
                    if (nativeTreeLitterBalances.IsCreated) nativeTreeLitterBalances.Dispose();
                    if (nativeDampShades.IsCreated) nativeDampShades.Dispose();
                    if (nativeOrganicFloorIntents.IsCreated) nativeOrganicFloorIntents.Dispose();
                    if (nativeGroundCovers.IsCreated) nativeGroundCovers.Dispose();
                }
            }
            catch (ThreadAbortException)
            {
            }
            catch (System.Exception ex)
            {
                ReportFailure(TerrainRequestKind.Data, chunkCoord, requestVersion);
                Debug.LogError($"TerrainData request failed for chunk={chunkCoord}, version={requestVersion}\n{ex}");
            }
            finally
            {
                if (generatedHeights.IsCreated) generatedHeights.Dispose();
                if (generatedSlopes.IsCreated) generatedSlopes.Dispose();
                if (generatedRiverMasks.IsCreated) generatedRiverMasks.Dispose();
                if (generatedMoistures.IsCreated) generatedMoistures.Dispose();
                if (generatedTemperatures.IsCreated) generatedTemperatures.Dispose();
                Interlocked.Decrement(ref activeTerrainDataJobs);
            }
        }, ref activeTerrainDataJobs);
    }

    public bool RequestFarTerrainData(
        ChunkCoord chunkCoord,
        int requestVersion,
        int chunkSize,
        int seed,
        float sampleScale,
        float meshHeightMultiplier,
        float worldScale,
        int heightGridResolution,
        int controlMapResolution,
        float skirtDepth,
        bool isMacroTile = false,
        int patchSizeInChunks = 1,
        int climateOctaves = 3,
        float climatePersistence = 0.5f,
        float climateLacunarity = 2f)
    {
        if (disposed || CompletedFarTerrainResultCount >= Mathf.Max(2, maxActiveFarTerrainJobs * 2) ||
            Interlocked.CompareExchange(ref activeFarTerrainJobs, 0, 0) >= maxActiveFarTerrainJobs)
            return false;

        Interlocked.Increment(ref activeFarTerrainJobs);

        return QueueWorker(_ =>
        {
            try
            {
                using var farTerrainWorkerScope = FarTerrainWorkerMarker.Auto();
                FarTerrainRequestResult result = FarTerrainGenerator.Generate(
                    chunkCoord,
                    requestVersion,
                    chunkSize,
                    seed,
                    sampleScale,
                    meshHeightMultiplier,
                    worldScale,
                    heightGridResolution,
                    controlMapResolution,
                    skirtDepth,
                    waterSettings.WaterLevel,
                    isMacroTile,
                    patchSizeInChunks,
                    mountainHorizontalScale,
                    mountainSnowRenderCoverageGamma,
                    climateOctaves,
                    climatePersistence,
                    climateLacunarity, erosion);

                lock (farTerrainResultsLock)
                {
                    if (!disposed) completedFarTerrainResults.Enqueue(result);
                }
            }
            catch (ThreadAbortException)
            {
            }
            catch (System.Exception ex)
            {
                ReportFailure(TerrainRequestKind.Far, chunkCoord, requestVersion, patchSize: patchSizeInChunks, macro: isMacroTile);
                Debug.LogError($"Far terrain request failed for chunk={chunkCoord}, version={requestVersion}\n{ex}");
            }
            finally
            {
                Interlocked.Decrement(ref activeFarTerrainJobs);
            }
        }, ref activeFarTerrainJobs);
    }

    public bool RequestLODMesh(ChunkCoord chunkCoord, int lod, int requestVersion, ChunkRecord.NativeTerrainData nativeData, float meshHeightMultiplier,
        int stepIncrement, float worldScale)
    {
        if (disposed || CompletedMeshResultCount >= Mathf.Max(2, maxActiveMeshJobs * 2) ||
            Interlocked.CompareExchange(ref activeMeshJobs, 0, 0) >= maxActiveMeshJobs)
            return false;

        stepIncrement = Mathf.Min(stepIncrement, erosion.maxMeshSpacing);
        int meshChunkSize = nativeData.HeightMapWidth - 3;
        while (stepIncrement > 1 && meshChunkSize % stepIncrement != 0) stepIncrement--;
        ChunkRecord.NativeTerrainData.Lease nativeLease = nativeData.AcquireLease();
        if (nativeLease == null)
            return false;

        if(!TryAcquire(ref activeMeshJobs,maxActiveMeshJobs)){nativeLease.Dispose();return false;}

        LODMeshRequestWorkItem workItem = new LODMeshRequestWorkItem(
            this,
            chunkCoord,
            lod,
            requestVersion,
            nativeLease,
            meshHeightMultiplier,
            stepIncrement,
            worldScale);

        try
        {
            if (ThreadPool.UnsafeQueueUserWorkItem(ProcessLODMeshRequest, workItem)) return true;
        }
        catch { nativeLease.Dispose(); Interlocked.Decrement(ref activeMeshJobs); throw; }
        nativeLease.Dispose(); Interlocked.Decrement(ref activeMeshJobs);
        return false;
    }

    private static void ProcessLODMeshRequest(object state)
    {
        LODMeshRequestWorkItem workItem = (LODMeshRequestWorkItem)state;

        try
        {
            using var lodMeshWorkerScope = LodMeshWorkerMarker.Auto();
            long totalStart = TerrainGenerationProfiler.GetTimestamp();
            long stageStart = TerrainGenerationProfiler.GetTimestamp();
            MeshData terrainMeshData = MeshGenerator.GenerateTerrainMesh(
                workItem.ChunkCoord,
                workItem.NativeData,
                workItem.MeshHeightMultiplier,
                workItem.StepIncrement,
                workItem.WorldScale);
            TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.TerrainMeshBuild, stageStart);

            stageStart = TerrainGenerationProfiler.GetTimestamp();
            WaterMeshData waterMeshData = WaterMeshGenerator.GenerateWaterMesh(
                workItem.NativeData.WaterStateMap,
                workItem.NativeData.WaterStateMapWidth,
                workItem.StepIncrement,
                workItem.WorldScale,
                workItem.Manager.waterSettings.SurfaceY);
            TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.WaterMeshBuild, stageStart);

            MeshRequestResult result = new MeshRequestResult(
                workItem.ChunkCoord,
                workItem.LOD,
                workItem.RequestVersion,
                terrainMeshData,
                waterMeshData);
            TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.LODMeshTotal, totalStart);

            lock (workItem.Manager.meshResultsLock)
            {
                if (!workItem.Manager.disposed) workItem.Manager.completedMeshResults.Enqueue(result);
            }
        }
        catch (System.Exception ex)
        {
            workItem.Manager.ReportFailure(TerrainRequestKind.Lod, workItem.ChunkCoord, workItem.RequestVersion, workItem.LOD);
            Debug.LogError(
                $"Mesh request failed for chunk={workItem.ChunkCoord}, lod={workItem.LOD}, version={workItem.RequestVersion}\n{ex}");
        }
        finally
        {
            workItem.NativeLease.Dispose();
            Interlocked.Decrement(ref activeMeshJobs);
        }
    }

    private sealed class LODMeshRequestWorkItem
    {
        public readonly TerrainRequestManager Manager;
        public readonly ChunkCoord ChunkCoord;
        public readonly int LOD;
        public readonly int RequestVersion;
        public readonly ChunkRecord.NativeTerrainData.Lease NativeLease;
        public ChunkRecord.NativeTerrainData NativeData => NativeLease.Data;
        public readonly float MeshHeightMultiplier;
        public readonly int StepIncrement;
        public readonly float WorldScale;

        public LODMeshRequestWorkItem(
            TerrainRequestManager manager,
            ChunkCoord chunkCoord,
            int lod,
            int requestVersion,
            ChunkRecord.NativeTerrainData.Lease nativeLease,
            float meshHeightMultiplier,
            int stepIncrement,
            float worldScale)
        {
            Manager = manager;
            ChunkCoord = chunkCoord;
            LOD = lod;
            RequestVersion = requestVersion;
            NativeLease = nativeLease;
            MeshHeightMultiplier = meshHeightMultiplier;
            StepIncrement = stepIncrement;
            WorldScale = worldScale;
        }
    }

    public bool RequestColliderMesh(
        ChunkCoord chunkCoord,
        int requestVersion,
        float[,] heightMap,
        float meshHeightMultiplier,
        float worldScale)
    {
        if (disposed || CompletedColliderResultCount >= Mathf.Max(2, maxActiveColliderJobs * 2) ||
            !TryAcquire(ref activeColliderJobs,maxActiveColliderJobs))
            return false;

        return QueueWorker(_ =>
        {
            try
            {
                using var colliderWorkerScope = ColliderWorkerMarker.Auto();
                // ⭐ fixed collider step, LOD of 3
                const int colliderStep = 8;

                long stageStart = TerrainGenerationProfiler.GetTimestamp();
                MeshData colliderMeshData =
                    ColliderMeshGenerator.GenerateColliderMesh(
                        heightMap,
                        meshHeightMultiplier,
                        colliderStep,
                        worldScale);
                TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.ColliderMeshBuild, stageStart);

                ColliderRequestResult result =
                    new ColliderRequestResult(
                        chunkCoord,
                        requestVersion,
                        colliderMeshData);

                lock (colliderResultsLock)
                {
                    if (!disposed) completedColliderResults.Enqueue(result);
                }
            }
            catch (System.Exception ex)
            {
                ReportFailure(TerrainRequestKind.Collider, chunkCoord, requestVersion);
                Debug.LogError($"Collider request failed for chunk={chunkCoord}, version={requestVersion}\n{ex}");
            }
            finally
            {
                Interlocked.Decrement(ref activeColliderJobs);
            }
        }, ref activeColliderJobs);
    }

    public void WaitForActiveRequestsToFinish(int timeoutMilliseconds = 2000)
    {
        int waitedMilliseconds = 0;
        while (waitedMilliseconds < timeoutMilliseconds &&
               (ActiveTerrainDataJobCount > 0 || ActiveFarTerrainJobCount > 0 || ActiveMeshJobCount > 0 || ActiveColliderJobCount > 0))
        {
            Thread.Sleep(1);
            waitedMilliseconds++;
        }
    }


    // Borrowed queue heads for main-thread publication admission only. Peeking transfers no ownership;
    // deferred results stay in the existing queues for retirement/shutdown to dispose normally.
    internal bool TryPeekTerrainDataResult(out TerrainDataRequestResult result)
    {
        lock (terrainDataResultsLock)
        {
            result = completedTerrainDataResults.Count > 0 ? completedTerrainDataResults.Peek() : null;
            return result != null;
        }
    }

    internal bool TryPeekFarTerrainResult(out FarTerrainRequestResult result)
    {
        lock (farTerrainResultsLock)
        {
            result = completedFarTerrainResults.Count > 0 ? completedFarTerrainResults.Peek() : null;
            return result != null;
        }
    }

    public bool TryDequeueTerrainDataResult(out TerrainDataRequestResult result)
    {
        lock (terrainDataResultsLock)
        {
            if (completedTerrainDataResults.Count > 0)
            {
                result = completedTerrainDataResults.Dequeue();
                return true;
            }
        }

        result = null;
        return false;
    }

    public void Dispose()
    {
        lock (terrainDataResultsLock)
        {
            if (disposed) return;
            disposed = true;
            controlMapScratchPool.Dispose();
            while (completedTerrainDataResults.Count > 0)
                completedTerrainDataResults.Dequeue().Dispose();
        }
        lock (farTerrainResultsLock) completedFarTerrainResults.Clear();
        lock (meshResultsLock) completedMeshResults.Clear();
        lock (colliderResultsLock) completedColliderResults.Clear();
        lock (failuresLock) failedRequests.Clear();
    }

    internal void DiscardChunkResults(ChunkCoord coord)
    {
        lock (terrainDataResultsLock) Discard(completedTerrainDataResults, r => r.ChunkCoord == coord, r => r.Dispose());
        lock (farTerrainResultsLock) Discard(completedFarTerrainResults, r => !r.IsMacroTile && r.ChunkCoord == coord);
        lock (meshResultsLock) Discard(completedMeshResults, r => r.ChunkCoord == coord);
        lock (colliderResultsLock) Discard(completedColliderResults, r => r.ChunkCoord == coord);
        lock (failuresLock) Discard(failedRequests, r => !r.Macro && r.Coord == coord);
    }

    internal void DiscardFarResults(FarTerrainPatchKey patch)
    {
        lock (farTerrainResultsLock) Discard(completedFarTerrainResults,
            r => r.IsMacroTile && r.ChunkCoord == patch.Origin && r.PatchSizeInChunks == patch.SizeInChunks);
        lock (failuresLock) Discard(failedRequests, r => r.Macro && r.Coord == patch.Origin && r.PatchSize == patch.SizeInChunks);
    }

    private static bool QueueWorker(WaitCallback callback, ref int activeCount)
    {
        try { if (ThreadPool.QueueUserWorkItem(callback)) return true; }
        catch { Interlocked.Decrement(ref activeCount); throw; }
        Interlocked.Decrement(ref activeCount); return false;
    }

    private void ReportFailure(TerrainRequestKind kind, ChunkCoord coord, int version, int lod = 0, int patchSize = 1, bool macro = false)
    {
        lock (failuresLock)
            if (!disposed) failedRequests.Enqueue(new TerrainRequestFailure(kind, coord, version, lod, patchSize, macro));
    }

    internal bool TryDequeueFailure(out TerrainRequestFailure failure)
    {
        lock (failuresLock) return failedRequests.TryDequeue(out failure);
    }

    private static void Discard<T>(Queue<T> queue, System.Predicate<T> matches, System.Action<T> release = null)
    {
        int count = queue.Count;
        for (int i = 0; i < count; i++)
        {
            var result = queue.Dequeue();
            if (matches(result)) release?.Invoke(result);
            else queue.Enqueue(result);
        }
    }

    public bool TryDequeueFarTerrainResult(out FarTerrainRequestResult result)
    {
        lock (farTerrainResultsLock)
        {
            if (completedFarTerrainResults.Count > 0)
            {
                result = completedFarTerrainResults.Dequeue();
                return true;
            }
        }

        result = null;
        return false;
    }

    public bool TryDequeueMeshResult(out MeshRequestResult result)
    {
        lock (meshResultsLock)
        {
            if (completedMeshResults.Count > 0)
            {
                result = completedMeshResults.Dequeue();
                return true;
            }
        }

        result = null;
        return false;
    }

    public bool TryDequeueColliderResult(out ColliderRequestResult result)
    {
        lock (colliderResultsLock)
        {
            if (completedColliderResults.Count > 0)
            {
                result = completedColliderResults.Dequeue();
                return true;
            }
        }

        result = null;
        return false;
    }
}

public static class TerrainMapNativeUtility
{
    public static NativeArray<float> CopyFloatMapToNative(float[,] source, Allocator allocator, out int width, out int height)
    {
        width = source.GetLength(0);
        height = source.GetLength(1);
        NativeArray<float> result =
            new NativeArray<float>(width * height, allocator, NativeArrayOptions.UninitializedMemory);

        for (int x = 0; x < width; x++)
        {
            int rowOffset = x * height;
            for (int z = 0; z < height; z++)
                result[rowOffset + z] = source[x, z];
        }

        return result;
    }

    public static NativeArray<T> CopyMapToNative<T>(T[,] source, Allocator allocator, out int width, out int height)
        where T : unmanaged
    {
        width = source.GetLength(0);
        height = source.GetLength(1);
        NativeArray<T> result =
            new NativeArray<T>(width * height, allocator, NativeArrayOptions.UninitializedMemory);

        for (int x = 0; x < width; x++)
        {
            int rowOffset = x * height;
            for (int z = 0; z < height; z++)
                result[rowOffset + z] = source[x, z];
        }

        return result;
    }

    public static void CopyNativeToFloatMap(NativeArray<float> source, float[,] target)
    {
        int width = target.GetLength(0);
        int height = target.GetLength(1);

        for (int x = 0; x < width; x++)
        {
            int rowOffset = x * height;
            for (int z = 0; z < height; z++)
                target[x, z] = source[rowOffset + z];
        }
    }

    public static void CopyNativeToMap<T>(NativeArray<T> source, T[,] target) where T : unmanaged
    {
        int width = target.GetLength(0);
        int height = target.GetLength(1);

        for (int x = 0; x < width; x++)
        {
            int rowOffset = x * height;
            for (int z = 0; z < height; z++)
                target[x, z] = source[rowOffset + z];
        }
    }
}
