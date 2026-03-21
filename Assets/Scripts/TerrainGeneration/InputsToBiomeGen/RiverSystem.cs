using UnityEngine;

public static class RiverGenerator
{
    private const int CenterlineOctaves = 2;
    private const float CenterlinePersistence = 0.5f;
    private const float CenterlineLacunarity = 2f;

    private const float CenterlineScale = 0.35f;
    private const float MeanderAmplitude = 0.6f;

    private const float RiverHalfWidth = 0.015f;
    private const float BankFalloffWidth = 0.02f;

    private static readonly float maxPossibleHeight =
        TerrainNoiseUtility.ComputeMaxPossibleHeight(CenterlineOctaves, CenterlinePersistence);

    public static float Sample(float sampleX, float sampleZ, Vector2[] octaveOffsets)
    {
        float baseCenterX = Mathf.Round(sampleX * 0.2f) / 0.2f;

        float centerOffset = TerrainNoiseUtility.SamplePerlinFbm(
            0f,
            sampleZ * CenterlineScale,
            CenterlineOctaves,
            CenterlinePersistence,
            CenterlineLacunarity,
            octaveOffsets);

        float normalizedOffset = centerOffset / maxPossibleHeight;
        float riverCenterX = baseCenterX + normalizedOffset * MeanderAmplitude;

        float distanceFromCenter = Mathf.Abs(sampleX - riverCenterX);

        float innerRadius = RiverHalfWidth;
        float outerRadius = RiverHalfWidth + BankFalloffWidth;

        if (distanceFromCenter <= innerRadius)
            return 1f;

        if (distanceFromCenter >= outerRadius)
            return 0f;

        float t = Mathf.InverseLerp(outerRadius, innerRadius, distanceFromCenter);
        return Mathf.SmoothStep(0f, 1f, t);
    }
}