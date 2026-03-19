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
    private Material baseMaterial;

    private Dictionary<ChunkCoord, ChunkRecord> chunkRecords = new();
    private Dictionary<ChunkCoord, ChunkRuntime> loadedChunks = new();

    private HashSet<ChunkCoord> activeLastUpdate = new();
    private HashSet<ChunkCoord> activeThisUpdate = new();

    private TerrainRequestManager terrainRequestManager;

    public ChunkManager(int viewDistance, int chunkSize, int seed, Transform viewer, Transform chunkParent, float sampleScale,
        int octaves, float persistence, float lacunarity, float meshHeightMultiplier, Material baseMaterial)
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
        this.meshHeightMultiplier = meshHeightMultiplier;
        this.baseMaterial = baseMaterial;

        terrainRequestManager = new TerrainRequestManager();
    }

    public ChunkCoord GetViewerChunkCoord()
    {
        int cx = Mathf.FloorToInt(viewer.position.x / chunkSize);
        int cz = Mathf.FloorToInt(viewer.position.z / chunkSize);
        return new ChunkCoord(cx, cz);
    }

    public void UpdateActiveChunks()
    {
        ProcessCompletedRequests();

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
                ChunkRuntime runtime = GetOrCreateChunkRuntime(record);

                int lod = ChunkRingLODPolicy.GetLOD(viewerCoord, targetCoord);

                EnsureTerrainDataRequested(record);
                EnsureLODMeshRequested(record, lod);
                TryApplyLODMesh(record, runtime, lod);

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

    private void EnsureTerrainDataRequested(ChunkRecord record)
    {
        if (record.HasTerrainData)
            return;

        if (record.IsTerrainDataRequestInFlight)
            return;

        int requestVersion = record.BeginTerrainDataRequest();

        terrainRequestManager.RequestTerrainData(
            record.ChunkCoord,
            requestVersion,
            chunkSize,
            seed,
            sampleScale,
            octaves,
            persistence,
            lacunarity
        );
    }

    private void EnsureLODMeshRequested(ChunkRecord record, int lod)
    {
        if (!record.HasTerrainData)
            return;

        if (record.TryGetLODMesh(lod, out _))
            return;

        if (record.IsMeshRequestInFlight(lod))
            return;

        int stepIncrement = 1 << lod;
        int requestVersion = record.BeginMeshRequest(lod);

        terrainRequestManager.RequestLODMesh(
            record.ChunkCoord,
            lod,
            requestVersion,
            record.HeightMap,
            record.BiomeMap,
            meshHeightMultiplier,
            stepIncrement
        );
    }

    private void TryApplyLODMesh(ChunkRecord record, ChunkRuntime runtime, int lod)
    {
        if (!record.TryGetLODMesh(lod, out Mesh mesh))
            return;

        if (!runtime.IsShowingLOD(lod))
            runtime.SetMesh(mesh, lod);
    }

    private void ProcessCompletedRequests()
    {
        while (terrainRequestManager.TryDequeueTerrainDataResult(out TerrainDataRequestResult terrainResult))
        {
            if (!chunkRecords.TryGetValue(terrainResult.ChunkCoord, out ChunkRecord record))
                continue;

            record.TryCompleteTerrainDataRequest(
                terrainResult.RequestVersion,
                terrainResult.HeightMap,
                terrainResult.MoistureMap,
                terrainResult.TemperatureMap,
                terrainResult.BiomeMap
            );
        }

        while (terrainRequestManager.TryDequeueMeshResult(out MeshRequestResult meshResult))
        {
            if (!chunkRecords.TryGetValue(meshResult.ChunkCoord, out ChunkRecord record))
                continue;

            Mesh mesh = meshResult.MeshData.CreateMesh();

            record.TryCompleteMeshRequest(meshResult.LOD, meshResult.RequestVersion, mesh);
        }
    }

}
