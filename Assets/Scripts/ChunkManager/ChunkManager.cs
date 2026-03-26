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

    private HashSet<ChunkCoord> activeLastUpdate;
    private HashSet<ChunkCoord> activeThisUpdate;
    private List<ChunkCoord> orderedActiveCoords;
    private ChunkCoord lastUpdateViewerCoord = new ChunkCoord(int.MinValue, int.MinValue);

    private TerrainRequestManager terrainRequestManager;

    public ChunkManager(
        int viewDistance,
        int colliderDistance,
        int chunkSize,
        int seed,
        Transform viewer,
        Transform chunkParent,
        float sampleScale,
        float worldScale,
        int octaves,
        float persistence,
        float lacunarity,
        float erosionStrength,
        float meshHeightMultiplier,
        Material terrainMaterial,
        Material waterMaterial)
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

        int maxChunks = ComputeMaxActiveChunkCount(viewDistance);

        activeLastUpdate = new HashSet<ChunkCoord>(maxChunks);
        activeThisUpdate = new HashSet<ChunkCoord>(maxChunks);
        orderedActiveCoords = new List<ChunkCoord>(maxChunks);
        terrainRequestManager = new TerrainRequestManager();
    }

    public ChunkCoord GetViewerChunkCoord()
    {
        int cx = Mathf.FloorToInt(viewer.position.x / (chunkSize * worldScale));
        int cz = Mathf.FloorToInt(viewer.position.z / (chunkSize * worldScale));
        return new ChunkCoord(cx, cz);
    }

    public void UpdateActiveChunks()
    {
        ProcessCompletedRequests();

        ChunkCoord viewerCoord = GetViewerChunkCoord();

        if (viewerCoord != lastUpdateViewerCoord)
        {
            RebuildActiveChunkSet(viewerCoord);
            lastUpdateViewerCoord = viewerCoord;
        }

        UpdateVisibleChunkContent(viewerCoord);
    }

    private void RebuildActiveChunkSet(ChunkCoord viewerCoord)
    {
        activeThisUpdate.Clear();
        orderedActiveCoords.Clear();

        int sqrViewRadius = viewDistance * viewDistance;

        for (int x = -viewDistance; x <= viewDistance; x++)
        {
            for (int z = -viewDistance; z <= viewDistance; z++)
            {
                int sqrDistance = x * x + z * z;
                if (sqrDistance > sqrViewRadius)
                    continue;

                ChunkCoord targetCoord = new ChunkCoord(viewerCoord.x + x, viewerCoord.z + z);

                activeThisUpdate.Add(targetCoord);
                orderedActiveCoords.Add(targetCoord);
            }
        }

        SortOrderedActiveCoords(viewerCoord);

        foreach (ChunkCoord targetCoord in orderedActiveCoords)
        {
            ChunkRecord record = GetOrCreateChunkRecord(targetCoord);
            ChunkRuntime runtime = GetOrCreateChunkRuntime(record);

            if (!runtime.IsVisible)
                runtime.SetVisible(true);

            EnsureTerrainDataRequested(record);
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

                // if (chunkRecords.TryGetValue(coord, out ChunkRecord record))
                //     record.ClearAllLODMeshes();
            }
        }

        var temp = activeLastUpdate;
        activeLastUpdate = activeThisUpdate;
        activeThisUpdate = temp;
    }

    private void UpdateVisibleChunkContent(ChunkCoord viewerCoord)
    {
        int sqrColliderRadius = colliderDistance * colliderDistance;

        foreach (ChunkCoord coord in orderedActiveCoords)
        {
            if (!loadedChunks.TryGetValue(coord, out ChunkRuntime runtime))
                continue;

            if (!chunkRecords.TryGetValue(coord, out ChunkRecord record))
                continue;

            EnsureTerrainDataRequested(record);

            int dx = coord.x - viewerCoord.x;
            int dz = coord.z - viewerCoord.z;
            int sqrDistance = dx * dx + dz * dz;

            int lod = ChunkRingLODPolicy.GetLOD(viewerCoord, coord);

            EnsureLODMeshRequested(record, lod);
            TryApplyLODMesh(record, runtime, lod);

            bool colliderDesired = sqrDistance <= sqrColliderRadius;
            record.ColliderDesired = colliderDesired;

            if (colliderDesired)
            {
                EnsureColliderRequested(record);
                TryApplyCollider(record, runtime);
            }
            else if (runtime.HasCollider())
            {
                runtime.RemoveCollider();
                record.ClearColliderMesh();
            }

            if (!runtime.IsVisible)
                runtime.SetVisible(true);
        }
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

            record.TryCompleteMeshRequest(
                meshResult.LOD,
                meshResult.RequestVersion,
                terrainMesh,
                lakeMesh,
                riverMesh
            );
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

    private void SortOrderedActiveCoords(ChunkCoord viewerCoord)
    {
        Vector2 forward = new Vector2(viewer.forward.x, viewer.forward.z).normalized;

        orderedActiveCoords.Sort((a, b) =>
        {
            int adx = a.x - viewerCoord.x;
            int adz = a.z - viewerCoord.z;
            int bdx = b.x - viewerCoord.x;
            int bdz = b.z - viewerCoord.z;

            int aRing = Mathf.Max(Mathf.Abs(adx), Mathf.Abs(adz));
            int bRing = Mathf.Max(Mathf.Abs(bdx), Mathf.Abs(bdz));

            int ringCompare = aRing.CompareTo(bRing);
            if (ringCompare != 0)
                return ringCompare;

            int aSqrDist = adx * adx + adz * adz;
            int bSqrDist = bdx * bdx + bdz * bdz;

            float aDot = aSqrDist == 0 ? 2f : Vector2.Dot(forward, new Vector2(adx, adz).normalized);
            float bDot = bSqrDist == 0 ? 2f : Vector2.Dot(forward, new Vector2(bdx, bdz).normalized);

            int dotCompare = bDot.CompareTo(aDot);
            if (dotCompare != 0)
                return dotCompare;

            return aSqrDist.CompareTo(bSqrDist);
        });

    }

    private static int ComputeMaxActiveChunkCount(int viewDistance)
    {
        int count = 0;
        int sqrViewRadius = viewDistance * viewDistance;

        for (int x = -viewDistance; x <= viewDistance; x++)
        {
            for (int z = -viewDistance; z <= viewDistance; z++)
            {
                if (x * x + z * z <= sqrViewRadius)
                {
                    count++;
                }
            }
        }

        return count;
    }
}