using UnityEngine;

// Terrain maps have one padded sample on each side of the chunk's vertex grid.
public static class ButterflyTerrainSampler
{
    public static bool TrySampleDryGround(ChunkRecord record, ChunkCoord coord,
        float worldX, float worldZ, int chunkSize, float worldScale, float terrainHeightScale,
        float waterSurfaceY, float shoreBuffer, out float groundY)
    {
        groundY = 0f;
        if (record == null || record.HeightMap == null || record.WaterStateMap == null ||
            chunkSize < 1 || worldScale <= 0f)
            return false;

        float chunkWorldSize = chunkSize * worldScale;
        float sampleX = Mathf.Clamp((worldX - coord.x * chunkWorldSize) / worldScale, 0f, chunkSize);
        float sampleZ = Mathf.Clamp((worldZ - coord.z * chunkWorldSize) / worldScale, 0f, chunkSize);
        int x0 = Mathf.FloorToInt(sampleX);
        int z0 = Mathf.FloorToInt(sampleZ);
        int x1 = Mathf.Min(x0 + 1, chunkSize);
        int z1 = Mathf.Min(z0 + 1, chunkSize);
        float[,] heights = record.HeightMap;
        WaterState[,] water = record.WaterStateMap;
        if (heights.GetLength(0) <= x1 + 1 || heights.GetLength(1) <= z1 + 1 ||
            water.GetLength(0) <= x1 + 1 || water.GetLength(1) <= z1 + 1)
            return false;

        int px0 = x0 + 1, px1 = x1 + 1, pz0 = z0 + 1, pz1 = z1 + 1;
        float hx0 = Mathf.Lerp(heights[px0, pz0], heights[px1, pz0], sampleX - x0);
        float hx1 = Mathf.Lerp(heights[px0, pz1], heights[px1, pz1], sampleX - x0);
        groundY = Mathf.Lerp(hx0, hx1, sampleZ - z0) * terrainHeightScale;
        if (groundY <= waterSurfaceY + Mathf.Max(0f, shoreBuffer))
            return false;
        int nearestX = Mathf.RoundToInt(sampleX) + 1;
        int nearestZ = Mathf.RoundToInt(sampleZ) + 1;
        return water[nearestX, nearestZ] == WaterState.Dry;
    }
}
