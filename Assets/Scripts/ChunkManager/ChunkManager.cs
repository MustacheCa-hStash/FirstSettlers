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
    private float meshHeightMultiplier;
    private AnimationCurve heightCurve;
    private Material baseMaterial;

    private Dictionary<ChunkCoord, ChunkRecord> chunkRecords = new();
    private Dictionary<ChunkCoord, ChunkRuntime> loadedChunks = new();

    private HashSet<ChunkCoord> activeLastUpdate = new();
    private HashSet<ChunkCoord> activeThisUpdate = new();

    public ChunkManager(int viewDistance, int chunkSize, int seed, Transform viewer, Transform chunkParent, float sampleScale,
        int octaves, float persistence, float lacunarity, float meshHeightMultiplier, AnimationCurve heightCurve, Material baseMaterial)
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
        this.baseMaterial = baseMaterial;
        this.meshHeightMultiplier = meshHeightMultiplier;
        this.heightCurve = heightCurve;
    }

    public ChunkCoord GetViewerChunkCoord()
    {
        int cx = Mathf.FloorToInt(viewer.position.x / chunkSize);
        int cz = Mathf.FloorToInt(viewer.position.z / chunkSize);
        ChunkCoord viewerChunk = new ChunkCoord(cx, cz);
        return viewerChunk;
    }

    public void UpdateActiveChunks()
    {
        activeThisUpdate.Clear();

        ChunkCoord viewerCoord = GetViewerChunkCoord();

        for (int x = -viewDistance; x <= viewDistance; x++)
        {
            for (int z = -viewDistance; z <= viewDistance; z++)
            {
                int sqrDistance = x * x + z * z;
                int sqrViewRadius = viewDistance * viewDistance;
                if (sqrDistance > sqrViewRadius) continue;

                ChunkCoord targetCoord = new ChunkCoord(viewerCoord.x + x, viewerCoord.z + z);
                activeThisUpdate.Add(targetCoord);
                ChunkRecord record = GetOrCreateChunkRecord(targetCoord);

                if (!record.HasHeightMap)
                {
                    float[,] map = TerrainGenerator.GenerateTerrainHeightMap(chunkSize, seed, sampleScale, 
                        octaves, persistence, lacunarity, heightCurve, targetCoord);
                    record.SetHeightMap(map);
                }

                int lod = ChunkRingLODPolicy.GetLOD(viewerCoord, targetCoord);
                ChunkRuntime runtime = GetOrCreateChunkRuntime(record);

                if (!runtime.IsShowingLOD(lod))
                {
                    Mesh mesh = GetOrCreateLODMesh(record, lod);
                    runtime.SetMesh(mesh, lod);
                }

                if (!runtime.IsVisible)
                {
                    runtime.SetVisible(true);
                }
            }
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

                if (chunkRecords.TryGetValue(coord, out ChunkRecord record))
                    record.ClearLODMeshes();
            }
        }

        var temp = activeLastUpdate;
        activeLastUpdate = activeThisUpdate;
        activeThisUpdate = temp;
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
            runtime = new ChunkRuntime(record, chunkSize, chunkParent, baseMaterial);
            loadedChunks.Add(coord, runtime);
        }
        return runtime;
    }

    private Mesh GetOrCreateLODMesh(ChunkRecord chunkRecord, int lod)
    {
        if (chunkRecord.TryGetLODMesh(lod, out Mesh mesh))
            return mesh;

        int stepIncrement = 1 << lod;
        MeshData meshData = MeshGenerator.GenerateTerrainMesh(
            chunkRecord.HeightMap,
            meshHeightMultiplier,
            stepIncrement
        );

        mesh = meshData.CreateMesh();
        chunkRecord.StoreLODMesh(lod, mesh);

        return mesh;
    }



}
