using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;
using UnityEngine;

public class TerrainRequestManager
{
    private Queue<TerrainDataRequestResult> completedTerrainDataResults = new Queue<TerrainDataRequestResult>();
    private Queue<MeshRequestResult> completedMeshResults = new Queue<MeshRequestResult>();

    private object terrainDataResultsLock = new object();
    private object meshResultsLock = new object();

    private static int activeTerrainDataJobs;
    private static int activeMeshJobs;

    private const int MaxActiveTerrainDataJobs = 6;

    public bool RequestTerrainData(
    ChunkCoord chunkCoord,
    int requestVersion,
    int chunkSize,
    int seed,
    float sampleScale,
    int octaves,
    float persistence,
    float lacunarity,
    float erosionStrength)
    {
        if (Interlocked.CompareExchange(ref activeTerrainDataJobs, 0, 0) >= MaxActiveTerrainDataJobs)
            return false;

        Interlocked.Increment(ref activeTerrainDataJobs);

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                HeightFieldResult heightField = HeightMapGenerator.GenerateTerrainHeightField(
                    chunkSize,
                    seed,
                    sampleScale,
                    chunkCoord
                );

                float[,] finalHeightMap = heightField.HeightMap;
                float[,] gradientXMap = heightField.GradientXMap;
                float[,] gradientZMap = heightField.GradientZMap;
                float[,] mountainMaskMap = heightField.MountainMaskMap;

                float[,] moistureMap = ClimateGenerator.GenerateTerrainMoistureMap(
                    chunkSize,
                    seed,
                    sampleScale,
                    octaves,
                    persistence,
                    lacunarity,
                    chunkCoord
                );

                float[,] temperatureMap = ClimateGenerator.GenerateTerrainTemperatureMap(
                    chunkSize,
                    seed,
                    sampleScale,
                    octaves,
                    persistence,
                    lacunarity,
                    chunkCoord
                );

                BiomeType[,] biomeMap = BiomeMapGenerator.GenerateBiomeMap(
                    finalHeightMap,
                    moistureMap,
                    temperatureMap
                );

                TerrainDataRequestResult result = new TerrainDataRequestResult(
                    chunkCoord,
                    requestVersion,
                    finalHeightMap,
                    gradientXMap,
                    gradientZMap,
                    mountainMaskMap,
                    moistureMap,
                    temperatureMap,
                    biomeMap
                );

                lock (terrainDataResultsLock)
                {
                    completedTerrainDataResults.Enqueue(result);
                }
            }
            catch (System.Threading.ThreadAbortException)
            {
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"TerrainData FAILED chunk={chunkCoord} ver={requestVersion}\n{ex}");
            }
            finally
            {
                Interlocked.Decrement(ref activeTerrainDataJobs);
            }
        });

        return true;
    }

    public void RequestLODMesh(
        ChunkCoord chunkCoord,
        int lod,
        int requestVersion,
        float[,] heightMap,
        BiomeType[,] biomeMap,
        float meshHeightMultiplier,
        int stepIncrement,
        float[,] mountainMask)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            int currentJobs = Interlocked.Increment(ref activeMeshJobs);
            Stopwatch timer = Stopwatch.StartNew();

            try
            {
                MeshData meshData = MeshGenerator.GenerateTerrainMesh(
                    heightMap,
                    biomeMap,
                    meshHeightMultiplier,
                    stepIncrement,
                    mountainMask
                );

                MeshRequestResult result = new MeshRequestResult(
                    chunkCoord,
                    lod,
                    requestVersion,
                    meshData
                );

                int queueCountAfterEnqueue;
                lock (meshResultsLock)
                {
                    completedMeshResults.Enqueue(result);
                    queueCountAfterEnqueue = completedMeshResults.Count;
                }

                timer.Stop();

                UnityEngine.Debug.Log(
                    $"Mesh DONE chunk={chunkCoord} lod={lod} ver={requestVersion} " +
                    $"activeMeshJobs={currentJobs} total={timer.ElapsedMilliseconds}ms " +
                    $"meshQueue={queueCountAfterEnqueue}"
                );
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError(
                    $"Mesh FAILED chunk={chunkCoord} lod={lod} ver={requestVersion}\n{ex}"
                );
            }
            finally
            {
                Interlocked.Decrement(ref activeMeshJobs);
            }
        });
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
}