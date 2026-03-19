using UnityEngine;

public static class BiomeClassifier
{
    // Height thresholds
    private const float WaterLevel = 0.28f;
    private const float BeachLevel = 0.32f;
    private const float RockLevel = 0.72f;
    private const float SnowLevel = 0.95f;

    // Temperature thresholds
    private const float ColdTemp = 0.30f;
    private const float HotTemp = 0.65f;

    // Moisture thresholds
    private const float DryMoisture = 0.35f;
    private const float WetMoisture = 0.65f;

    public static BiomeType Classify(float height, float moisture, float temperature)
    {
        // Water & coast
        if (height < WaterLevel)
            return BiomeType.Water;

        if (height < BeachLevel)
            return BiomeType.Beach;

        // Mountains override
        if (height > RockLevel)
        {
            if (temperature < ColdTemp && height > SnowLevel)
                return BiomeType.Snow;

            return BiomeType.Rock;
        }

        // Regular land biomes
        // Hot & dry → Desert
        if (temperature > HotTemp && moisture < DryMoisture)
            return BiomeType.Desert;

        // Wet → Forest
        if (moisture > WetMoisture)
            return BiomeType.Forest;

        // Default → Grassland
        return BiomeType.Grassland;
    }
}