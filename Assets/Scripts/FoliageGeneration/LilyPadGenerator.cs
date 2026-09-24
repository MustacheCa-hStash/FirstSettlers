using System.Collections.Generic;
using UnityEngine;

public static class LilyPadGenerator
{
    // Eight-neighbor chamfer distance, in height-map samples. The map includes the
    // terrain's one-sample border, so a shore exactly at a chunk seam is visible.
    private const float DiagonalStep = 1.41421356f;

    public static IEnumerator<bool> GenerateIncrementally(
        ChunkRecord record, LilyPadSettings settings, int worldSeed, int chunkSize,
        float worldScale, float waterLevel, float waterSurfaceY)
    {
        if (record.FoliageData == null)
            record.FoliageData = new ChunkFoliageData();

        ChunkFoliageData data = record.FoliageData;
        data.ClearLilyPads();
        int revision = data.LilyPadsRevision;
        float[,] heights = record.HeightMap;
        WaterState[,] waterStates = record.WaterStateMap;

        if (settings == null || !settings.enableLilyPads || heights == null || waterStates == null)
            yield break;

        int width = heights.GetLength(0);
        int depth = heights.GetLength(1);
        float[] shoreDistance = new float[width * depth];
        bool hasWater = false;
        bool hasDryShore = false;
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                bool dry = heights[x, z] >= waterLevel;
                shoreDistance[x * depth + z] = dry ? 0f : float.PositiveInfinity;
                hasDryShore |= dry;
                hasWater |= !dry;
            }
            if ((x & 31) == 31) yield return true;
        }

        if (!hasWater || !hasDryShore)
        {
            if (ReferenceEquals(record.FoliageData, data) && revision == data.LilyPadsRevision &&
                ReferenceEquals(record.HeightMap, heights) && ReferenceEquals(record.WaterStateMap, waterStates))
                data.lilyPadsGenerated = true;
            yield break;
        }

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
            {
                int index = x * depth + z;
                if (x > 0)
                    shoreDistance[index] = Mathf.Min(shoreDistance[index], shoreDistance[(x - 1) * depth + z] + 1f);
                if (z > 0)
                    shoreDistance[index] = Mathf.Min(shoreDistance[index], shoreDistance[x * depth + z - 1] + 1f);
                if (x > 0 && z > 0)
                    shoreDistance[index] = Mathf.Min(shoreDistance[index], shoreDistance[(x - 1) * depth + z - 1] + DiagonalStep);
                if (x > 0 && z + 1 < depth)
                    shoreDistance[index] = Mathf.Min(shoreDistance[index],
                        shoreDistance[(x - 1) * depth + z + 1] + DiagonalStep);
            }
            if ((x & 31) == 31) yield return true;
        }

        for (int x = width - 1; x >= 0; x--)
        {
            for (int z = depth - 1; z >= 0; z--)
            {
                int index = x * depth + z;
                if (x + 1 < width)
                    shoreDistance[index] = Mathf.Min(shoreDistance[index], shoreDistance[(x + 1) * depth + z] + 1f);
                if (z + 1 < depth)
                    shoreDistance[index] = Mathf.Min(shoreDistance[index], shoreDistance[x * depth + z + 1] + 1f);
                if (x + 1 < width && z + 1 < depth)
                    shoreDistance[index] = Mathf.Min(shoreDistance[index], shoreDistance[(x + 1) * depth + z + 1] + DiagonalStep);
                if (x + 1 < width && z > 0)
                    shoreDistance[index] = Mathf.Min(shoreDistance[index],
                        shoreDistance[(x + 1) * depth + z - 1] + DiagonalStep);
            }
            if ((x & 31) == 0) yield return true;
        }

        float spacing = Mathf.Max(0.25f, settings.candidateSpacing) / worldScale;
        float chunkMinX = record.ChunkCoord.x * chunkSize;
        float chunkMinZ = record.ChunkCoord.z * chunkSize;
        int cellMinX = Mathf.FloorToInt(chunkMinX / spacing);
        int cellMaxX = Mathf.CeilToInt((chunkMinX + chunkSize) / spacing);
        int cellMinZ = Mathf.FloorToInt(chunkMinZ / spacing);
        int cellMaxZ = Mathf.CeilToInt((chunkMinZ + chunkSize) / spacing);
        float minShoreDistance = Mathf.Max(0f, settings.minDistanceFromShore);
        float maxShoreDistance = Mathf.Max(minShoreDistance + 0.01f, settings.maxDistanceFromShore);
        float minScale = Mathf.Max(0.01f, Mathf.Min(settings.uniformScaleRange.x, settings.uniformScaleRange.y));
        float maxScale = Mathf.Max(minScale, Mathf.Max(settings.uniformScaleRange.x, settings.uniformScaleRange.y));
        float surfaceLocalY = waterSurfaceY + Mathf.Max(0f, settings.waterSurfaceOffset);

        if (!ReferenceEquals(record.FoliageData, data) || revision != data.LilyPadsRevision ||
            !ReferenceEquals(record.HeightMap, heights) || !ReferenceEquals(record.WaterStateMap, waterStates))
            yield break;

        for (int cellX = cellMinX; cellX < cellMaxX; cellX++)
        {
            if (!ReferenceEquals(record.FoliageData, data) || revision != data.LilyPadsRevision ||
                !ReferenceEquals(record.HeightMap, heights) || !ReferenceEquals(record.WaterStateMap, waterStates))
                yield break;
            for (int cellZ = cellMinZ; cellZ < cellMaxZ; cellZ++)
            {
                float sampleX = (cellX + 0.5f + (Hash01(worldSeed, settings.seedOffset, cellX, cellZ, 1) - 0.5f) * 0.7f) * spacing;
                float sampleZ = (cellZ + 0.5f + (Hash01(worldSeed, settings.seedOffset, cellX, cellZ, 2) - 0.5f) * 0.7f) * spacing;
                float localSampleX = sampleX - chunkMinX;
                float localSampleZ = sampleZ - chunkMinZ;
                if (localSampleX < 0f || localSampleX >= chunkSize ||
                    localSampleZ < 0f || localSampleZ >= chunkSize)
                    continue;

                int mapX = Mathf.Clamp(Mathf.RoundToInt(localSampleX) + 1, 1, width - 2);
                int mapZ = Mathf.Clamp(Mathf.RoundToInt(localSampleZ) + 1, 1, depth - 2);
                WaterState state = waterStates[mapX, mapZ];
                if (state != WaterState.Shallow && state != WaterState.Deep)
                    continue;
                if (SampleHeight(heights, localSampleX, localSampleZ, chunkSize) >= waterLevel)
                    continue;

                float distance = shoreDistance[mapX * depth + mapZ] * worldScale;
                if (distance < minShoreDistance || distance > maxShoreDistance)
                    continue;

                float worldX = sampleX * worldScale;
                float worldZ = sampleZ * worldScale;
                float noiseScale = Mathf.Max(0.001f, settings.colonyNoiseScale);
                float colony = Mathf.PerlinNoise(
                    (worldX + settings.seedOffset * 0.73f) * noiseScale,
                    (worldZ - settings.seedOffset * 0.51f) * noiseScale);
                if (colony < settings.colonyThreshold)
                    continue;

                // Colonies are denser near the bank and fade toward open water.
                float shoreT = Mathf.Clamp01((distance - minShoreDistance) / (maxShoreDistance - minShoreDistance));
                float density = settings.spawnChance * Mathf.Lerp(1f, 0.2f, shoreT * shoreT);
                density *= Mathf.Lerp(0.5f, 1f,
                    Mathf.InverseLerp(settings.colonyThreshold,
                        Mathf.Min(1f, settings.colonyThreshold + 0.25f), colony));
                if (Hash01(worldSeed, settings.seedOffset, cellX, cellZ, 3) > density)
                    continue;

                float scale = Mathf.Lerp(minScale, maxScale,
                    Hash01(worldSeed, settings.seedOffset, cellX, cellZ, 4));
                float yaw = Hash01(worldSeed, settings.seedOffset, cellX, cellZ, 5) * 360f;
                data.lilyPadInstances.Add(new LilyPadInstanceData(
                    new Vector3((localSampleX - chunkSize * 0.5f) * worldScale,
                        surfaceLocalY,
                        (localSampleZ - chunkSize * 0.5f) * worldScale),
                    Quaternion.Euler(0f, yaw, 0f), scale));
            }
            if ((cellX & 15) == 15) yield return true;
            if (!ReferenceEquals(record.FoliageData, data) || revision != data.LilyPadsRevision ||
                !ReferenceEquals(record.HeightMap, heights) || !ReferenceEquals(record.WaterStateMap, waterStates))
                yield break;
        }

        data.lilyPadsGenerated = true;
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
