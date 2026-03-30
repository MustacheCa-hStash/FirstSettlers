using UnityEngine;

public static class FoliageGenerator
{
    public static void GenerateGrassForChunk(ChunkRecord record, GrassSettings grassSettings, int worldSeed, int chunkSize, float worldScale, float meshHeightMultiplier)
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

        for (int cellZ = 0; cellZ < cellsPerAxis; cellZ++)
        {
            for (int cellX = 0; cellX < cellsPerAxis; cellX++)
            {
                int mapX = Mathf.Clamp(Mathf.FloorToInt((cellX + 0.5f) * cellSize), 0, chunkSize - 1);
                int mapZ = Mathf.Clamp(Mathf.FloorToInt((cellZ + 0.5f) * cellSize), 0, chunkSize - 1);

                if (record.SurfaceTypeMap[mapZ, mapX] != SurfaceType.Grass)
                {
                    continue;
                }

                float localX = (cellX + 0.5f) * cellSize * worldScale;
                float localZ = (cellZ + 0.5f) * cellSize * worldScale;

                float height = record.HeightMap[mapZ + 1, mapX + 1];
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

                foliageData.grassInstances.Add(new FoliageInstanceData(localPosition, localRotation, localScale));
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