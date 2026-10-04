using UnityEngine;

public readonly struct WorldDebugInfo
{
    public readonly Vector3 WorldPosition;
    public readonly ChunkCoord ChunkCoord;
    public readonly bool HasChunkRecord;
    public readonly bool HasTerrainData;
    public readonly BiomeType Biome;
    public readonly float ForestMembership;
    public readonly SurfaceType SurfaceType;
    public readonly float WorldHeight;
    public readonly float Slope;
    public readonly float Moisture;
    public readonly float Temperature;
    public readonly float RiverMask;

    public WorldDebugInfo(
        Vector3 worldPosition,
        ChunkCoord chunkCoord,
        bool hasChunkRecord,
        bool hasTerrainData,
        BiomeType biome,
        SurfaceType surfaceType,
        float worldHeight,
        float slope,
        float moisture,
        float temperature,
        float riverMask, float forestMembership = -1f)
    {
        WorldPosition = worldPosition;
        ChunkCoord = chunkCoord;
        HasChunkRecord = hasChunkRecord;
        HasTerrainData = hasTerrainData;
        Biome = biome;
        ForestMembership = forestMembership;
        SurfaceType = surfaceType;
        WorldHeight = worldHeight;
        Slope = slope;
        Moisture = moisture;
        Temperature = temperature;
        RiverMask = riverMask;
    }
}
