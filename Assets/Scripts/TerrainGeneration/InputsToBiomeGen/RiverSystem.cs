using UnityEngine;

public static class RiverGenerator
{
    const int octaves = 2;
    const float persistence = 0.3f;
    const float lacunarity = 2f;

    const float riverFalloffScale = 50f;
    const float riverSharpness = 2.0f;

    static readonly float maxPossibleHeight = TerrainNoiseUtility.ComputeMaxPossibleHeight(octaves, persistence);

    public static float Sample(float sampleX, float sampleZ, Vector2[] octaveOffsets)
    {
        float raw = TerrainNoiseUtility.SampleBasicFbm(sampleX, sampleZ, 0, octaves, persistence, lacunarity, octaveOffsets);
        float normalized = TerrainNoiseUtility.NormalizeSymmetric01(raw, maxPossibleHeight);

        float line = Mathf.Abs(normalized - 0.5f);

        float mask = 1f - Mathf.Clamp01(line * riverFalloffScale);
        mask = Mathf.Pow(mask, riverSharpness);

        return mask;
    }
}



