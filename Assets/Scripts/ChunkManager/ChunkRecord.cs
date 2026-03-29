using System.Collections.Generic;
using UnityEngine;

public class ChunkRecord
{
    private ChunkCoord chunkCoord;
    private ChunkRuntime activeRuntime;
    private float[,] heightMap;
    private float[,] moistureMap;
    private float[,] temperatureMap;
    private BiomeType[,] biomeMap;
    private SurfaceType[,] surfaceTypeMap;
    private WaterState[,] waterStateMap;
    private float[,] riverMaskMap;
    private Texture2D[] controlMapData;

    private Dictionary<int, Mesh> LODTerrainMeshes = new Dictionary<int, Mesh>();
    private Dictionary<int, Mesh> LODLakeMeshes = new Dictionary<int, Mesh>();
    private Dictionary<int, Mesh> LODRiverMeshes = new Dictionary<int, Mesh>();

    private Mesh colliderMesh;

    private bool colliderRequestInFlight;
    private int colliderRequestVersion;

    private bool colliderReady;
    private bool colliderDesired;

    private bool terrainDataRequestInFlight;
    private int terrainDataRequestVersion;

    private Dictionary<int, int> meshRequestVersionsByLOD = new Dictionary<int, int>();
    private HashSet<int> meshRequestsInFlight = new HashSet<int>();

    public ChunkCoord ChunkCoord => chunkCoord;
    public ChunkRuntime ActiveRuntime => activeRuntime;
    public bool IsLoaded => activeRuntime != null;
    public bool HasTerrainData =>
        heightMap != null &&
        moistureMap != null &&
        temperatureMap != null &&
        biomeMap != null &&
        surfaceTypeMap != null &&
        waterStateMap != null &&
        riverMaskMap != null &&
        controlMapData != null;
    public float[,] HeightMap => heightMap;
    public float[,] MoistureMap => moistureMap;
    public float[,] TemperatureMap => temperatureMap;
    public BiomeType[,] BiomeMap => biomeMap;
    public SurfaceType[,] SurfaceTypeMap => surfaceTypeMap;
    public WaterState[,] WaterStateMap => waterStateMap;
    public float[,] RiverMaskMap => riverMaskMap;
    public Texture2D[] ControlMapData => controlMapData;
    public bool IsTerrainDataRequestInFlight => terrainDataRequestInFlight;
    public int TerrainDataRequestVersion => terrainDataRequestVersion;

    public bool ColliderRequestInFlight => colliderRequestInFlight;
    public bool ColliderReady => colliderReady;
    public bool ColliderDesired
    {
        get => colliderDesired;
        set => colliderDesired = value;
    }

    public ChunkRecord(ChunkCoord chunkCoord)
    {
        this.chunkCoord = chunkCoord;
    }

    public int BeginColliderRequest()
    {
        colliderRequestInFlight = true;
        colliderRequestVersion++;
        return colliderRequestVersion;
    }
    public bool TryCompleteColliderRequest(int requestVersion, Mesh mesh)
    {
        if (!colliderRequestInFlight || requestVersion != colliderRequestVersion)
            return false;

        colliderRequestInFlight = false;

        colliderMesh = mesh;
        colliderReady = true;

        return true;
    }

    public bool TryGetColliderMesh(out Mesh mesh)
    {
        mesh = colliderMesh;
        return colliderReady && colliderMesh != null;
    }

    public void ClearColliderMesh()
    {
        colliderMesh = null;
        colliderReady = false;
    }

    public bool TryGetLODTerrainMesh(int lod, out Mesh terrainMesh)
    {
        return LODTerrainMeshes.TryGetValue(lod, out terrainMesh);
    }

    public bool TryGetLODLakeMesh(int lod, out Mesh lakeMesh)
    {
        return LODLakeMeshes.TryGetValue(lod, out lakeMesh);
    }

