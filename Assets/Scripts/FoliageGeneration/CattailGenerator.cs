using System.Collections.Generic;
using UnityEngine;

public static class CattailGenerator
{
    private const float DiagonalStep = 1.41421356f;

    public static IEnumerator<bool> GenerateIncrementally(
        ChunkRecord record, CattailSettings settings, int worldSeed, int chunkSize,
        float worldScale, float heightMultiplier, float waterLevel)
    {
        if (record.FoliageData == null)
            record.FoliageData = new ChunkFoliageData();

        ChunkFoliageData data = record.FoliageData;
        data.ClearCattails();
        int revision = data.CattailsRevision;
        float[,] heights = record.HeightMap;
        float[,] slopes = record.SlopeMap;
        WaterState[,] waterStates = record.WaterStateMap;
        SurfaceType[,] surfaces = record.SurfaceTypeMap;

        if (settings == null || !settings.enableCattails || heights == null ||
            waterStates == null || surfaces == null)
            yield break;

        int width = heights.GetLength(0);
        int depth = heights.GetLength(1);
        float[] toDry = new float[width * depth];
        float[] toWater = new float[width * depth];
        bool hasDry = false;
        bool hasWater = false;
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                bool dry = heights[x, z] > waterLevel;
                int index = x * depth + z;
                toDry[index] = dry ? 0f : float.PositiveInfinity;
                toWater[index] = dry ? float.PositiveInfinity : 0f;
                hasDry |= dry;
                hasWater |= !dry;
            }
            if ((x & 31) == 31) yield return true;
        }

        if (!hasDry || !hasWater)
        {
            if (IsCurrent(record, data, revision, heights, slopes, waterStates, surfaces))
                data.cattailsGenerated = true;
            yield break;
        }

        // Chamfer distances to both sides of the shoreline. Padded map samples
        // let a shoreline at a chunk boundary influence the adjacent chunk.
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                RelaxForward(toDry, x, z, depth);
                RelaxForward(toWater, x, z, depth);
            }
            if ((x & 31) == 31) yield return true;
        }
        for (int x = width - 1; x >= 0; x--)
        {
            for (int z = depth - 1; z >= 0; z--)
            {
                RelaxBackward(toDry, x, z, width, depth);
                RelaxBackward(toWater, x, z, width, depth);
            }
            if ((x & 31) == 0) yield return true;
        }

        if (!IsCurrent(record, data, revision, heights, slopes, waterStates, surfaces))
            yield break;

        float spacing = Mathf.Max(0.25f, settings.candidateSpacing) / worldScale;
        float chunkMinX = record.ChunkCoord.x * chunkSize;
        float chunkMinZ = record.ChunkCoord.z * chunkSize;
        int cellMinX = Mathf.FloorToInt(chunkMinX / spacing);
        int cellMaxX = Mathf.CeilToInt((chunkMinX + chunkSize) / spacing);
        int cellMinZ = Mathf.FloorToInt(chunkMinZ / spacing);
        int cellMaxZ = Mathf.CeilToInt((chunkMinZ + chunkSize) / spacing);
        float minScale = Mathf.Max(0.01f, Mathf.Min(settings.uniformScaleRange.x, settings.uniformScaleRange.y));
        float maxScale = Mathf.Max(minScale, Mathf.Max(settings.uniformScaleRange.x, settings.uniformScaleRange.y));
        float waterBand = Mathf.Max(0f, settings.waterwardDistance);
        float landBand = Mathf.Max(0f, settings.landwardDistance);
        float maxDepth = Mathf.Max(0f, settings.maxWaterDepth);
        float maxBankHeight = Mathf.Max(0f, settings.maxBankHeight);
        float noiseScale = Mathf.Max(0.001f, settings.colonyNoiseScale);

        for (int cellX = cellMinX; cellX < cellMaxX; cellX++)
        {
            if (!IsCurrent(record, data, revision, heights, slopes, waterStates, surfaces))
                yield break;

            for (int cellZ = cellMinZ; cellZ < cellMaxZ; cellZ++)
            {
                float sampleX = (cellX + 0.5f + (Hash01(worldSeed, settings.seedOffset, cellX, cellZ, 1) - 0.5f) * 0.65f) * spacing;
                float sampleZ = (cellZ + 0.5f + (Hash01(worldSeed, settings.seedOffset, cellX, cellZ, 2) - 0.5f) * 0.65f) * spacing;
                float localX = sampleX - chunkMinX;
                float localZ = sampleZ - chunkMinZ;
                if (localX < 0f || localX >= chunkSize || localZ < 0f || localZ >= chunkSize)
                    continue;

                int mapX = Mathf.Clamp(Mathf.RoundToInt(localX) + 1, 1, width - 2);
                int mapZ = Mathf.Clamp(Mathf.RoundToInt(localZ) + 1, 1, depth - 2);
                if (slopes != null && slopes[mapX, mapZ] > settings.maxSlope)
                    continue;

                float height = SampleHeight(heights, localX, localZ, chunkSize);
                bool underwater = height <= waterLevel;
                float distance = (underwater ? toDry : toWater)[mapX * depth + mapZ] * worldScale;
                float band = underwater ? waterBand : landBand;
                if (band <= 0f || distance > band)
                    continue;

                float heightDelta = Mathf.Abs(height - waterLevel) * heightMultiplier * worldScale;
                if (underwater)
                {
                    WaterState state = waterStates[mapX, mapZ];
                    if ((state != WaterState.Shallow && state != WaterState.Deep) || heightDelta > maxDepth)
                        continue;
                }
                else
                {
                    SurfaceType surface = surfaces[mapX, mapZ];
                    if (waterStates[mapX, mapZ] != WaterState.Wet ||
                        (surface != SurfaceType.Sand && surface != SurfaceType.Mud) ||
                        heightDelta > maxBankHeight)
                        continue;
                }

                float worldX = sampleX * worldScale;
                float worldZ = sampleZ * worldScale;
                float colony = Mathf.PerlinNoise(
                    (worldX + settings.seedOffset * 0.61f) * noiseScale,
                    (worldZ - settings.seedOffset * 0.43f) * noiseScale);
                if (colony < settings.colonyThreshold)
                    continue;

                float edgeT = Mathf.Clamp01(distance / band);
                float density = settings.spawnChance * Mathf.Lerp(1f, 0.25f, edgeT * edgeT);
                density *= Mathf.Lerp(0.55f, 1f,
                    Mathf.InverseLerp(settings.colonyThreshold,
                        Mathf.Min(1f, settings.colonyThreshold + 0.25f), colony));
                if (underwater && maxDepth > 0f)
                    density *= Mathf.Lerp(1f, 0.65f, heightDelta / maxDepth);
                if (Hash01(worldSeed, settings.seedOffset, cellX, cellZ, 3) > density)
                    continue;

                float scale = Mathf.Lerp(minScale, maxScale,
                    Hash01(worldSeed, settings.seedOffset, cellX, cellZ, 4));
                float yaw = Hash01(worldSeed, settings.seedOffset, cellX, cellZ, 5) * 360f;
                data.cattailInstances.Add(new CattailInstanceData(
                    new Vector3((localX - chunkSize * 0.5f) * worldScale,
                        height * heightMultiplier * worldScale + settings.rootHeightOffset,
                        (localZ - chunkSize * 0.5f) * worldScale),
                    Quaternion.Euler(0f, yaw, 0f), scale));
            }
            if ((cellX & 15) == 15) yield return true;
        }

        if (IsCurrent(record, data, revision, heights, slopes, waterStates, surfaces))
            data.cattailsGenerated = true;
    }

    private static bool IsCurrent(ChunkRecord record, ChunkFoliageData data, int revision,
        float[,] heights, float[,] slopes, WaterState[,] waterStates, SurfaceType[,] surfaces)
    {
        return ReferenceEquals(record.FoliageData, data) && revision == data.CattailsRevision &&
               ReferenceEquals(record.HeightMap, heights) && ReferenceEquals(record.SlopeMap, slopes) &&
               ReferenceEquals(record.WaterStateMap, waterStates) && ReferenceEquals(record.SurfaceTypeMap, surfaces);
    }

    private static void RelaxForward(float[] distances, int x, int z, int depth)
    {
        int index = x * depth + z;
        if (x > 0) distances[index] = Mathf.Min(distances[index], distances[(x - 1) * depth + z] + 1f);
        if (z > 0) distances[index] = Mathf.Min(distances[index], distances[index - 1] + 1f);
        if (x > 0 && z > 0) distances[index] = Mathf.Min(distances[index], distances[(x - 1) * depth + z - 1] + DiagonalStep);
        if (x > 0 && z + 1 < depth) distances[index] = Mathf.Min(distances[index], distances[(x - 1) * depth + z + 1] + DiagonalStep);
    }

    private static void RelaxBackward(float[] distances, int x, int z, int width, int depth)
    {
        int index = x * depth + z;
        if (x + 1 < width) distances[index] = Mathf.Min(distances[index], distances[(x + 1) * depth + z] + 1f);
        if (z + 1 < depth) distances[index] = Mathf.Min(distances[index], distances[index + 1] + 1f);
        if (x + 1 < width && z + 1 < depth) distances[index] = Mathf.Min(distances[index], distances[(x + 1) * depth + z + 1] + DiagonalStep);
        if (x + 1 < width && z > 0) distances[index] = Mathf.Min(distances[index], distances[(x + 1) * depth + z - 1] + DiagonalStep);
    }

    private static float SampleHeight(float[,] map, float x, float z, int chunkSize)
    {
        x = Mathf.Clamp(x, 0f, chunkSize);
        z = Mathf.Clamp(z, 0f, chunkSize);
        int x0 = Mathf.FloorToInt(x) + 1;
        int z0 = Mathf.FloorToInt(z) + 1;
        int x1 = Mathf.Min(x0 + 1, map.GetLength(0) - 1);
        int z1 = Mathf.Min(z0 + 1, map.GetLength(1) - 1);
        float tx = x - Mathf.Floor(x);
        float tz = z - Mathf.Floor(z);
        return Mathf.Lerp(Mathf.Lerp(map[x0, z0], map[x1, z0], tx),
            Mathf.Lerp(map[x0, z1], map[x1, z1], tx), tz);
    }

    private static float Hash01(int seed, int offset, int x, int z, int salt)
    {
        unchecked
        {
            uint h = (uint)(seed + offset + salt * 7919);
            h ^= (uint)x * 0x9E3779B9u;
            h ^= (uint)z * 0x85EBCA6Bu;
            h ^= h >> 16;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            return (h & 0x00FFFFFFu) / 16777215f;
        }
    }
}
