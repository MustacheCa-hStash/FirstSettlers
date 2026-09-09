using UnityEngine;

public static class BiomeClassifier
{
    public static BiomeType Classify(float height, float moisture, float temperature,
    float slope, float mountainMask, float riverMask, float waterLevel)
    {
        return TerrainSlopePolicy.ClassifyBiome(height, moisture, temperature, slope, mountainMask, riverMask, waterLevel);
        }

    public static Color GenerateColorFromBiomeType(BiomeType biomeType)
    {
        switch (biomeType)
        {
            case BiomeType.Water:
                return new Color(0.05f, 0.25f, 0.6f);     // deep ocean blue

            case BiomeType.Beach:
                return new Color(0.85f, 0.78f, 0.55f);    // sand

            case BiomeType.Grassland:
                return new Color(0.35f, 0.7f, 0.3f);      // vivid green plains

            case BiomeType.Forest:
                return new Color(0.05f, 0.45f, 0.08f);    // dense green

            case BiomeType.Desert:
                return new Color(0.92f, 0.82f, 0.45f);    // warm tan

            case BiomeType.Rock:
                return new Color(0.5f, 0.5f, 0.5f);       // neutral grey cliffs

            case BiomeType.Snow:
                return new Color(1f, 1f, 1f);             // pure white

            case BiomeType.Tundra:
                return new Color(0.6f, 0.7f, 0.6f);       // pale cold grass / moss

            case BiomeType.Taiga:
                return new Color(0.1f, 0.35f, 0.2f);      // cold conifer forest

            default:
                return Color.magenta;
        }
    }

    public static Color GenerateDebugColorFromRiverMask(float riverMaskMap)
    {
        Color riverDebug = Color.Lerp(Color.black, Color.white, riverMaskMap);
        return riverDebug;
    }
}
