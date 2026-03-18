using System.Collections.Generic;
using System.Threading;

public class TerrainRequestManager
{
    private Queue<HeightMapRequestResult> completedHeightMapResults = new Queue<HeightMapRequestResult>();
    private Queue<MeshRequestResult> completedMeshResults = new Queue<MeshRequestResult>();

    private object heightMapResultsLock = new object();
    private object meshResultsLock = new object();

    public void RequestHeightMap(
        ChunkCoord chunkCoord,
        int requestVersion,
        int chunkSize,
        int seed,
        float sampleScale,
        int octaves,
        float persistence,
        float lacunarity)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            float[,] heightMap = TerrainGenerator.GenerateTerrainHeightMap(
                chunkSize,
                seed,
                sampleScale,
                octaves,
                persistence,
                lacunarity,
                chunkCoord
            );

            HeightMapRequestResult result = new HeightMapRequestResult(
                chunkCoord,
                requestVersion,
                heightMap
            );

            lock (heightMapResultsLock)
            {
                completedHeightMapResults.Enqueue(result);
            }
        });
    }

    public void RequestLODMesh(
        ChunkCoord chunkCoord,
        int lod,
        int requestVersion,
        float[,] heightMap,
        float meshHeightMultiplier,
        int stepIncrement)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            MeshData meshData = MeshGenerator.GenerateTerrainMesh(
                heightMap,
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

    public bool TryDequeueHeightMapResult(out HeightMapRequestResult result)
    {
        lock (heightMapResultsLock)
        {
            if (completedHeightMapResults.Count > 0)
            {
                result = completedHeightMapResults.Dequeue();
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