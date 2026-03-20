using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class TerrainRequestManager
{
    private readonly Queue<TerrainDataRequestResult> completedTerrainDataResults = new Queue<TerrainDataRequestResult>();
    private readonly Queue<MeshRequestResult> completedMeshResults = new Queue<MeshRequestResult>();

    private readonly object terrainDataResultsLock = new object();
    private readonly object meshResultsLock = new object();

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
                float[,] slopeMap = heightField.SlopeMap;
                float[,] mountainMaskMap = heightField.MountainMaskMap;
                float[,] riverMaskMap = heightField.RiverMaskMap;

                float[,] moistureMap = ClimateGenerator.GenerateTerrainMoistureMap(chunkSize, seed, sampleScale,
                    octaves, persistence, lacunarity, chunkCoord);

                float[,] temperatureMap = ClimateGenerator.GenerateTerrainTemperatureMap(chunkSize, seed, 
                    sampleScale, octaves, persistence, lacunarity, chunkCoord);

                BiomeType[,] biomeMap = BiomeMapGenerator.GenerateBiomeMap(finalHeightMap, moistureMap, 
                    temperatureMap, slopeMap, mountainMaskMap, riverMaskMap);

                TerrainDataRequestResult result = new TerrainDataRequestResult(chunkCoord, requestVersion, 
                    finalHeightMap, gradientXMap, gradientZMap, moistureMap, temperatureMap, biomeMap, riverMaskMap);

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

    public void RequestLODMesh(ChunkCoord chunkCoord, int lod, int requestVersion, float[,] heightMap, 
        BiomeType[,] biomeMap, float meshHeightMultiplier, int stepIncrement, float[,] riverMaskMap)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Interlocked.Increment(ref activeMeshJobs);

            try
            {
                MeshData meshData = MeshGenerator.GenerateTerrainMesh(heightMap, biomeMap, 
                    meshHeightMultiplier, stepIncrement, riverMaskMap);

                MeshRequestResult result = new MeshRequestResult(chunkCoord, lod, requestVersion, meshData);

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