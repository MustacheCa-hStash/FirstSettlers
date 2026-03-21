using UnityEngine;

public static class RiverGenerator
{
    private const int RiverOctaves = 1;
    private const float RiverPersistence = 0.5f;
    private const float RiverLacunarity = 2f;

    private const float RiverHalfWidth = 0.025f;
    private const float RiverBankFalloff = 0.02f;

    public static float Sample(float sampleX, float sampleZ, Vector2[] octaveOffsets)
    {
        float riverNoise = TerrainNoiseUtility.SamplePerlinFbm(
            sampleX,
            sampleZ,
            RiverOctaves,
            RiverPersistence,
            RiverLacunarity,
            octaveOffsets);

        float riverDistance = Mathf.Abs(riverNoise - 0.5f);
        return riverDistance;

        float riverMask = 1f - Mathf.SmoothStep(
            RiverHalfWidth,
            RiverHalfWidth + RiverBankFalloff,
            riverDistance);

        return riverMask;
    }
}