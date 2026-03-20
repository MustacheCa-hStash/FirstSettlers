using UnityEngine;

public static class BiomeClassifier
{
    // Height thresholds
    private const float WaterLevel = 0.24f;
    private const float BeachLevel = 0.26f;
    private const float RockLevel = 0.62f;
    private const float SnowLevel = 0.93f;

    // Temperature thresholds
    private const float ColdTemp = 0.30f;
    private const float HotTemp = 0.65f;

    // Moisture thresholds
    private const float DryMoisture = 0.35f;
    private const float WetMoisture = 0.65f;

    private const float slopeScale = 4f;

    public static BiomeType Classify(float height, float moisture, float temperature, float slope, float mountainMask, 
        float riverMask)
    {
        //normalize slope
        slope = Mathf.Clamp01(slope * slopeScale);

        bool isRiver =
        riverMask > 0.22f &&
        height < WaterLevel + 0.3f &&
        slope < 0.55f;

        if (height < WaterLevel || isRiver)
            return BiomeType.Water;

        if (height < BeachLevel)
            return BiomeType.Beach;

        if (temperature < 0.18f)
        {
            if (moisture < 0.35f)
                return BiomeType.Tundra;

            return BiomeType.Snow;
        }

        if (temperature < ColdTemp)
        {
            if (moisture > WetMoisture)
                return BiomeType.Taiga;
        }

        float adjustedRockLevel = RockLevel;
        adjustedRockLevel -= slope * 0.14f;
        adjustedRockLevel -= mountainMask * 0.18f;
        adjustedRockLevel = Mathf.Clamp(adjustedRockLevel, 0.50f, RockLevel);

        if (height > adjustedRockLevel)
        {
            float adjustedSnowLevel = SnowLevel;
            adjustedSnowLevel -= mountainMask * 0.06f;
            adjustedSnowLevel -= slope * 0.03f;
            adjustedSnowLevel = Mathf.Clamp(adjustedSnowLevel, 0.80f, SnowLevel);

            if (temperature < ColdTemp && height > adjustedSnowLevel)
                return BiomeType.Snow;

            return BiomeType.Rock;
        }

        if (temperature > HotTemp && moisture < DryMoisture)
            return BiomeType.Desert;

        if (moisture > WetMoisture)
            return BiomeType.Forest;

        return BiomeType.Grassland;
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
