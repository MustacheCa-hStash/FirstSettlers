using UnityEngine;

[System.Serializable]
public class CloverSettings
{
    public bool enableClover = true;
    public GameObject cloverClumpPrefab;
    public GameObject[] cloverClumpPrefabs;

    [Header("Render Range")]
    [Tooltip("Circular radius in whole chunks around the player chunk; diagonal chunks outside the radius are excluded.")]
    public int activeRingRadius = 1;
    public int preGenerationRingPadding = 1;
    public bool receiveCloverShadows = false;

    [Header("Patch Placement")]
    public float patchCellSize = 10f;
    [Min(1)] public int maxPatchCentersPerCell = 2;
    [Range(0f, 1f)] public float patchSpawnChance = 0.55f;
    public float patchNoiseScale = 0.028f;
    [Range(0f, 1f)] public float patchNoiseThreshold = 0.50f;
    [Min(1)] public int minClumpsPerPatch = 3;
    [Min(1)] public int maxClumpsPerPatch = 9;
    public Vector2 patchRadiusRange = new Vector2(1.2f, 3.4f);

    [Header("Per Clump Variation")]
    public Vector2 uniformScaleRange = new Vector2(0.85f, 1.2f);
    public bool randomizeYaw = true;
    public int seedOffset = 24000;
    public string cloverInstanceDataPropertyName = "_CloverInstanceData";

    [Header("Surface Filters")]
    [Range(0f, 90f), Tooltip("Maximum terrain slope in degrees.")]
    public float maxSlope = 40f;
    public float treeExclusionRadius = 1.25f;
    public float bushExclusionRadius = 1.1f;
    public float rockExclusionRadius = 0.85f;

    [Header("Grass Blending")]
    [Range(0f, 1f)] public float grassDensityInsidePatch = 0.35f;
    public float grassInfluenceRadius = 0.65f;
    public float grassFadePadding = 0.65f;
}
