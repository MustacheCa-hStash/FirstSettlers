using UnityEngine;

public static class WaterStateClassifier
{
    private const float OceanWaterLevel = 0.20f;

    private const float OceanShallowBand = 0.015f;
    private const float OceanWetBand = 0.035f;

    private const float RiverShallowThreshold = 0.40f;
    private const float RiverDeepThreshold = 0.65f;
    private const float RiverWetBand = 0.15f;

    public static WaterState Classify(float height, float riverMask)
    {
        if (height < OceanWaterLevel)
        {
            float oceanDepth = OceanWaterLevel - height;

            if (oceanDepth > OceanShallowBand)
                return WaterState.Deep;

            return WaterState.Shallow;
        }

        if (riverMask >= RiverShallowThreshold)
        {
            float riverStrength = Mathf.InverseLerp(RiverShallowThreshold, 1f, riverMask);
            float riverSurfaceHeight = OceanWaterLevel + Mathf.Lerp(0.01f, 0.05f, riverStrength);

            if (height <= riverSurfaceHeight)
            {
                if (riverMask >= RiverDeepThreshold)
                    return WaterState.Deep;

                return WaterState.Shallow;
            }

            if (height <= riverSurfaceHeight + RiverWetBand)
                return WaterState.Wet;
        }

        if (height <= OceanWaterLevel + OceanWetBand)
            return WaterState.Wet;

        return WaterState.Dry;
    }
}