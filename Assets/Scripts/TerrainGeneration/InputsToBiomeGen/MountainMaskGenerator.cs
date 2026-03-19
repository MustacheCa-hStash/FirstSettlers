using UnityEngine;

public static class MountainMaskGenerator
{
    const int octaves = 3;
    const float persistence = 0.5f;
    const float lacunarity = 2f;

    static readonly float maxPossibleHeight = TerrainNoiseUtility.ComputeMaxPossibleHeight(octaves, persistence);
    public static float Sample(float sampleX, float sampleZ, Vector2[] octaveOffsets)
    {
        float raw = TerrainNoiseUtility.SampleBasicFbm(sampleX, sampleZ, 0, octaves, persistence, lacunarity, octaveOffsets);
        float normalized = TerrainNoiseUtility.NormalizeSymmetric01(raw, maxPossibleHeight);

        // Push the mask so only some broad regions become strongly mountainous.
        float mask = Mathf.Pow(normalized, 3.0f);
        //suppress weak mask values
        mask = Mathf.SmoothStep(0.01f, 0.9f, mask);
        return mask;
    }
}