using UnityEngine;

[System.Serializable]
public class ButterflySettings
{
    [UnityEngine.Serialization.FormerlySerializedAs("enableButterflies")]
    public bool enabled = true;
    [UnityEngine.Serialization.FormerlySerializedAs("butterflyPrefab")]
    [Tooltip("One MeshFilter and one MeshRenderer using one material across all submeshes.")]
    public GameObject prefab;

    [Header("Population")]
    [Min(0)] public int activeRingRadius = 2;
    [Tooltip("Butterflies: chance per resident slot. Bees: chance a flower-rich chunk hosts a group.")]
    [Range(0f, 1f)] public float spawnChance = 0.55f;
    [Min(1)] public int maxPerChunk = 1;
    [Min(1)] public int maxActive = 24;
    [Min(1)] public int maxHotspotsPerChunk = 16;
    [Min(0.5f)] public float hotspotCellSize = 5f;

    [Header("Flight (world units)")]
    [Tooltip("Maximum length of one flight leg. Flyers may cross chunks over several legs.")]
    [Min(0.5f)] public float territoryRadius = 7f;
    [Min(0.1f)] public float flightSpeed = 1.6f;
    [Tooltip("How quickly the flight path bends toward a new direction.")]
    [Min(0.1f)] public float turnDegreesPerSecond = 95f;
    [Tooltip("Acceleration from a pause to the normal flight speed.")]
    [Min(0.1f)] public float flightAcceleration = 2.4f;
    [Tooltip("How quickly the body catches up to the direction of travel.")]
    [Min(0.1f)] public float bodyTurnDegreesPerSecond = 75f;
    [Tooltip("Maximum angle between the body and its direction of travel.")]
    [Range(0f, 60f)] public float maxFacingLagDegrees = 35f;
    [Tooltip("Gentle roll when the flight path curves.")]
    [Range(0f, 20f)] public float turnBankDegrees = 8f;
    [Min(0.1f)] public float verticalSpeed = 2.5f;
    [Min(0.1f)] public float preferredHeightMin = 1f;
    [Min(0.1f)] public float preferredHeightMax = 2.2f;
    [Min(0.1f)] public float terrainClearance = 0.45f;
    [Min(0f)] public float shoreBuffer = 0.8f;
    [Min(0.5f)] public float waterLookAhead = 2.5f;
    [Min(0.1f)] public float modelScaleMin = 0.8f;
    [Min(0.1f)] public float modelScaleMax = 1.2f;
    public float modelYawOffset;

    [Header("Flight pose and path")]
    [Tooltip("Positive values raise the head above the tail. Wing flap angles remain relative to the body.")]
    [Range(-30f, 30f)] public float flightPitchDegrees = 10f;
    [Tooltip("Additional nose-up or nose-down pitch based on actual climbing or descending speed.")]
    [Range(0f, 30f)] public float climbPitchDegrees = 8f;
    [Tooltip("Maximum slow steering deviation from the direct path to a target.")]
    [Range(0f, 35f)] public float wanderDegrees = 15f;
    [Tooltip("Cycles per second of the gentle path curvature.")]
    [Range(0.02f, 1f)] public float wanderFrequency = 0.22f;

    [Header("Landing and rest")]
    [Range(0f, 1f)] public float landingChance = 0.7f;
    [Tooltip("Minimum distance from a landing target at which the gradual descent begins.")]
    [Min(1f)] public float landingApproachDistance = 3.5f;
    [Min(0.1f)] public float restDurationMin = 2.5f;
    [Min(0.1f)] public float restDurationMax = 4.5f;
    [Tooltip("Wingbeat speed while resting, relative to the material's Flaps Per Second.")]
    [Range(0.05f, 1f)] public float restingFlapRate = 0.23f;
}
