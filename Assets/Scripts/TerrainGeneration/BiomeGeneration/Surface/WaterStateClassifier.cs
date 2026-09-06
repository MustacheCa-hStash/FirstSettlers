public static class WaterStateClassifier
{
    public static WaterState Classify(float height, float riverMask, float waterLevel)
    {
        if (height <= waterLevel)
            return waterLevel - height > TerrainWaterSettings.ShallowDepth ? WaterState.Deep : WaterState.Shallow;

        float wetLevel = waterLevel + TerrainWaterSettings.WetBand;
        if (height <= wetLevel)
            return WaterState.Wet;

        return WaterState.Dry;
    }
}
