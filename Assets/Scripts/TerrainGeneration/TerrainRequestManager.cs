using System.Collections.Generic;
using System.Threading;

public class TerrainRequestManager
{
    private Queue<TerrainDataRequestResult> completedTerrainDataResults = new Queue<TerrainDataRequestResult>();
    private Queue<MeshRequestResult> completedMeshResults = new Queue<MeshRequestResult>();

    private object terrainDataResultsLock = new object();
    private object meshResultsLock = new object();

    public void RequestTerrainData(
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
        ThreadPool.QueueUserWorkItem(_ =>
        {
            float[,] rawHeightMap = HeightMapGenerator.GenerateTerrainHeightMap(chunkSize, seed, sampleScale, octaves, 
                persistence, lacunarity, erosionStrength, chunkCoord).HeightMap;
            float[,] moistureMap = ClimateGenerator.GenerateTerrainMoistureMap(chunkSize, seed, sampleScale, octaves,
                persistence, lacunarity, chunkCoord);
            float[,] temperatureMap = ClimateGenerator.GenerateTerrainTemperatureMap(chunkSize, seed, sampleScale, octaves,
                persistence, lacunarity, chunkCoord);
            BiomeType[,] biomeMap = BiomeMapGenerator.GenerateBiomeMap(rawHeightMap, moistureMap, temperatureMap);
            float[,] postProcessedHeightMap = HeightMapGenerator.ApplyBiomeHeightModifiers(rawHeightMap, biomeMap);

            TerrainDataRequestResult result = new TerrainDataRequestResult(
                chunkCoord,
                requestVersion,
                postProcessedHeightMap,
                moistureMap,
                temperatureMap,
                biomeMap
            );

            lock (terrainDataResultsLock)
            {
                completedTerrainDataResults.Enqueue(result);
            }
        });
    }

    public void RequestLODMesh(
        ChunkCoord chunkCoord,
        int lod,
        int requestVersion,
        float[,] heightMap,
        BiomeType[,] biomeMap,
        float meshHeightMultiplier,
        int stepIncrement)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            MeshData meshData = MeshGenerator.GenerateTerrainMesh(
                heightMap,
                biomeMap,
                meshHeightMultiplier,
                stepIncrement
            );

            MeshRequestResult result = new MeshRequestResult(
                chunkCoord,
                lod,
                requestVersion,
                meshData
            );

            lock (meshResultsLock)
            {
                completedMeshResults.Enqueue(result);
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