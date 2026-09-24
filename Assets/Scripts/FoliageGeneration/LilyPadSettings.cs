using UnityEngine;

[System.Serializable]
public class LilyPadSettings
{
    public bool enableLilyPads = true;
    [Tooltip("The mesh and material used for GPU instancing. The prefab itself is not spawned.")]
    public GameObject lilyPadPrefab;

    [Header("Render Range")]
    [Min(0)] public int activeRingRadius = 2;

    [Header("Shore Placement (world units)")]
    [Min(0f), Tooltip("Vertical distance above the global water surface. Adjust this to avoid z fighting; regenerate terrain to apply during Play mode.")]
    public float waterSurfaceOffset = 0.04f;
    [Min(0f), Tooltip("Keeps the pad center clear of the bank.")]
    public float minDistanceFromShore = 0.7f;
    [Min(0f), Tooltip("Farthest the pad center can extend into the water.")]
    public float maxDistanceFromShore = 8f;
    [Min(0.25f), Tooltip("Spacing of deterministic placement candidates.")]
    public float candidateSpacing = 1.8f;
    [Range(0f, 1f)] public float spawnChance = 0.38f;
    [Min(0.001f), Tooltip("Larger values make smaller colonies and gaps.")]
    public float colonyNoiseScale = 0.055f;
    [Range(0f, 1f)] public float colonyThreshold = 0.43f;

    [Header("Per Pad Variation")]
    public Vector2 uniformScaleRange = new Vector2(0.8f, 1.25f);
    public int seedOffset = 71000;
}
