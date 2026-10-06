using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Owns inactive chunk/far runtime reuse and the hidden pool root, independently of coverage and generated records.</summary>
public sealed class TerrainRuntimePool : IDisposable
{
    private readonly Stack<ChunkRuntime> normal = new();
    private readonly Stack<FarTerrainTileRuntime> far = new();
    private Transform runtimePoolParent;
    private readonly Transform chunkParent;
    private readonly int chunkSize;
    private readonly float worldScale;
    private readonly Material terrainMaterial, waterMaterial;
    private readonly bool terrainReceiveShadows;
    public TerrainRuntimePool(WorldGenerationConfiguration generation, WorldRenderingConfiguration rendering, Transform parent)
    {
        chunkSize = generation.ChunkSize; worldScale = generation.WorldScale; chunkParent = parent;
        terrainMaterial = rendering.TerrainMaterial; waterMaterial = rendering.WaterMaterial; terrainReceiveShadows = rendering.TerrainReceiveShadows;
    }
    public ChunkRuntime Acquire(ChunkRecord record)
    {
        if (normal.Count == 0) return new ChunkRuntime(record, chunkSize, worldScale, chunkParent, terrainMaterial, waterMaterial, terrainReceiveShadows);
        var runtime = normal.Pop(); runtime.Reinitialize(record, chunkSize, worldScale, chunkParent, terrainReceiveShadows); return runtime;
    }
    public FarTerrainTileRuntime Acquire(FarTerrainTileRecord record)
    {
        if (far.Count == 0) return new FarTerrainTileRuntime(record, chunkSize * record.SizeInChunks, worldScale, chunkParent, terrainMaterial, terrainReceiveShadows, waterMaterial);
        var runtime = far.Pop(); runtime.Reinitialize(record, chunkSize * record.SizeInChunks, worldScale, chunkParent, terrainReceiveShadows); return runtime;
    }
    public void Release(ChunkRuntime runtime) { runtime.ReleaseToPool(GetRuntimePoolParent()); normal.Push(runtime); }
    public void Release(FarTerrainTileRuntime runtime) { runtime.ReleaseToPool(GetRuntimePoolParent()); far.Push(runtime); }
    public void Dispose()
    {
        while (normal.Count > 0) normal.Pop().DestroyRuntime();
        while (far.Count > 0) far.Pop().DestroyRuntime();
        if (runtimePoolParent != null) { Object.Destroy(runtimePoolParent.gameObject); runtimePoolParent = null; }
    }
    private Transform GetRuntimePoolParent()
    {
        if (runtimePoolParent != null)
            return runtimePoolParent;

        GameObject poolRoot = new GameObject("Terrain_Runtime_Pool");
        poolRoot.SetActive(false);
        runtimePoolParent = poolRoot.transform;
        if (chunkParent != null)
            runtimePoolParent.SetParent(chunkParent, false);
        return runtimePoolParent;
    }
}
