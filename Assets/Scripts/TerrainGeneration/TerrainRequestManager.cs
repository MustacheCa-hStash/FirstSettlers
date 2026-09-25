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

    private readonly object terrainDataResultsLock = new object();
    private bool disposed;
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
        if (Interlocked.CompareExchange(ref activeTerrainDataJobs, 0, 0) >= maxActiveTerrainDataJobs)
            return false;

        Interlocked.Increment(ref activeTerrainDataJobs);

        ThreadPool.QueueUserWorkItem(_ =>
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
                    nativeMountainMasks = TerrainMapNativeUtility.CopyFloatMapToNative(mountainMaskMap, Allocator.TempJob, out unusedMapWidth, out unusedMapHeight);
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
                        waterSettings.WaterLevel);
                    TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.TerrainWorldFeaturePlan, stageStart);

                    nativeCanopyDensities = TerrainMapNativeUtility.CopyFloatMapToNative(worldFeaturePlan.CanopyDensityMap, Allocator.TempJob, out unusedMapWidth, out unusedMapHeight);
                    nativeLocalMoistureAdjustments = TerrainMapNativeUtility.CopyFloatMapToNative(worldFeaturePlan.LocalMoistureAdjustmentMap, Allocator.TempJob, out unusedMapWidth, out unusedMapHeight);
                    nativeClearings = TerrainMapNativeUtility.CopyFloatMapToNative(worldFeaturePlan.ForestStructure.ClearingMap, Allocator.TempJob, out unusedMapWidth, out unusedMapHeight);
                    nativeRockInfluences = TerrainMapNativeUtility.CopyFloatMapToNative(worldFeaturePlan.ForestStructure.RockInfluenceMap, Allocator.TempJob, out unusedMapWidth, out unusedMapHeight);
                    nativeTreeLitterBalances = TerrainMapNativeUtility.CopyFloatMapToNative(worldFeaturePlan.ForestStructure.TreeLitterBalanceMap, Allocator.TempJob, out unusedMapWidth, out unusedMapHeight);
                    nativeDampShades = TerrainMapNativeUtility.CopyFloatMapToNative(worldFeaturePlan.ForestStructure.DampShadeMap, Allocator.TempJob, out unusedMapWidth, out unusedMapHeight);
                    nativeOrganicFloorIntents = TerrainMapNativeUtility.CopyFloatMapToNative(worldFeaturePlan.ForestStructure.OrganicFloorIntentMap, Allocator.TempJob, out unusedMapWidth, out unusedMapHeight);

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

                    stageStart = TerrainGenerationProfiler.GetTimestamp();
                    var mountainSnow = MountainSnow.Generate(finalHeightMap, mountainMaskMap, riverMaskMap,
                        temperatureMap, moistureMap, chunkSize, chunkCoord, seed, sampleScale, waterSettings, mountainHorizontalScale, erosion);
                    ControlMapPixelData controlMapRawData = TerrainControlMapBuilder.BuildRaw(
                        surfaceTypeMap,
                        groundCoverMap,
                        mountainSnow,
                        mountainSnowRenderCoverageGamma);
                    TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.TerrainControlMapBuild, stageStart);

                    retainedNativeData = new ChunkRecord.NativeTerrainData(
                        finalHeightMap, slopeMap, biomeMap, surfaceTypeMap, waterStateMap, groundCoverMap, riverMaskMap,
                        nativeHeights, nativeSlopes, nativeBiomes, nativeSurfaces, nativeWaterStates,
                        nativeGroundCovers, nativeRiverMasks);
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
        });

        return true;
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
        if (Interlocked.CompareExchange(ref activeFarTerrainJobs, 0, 0) >= maxActiveFarTerrainJobs)
            return false;

        Interlocked.Increment(ref activeFarTerrainJobs);

        ThreadPool.QueueUserWorkItem(_ =>
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
                    completedFarTerrainResults.Enqueue(result);
                }
            }
            catch (ThreadAbortException)
            {
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Far terrain request failed for chunk={chunkCoord}, version={requestVersion}\n{ex}");
            }
            finally
            {
                Interlocked.Decrement(ref activeFarTerrainJobs);
            }
        });

        return true;
    }

    public bool RequestLODMesh(ChunkCoord chunkCoord, int lod, int requestVersion, ChunkRecord.NativeTerrainData nativeData, float meshHeightMultiplier,
        int stepIncrement, float worldScale)
    {
        stepIncrement = Mathf.Min(stepIncrement, erosion.maxMeshSpacing);
        int meshChunkSize = nativeData.HeightMapWidth - 3;
        while (stepIncrement > 1 && meshChunkSize % stepIncrement != 0) stepIncrement--;
        if (Interlocked.CompareExchange(ref activeMeshJobs, 0, 0) >= maxActiveMeshJobs)
            return false;

        ChunkRecord.NativeTerrainData.Lease nativeLease = nativeData.AcquireLease();
        if (nativeLease == null)
            return false;

        Interlocked.Increment(ref activeMeshJobs);

        LODMeshRequestWorkItem workItem = new LODMeshRequestWorkItem(
            this,
            chunkCoord,
            lod,
            requestVersion,
            nativeLease,
            meshHeightMultiplier,
            stepIncrement,
            worldScale);

        if (!ThreadPool.UnsafeQueueUserWorkItem(ProcessLODMeshRequest, workItem))
        {
            nativeLease.Dispose();
            Interlocked.Decrement(ref activeMeshJobs);
            return false;
        }

        return true;
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
                workItem.Manager.completedMeshResults.Enqueue(result);
            }
        }
        catch (System.Exception ex)
        {
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
        if (Interlocked.CompareExchange(ref activeColliderJobs, 0, 0) >= maxActiveColliderJobs)
            return false;

        Interlocked.Increment(ref activeColliderJobs);

        ThreadPool.QueueUserWorkItem(_ =>
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
                    completedColliderResults.Enqueue(result);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Collider request failed for chunk={chunkCoord}, version={requestVersion}\n{ex}");
            }
            finally
            {
                Interlocked.Decrement(ref activeColliderJobs);
            }
        });

        return true;
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
            while (completedTerrainDataResults.Count > 0)
                completedTerrainDataResults.Dequeue().Dispose();
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
