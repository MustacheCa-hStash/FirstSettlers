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

    private Dictionary<int, Mesh> LODTerrainMeshes = new Dictionary<int, Mesh>();
    private Dictionary<int, Mesh> LODWaterMeshes = new Dictionary<int, Mesh>();

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
        waterStateMap != null;
    public float[,] HeightMap => heightMap;
    public float[,] MoistureMap => moistureMap;
    public float[,] TemperatureMap => temperatureMap;
    public BiomeType[,] BiomeMap => biomeMap;
    public SurfaceType[,] SurfaceTypeMap => surfaceTypeMap;
    public WaterState[,] WaterStateMap => waterStateMap;
    public float[,] RiverMaskMap => riverMaskMap;
    public bool IsTerrainDataRequestInFlight => terrainDataRequestInFlight;
    public int TerrainDataRequestVersion => terrainDataRequestVersion;

    public ChunkRecord(ChunkCoord chunkCoord)
    {
        this.chunkCoord = chunkCoord;
    }

    public bool TryGetLODTerrainMesh(int lod, out Mesh terrainMesh)
    {
        return LODTerrainMeshes.TryGetValue(lod, out terrainMesh);
    }

    public bool TryGetLODWaterMesh(int lod, out Mesh waterMesh)
    {
        return LODWaterMeshes.TryGetValue(lod, out waterMesh);
    }

    public void StoreLODTerrainMesh(int lod, Mesh terrainMesh)
    {
        LODTerrainMeshes[lod] = terrainMesh;
    }

    public void StoreLODWaterMesh(int lod, Mesh waterMesh)
    {
        LODWaterMeshes[lod] = waterMesh;
    }

    public void ClearLODTerrainMeshes()
    {
        LODTerrainMeshes.Clear();
    }

    public void ClearLODWaterMeshes()
    {
        LODWaterMeshes.Clear();
    }

    public void ClearAllLODMeshes()
    {
        LODTerrainMeshes.Clear();
        LODWaterMeshes.Clear();
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
        SurfaceType[,] returnedSurfaceTypeMap, WaterState[,] returnedWaterStateMap, float[,] returnedRiverMaskMap)
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

    public bool TryCompleteMeshRequest(int lod, int requestVersion, Mesh terrainMesh, Mesh waterMesh)
    {
        if (!meshRequestsInFlight.Contains(lod))
            return false;

        if (!meshRequestVersionsByLOD.TryGetValue(lod, out int currentVersion))
            return false;

        if (currentVersion != requestVersion)
            return false;

        LODTerrainMeshes[lod] = terrainMesh;
        LODWaterMeshes[lod] = waterMesh;
        meshRequestsInFlight.Remove(lod);
        return true;
    }

}
