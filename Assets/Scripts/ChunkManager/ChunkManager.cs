using System.Collections.Generic;
using UnityEngine;

public class ChunkManager
{
    private int viewDistance;
    private int chunkSize;
    private int seed;
    private Transform viewer;
    private Transform chunkParent;
    private float sampleScale;
    private int octaves;
    private float persistence;
    private float lacunarity;

    private Dictionary<ChunkCoord, ChunkRecord> chunkRecords = new();
    private Dictionary<ChunkCoord, ChunkRuntime> loadedChunks = new();

    private HashSet<ChunkCoord> visibleLastUpdate = new();
    private HashSet<ChunkCoord> visibleThisUpdate = new();

    public ChunkManager(int viewDistance, int chunkSize, int seed, Transform viewer, Transform chunkParent, float sampleScale,
        int octaves, float persistence, float lacunarity)
    {
        this.viewDistance = viewDistance;
        this.chunkSize = chunkSize;
        this.seed = seed;
        this.viewer = viewer;
        this.chunkParent = chunkParent;
        this.sampleScale = sampleScale;
        this.octaves = octaves;
        this.persistence = persistence;
        this.lacunarity = lacunarity;
    }

    public ChunkCoord GetViewerChunkCoord()
    {
        int cx = Mathf.FloorToInt(viewer.position.x / chunkSize);
        int cz = Mathf.FloorToInt(viewer.position.z / chunkSize);

        ChunkCoord viewerChunk = new ChunkCoord(cx, cz);
        Debug.Log($"Viewer Chunk: {viewerChunk.x}, {viewerChunk.z}");

        return viewerChunk;
    }

    public void UpdateVisibleChunks()
    {
        visibleThisUpdate.Clear();

        ChunkCoord viewerCoord = GetViewerChunkCoord();

        for (int x = -viewDistance; x <= viewDistance; x++)
        {
            for (int z = -viewDistance; z <= viewDistance; z++)
            {
                int sqrDistance = x * x + z * z;
                int sqrViewRadius = viewDistance * viewDistance;
                if (sqrDistance > sqrViewRadius) continue;

                ChunkCoord coord = new ChunkCoord(viewerCoord.x + x, viewerCoord.z + z);
                visibleThisUpdate.Add(coord);

                ChunkRecord record = GetOrCreateChunkRecord(coord);

                if (!record.HasHeightMap)
                {
                    float[,] map = TerrainGenerator.GenerateTerrainHeightMap(chunkSize, seed, sampleScale, 
                        octaves, persistence, lacunarity, coord);
                    record.SetHeightMap(map);
                }

                ChunkRuntime runtime = GetOrCreateChunkRuntime(record);

                if (!runtime.IsVisible)
                {
                    runtime.SetVisible(true);
                }
            }
        }

        foreach (ChunkCoord coord in visibleLastUpdate)
        {
            if (!visibleThisUpdate.Contains(coord) && loadedChunks.TryGetValue(coord, out ChunkRuntime runtime))
            {
                runtime.SetVisible(false);
            }
        }

        var temp = visibleLastUpdate;
        visibleLastUpdate = visibleThisUpdate;
        visibleThisUpdate = temp;
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
            runtime = new ChunkRuntime(record, chunkSize, chunkParent);
            loadedChunks.Add(coord, runtime);
        }
        return runtime;
    }

}
