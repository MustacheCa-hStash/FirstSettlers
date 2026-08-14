using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class TerrainRequestManager
{
    private readonly Queue<TerrainDataRequestResult> completedTerrainDataResults = new Queue<TerrainDataRequestResult>();
    private readonly Queue<FarTerrainRequestResult> completedFarTerrainResults = new Queue<FarTerrainRequestResult>();
    private readonly Queue<MeshRequestResult> completedMeshResults = new Queue<MeshRequestResult>();
    private readonly Queue<ColliderRequestResult> completedColliderResults = new();

    private readonly object terrainDataResultsLock = new object();
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
        int maxActiveColliderJobs)
    {
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
        float erosionStrength,
        WorldFeatureGenerationSettings worldFeatureGenerationSettings)
    {
        if (Interlocked.CompareExchange(ref activeTerrainDataJobs, 0, 0) >= maxActiveTerrainDataJobs)
            return false;

        Interlocked.Increment(ref activeTerrainDataJobs);

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                long totalStart = TerrainGenerationProfiler.GetTimestamp();
                long stageStart = TerrainGenerationProfiler.GetTimestamp();
                HeightFieldResult heightField = HeightMapGenerator.GenerateTerrainHeightField(
                    chunkSize,
                    seed,
                    sampleScale,
                    chunkCoord
                );
                TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.TerrainHeightField, stageStart);

                float[,] finalHeightMap = heightField.HeightMap;
                float[,] gradientXMap = heightField.GradientXMap;
                float[,] gradientZMap = heightField.GradientZMap;
                float[,] slopeMap = heightField.SlopeMap;
                float[,] mountainMaskMap = heightField.MountainMaskMap;
                float[,] riverMaskMap = heightField.RiverMaskMap;

                stageStart = TerrainGenerationProfiler.GetTimestamp();
                float[,] moistureMap = ClimateGenerator.GenerateTerrainMoistureMap(chunkSize, seed, sampleScale,
                    octaves, persistence, lacunarity, chunkCoord);
                TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.TerrainClimateMoisture, stageStart);

                stageStart = TerrainGenerationProfiler.GetTimestamp();
                float[,] temperatureMap = ClimateGenerator.GenerateTerrainTemperatureMap(chunkSize, seed, 
                    sampleScale, octaves, persistence, lacunarity, chunkCoord);
                TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.TerrainClimateTemperature, stageStart);

                stageStart = TerrainGenerationProfiler.GetTimestamp();
                BiomeType[,] biomeMap = BiomeMapGenerator.GenerateBiomeMap(finalHeightMap, moistureMap, 
                    temperatureMap, slopeMap, mountainMaskMap, riverMaskMap);
                TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.TerrainBiomeMap, stageStart);

                stageStart = TerrainGenerationProfiler.GetTimestamp();
                SurfaceType[,] surfaceTypeMap = SurfaceMapGenerator.GenerateSurfaceTypeMap(finalHeightMap, slopeMap, 
                    riverMaskMap, biomeMap);
                TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.TerrainSurfaceMap, stageStart);

                stageStart = TerrainGenerationProfiler.GetTimestamp();
                WaterState[,] waterStateMap = WaterStateMapGenerator.GenerateWaterStateMap(finalHeightMap, riverMaskMap);
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
                    worldFeatureGenerationSettings);
                TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.TerrainWorldFeaturePlan, stageStart);

                stageStart = TerrainGenerationProfiler.GetTimestamp();
                GroundCoverType[,] groundCoverMap = GroundCoverMapGenerator.GenerateGroundCoverMap(
                    biomeMap,
                    surfaceTypeMap,
                    moistureMap,
                    slopeMap,
                    riverMaskMap,
                    worldFeaturePlan,
                    chunkSize,
                    seed,
                    chunkCoord);
                TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.TerrainGroundCoverMap, stageStart);

                stageStart = TerrainGenerationProfiler.GetTimestamp();
                ControlMapPixelData controlMapRawData = TerrainControlMapBuilder.BuildRaw(surfaceTypeMap, groundCoverMap);
                TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.TerrainControlMapBuild, stageStart);

                TerrainDataRequestResult result = new TerrainDataRequestResult(chunkCoord, requestVersion, 
                    finalHeightMap, gradientXMap, gradientZMap, slopeMap, moistureMap, temperatureMap, biomeMap, 
                    surfaceTypeMap, waterStateMap, groundCoverMap, worldFeaturePlan, riverMaskMap, controlMapRawData);
                TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.TerrainDataTotal, totalStart);

                lock (terrainDataResultsLock)
                {
                    completedTerrainDataResults.Enqueue(result);
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
        bool isMacroTile = false)
    {
        if (Interlocked.CompareExchange(ref activeFarTerrainJobs, 0, 0) >= maxActiveFarTerrainJobs)
            return false;

        Interlocked.Increment(ref activeFarTerrainJobs);

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
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
                    isMacroTile);

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

    public bool RequestLODMesh(ChunkCoord chunkCoord, int lod, int requestVersion, float[,] heightMap,
        BiomeType[,] biomeMap, SurfaceType[,] surfaceTypeMap, WaterState[,] waterStateMap, float meshHeightMultiplier, 
        int stepIncrement, float worldScale, float[,] riverMaskMap)
    {
        if (Interlocked.CompareExchange(ref activeMeshJobs, 0, 0) >= maxActiveMeshJobs)
            return false;

        Interlocked.Increment(ref activeMeshJobs);

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                long totalStart = TerrainGenerationProfiler.GetTimestamp();
                long stageStart = TerrainGenerationProfiler.GetTimestamp();
                MeshData terrainMeshData = MeshGenerator.GenerateTerrainMesh(chunkCoord, heightMap, biomeMap, surfaceTypeMap, 
                    waterStateMap, meshHeightMultiplier, stepIncrement, worldScale, riverMaskMap);
                TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.TerrainMeshBuild, stageStart);

                stageStart = TerrainGenerationProfiler.GetTimestamp();
                WaterMeshData lakeMeshData = LakeMeshGenerator.GenerateLakeMesh(heightMap, waterStateMap,
                    riverMaskMap, meshHeightMultiplier, stepIncrement, worldScale);
                TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.LakeMeshBuild, stageStart);

                stageStart = TerrainGenerationProfiler.GetTimestamp();
                WaterMeshData riverMeshData = RiverMeshGenerator.GenerateRiverMesh(heightMap, waterStateMap,
                    riverMaskMap, meshHeightMultiplier, stepIncrement, worldScale);
                TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.RiverMeshBuild, stageStart);

                //only taking rivermeshdata here
                MeshRequestResult result = new MeshRequestResult(chunkCoord, lod, requestVersion, 
                    terrainMeshData, lakeMeshData, riverMeshData);
                TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.LODMeshTotal, totalStart);

                lock (meshResultsLock)
                {
                    completedMeshResults.Enqueue(result);
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Mesh request failed for chunk={chunkCoord}, lod={lod}, version={requestVersion}\n{ex}");
            }
            finally
            {
                Interlocked.Decrement(ref activeMeshJobs);
            }
        });

        return true;
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
