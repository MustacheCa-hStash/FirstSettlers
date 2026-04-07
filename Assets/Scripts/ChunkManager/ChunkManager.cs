using System.Collections.Generic;
using UnityEngine;

public class ChunkManager
{
    private readonly int subChunksPerChunk = 10;

    private readonly int viewDistance;
    private readonly int colliderDistance;
    private readonly int chunkSize;
    private readonly int seed;
    private readonly Transform viewer;
    private readonly Transform chunkParent;
    private readonly float sampleScale;
    private readonly float worldScale;
    private readonly int octaves;
    private readonly float persistence;
    private readonly float lacunarity;
    private readonly float erosionStrength;
    private readonly float meshHeightMultiplier;
    private readonly Material terrainMaterial;
    private readonly Material waterMaterial;

    private readonly Dictionary<ChunkCoord, ChunkRecord> chunkRecords = new();
    private readonly Dictionary<ChunkCoord, ChunkRuntime> loadedChunks = new();

    private HashSet<ChunkCoord> activeLastUpdate;
    private HashSet<ChunkCoord> activeThisUpdate;
    private readonly List<ChunkCoord> orderedActiveCoords;

    private ChunkCoord lastUpdateViewerCoord = new ChunkCoord(int.MinValue, int.MinValue);
    private SubChunkCoord lastViewerGlobalSubChunk = new SubChunkCoord(int.MinValue, int.MinValue);

    private readonly TerrainRequestManager terrainRequestManager;
    private readonly FoliageManager foliageManager;

    public ChunkManager(
        int viewDistance,
        int colliderDistance,
        int chunkSize,
        int seed,
        Transform viewer,
        Transform chunkParent,
        Transform foliageParent,
        GrassSettings grassSettings,
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
        foliageManager = new FoliageManager(
            foliageParent,
            grassSettings,
            seed,
            chunkSize,
            worldScale,
            meshHeightMultiplier);
    }

    public ChunkCoord GetViewerChunkCoord()
    {
        float chunkWorldSize = chunkSize * worldScale;

        int cx = Mathf.FloorToInt(viewer.position.x / chunkWorldSize);
        int cz = Mathf.FloorToInt(viewer.position.z / chunkWorldSize);

        return new ChunkCoord(cx, cz);
    }

    public SubChunkCoord GetViewerGlobalSubChunkCoord()
    {
        ChunkCoord viewerChunk = GetViewerChunkCoord();

        float chunkWorldSize = chunkSize * worldScale;
        float subChunkWorldSize = chunkWorldSize / subChunksPerChunk;

        float chunkMinWorldX = viewerChunk.x * chunkWorldSize;
        float chunkMinWorldZ = viewerChunk.z * chunkWorldSize;

        float localWorldX = viewer.position.x - chunkMinWorldX;
        float localWorldZ = viewer.position.z - chunkMinWorldZ;

        int localSubChunkX = Mathf.Clamp(
            Mathf.FloorToInt(localWorldX / subChunkWorldSize),
            0,
            subChunksPerChunk - 1);

        int localSubChunkZ = Mathf.Clamp(
            Mathf.FloorToInt(localWorldZ / subChunkWorldSize),
            0,
            subChunksPerChunk - 1);

        int globalSubChunkX = viewerChunk.x * subChunksPerChunk + localSubChunkX;
        int globalSubChunkZ = viewerChunk.z * subChunksPerChunk + localSubChunkZ;

        return new SubChunkCoord(globalSubChunkX, globalSubChunkZ);
    }

    public int GetSubChunksPerChunk()
    {
        return subChunksPerChunk;
    }

    public float GetChunkWorldSize()
    {
        return chunkSize * worldScale;
    }

    public float GetSubChunkWorldSize()
    {
        return (chunkSize * worldScale) / subChunksPerChunk;
    }

    public void UpdateActiveChunks()
    {
        ProcessCompletedRequests();

        ChunkCoord viewerCoord = GetViewerChunkCoord();
        SubChunkCoord viewerGlobalSubChunk = GetViewerGlobalSubChunkCoord();

        bool viewerChunkChanged = viewerCoord != lastUpdateViewerCoord;
        bool viewerSubChunkChanged = viewerGlobalSubChunk != lastViewerGlobalSubChunk;

        if (viewerChunkChanged)
        {
            RebuildActiveChunkSet(viewerCoord);
            lastUpdateViewerCoord = viewerCoord;
        }

        UpdateVisibleChunkContent(viewerCoord);

        if (viewerChunkChanged || viewerSubChunkChanged)
        {
            foliageManager.HandleViewerSubChunkChanged(
                this,
                viewerCoord,
                viewerGlobalSubChunk,
                orderedActiveCoords);
        }

        foliageManager.DrawVisibleFoliageEveryFrame(
            this,
            viewerCoord,
            viewerGlobalSubChunk,
            orderedActiveCoords);

        lastViewerGlobalSubChunk = viewerGlobalSubChunk;
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

    public ChunkRecord GetChunkRecord(ChunkCoord coord)
    {
        chunkRecords.TryGetValue(coord, out ChunkRecord record);
        return record;
    }

    public ChunkRuntime GetChunkRuntime(ChunkRecord record)
    {
        if (record == null)
            return null;

        loadedChunks.TryGetValue(record.ChunkCoord, out ChunkRuntime runtime);
        return runtime;
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

            runtime.SetControlMaps(record.ControlMapData);
            runtime.SetMeshes(terrainMesh, lakeMesh, riverMesh, lod);
        }
    }

    private void ProcessCompletedRequests()
    {
        while (terrainRequestManager.TryDequeueTerrainDataResult(out TerrainDataRequestResult terrainResult))
        {
            if (!chunkRecords.TryGetValue(terrainResult.ChunkCoord, out ChunkRecord record))
                continue;

            Texture2D[] controlMaps = CreateControlMapTextures(terrainResult.ControlMapsRawData);

            record.TryCompleteTerrainDataRequest(
                terrainResult.RequestVersion,
                terrainResult.HeightMap,
                terrainResult.MoistureMap,
                terrainResult.TemperatureMap,
                terrainResult.BiomeMap,
                terrainResult.SurfaceTypeMap,
                terrainResult.WaterStateMap,
                terrainResult.RiverMaskMap,
                controlMaps
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
                    count++;
            }
        }

        return count;
    }

    private Texture2D[] CreateControlMapTextures(ControlMapPixelData rawData)
    {
        if (rawData == null || rawData.Maps == null || rawData.Maps.Length == 0)
            return null;

        Texture2D[] textures = new Texture2D[rawData.Maps.Length];

        for (int i = 0; i < rawData.Maps.Length; i++)
        {
            Texture2D tex = new Texture2D(rawData.Width, rawData.Height, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels32(rawData.Maps[i]);
            tex.Apply(false, false);
            textures[i] = tex;
        }

        return textures;
    }
}