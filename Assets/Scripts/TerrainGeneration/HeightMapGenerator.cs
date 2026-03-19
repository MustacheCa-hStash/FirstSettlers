using UnityEngine;

public static class HeightMapGenerator
{
    public static HeightFieldResult GenerateTerrainHeightMap(int chunkSize, int seed, float sampleScale, int octaves, 
        float persistence, float lacunarity, float erosionStrength, ChunkCoord chunkCoord)
    {
        int width = chunkSize + 1;
        int height = chunkSize + 1;

        float[,] rawSynthesizedHeightMap = new float[width, height];
        float[,] finalHeightMap = new float[width, height];

        if (sampleScale <= 0f)
            sampleScale = 0.0001f;

        if (lacunarity < 1f)
            lacunarity = 1f;

        Vector2[] octaveOffsets = GenerateOctaveOffsets(seed, octaves);

        float minRawHeight = float.PositiveInfinity;
        float maxRawHeight = float.NegativeInfinity;

        // Stage A:
        // Derivative-aware terrain synthesis.
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                float worldX = chunkCoord.x * chunkSize + x;
                float worldZ = chunkCoord.z * chunkSize + z;

                float sampleX = worldX / sampleScale;
                float sampleZ = worldZ / sampleScale;

                NoiseSample2D synthesized = DerivativeFbm2D.Sample(
                    sampleX,
                    sampleZ,
                    seed,
                    octaves,
                    persistence,
                    lacunarity,
                    erosionStrength,
                    octaveOffsets
                );

                float rawHeight = synthesized.Value;
                rawSynthesizedHeightMap[x, z] = rawHeight;

                if (rawHeight < minRawHeight)
                    minRawHeight = rawHeight;

                if (rawHeight > maxRawHeight)
                    maxRawHeight = rawHeight;
            }
        }

        // Stage B:
        // Normalize chunk-local raw synthesis result and apply your shaping pipeline.
        float maxPossibleHeight = 0f;
        float amplitude = 1f;

        for (int i = 0; i < octaves; i++)
        {
            maxPossibleHeight += amplitude;
            amplitude *= persistence;
        }

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                float normalizedHeight01 =
                    (rawSynthesizedHeightMap[x, z] + maxPossibleHeight)
                    / (2f * maxPossibleHeight);

                finalHeightMap[x, z] = ApplyHeightPipeline(normalizedHeight01);
            }
        }

        // Stage C:
        // Compute final terrain gradients from the final terrain surface.
        ComputeFinalGradients(finalHeightMap, out float[,] gradientXMap, out float[,] gradientZMap);

        return new HeightFieldResult(finalHeightMap, gradientXMap, gradientZMap);
    }

    //DO NOT MODIFY, WILL MESS UP FINAL GRADIENTS
    public static float[,] ApplyBiomeHeightModifiers(float[,] rawHeightMap, BiomeType[,] biomeMap)
    {
        return rawHeightMap;
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

    private static Vector2[] GenerateOctaveOffsets(int seed, int octaves)
    {
        Vector2[] octaveOffsets = new Vector2[octaves];
        System.Random prng = new System.Random(seed);

        for (int i = 0; i < octaves; i++)
        {
            float offsetX = prng.Next(-100000, 100000);
            float offsetZ = prng.Next(-100000, 100000);
            octaveOffsets[i] = new Vector2(offsetX, offsetZ);
        }

        return octaveOffsets;
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
