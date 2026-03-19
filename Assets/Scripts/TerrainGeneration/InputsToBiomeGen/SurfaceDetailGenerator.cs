using UnityEngine;

public static class SurfaceDetailGenerator
{
    private const int MaxOctaves = 3;
    private const float Persistence = 0.5f;
    private const float Lacunarity = 2f;
    private const int SeedOffset = 50000;

    public static Vector2[] CreateOctaveOffsets(int seed)
    {
        return TerrainNoiseUtility.GenerateOctaveOffsets(seed + SeedOffset, MaxOctaves);
    }

    public static float Sample(
        float sampleX,
        float sampleZ,
        int seed,
        int octaves,
        Vector2[] octaveOffsets)
    {
        octaves = Mathf.Clamp(octaves, 1, MaxOctaves);

        float raw = TerrainNoiseUtility.SampleBasicFbm(
            sampleX,
            sampleZ,
            seed + SeedOffset,
            octaves,
            Persistence,
            Lacunarity,
            octaveOffsets
        );

        float maxPossibleHeight = TerrainNoiseUtility.ComputeMaxPossibleHeight(octaves, Persistence);
        float normalized = TerrainNoiseUtility.NormalizeSymmetric01(raw, maxPossibleHeight);

        return (normalized - 0.5f) * 2f;
    }

    public static float SampleAdaptive(
        float sampleX,
        float sampleZ,
        int seed,
        float mountainMask,
        Vector2[] octaveOffsets)
    {
        float shapedMask = Mathf.Pow(Mathf.Clamp01(mountainMask), 1.5f);

        int octaves;
        if (shapedMask < 0.25f)
            octaves = 1;
        else if (shapedMask < 0.6f)
            octaves = 1;
        else
            octaves = 3;

        float detail = Sample(sampleX, sampleZ, seed, octaves, octaveOffsets);

        float amplitudeMultiplier = Mathf.Lerp(0.15f, 1f, shapedMask);
        return detail * amplitudeMultiplier;
    }
}