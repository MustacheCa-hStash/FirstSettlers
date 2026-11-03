using Unity.Mathematics;

// Slopes consumed by ecology and debug maps are degrees. Raw gradients remain
// height/sample derivatives for mesh normals and MountainSnow calculations.
public static class TerrainSlopePolicy
{
    public const float ForestFadeStartDegrees = 25f;
    // Rooted vegetation can hold soil beyond a loose granular surface's repose angle.
    // Tree placement ends before the forest substrate, avoiding meadow rims at rock edges.
    public const float ForestMaxDegrees = 40f;
    public const float GrassMaxDegrees = 45f;
    public const float LooseSurfaceMaxDegrees = 34f;
    public const float MudMaxDegrees = 25f;
    public const float CliffDegrees = 60f;
    public const float GroundCoverExposedDegrees = 30f;
    public const float LitterThinningStartDegrees = 28f;
    public const float LitterThinningEndDegrees = 42f;
    public static float FromGradient(float gradient, float heightMultiplier)
        => math.degrees(math.atan(math.abs(gradient * heightMultiplier)));

    public static float LooseLitterExposure(float slope) =>
        math.smoothstep(LitterThinningStartDegrees, LitterThinningEndDegrees, slope);

    public static BiomeType ClassifyBiome(float height, float moisture, float temperature,
        float slope, float mountainMask, float riverMask, float waterLevel)
    {
        if (height <= waterLevel) return BiomeType.Water;
        if (slope >= CliffDegrees) return BiomeType.Rock;
        // Preserve cold/alpine snow; warm lower mountain benches can grow grass.
        if (mountainMask > 0.45f)
        {
            if (temperature < 0.30f) return BiomeType.Snow;
            float snowHeight = math.saturate((height - 3f) / 5f);
            float snowSlope = 1f - math.saturate((slope - 45f) / 25f);
            if (temperature < 0.65f && snowHeight * snowSlope > 0.5f)
                return BiomeType.Snow;
            if (height >= 3f || slope > GrassMaxDegrees) return BiomeType.Rock;
            return BiomeType.Grassland;
        }
        if (slope > GrassMaxDegrees) return BiomeType.Rock;
        if (temperature < 0.18f)
            return moisture < 0.35f ? BiomeType.Tundra : BiomeType.Snow;
        // Moderate mountain shoulders become meadows, not forests above treeline.
        if (mountainMask > 0.30f && height > math.lerp(0.8f, 0.62f,
                math.saturate((mountainMask - 0.30f) / 0.15f)))
            return temperature < 0.30f ? BiomeType.Snow : BiomeType.Grassland;
        if (temperature < 0.30f && moisture > 0.65f)
            return BiomeType.Taiga;
        if (temperature > 0.65f && moisture < 0.35f)
            return slope <= LooseSurfaceMaxDegrees ? BiomeType.Desert : BiomeType.Rock;
        // Tree suitability ends at ForestMaxDegrees, but the forest floor continues to
        // the rock boundary. Converting the 40-45 degree band to meadow creates grass rims.
        if (moisture > 0.65f) return BiomeType.Forest;
        return BiomeType.Grassland;
    }
}
