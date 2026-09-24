using UnityEngine;

[System.Serializable]
public class CattailSettings
{
    public bool enableCattails = true;
    [Tooltip("Clump mesh and material used for GPU instancing. The prefab is not spawned.")]
    public GameObject cattailPrefab;

    [Header("Render Range")]
    [Min(0)] public int activeRingRadius = 2;

    [Header("Shore Habitat (world units)")]
    [Min(0f), Tooltip("How far a rooted clump can extend into shallow water from dry shore.")]
    public float waterwardDistance = 3.5f;
    [Min(0f), Tooltip("How far a clump can extend onto damp ground from the water edge.")]
    public float landwardDistance = 1.8f;
    [Min(0f), Tooltip("Maximum water depth at the clump's roots.")]
    public float maxWaterDepth = 0.8f;
    [Min(0f), Tooltip("Maximum bank height above the water surface.")]
    public float maxBankHeight = 0.65f;
    [Range(0f, 90f)] public float maxSlope = 28f;
    [Tooltip("Vertical adjustment to the terrain bed or bank. Regenerate terrain to apply during Play mode.")]
    public float rootHeightOffset = 0f;

    [Header("Colony Distribution")]
    [Min(0.25f)] public float candidateSpacing = 1.15f;
    [Range(0f, 1f)] public float spawnChance = 0.58f;
    [Min(0.001f), Tooltip("Larger values make smaller stands and gaps.")]
    public float colonyNoiseScale = 0.045f;
    [Range(0f, 1f)] public float colonyThreshold = 0.46f;
    public int seedOffset = 83000;

    [Header("Per Clump Variation")]
    public Vector2 uniformScaleRange = new Vector2(0.82f, 1.18f);
}
