using UnityEngine;

/// <summary>Shared placement controls for small grassland flower drifts. Distances use terrain samples.</summary>
[System.Serializable]
public abstract class MeadowFlowerPatchSettings
{
    public bool enabled = true;
    public GameObject prefab;
    [Min(1f)] public float patchCellSize = 32f;
    [Range(0f, 1f)] public float patchSpawnChance = 0.32f;
    [Min(0.0001f)] public float patchNoiseScale = 0.024f;
    [Range(0f, 1f)] public float patchNoiseThreshold = 0.48f;
    [Min(1)] public int minPlantsPerPatch = 10;
    [Min(1)] public int maxPlantsPerPatch = 24;
    public Vector2 patchRadiusRange = new Vector2(3f, 6f);
    [Range(0f, 0.5f)] public float edgeIrregularity = 0.25f;
    [Range(0f, 1f)] public float gapStrength = 0.22f;
    [Tooltip("Independent metre-scale size; does not inherit the legacy flower mesh scale.")]
    public Vector2 uniformScaleRange = new Vector2(0.46f, 0.62f);
    [Range(0f, 90f)] public float maxSlope = 32f;
    [Min(0f)] public float treeExclusionRadius = 1.1f;
    [Range(0f, 1f)] public float clearanceScale = 0.7f;
    public int seedOffset = 72000;
    [Tooltip("Subtle multiplicative petal variation; the painted atlas supplies the flower color.")]
    public Color darkTint = new Color(0.86f, 0.92f, 0.92f, 1f);
    public Color lightTint = new Color(1f, 1f, 0.96f, 1f);
}
