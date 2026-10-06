using UnityEngine;

/// <summary>Shared authored identity and generation data for a world-object type. Contains no per-instance state or actions.</summary>
[CreateAssetMenu(menuName = "First Settlers/Content/World Object Datacard")]
public sealed class WorldObjectDefinition : ScriptableObject
{
    [SerializeField] private string contentId;
    [SerializeField] private string displayName;
    [SerializeField] private Sprite icon;
    [SerializeField, TextArea(1, 3)] private string shortDescription;

    [Header("Tree Scale")]
    [Tooltip("Minimum and maximum uniform instance scale for this tree species. Shared by forest and grassland variants, near meshes, distant billboards and gameplay colliders. Multiplies the authored prefab size; not a height in metres. Regenerate the world or restart Play Mode after changing this range. Ignored for non-tree objects.")]
    [SerializeField] private Vector2 treeUniformScaleRange = new Vector2(2f, 2f);

    [Header("Tree Placement")]
    [Tooltip("Minimum and maximum exclusion radius for forest, Taiga and snow trees, in terrain sample units (multiply by worldScale for world distance). Placement uses the sum of both objects' radii. Independent of visual tree scale and grass clearance; ignored for non-tree objects.")]
    [SerializeField] private Vector2 forestTreeExclusionRadiusRange = new Vector2(7.2f, 9.4f);
    [Tooltip("Minimum and maximum exclusion radius for grassland trees, in terrain sample units. Used only for species that generate in grassland. Restart Play Mode or regenerate the world after editing placement ranges.")]
    [SerializeField] private Vector2 grasslandTreeExclusionRadiusRange = new Vector2(8f, 10.8f);

    public string ContentId => contentId;
    public string DisplayName => displayName;
    public Sprite Icon => icon;
    public string ShortDescription => shortDescription;
    public Vector2 TreeUniformScaleRange => SanitizeScaleRange(treeUniformScaleRange);
    public Vector2 ForestTreeExclusionRadiusRange => SanitizeRadiusRange(forestTreeExclusionRadiusRange);
    public Vector2 GrasslandTreeExclusionRadiusRange => SanitizeRadiusRange(grasslandTreeExclusionRadiusRange);

    private static Vector2 SanitizeScaleRange(Vector2 range)
    {
        if (!float.IsFinite(range.x) || !float.IsFinite(range.y))
            range = new Vector2(2f, 2f);
        float min = Mathf.Max(0.01f, Mathf.Min(range.x, range.y));
        return new Vector2(min, Mathf.Max(min, Mathf.Max(range.x, range.y)));
    }

    private static Vector2 SanitizeRadiusRange(Vector2 range)
    {
        float a = float.IsNaN(range.x) || float.IsInfinity(range.x) ? 0f : Mathf.Max(0f, range.x);
        float b = float.IsNaN(range.y) || float.IsInfinity(range.y) ? 0f : Mathf.Max(0f, range.y);
        return new Vector2(Mathf.Min(a, b), Mathf.Max(a, b));
    }
}
