using UnityEngine;

/// <summary>Authored ground footprint at instance scale one, independent of placement spacing.</summary>
[CreateAssetMenu(menuName = "First Settlers/Foliage/Grass Exclusion Profile")]
public sealed class FoliageExclusionProfile : ScriptableObject
{
    [Min(0f), Tooltip("Radius for clover and rock profiles without a baked hull, in prefab-local units at instance scale 1. Baked rocks use the ground-contact settings below. Restart Play Mode or regenerate after editing.")]
    public float grassRadius = 0.5f;
    [Min(0f), Tooltip("Additional fade width for clover grass thinning, in prefab-local units. Rocks use a hard exclusion and ignore this field.")]
    public float grassFadePadding;

    [Header("Rock Ground Contact")]
    [Min(0f), Tooltip("Extra distance outside the baked collision hull, in prefab-local units. Keeps blades from clipping the contact edge without clearing the full upper silhouette.")]
    public float rockContactPadding = .025f;
    [Min(0f), Tooltip("Test the grass root and this short segment above it against the hull. Prefab-local units, scaled with the instance. Restart Play Mode or regenerate after editing.")]
    public float rockContactHeight = .06f;
    [HideInInspector] public Vector4[] rockHullPlanes;
    [HideInInspector] public Bounds rockHullBounds;
    public bool HasRockHull => rockHullPlanes != null && rockHullPlanes.Length >= 4;

    public static float NonNegative(float value) => float.IsFinite(value) ? Mathf.Max(0f, value) : 0f;

    public Vector2 ScaledZone(Vector3 instanceScale)
    {
        float scale = Mathf.Max(Mathf.Abs(instanceScale.x), Mathf.Abs(instanceScale.z));
        return new Vector2(NonNegative(grassRadius) * NonNegative(scale),
            NonNegative(grassFadePadding) * NonNegative(scale));
    }
}
