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
    public readonly float WorldHeight;
    public readonly float Moisture;
    public readonly float Temperature;
    public readonly float RiverMask;
    public readonly int GpuGrassInstanceCount;
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
        float worldHeight,
        float moisture,
        float temperature,
        float riverMask,
        int gpuGrassInstanceCount,
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
        WorldHeight = worldHeight;
        Moisture = moisture;
        Temperature = temperature;
        RiverMask = riverMask;
        GpuGrassInstanceCount = gpuGrassInstanceCount;
        GpuTreeInstanceCount = gpuTreeInstanceCount;
    }
}
