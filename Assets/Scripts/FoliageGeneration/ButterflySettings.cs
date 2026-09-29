using UnityEngine;

[System.Serializable]
public sealed class ButterflySettings
{
    public bool enableButterflies = true;
    [Tooltip("One MeshFilter and one MeshRenderer using one material across all submeshes. This may be assigned later.")]
    public GameObject butterflyPrefab;

    [Header("Population")]
    [Min(0)] public int activeRingRadius = 2;
    [Range(0f, 1f)] public float spawnChance = 0.55f;
    [Min(1)] public int maxPerChunk = 2;
    [Min(1)] public int maxActive = 24;
    [Min(1)] public int maxHotspotsPerChunk = 16;
    [Min(0.5f)] public float hotspotCellSize = 5f;

    [Header("Flight (world units)")]
    [Tooltip("Maximum length of one flight leg. Butterflies may cross chunks over several legs.")]
    [Min(0.5f)] public float territoryRadius = 7f;
    [Min(0.1f)] public float flightSpeed = 1.6f;
    [Min(0.1f)] public float turnDegreesPerSecond = 180f;
    [Min(0.1f)] public float verticalSpeed = 2.5f;
    [Min(0.1f)] public float preferredHeightMin = 1f;
    [Min(0.1f)] public float preferredHeightMax = 2.2f;
    [Min(0.1f)] public float terrainClearance = 0.45f;
    [Min(0f)] public float shoreBuffer = 0.8f;
    [Min(0.5f)] public float waterLookAhead = 2.5f;
    [Min(0.1f)] public float modelScaleMin = 0.8f;
    [Min(0.1f)] public float modelScaleMax = 1.2f;
    public float modelYawOffset;
}
