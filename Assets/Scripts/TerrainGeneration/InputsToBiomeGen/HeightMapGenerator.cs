using UnityEngine;

public static class HeightMapGenerator
{
    public static HeightFieldResult GenerateTerrainHeightField(
        int chunkSize,
        int seed,
        float sampleScale,
        ChunkCoord chunkCoord)
    {
        int width = chunkSize + 1;
        int height = chunkSize + 1;

        float[,] finalHeightMap = new float[width, height];
        float[,] mountainMaskMap = new float[width, height];

        if (sampleScale <= 0f)
            sampleScale = 0.0001f;

        Vector2[] baseLandOffsets = TerrainNoiseUtility.GenerateOctaveOffsets(seed + 20000, 4);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                float worldX = chunkCoord.x * chunkSize + x;
                float worldZ = chunkCoord.z * chunkSize + z;

                float baseLandSampleX = worldX / (sampleScale * 1.6f);
                float baseLandSampleZ = worldZ / (sampleScale * 1.6f);

                float baseLand = BaseLandGenerator.Sample(
                    baseLandSampleX,
                    baseLandSampleZ,
                    seed,
                    baseLandOffsets
                );

                float finalHeight = ApplyHeightPipeline(baseLand);

                finalHeightMap[x, z] = finalHeight;
                mountainMaskMap[x, z] = 0f;
            }
        }

        ComputeFinalGradients(finalHeightMap, out float[,] gradientXMap, out float[,] gradientZMap);

        return new HeightFieldResult(finalHeightMap, gradientXMap, gradientZMap, mountainMaskMap);
    }

    public static float[,] ApplyBiomeHeightModifiers(float[,] rawHeightMap, BiomeType[,] biomeMap)
    {
        return rawHeightMap;
    }

    private static float ApplyHeightPipeline(float normalizedHeight)
    {
        return normalizedHeight;
    }

    private static float ApplyWaterFlattening(float normalizedHeight)
    {
        float waterLevel = 0.2f;
        float shoreBlend = 0.03f;

        float height = normalizedHeight;

        if (height < waterLevel)
        {
            float t = Mathf.InverseLerp(waterLevel - shoreBlend, waterLevel, height);
            height = Mathf.Lerp(waterLevel, height, t);
        }

        return height;
    }

    private static void ComputeFinalGradients(float[,] finalHeightMap, out float[,] gradientXMap, out float[,] gradientZMap)
    {
        int width = finalHeightMap.GetLength(0);
        int height = finalHeightMap.GetLength(1);

        gradientXMap = new float[width, height];
        gradientZMap = new float[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                float left = finalHeightMap[Mathf.Max(x - 1, 0), z];
                float right = finalHeightMap[Mathf.Min(x + 1, width - 1), z];
                float down = finalHeightMap[x, Mathf.Max(z - 1, 0)];
                float up = finalHeightMap[x, Mathf.Min(z + 1, height - 1)];

                float dx;
                if (x == 0)
                    dx = right - finalHeightMap[x, z];
                else if (x == width - 1)
                    dx = finalHeightMap[x, z] - left;
                else
                    dx = (right - left) * 0.5f;

                float dz;
                if (z == 0)
                    dz = up - finalHeightMap[x, z];
                else if (z == height - 1)
                    dz = finalHeightMap[x, z] - down;
                else
                    dz = (up - down) * 0.5f;

                gradientXMap[x, z] = dx;
                gradientZMap[x, z] = dz;
            }
        }
    }
}