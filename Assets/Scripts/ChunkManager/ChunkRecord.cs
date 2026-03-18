using System.Collections.Generic;
using UnityEngine;

public class ChunkRecord
{
    private ChunkCoord chunkCoord;
    private ChunkRuntime activeRuntime;
    private float[,] heightMap;

    private Dictionary<int, Mesh> LODMeshes = new Dictionary<int, Mesh>();

    public ChunkCoord ChunkCoord => chunkCoord;
    public ChunkRuntime ActiveRuntime => activeRuntime;
    public bool IsLoaded => activeRuntime != null;
    public bool HasHeightMap => heightMap != null;
    public float[,] HeightMap => heightMap;

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
}
