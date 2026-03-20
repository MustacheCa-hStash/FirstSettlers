using UnityEngine;

public static class SurfaceTypeClassifier
{
    private const float OceanWaterLevel = 0.20f;
    private const float BeachBand = 0.025f;

    private const float RiverBankThreshold = 0.40f;
    private const float RiverCoreThreshold = 0.65f;

    private const float CliffSlopeThreshold = 0.60f;
    private const float RockSlopeThreshold = 0.42f;

    public static SurfaceType Classify(float height, float slope, float riverMask, BiomeType biome)
    {
        if (slope >= CliffSlopeThreshold)
            return SurfaceType.Cliff;

        if (riverMask >= RiverCoreThreshold)
            return SurfaceType.Riverbed;

        if (height <= OceanWaterLevel + BeachBand)
            return SurfaceType.Sand;

        if (riverMask >= RiverBankThreshold)
        {
            if (slope >= RockSlopeThreshold)
                return SurfaceType.Rock;

            return SurfaceType.Mud;
        }

        switch (biome)
        {
            case BiomeType.Beach:
                return SurfaceType.Sand;

            case BiomeType.Desert:
                return SurfaceType.Sand;

            case BiomeType.Forest:
                return SurfaceType.Grass;

            case BiomeType.Grassland:
                return SurfaceType.Grass;

            case BiomeType.Rock:
                return SurfaceType.Rock;

            case BiomeType.Snow:
                return SurfaceType.Snow;

            case BiomeType.Water:
                return SurfaceType.Riverbed;

            default:
                return SurfaceType.Grass;
        }
    }

    public static Color GenerateColor(SurfaceType surfaceType, WaterState waterState)
    {
        Color baseColor;

        switch (surfaceType)
        {
            case SurfaceType.Sand:
                baseColor = new Color(0.80f, 0.75f, 0.55f);
                break;

            case SurfaceType.Mud:
                baseColor = new Color(0.42f, 0.32f, 0.22f);
                break;

            case SurfaceType.Grass:
                baseColor = new Color(0.25f, 0.60f, 0.25f);
                break;

            case SurfaceType.Rock:
                baseColor = new Color(0.45f, 0.45f, 0.45f);
                break;

            case SurfaceType.Snow:
                baseColor = new Color(0.92f, 0.94f, 0.98f);
                break;

            case SurfaceType.Cliff:
                baseColor = new Color(0.30f, 0.30f, 0.30f);
                break;

            case SurfaceType.Riverbed:
                baseColor = new Color(0.35f, 0.30f, 0.24f);
                break;

            default:
                baseColor = Color.magenta;
                break;
        }

        switch (waterState)
        {
            case WaterState.Wet:
                return Color.Lerp(baseColor, new Color(0.10f, 0.18f, 0.22f), 0.25f);

            case WaterState.Shallow:
                return Color.Lerp(baseColor, new Color(0.05f, 0.25f, 0.60f), 0.35f);

            case WaterState.Deep:
                return Color.Lerp(baseColor, new Color(0.05f, 0.20f, 0.50f), 0.55f);

            default:
                return baseColor;
        }
    }
}