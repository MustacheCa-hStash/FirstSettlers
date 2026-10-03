using UnityEngine;

[System.Serializable]
public class CloverSettings
{
    public bool enableClover = true;
    public GameObject cloverClumpPrefab;
    public GameObject[] cloverClumpPrefabs;
    [Header("Forest Openings")]
    public bool enableForestClover = true;
    [Range(0,1), Tooltip("Relative colony frequency in forest openings; dense litter/moss floor remains excluded.")]
    public float forestPatchChance = 0.55f;
    [Range(0,40)] public float forestMaxSlope = 22f;

    [Header("Render Range")]
    [Min(1), Tooltip("Horizontal render distance in chunk widths from the actual player. Independent of detailed-grass range.")]
    public int activeRingRadius = 3;
    [Min(.01f), Tooltip("Outer distance fade in chunk widths. Runtime overrides prefab material fade distances.")]
    public float renderFadeWidthChunks = .5f;
    [Min(0), Tooltip("Prepare placements and cached render batches this many chunk widths beyond the render distance.")]
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
