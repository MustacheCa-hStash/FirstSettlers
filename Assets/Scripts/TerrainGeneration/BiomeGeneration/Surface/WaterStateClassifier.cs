public static class WaterStateClassifier
{
    public static WaterState Classify(float height, float riverMask, float waterLevel)
    {
        if (height <= waterLevel)
            return waterLevel - height > TerrainWaterSettings.ShallowDepth ? WaterState.Deep : WaterState.Shallow;

        if (height <= waterLevel + TerrainWaterSettings.WetBand)
            return WaterState.Wet;

        return WaterState.Dry;
    }
}
