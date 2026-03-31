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

        for (int cellZ = 0; cellZ < cellsPerAxis; cellZ++)
        {
            for (int cellX = 0; cellX < cellsPerAxis; cellX++)
            {
                float sampleX = (cellX + 0.5f) * cellSize;
                float sampleZ = (cellZ + 0.5f) * cellSize;

                int mapX = Mathf.Clamp(Mathf.RoundToInt(sampleX), 0, chunkSize);
                int mapZ = Mathf.Clamp(Mathf.RoundToInt(sampleZ), 0, chunkSize);

                int paddedX = mapX + 1;
                int paddedZ = mapZ + 1;

                if (record.SurfaceTypeMap[paddedX, paddedZ] != SurfaceType.Grass)
                {
                    continue;
                }

                float height = record.HeightMap[paddedX, paddedZ];

                float localX = (topLeftX + sampleX) * worldScale;
                float localZ = (bottomLeftZ + sampleZ) * worldScale;
                float localY = height * meshHeightMultiplier * worldScale;

                float yaw = 0f;
                if (grassSettings.randomizeYaw)
                {
                    yaw = GetDeterministicYaw(worldSeed, grassSettings.seedOffset, record.ChunkCoord, cellX, cellZ);
                }

                float uniformScale = GetDeterministicScale(
                    worldSeed,
                    grassSettings.seedOffset,
                    record.ChunkCoord,
                    cellX,
                    cellZ,
                    grassSettings.uniformScaleRange);

                Vector3 localPosition = new Vector3(localX, localY, localZ);
                Quaternion localRotation = Quaternion.Euler(0f, yaw, 0f);
                Vector3 localScale = Vector3.one * uniformScale;

                foliageData.grassInstances.Add(
                    new FoliageInstanceData(localPosition, localRotation, localScale));
            }
        }

        foliageData.grassGenerated = true;
    }

    private static float GetDeterministicYaw(int worldSeed, int seedOffset, ChunkCoord chunkCoord, int cellX, int cellZ)
    {
        int hash = Hash(worldSeed, seedOffset, chunkCoord.x, chunkCoord.z, cellX, cellZ, 17);
        float t = Hash01(hash);
        return t * 360f;
    }

    private static float GetDeterministicScale(int worldSeed, int seedOffset, ChunkCoord chunkCoord, int cellX, int cellZ, Vector2 scaleRange)
    {
        int hash = Hash(worldSeed, seedOffset, chunkCoord.x, chunkCoord.z, cellX, cellZ, 29);
        float t = Hash01(hash);
        return Mathf.Lerp(scaleRange.x, scaleRange.y, t);
    }

    private static int Hash(params int[] values)
    {
        unchecked
        {
            int hash = 17;
            for (int i = 0; i < values.Length; i++)
            {
                hash = hash * 31 + values[i];
            }
            return hash;
        }
    }

    private static float Hash01(int hash)
    {
        unchecked
        {
            uint value = (uint)hash;
            return (value & 0x00FFFFFF) / 16777215f;
        }
    }
}