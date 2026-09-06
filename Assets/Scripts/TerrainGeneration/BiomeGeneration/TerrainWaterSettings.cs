public readonly struct TerrainWaterSettings
{
    public const float DefaultWaterLevel = 0.24f;
    public const float ShoreBand = 0.01f;
    public const float WetBand = ShoreBand;
    public const float ShallowDepth = 0.015f;
    public const float RiverCarveStart = 0.60f;
    // The typical shoreline is near mask 0.82; halfway from the old 0.60 bank is 0.71.
    public const float RiverBankThreshold = 0.71f;
    public const float RiverCoreThreshold = 0.75f;
    public const float RiverShoulderHeight = 0.04f;
    public const float RiverBedDepth = 0.03f;
    public const float RiverBasinFalloffWidth = 0.12f;

    public readonly float SurfaceY;
    public readonly float WaterLevel;

    public TerrainWaterSettings(float surfaceY, float heightMultiplier, float worldScale)
    {
        if (float.IsNaN(surfaceY) || float.IsInfinity(surfaceY) ||
            !(heightMultiplier > 0f) || float.IsInfinity(heightMultiplier) ||
            !(worldScale > 0f) || float.IsInfinity(worldScale))
            throw new System.ArgumentOutOfRangeException(nameof(surfaceY), "Water Y must be finite and terrain scales must be positive and finite.");

        SurfaceY = surfaceY;
        WaterLevel = surfaceY / (heightMultiplier * worldScale);
    }
}
