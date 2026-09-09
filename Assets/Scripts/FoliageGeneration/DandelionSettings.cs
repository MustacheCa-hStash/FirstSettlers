using UnityEngine;

[System.Serializable]
public class DandelionSettings
{
    public bool enableDandelions = true;
    public GameObject dandelionPrefab;

    [Header("Render Range")]
    [Tooltip("Circular radius in whole chunks around the player chunk; diagonal chunks outside the radius are excluded.")]
    public int activeRingRadius = 2;
    public bool receiveDandelionShadows = false;

    [Header("Patch Placement")]
    public float patchCellSize = 18f;
    [Min(1)] public int maxPatchCentersPerCell = 1;
    [Range(0f, 1f)] public float patchSpawnChance = 0.45f;
    public float patchNoiseScale = 0.026f;
    [Range(0f, 1f)] public float patchNoiseThreshold = 0.54f;
    [Min(1)] public int minDandelionsPerPatch = 3;
    [Min(1)] public int maxDandelionsPerPatch = 12;
    public Vector2 patchRadiusRange = new Vector2(1.8f, 5.5f);

    [Header("Per Dandelion Variation")]
    public Vector2 uniformScaleRange = new Vector2(0.85f, 1.18f);
    public bool randomizeYaw = true;
    public int seedOffset = 32000;
    public string dandelionInstanceDataPropertyName = "_DandelionInstanceData";

    [Header("Surface Filters")]
    [Range(0f, 90f), Tooltip("Maximum terrain slope in degrees.")]
    public float maxSlope = 40f;
    public float treeExclusionRadius = 1.0f;
    public float bushExclusionRadius = 0.9f;
    public float rockExclusionRadius = 0.75f;
}