    public bool TryGetLODRiverMesh(int lod, out Mesh riverMesh)
    {
        return LODRiverMeshes.TryGetValue(lod, out riverMesh);
    }

    public void StoreLODTerrainMesh(int lod, Mesh terrainMesh)
    {
        LODTerrainMeshes[lod] = terrainMesh;
    }

    public void StoreLODLakeMesh(int lod, Mesh lakeMesh)
    {
        LODLakeMeshes[lod] = lakeMesh;
    }

    public void StoreLODRiverMesh(int lod, Mesh riverMesh)
    {
        LODRiverMeshes[lod] = riverMesh;
    }

    public void ClearLODTerrainMeshes()
    {
        LODTerrainMeshes.Clear();
    }

    public void ClearLODLakeMeshes()
    {
        LODLakeMeshes.Clear();
    }

    public void ClearLODRiverMeshes()
    {
        LODRiverMeshes.Clear();
    }

    public void ClearAllLODMeshes()
    {
        LODTerrainMeshes.Clear();
        LODLakeMeshes.Clear();
        LODRiverMeshes.Clear();
    }

    public void SetActiveRuntime(ChunkRuntime activeRuntime)
    {
        this.activeRuntime = activeRuntime;
    }

    public void ClearActiveRuntime(ChunkRuntime runtime)
    {
        if (activeRuntime == runtime) { activeRuntime = null; }
    }

    public int BeginTerrainDataRequest()
    {
        terrainDataRequestVersion++;
        terrainDataRequestInFlight = true;
        return terrainDataRequestVersion;
    }

    public void CancelTerrainDataRequest(int requestVersion)
    {
        if (terrainDataRequestVersion == requestVersion)
        {
            terrainDataRequestInFlight = false;
        }
    }

    public bool TryCompleteTerrainDataRequest(int requestVersion, float[,] returnedHeightMap,
        float[,] returnedMoistureMap, float[,] returnedTemperatureMap, BiomeType[,] returnedBiomeMap, 
        SurfaceType[,] returnedSurfaceTypeMap, WaterState[,] returnedWaterStateMap, float[,] returnedRiverMaskMap, 
        Texture2D[] returnedControlMapData)
    {
        if (!terrainDataRequestInFlight) 
            return false;
        if (requestVersion != terrainDataRequestVersion) 
            return false;

        heightMap = returnedHeightMap;
        moistureMap = returnedMoistureMap;
        temperatureMap = returnedTemperatureMap;
        biomeMap = returnedBiomeMap;
        surfaceTypeMap = returnedSurfaceTypeMap;
        waterStateMap = returnedWaterStateMap;
        riverMaskMap = returnedRiverMaskMap;
        controlMapData = returnedControlMapData;

        terrainDataRequestInFlight = false;
        return true;
    }

    public bool IsMeshRequestInFlight(int lod)
    {
        return meshRequestsInFlight.Contains(lod);
    }

    public int BeginMeshRequest(int lod)
    {
        int nextVersion = 1;

        if (meshRequestVersionsByLOD.TryGetValue(lod, out int currentVersion))
            nextVersion = currentVersion + 1;

        meshRequestVersionsByLOD[lod] = nextVersion;
        meshRequestsInFlight.Add(lod);
        return nextVersion;
    }

    public bool TryCompleteMeshRequest(int lod, int requestVersion, Mesh terrainMesh, Mesh lakeMesh, Mesh riverMesh)
    {
        if (!meshRequestsInFlight.Contains(lod))
            return false;

        if (!meshRequestVersionsByLOD.TryGetValue(lod, out int currentVersion))
            return false;

        if (currentVersion != requestVersion)
            return false;

        LODTerrainMeshes[lod] = terrainMesh;
        LODLakeMeshes[lod] = lakeMesh;
        LODRiverMeshes[lod] = riverMesh;
        meshRequestsInFlight.Remove(lod);
        return true;
    }

}
