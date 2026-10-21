public readonly struct TerrainWaterSettings
{
    public const float DefaultWaterLevel = 0.24f;
    // One third of the former .01 elevation band: gentle banks retain a thin sandy rim.
    public const float ShoreBand = 0.01f / 3f;
    public const float WetBand = 0.01f;
    public const float ShallowDepth = 0.015f;
    public const float RiverCarveStart = 0.60f;
    // The typical shoreline is near .82. Keep roughly one third of the old .71-.82 dry bank.
    // This changes surface cover, not channel carving or the water plane.
    public const float RiverBankThreshold = 0.78f;
    public const float RiverCoreThreshold = 0.75f;
    public const float RiverShoulderHeight = 0.04f;
    public const float RiverBedDepth = 0.03f;
    public const float RiverBasinFalloffWidth = 0.12f;

    public readonly float SurfaceY;
    public readonly float WaterLevel;
    public readonly float HeightMultiplier;

    public TerrainWaterSettings(float surfaceY, float heightMultiplier, float worldScale)
    {
        if (float.IsNaN(surfaceY) || float.IsInfinity(surfaceY) ||
            !(heightMultiplier > 0f) || float.IsInfinity(heightMultiplier) ||
            !(worldScale > 0f) || float.IsInfinity(worldScale))
            throw new System.ArgumentOutOfRangeException(nameof(surfaceY), "Water Y must be finite and terrain scales must be positive and finite.");

        SurfaceY = surfaceY;
        HeightMultiplier = heightMultiplier;
        WaterLevel = surfaceY / (heightMultiplier * worldScale);
    }
}
