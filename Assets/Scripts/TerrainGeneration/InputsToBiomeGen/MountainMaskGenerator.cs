using UnityEngine;

public static class MountainMaskGenerator
{
    public static float Sample(
        float sampleX,
        float sampleZ,
        int seed)
    {
        const int octaves = 3;
        const float persistence = 0.5f;
        const float lacunarity = 2f;

        Vector2[] octaveOffsets = TerrainNoiseUtility.GenerateOctaveOffsets(seed + 30000, octaves);

        float raw = TerrainNoiseUtility.SampleBasicFbm(
            sampleX,
            sampleZ,
            seed + 30000,
            octaves,
            persistence,
            lacunarity,
            octaveOffsets
        );

        float maxPossibleHeight = TerrainNoiseUtility.ComputeMaxPossibleHeight(octaves, persistence);
        float normalized = TerrainNoiseUtility.NormalizeSymmetric01(raw, maxPossibleHeight);

        // Push the mask so only some broad regions become strongly mountainous.
        float mask = Mathf.Pow(normalized, 2.2f);

        return mask;
    }
}