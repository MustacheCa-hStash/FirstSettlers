using UnityEngine;

public readonly struct WorldDebugInfo
{
    public readonly Vector3 WorldPosition;
    public readonly ChunkCoord ChunkCoord;
    public readonly bool HasChunkRecord;
    public readonly bool HasTerrainData;
    public readonly bool HasRuntime;
    public readonly bool HasFoliageRuntime;
    public readonly BiomeType Biome;
    public readonly SurfaceType SurfaceType;
    public readonly GroundCoverType GroundCoverType;
    public readonly float WorldHeight;
    public readonly float Slope;
    public readonly float Moisture;
    public readonly float Temperature;
    public readonly float RiverMask;
    public readonly int PlannedTreeCount;
    public readonly int GeneratedTreeCount;
    public readonly int TreeGameObjectCount;
    public readonly int GpuGrassInstanceCount;
    public readonly int GpuFlowerInstanceCount;
    public readonly int GpuCloverInstanceCount;
    public readonly int GpuTreeInstanceCount;

    public WorldDebugInfo(
        Vector3 worldPosition,
        ChunkCoord chunkCoord,
        bool hasChunkRecord,
        bool hasTerrainData,
        bool hasRuntime,
        bool hasFoliageRuntime,
        BiomeType biome,
        SurfaceType surfaceType,
        GroundCoverType groundCoverType,
        float worldHeight,
        float slope,
        float moisture,
        float temperature,
        float riverMask,
        int plannedTreeCount,
        int generatedTreeCount,
        int treeGameObjectCount,
        int gpuGrassInstanceCount,
        int gpuFlowerInstanceCount,
        int gpuCloverInstanceCount,
        int gpuTreeInstanceCount)
    {
        WorldPosition = worldPosition;
        ChunkCoord = chunkCoord;
        HasChunkRecord = hasChunkRecord;
        HasTerrainData = hasTerrainData;
        HasRuntime = hasRuntime;
        HasFoliageRuntime = hasFoliageRuntime;
        Biome = biome;
        SurfaceType = surfaceType;
        GroundCoverType = groundCoverType;
        WorldHeight = worldHeight;
        Slope = slope;
        Moisture = moisture;
        Temperature = temperature;
        RiverMask = riverMask;
        PlannedTreeCount = plannedTreeCount;
        GeneratedTreeCount = generatedTreeCount;
        TreeGameObjectCount = treeGameObjectCount;
        GpuGrassInstanceCount = gpuGrassInstanceCount;
        GpuFlowerInstanceCount = gpuFlowerInstanceCount;
        GpuCloverInstanceCount = gpuCloverInstanceCount;
        GpuTreeInstanceCount = gpuTreeInstanceCount;
    }
}
