using System.Collections.Generic;
using UnityEngine;

public class ChunkManager
{
    private int viewDistance;
    private int colliderDistance;
    private int chunkSize;
    private int seed;
    private Transform viewer;
    private Transform chunkParent;
    private float sampleScale;
    private float worldScale;
    private int octaves;
    private float persistence;
    private float lacunarity;
    private float erosionStrength;
    private float meshHeightMultiplier;
    private Material terrainMaterial;
    private Material waterMaterial;

    private Dictionary<ChunkCoord, ChunkRecord> chunkRecords = new();
    private Dictionary<ChunkCoord, ChunkRuntime> loadedChunks = new();

    private HashSet<ChunkCoord> activeLastUpdate = new();
    private HashSet<ChunkCoord> activeThisUpdate = new();

    private TerrainRequestManager terrainRequestManager;

    public ChunkManager(int viewDistance, int colliderDistance, int chunkSize, int seed, Transform viewer, Transform chunkParent, 
        float sampleScale, float worldScale, int octaves, float persistence, float lacunarity, 
        float erosionStrength, float meshHeightMultiplier, Material terrainMaterial, Material waterMaterial)
    {
        this.viewDistance = viewDistance;
        this.colliderDistance = colliderDistance;
        this.chunkSize = chunkSize;
        this.seed = seed;
        this.viewer = viewer;
        this.chunkParent = chunkParent;
        this.sampleScale = sampleScale;
        this.worldScale = worldScale;
        this.octaves = octaves;
        this.persistence = persistence;
        this.lacunarity = lacunarity;
        this.erosionStrength = erosionStrength;
        this.meshHeightMultiplier = meshHeightMultiplier;
        this.terrainMaterial = terrainMaterial;
        this.waterMaterial = waterMaterial;

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

                //modify later to match LOD ring policy method implementation for custom setup
                bool colliderDesired = sqrDistance <= colliderDistance * colliderDistance;
                record.ColliderDesired = colliderDesired;

                if (colliderDesired)
                {
                    EnsureColliderRequested(record);
                    TryApplyCollider(record, runtime);
                }
                else
                {
                    if (runtime.HasCollider())
                    {
                        runtime.RemoveCollider();
                        record.ClearColliderMesh();
                    }
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

                //if (chunkRecords.TryGetValue(coord, out ChunkRecord record))
                    //record.ClearAllLODMeshes();
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
            runtime = new ChunkRuntime(record, chunkSize, worldScale, chunkParent, terrainMaterial, waterMaterial);
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

        bool submitted = terrainRequestManager.RequestTerrainData(
            record.ChunkCoord,
            requestVersion,
            chunkSize,
            seed,
            sampleScale,
            octaves,
            persistence,
            lacunarity,
            erosionStrength
        );

        if (!submitted)
        {
            record.CancelTerrainDataRequest(requestVersion);
        }
    }

    private void EnsureColliderRequested(ChunkRecord record)
    {
        if (!record.HasTerrainData)
            return;

        if (record.ColliderReady)
            return;

        if (record.ColliderRequestInFlight)
            return;

        int requestVersion = record.BeginColliderRequest();

        terrainRequestManager.RequestColliderMesh(
            record.ChunkCoord,
            requestVersion,
            record.HeightMap,
            meshHeightMultiplier,
            worldScale
        );
    }

    private void TryApplyCollider(ChunkRecord record, ChunkRuntime runtime)
    {
        if (!record.TryGetColliderMesh(out Mesh colliderMesh))
            return;

        if (!runtime.HasCollider())
        {
            runtime.ApplyCollider(colliderMesh);
        }
    }

    private void EnsureLODMeshRequested(ChunkRecord record, int lod)
    {
        if (!record.HasTerrainData)
            return;

        if (record.TryGetLODTerrainMesh(lod, out _))
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
            record.SurfaceTypeMap,
            record.WaterStateMap,
            meshHeightMultiplier,
            stepIncrement,
            worldScale,
            record.RiverMaskMap
        );
    }

    private void TryApplyLODMesh(ChunkRecord record, ChunkRuntime runtime, int lod)
    {
        if (!record.TryGetLODTerrainMesh(lod, out Mesh terrainMesh))
            return;

        if (!runtime.IsShowingLOD(lod))
        {
            Mesh lakeMesh = null;
            Mesh riverMesh = null;

            record.TryGetLODLakeMesh(lod, out lakeMesh);
            record.TryGetLODRiverMesh(lod, out riverMesh);

            runtime.SetMeshes(terrainMesh, lakeMesh, riverMesh, lod);
        }
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
                terrainResult.BiomeMap,
                terrainResult.SurfaceTypeMap,
                terrainResult.WaterStateMap,
                terrainResult.RiverMaskMap
            );
        }

        while (terrainRequestManager.TryDequeueMeshResult(out MeshRequestResult meshResult))
        {
            if (!chunkRecords.TryGetValue(meshResult.ChunkCoord, out ChunkRecord record))
                continue;

            Mesh terrainMesh = meshResult.TerrainMeshData.CreateMesh();
            Mesh lakeMesh = meshResult.LakeMeshData.CreateMesh();
            Mesh riverMesh = meshResult.RiverMeshData.CreateMesh();

            record.TryCompleteMeshRequest(meshResult.LOD, meshResult.RequestVersion, terrainMesh, lakeMesh, riverMesh);
        }

        while (terrainRequestManager.TryDequeueColliderResult(out ColliderRequestResult colliderResult))
        {
            if (!chunkRecords.TryGetValue(colliderResult.ChunkCoord, out ChunkRecord record))
                continue;

            Mesh colliderMesh = colliderResult.ColliderMeshData.CreateMesh();

            record.TryCompleteColliderRequest(
                colliderResult.RequestVersion,
                colliderMesh
            );
        }
    }

}
