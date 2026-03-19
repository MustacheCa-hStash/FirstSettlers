using UnityEditor.Callbacks;
using UnityEngine;

public static class HeightMapGenerator
{
    public static float[,] GenerateTerrainHeightMap(int chunkSize, int seed, float sampleScale, int octaves, float persistence, 
        float lacunarity, ChunkCoord chunkCoord)
    {
        float[,] terrainHeightMap = new float[chunkSize + 1, chunkSize + 1];
        System.Random prng = new System.Random(seed);
        Vector2[] octaveOffsets = new Vector2[octaves];

        float maxPossibleHeight = 0;
        float amplitude = 1;

        for (int i = 0; i < octaves; i++)
        {
            float offsetX = prng.Next(-100000, 100000);
            float offsetY = prng.Next(-100000, 100000);
            octaveOffsets[i] = new Vector2(offsetX, offsetY);

            maxPossibleHeight += amplitude;
            amplitude *= persistence;
        }

        if (sampleScale <= 0f)
            sampleScale = 0.0001f;

        if (lacunarity < 1f)
            lacunarity = 1f;

        for (int x = 0; x < chunkSize + 1; x++)
        {
            for (int z = 0; z < chunkSize + 1; z++)
            {
                amplitude = 1;
                float frequency = 1;
                float noiseHeight = 0;

                for (int o = 0; o < octaves; o++)
                {
                    float sampleX = (chunkCoord.x * chunkSize + x + octaveOffsets[o].x) / sampleScale * frequency;
                    float sampleZ = (chunkCoord.z * chunkSize + z + octaveOffsets[o].y) / sampleScale * frequency;
                    float perlinValue = Mathf.PerlinNoise(sampleX, sampleZ) * 2 - 1;

                    noiseHeight += perlinValue * amplitude;
                    amplitude *= persistence;
                    frequency *= lacunarity;
                }

                terrainHeightMap[x, z] = noiseHeight;
            }
        }

        for (int x = 0; x < chunkSize + 1; x++)
        {
            for (int z = 0; z < chunkSize + 1; z++)
            {
                float normalizedHeight01 = NormalizeHeight01(terrainHeightMap[x, z], maxPossibleHeight);
                terrainHeightMap[x, z] = ApplyHeightPipeline(normalizedHeight01);
            }
        }

        return terrainHeightMap;
    }

    public static float[,] ApplyBiomeHeightModifiers(float[,] rawHeightMap, BiomeType[,] biomeMap)
    {
        int width = rawHeightMap.GetLength(0);
        float[,] postProcessedHeight = new float[width, width];

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < width; z++)
            {
                postProcessedHeight[x, z] = GetPostProcessedHeightFromBiomeType(rawHeightMap[x, z], biomeMap[x, z]);
            }
        }

        return postProcessedHeight;
    }

    private static float GetPostProcessedHeightFromBiomeType(float rawHeight, BiomeType biomeType)
    {
        float postProcessedHeight = rawHeight;

        if (biomeType == BiomeType.Rock)
        {
            float influence = Mathf.SmoothStep(0f, 1f, rawHeight);
            float extraHeight = influence * influence * 0.4f;
            postProcessedHeight = Mathf.Clamp01(rawHeight + extraHeight);
        }

        return postProcessedHeight;
    }

    private static float NormalizeHeight01(float rawHeight, float maxPossibleHeight)
    {
        float normalizedHeight = (rawHeight +  maxPossibleHeight) / (2f * maxPossibleHeight);
        return Mathf.Clamp01(normalizedHeight);
    }

    private static float ApplyHeightPipeline(float normalizedHeight)
    {
        float height = ApplyWaterFlattening(normalizedHeight);
        return height;
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

}
