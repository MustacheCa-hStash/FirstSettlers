using UnityEngine;

public static class ClimateGenerator
{
    public static float[,] GenerateTerrainMoistureMap(int chunkSize, int seed, float sampleScale, int octaves, float persistence,
        float lacunarity, ChunkCoord chunkCoord)
    {
        return GenerateTerrainClimateMap(chunkSize, seed + 1000, sampleScale * 10f, octaves, persistence, lacunarity, chunkCoord);
    }

    public static float[,] GenerateTerrainTemperatureMap(int chunkSize, int seed, float sampleScale, int octaves, float persistence,
        float lacunarity, ChunkCoord chunkCoord)
    {
        return GenerateTerrainClimateMap(chunkSize, seed + 2000, sampleScale * 12f, octaves, persistence, lacunarity, chunkCoord);
    }

    private static float[,] GenerateTerrainClimateMap(int chunkSize, int seed, float sampleScale, int octaves, float persistence,
        float lacunarity, ChunkCoord chunkCoord)
    {
        float noiseSampleScale = sampleScale;
        int noiseOctaves = Mathf.Max(1, octaves - 2);
        float noisePersistence = persistence;
        float noiseLacunarity = lacunarity;

        float[,] terrainNoiseMap = new float[chunkSize + 1, chunkSize + 1];
        System.Random prng = new System.Random(seed);
        Vector2[] octaveOffsets = new Vector2[noiseOctaves];

        float maxPossibleNoise = 0;
        float amplitude = 1;

        for (int i = 0; i < noiseOctaves; i++)
        {
            float offsetX = prng.Next(-100000, 100000);
            float offsetY = prng.Next(-100000, 100000);
            octaveOffsets[i] = new Vector2(offsetX, offsetY);

            maxPossibleNoise += amplitude;
            amplitude *= noisePersistence;
        }

        if (noiseSampleScale <= 0f)
            noiseSampleScale = 0.0001f;

        if (noiseLacunarity < 1f)
            noiseLacunarity = 1f;

        for (int x = 0; x < chunkSize + 1; x++)
        {
            for (int z = 0; z < chunkSize + 1; z++)
            {
                amplitude = 1;
                float frequency = 1;
                float noise = 0;

                for (int o = 0; o < noiseOctaves; o++)
                {
                    float sampleX = (chunkCoord.x * chunkSize + x + octaveOffsets[o].x) / noiseSampleScale * frequency;
                    float sampleZ = (chunkCoord.z * chunkSize + z + octaveOffsets[o].y) / noiseSampleScale * frequency;
                    float perlinValue = Mathf.PerlinNoise(sampleX, sampleZ) * 2 - 1;

                    noise += perlinValue * amplitude;
                    amplitude *= noisePersistence;
                    frequency *= noiseLacunarity;
                }

                terrainNoiseMap[x, z] = noise;
            }
        }

        for (int x = 0; x < chunkSize + 1; x++)
        {
            for (int z = 0; z < chunkSize + 1; z++)
            {
                terrainNoiseMap[x, z] = Normalize01(terrainNoiseMap[x, z], maxPossibleNoise);
            }
        }

        return terrainNoiseMap;
    }

    private static float Normalize01(float raw, float maxPossible)
    {
        float normalizedHeight = (raw + maxPossible) / (2f * maxPossible);
        return Mathf.Clamp01(normalizedHeight);
    }

}
