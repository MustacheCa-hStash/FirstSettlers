using UnityEngine;

public static class FoliageGenerator
{
    public static void GenerateGrassForChunk(
        ChunkRecord record,
        GrassSettings grassSettings,
        int worldSeed,
        int chunkSize,
        float worldScale,
        float meshHeightMultiplier)
    {
        if (record.FoliageData == null)
        {
            record.FoliageData = new ChunkFoliageData();
        }

        ChunkFoliageData foliageData = record.FoliageData;
        foliageData.Clear();

        if (record.SurfaceTypeMap == null || record.HeightMap == null)
        {
            return;
        }

        int cellsPerAxis = Mathf.Max(1, grassSettings.cellsPerAxis);
        float cellSize = (float)chunkSize / cellsPerAxis;

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;

        // Tune these as needed.
        // Typical outcome per valid cell:
        // - 0 clumps: common
        // - 1 clump: most common non-zero result
        // - 2 clumps: occasional
        const float singleClumpChance = 0.55f;
        const float secondClumpChance = 0.12f;

        // Jitter is applied around the cell center.
        // 0.35 means up to 35% of half-cell extent in each axis.
        const float jitterFractionOfHalfCell = 0.35f;

        for (int cellZ = 0; cellZ < cellsPerAxis; cellZ++)
        {
            for (int cellX = 0; cellX < cellsPerAxis; cellX++)
            {
                int clumpCount = GetDeterministicClumpCount(
                    worldSeed,
                    grassSettings.seedOffset,
                    record.ChunkCoord,
                    cellX,
                    cellZ,
                    singleClumpChance,
                    secondClumpChance);

                if (clumpCount == 0)
                    continue;

                float cellCenterX = (cellX + 0.5f) * cellSize;
                float cellCenterZ = (cellZ + 0.5f) * cellSize;

                float halfCell = cellSize * 0.5f;
                float maxJitter = halfCell * jitterFractionOfHalfCell;

                for (int clumpIndex = 0; clumpIndex < clumpCount; clumpIndex++)
                {
                    Vector2 jitter = GetDeterministicJitter(
                        worldSeed,
                        grassSettings.seedOffset,
                        record.ChunkCoord,
                        cellX,
                        cellZ,
                        clumpIndex,
                        maxJitter);

                    float sampleX = Mathf.Clamp(cellCenterX + jitter.x, 0f, chunkSize);
                    float sampleZ = Mathf.Clamp(cellCenterZ + jitter.y, 0f, chunkSize);

                    int mapX = Mathf.Clamp(Mathf.RoundToInt(sampleX), 0, chunkSize);
                    int mapZ = Mathf.Clamp(Mathf.RoundToInt(sampleZ), 0, chunkSize);

                    int paddedX = mapX + 1;
                    int paddedZ = mapZ + 1;

                    if (record.SurfaceTypeMap[paddedX, paddedZ] != SurfaceType.Grass)
                        continue;

                    float height = record.HeightMap[paddedX, paddedZ];

                    float localX = (topLeftX + sampleX) * worldScale;
                    float localZ = (bottomLeftZ + sampleZ) * worldScale;
                    float localY = height * meshHeightMultiplier * worldScale;

                    float yaw = 0f;
                    if (grassSettings.randomizeYaw)
                    {
                        yaw = GetDeterministicYaw(
                            worldSeed,
                            grassSettings.seedOffset,
                            record.ChunkCoord,
                            cellX,
                            cellZ,
                            clumpIndex);
                    }

                    float uniformScale = GetDeterministicScale(
                        worldSeed,
                        grassSettings.seedOffset,
                        record.ChunkCoord,
                        cellX,
                        cellZ,
                        clumpIndex,
                        grassSettings.uniformScaleRange);

                    Vector3 localPosition = new Vector3(localX, localY, localZ);
                    Quaternion localRotation = Quaternion.Euler(0f, yaw, 0f);
                    Vector3 localScale = Vector3.one * uniformScale;

                    foliageData.grassInstances.Add(
                        new FoliageInstanceData(localPosition, localRotation, localScale));
                }
            }
        }

        foliageData.grassGenerated = true;
    }

    private static int GetDeterministicClumpCount(
        int worldSeed,
        int seedOffset,
        ChunkCoord chunkCoord,
        int cellX,
        int cellZ,
        float singleClumpChance,
        float secondClumpChance)
    {
        int hash = Hash(worldSeed, seedOffset, chunkCoord.x, chunkCoord.z, cellX, cellZ, 7);
        float t = Hash01(hash);

        if (t < secondClumpChance)
            return 2;

        if (t < secondClumpChance + singleClumpChance)
            return 1;

        return 0;
    }

    private static Vector2 GetDeterministicJitter(
        int worldSeed,
        int seedOffset,
        ChunkCoord chunkCoord,
        int cellX,
        int cellZ,
        int clumpIndex,
        float maxJitter)
    {
        int hashX = Hash(worldSeed, seedOffset, chunkCoord.x, chunkCoord.z, cellX, cellZ, clumpIndex, 101);
        int hashZ = Hash(worldSeed, seedOffset, chunkCoord.x, chunkCoord.z, cellX, cellZ, clumpIndex, 211);

        float tx = Hash01(hashX);
        float tz = Hash01(hashZ);

        float offsetX = Mathf.Lerp(-maxJitter, maxJitter, tx);
        float offsetZ = Mathf.Lerp(-maxJitter, maxJitter, tz);

        return new Vector2(offsetX, offsetZ);
    }

    private static float GetDeterministicYaw(
        int worldSeed,
        int seedOffset,
        ChunkCoord chunkCoord,
        int cellX,
        int cellZ,
        int clumpIndex)
    {
        int hash = Hash(worldSeed, seedOffset, chunkCoord.x, chunkCoord.z, cellX, cellZ, clumpIndex, 17);
        float t = Hash01(hash);
        return t * 360f;
    }

    private static float GetDeterministicScale(
        int worldSeed,
        int seedOffset,
        ChunkCoord chunkCoord,
        int cellX,
        int cellZ,
        int clumpIndex,
        Vector2 scaleRange)
    {
        int hash = Hash(worldSeed, seedOffset, chunkCoord.x, chunkCoord.z, cellX, cellZ, clumpIndex, 29);
        float t = Hash01(hash);
        return Mathf.Lerp(scaleRange.x, scaleRange.y, t);
    }

    private static int Hash(params int[] values)
    {
        unchecked
        {
            uint hash = 2166136261u;

            for (int i = 0; i < values.Length; i++)
            {
                hash ^= (uint)values[i];
                hash *= 16777619u;

                hash ^= hash >> 13;
                hash *= 1274126177u;
                hash ^= hash >> 16;
            }

            return (int)hash;
        }
    }

    private static float Hash01(int hash)
    {
        unchecked
        {
            uint value = (uint)hash;

            value ^= value >> 17;
            value *= 0xed5ad4bbu;
            value ^= value >> 11;
            value *= 0xac4c1b51u;
            value ^= value >> 15;
            value *= 0x31848babu;
            value ^= value >> 14;

            return value / 4294967295f;
        }
    }
}