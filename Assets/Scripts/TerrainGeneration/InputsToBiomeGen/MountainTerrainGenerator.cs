using UnityEngine;

public static class MountainTerrainGenerator
{
    const int octaves = 4;
    const float persistence = 0.5f;
    const float lacunarity = 2f;

    static readonly float maxPossibleHeight = TerrainNoiseUtility.ComputeMaxPossibleHeight(octaves, persistence);
    public static float Sample(float sampleX, float sampleZ, Vector2[] octaveOffsets)
    {
        float raw = TerrainNoiseUtility.SampleBasicFbm(sampleX, sampleZ, 0, octaves, persistence, lacunarity, octaveOffsets);
        float normalized = TerrainNoiseUtility.NormalizeSymmetric01(raw, maxPossibleHeight);
        normalized = Mathf.Pow(normalized, 2.2f);

        return normalized;
    }
}