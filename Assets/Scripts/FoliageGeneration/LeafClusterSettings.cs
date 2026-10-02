using UnityEngine;

[System.Serializable]
public class LeafClusterSettings
{
    public bool enabled = true;
    [Tooltip("Optional override. Defaults to Resources/Foliage/LeafCluster.")]
    public GameObject prefab;
    [Tooltip("Optional distant override. Defaults to Resources/Foliage/LeafScatter_LOD1.")]
    public GameObject distantPrefab;
    public bool matchGrassRenderDistance = true;
    [Min(1)] public float renderDistance = 28f;
    [Min(0)] public float fadeWidth = 8f;
    [Min(0)] public float prewarmDistance = 8f;
    [Min(0.75f), Tooltip("Candidate spacing in world units, independent of terrain worldScale.")]
    public float cellSize = 1.5f;
    [Range(0, 1)] public float density = 0.38f;
    [Min(0)] public float lodStart = 18f;
    [Min(1)] public float lodEnd = 30f;
    [Range(0, 45)] public float maxSlope = 32f;
    public Vector2 scaleRange = new Vector2(0.8f, 1.25f);
    public int seedOffset = 47000;
    [Min(1)] public int candidateBudgetPerFrame = 256;
    [Min(0.01f)] public float generationBudgetMs = 0.35f;

    // Placement changes invalidate resident candidates; range/budget changes do not.
    public int PlacementSignature => System.HashCode.Combine(cellSize, density, maxSlope,
        scaleRange, seedOffset);
}
