using UnityEngine;

public static class TerrainNoiseUtility
{
    public static Vector2[] GenerateOctaveOffsets(int seed, int octaves)
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

    public static float ComputeMaxPossibleHeight(int octaves, float persistence)
    {
        float maxPossibleHeight = 0f;
        float amplitude = 1f;

        for (int i = 0; i < octaves; i++)
        {
            maxPossibleHeight += amplitude;
            amplitude *= persistence;
        }

        return maxPossibleHeight;
    }

    public static float NormalizeSymmetric01(float value, float maxAbsValue)
    {
        return Mathf.Clamp01((value + maxAbsValue) / (2f * maxAbsValue));
    }

    public static float SampleBasicFbm(
        float x,
        float z,
        int seed,
        int octaves,
        float persistence,
        float lacunarity,
        Vector2[] octaveOffsets)
    {
        float amplitude = 1f;
        float frequency = 1f;
        float value = 0f;

        for (int o = 0; o < octaves; o++)
        {
            float octaveX = (x + octaveOffsets[o].x) * frequency;
            float octaveZ = (z + octaveOffsets[o].y) * frequency;

            NoiseSample2D sample = AnalyticValueNoise2D.Sample(octaveX, octaveZ, seed + o * 1009);
            value += sample.Value * amplitude;

            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return value;
    }

    public static float SamplePerlinFbm(float x, float z, int octaves, float persistence, 
        float lacunarity, Vector2[] octaveOffsets)
    {
        float amplitude = 1f;
        float frequency = 1f;
        float value = 0f;

        for (int o = 0; o < octaves; o++)
        {
            float octaveX = (x + octaveOffsets[o].x) * frequency;
            float octaveZ = (z + octaveOffsets[o].y) * frequency;

            float perlinValue = Mathf.PerlinNoise(octaveX, octaveZ) * 2f - 1f;
            value += perlinValue * amplitude;

            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return value;
    }
}