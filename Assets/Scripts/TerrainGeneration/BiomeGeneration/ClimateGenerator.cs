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

        int size = chunkSize + 3;
        float[,] terrainNoiseMap = new float[size, size];

        System.Random prng = new System.Random(seed);
        Vector2[] octaveOffsets = new Vector2[noiseOctaves];

        float maxPossibleNoise = 0f;
        float amplitude = 1f;

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

        for (int x = 0; x < size; x++)
        {
            for (int z = 0; z < size; z++)
            {
                int localSampleX = x - 1;
                int localSampleZ = z - 1;

                float worldX = chunkCoord.x * chunkSize + localSampleX;
                float worldZ = chunkCoord.z * chunkSize + localSampleZ;

                amplitude = 1f;
                float frequency = 1f;
                float noise = 0f;

                for (int o = 0; o < noiseOctaves; o++)
                {
                    float sampleX = (worldX / noiseSampleScale + octaveOffsets[o].x) * frequency;
                    float sampleZ = (worldZ / noiseSampleScale + octaveOffsets[o].y) * frequency;

                    float perlinValue = Mathf.PerlinNoise(sampleX, sampleZ) * 2f - 1f;

                    noise += perlinValue * amplitude;
                    amplitude *= noisePersistence;
                    frequency *= noiseLacunarity;
                }

                terrainNoiseMap[x, z] = noise;
            }
        }

        for (int x = 0; x < size; x++)
        {
            for (int z = 0; z < size; z++)
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