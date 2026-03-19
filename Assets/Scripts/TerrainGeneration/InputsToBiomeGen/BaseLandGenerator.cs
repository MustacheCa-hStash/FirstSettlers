using UnityEngine;

public static class BaseLandGenerator
{
    const int octaves = 4;
    const float persistence = 0.5f;
    const float lacunarity = 2f;
    const float flatteningFactor = 1.15f; //increase for more flattening, inverse is true

    static readonly float maxPossibleHeight = TerrainNoiseUtility.ComputeMaxPossibleHeight(octaves, persistence);
    public static float Sample(float sampleX, float sampleZ, int seed, Vector2[] octaveOffsets)
    {
        float raw = TerrainNoiseUtility.SamplePerlinFbm(sampleX, sampleZ, octaves, persistence, lacunarity, octaveOffsets);
        float normalized = TerrainNoiseUtility.NormalizeSymmetric01(raw, maxPossibleHeight);

        normalized = Mathf.Pow(normalized, flatteningFactor);

        return normalized;
    }
}