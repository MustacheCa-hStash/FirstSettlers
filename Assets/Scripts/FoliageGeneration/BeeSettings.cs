using UnityEngine;

[System.Serializable]
public sealed class BeeSettings : ButterflySettings
{
    [Header("Bee groups and flowers")]
    [Tooltip("Given that a chunk has bees, chance of three residents instead of two.")]
    [Range(0f, 1f)] public float threeBeeChance = 0.3f;
    [Tooltip("Chance that a new flight target is a nearby flower hotspot.")]
    [Range(0f, 1f)] public float flowerTargetChance = 0.85f;
    [Tooltip("Approximate flower-top height above the sampled ground.")]
    [Min(0.1f)] public float flowerVisitHeight = 0.8f;

    [Header("Temporary wing motion")]
    [Range(0f, 20f)] public float wingbeatsPerSecond = 12f;
    [Range(0f, 85f)] public float raisedWingAngle = 35f;

    public BeeSettings()
    {
        spawnChance = 0.23f;
        maxPerChunk = 3;
        maxActive = 18;
        maxHotspotsPerChunk = 48;
        territoryRadius = 8f;
        flightSpeed = 2.1f;
        turnDegreesPerSecond = 150f;
        flightAcceleration = 4f;
        bodyTurnDegreesPerSecond = 125f;
        maxFacingLagDegrees = 25f;
        turnBankDegrees = 6f;
        verticalSpeed = 2.2f;
        preferredHeightMin = 0.8f;
        preferredHeightMax = 1.5f;
        terrainClearance = 0.35f;
        waterLookAhead = 2f;
        modelScaleMin = 0.12f;
        modelScaleMax = 0.17f;
        flightPitchDegrees = 6f;
        climbPitchDegrees = 8f;
        wanderDegrees = 20f;
        wanderFrequency = 0.4f;
        landingChance = 0.9f;
        landingApproachDistance = 2.5f;
        restDurationMin = 1.1f;
        restDurationMax = 2.2f;
        restingFlapRate = 0.35f;
    }
}
