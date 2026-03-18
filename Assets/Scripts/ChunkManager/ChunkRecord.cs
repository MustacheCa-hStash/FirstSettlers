using JetBrains.Annotations;
using Mono.Cecil.Cil;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class ChunkRecord
{
    private ChunkCoord chunkCoord;
    private ChunkRuntime activeRuntime;
    private float[,] heightMap;

    private Dictionary<int, Mesh> LODMeshes = new Dictionary<int, Mesh>();

    private bool heightMapRequestInFlight;
    private int heightMapRequestVersion;

    private Dictionary<int, int> meshRequestVersionsByLOD = new Dictionary<int, int>();
    private HashSet<int> meshRequestsInFlight = new HashSet<int>();

    public ChunkCoord ChunkCoord => chunkCoord;
    public ChunkRuntime ActiveRuntime => activeRuntime;
    public bool IsLoaded => activeRuntime != null;
    public bool HasHeightMap => heightMap != null;
    public float[,] HeightMap => heightMap;
    public bool IsHeightMapRequestInFlight => heightMapRequestInFlight;
    public int HeightMapRequestVersion => heightMapRequestVersion;

    public ChunkRecord(ChunkCoord chunkCoord)
    {
        this.chunkCoord = chunkCoord;
    }

    public bool TryGetLODMesh(int lod, out Mesh mesh)
    {
        return LODMeshes.TryGetValue(lod, out mesh);
    }

    public void StoreLODMesh(int lod, Mesh mesh)
    {
        LODMeshes[lod] = mesh;
    }

    public void ClearLODMeshes()
    {
        LODMeshes.Clear();
    }

    public void SetActiveRuntime(ChunkRuntime activeRuntime)
    {
        this.activeRuntime = activeRuntime;
    }

    public void ClearActiveRuntime(ChunkRuntime runtime)
    {
        if (activeRuntime == runtime) { activeRuntime = null; }
    }

    public void SetHeightMap(float[,] heightMap)
    {
        this.heightMap = heightMap; 
    }

    public int BeginHeightMapRequest()
    {
        heightMapRequestVersion++;
        heightMapRequestInFlight = true;
        return heightMapRequestVersion;
    }

    public bool TryCompleteHeightMapRequest(int requestVersion, float[,] returnedHeightMap)
    {
        if (!heightMapRequestInFlight) 
            return false;
        if (requestVersion != heightMapRequestVersion) 
            return false;

        heightMap = returnedHeightMap;
        heightMapRequestInFlight = false;
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

    public bool TryCompleteMeshRequest(int lod, int requestVersion, Mesh mesh)
    {
        if (!meshRequestsInFlight.Contains(lod))
            return false;

        if (!meshRequestVersionsByLOD.TryGetValue(lod, out int currentVersion))
            return false;

        if (currentVersion != requestVersion)
            return false;

        LODMeshes[lod] = mesh;
        meshRequestsInFlight.Remove(lod);
        return true;
    }

}
