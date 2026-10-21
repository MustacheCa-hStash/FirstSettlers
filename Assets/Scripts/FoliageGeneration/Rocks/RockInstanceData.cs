using UnityEngine;

public struct RockInstanceData
{
    public Vector3 localPosition;
    public Quaternion localRotation;
    public Vector3 localScale;
    public WorldFeatureVariant variant;
    public int prefabIndex;
    // Negative means a legacy prefab without a profile; grass uses TreeSettings' fallback.
    public float grassExclusionRadius;
    // Immutable main-thread snapshot. Copied to flat native buffers before dispatching grass jobs.
    public Vector4[] grassExclusionHull;
    public float grassContactPadding, grassContactHeight, groundEmbed;
    public Bounds grassHullBounds;

    public RockInstanceData(
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale,
        WorldFeatureVariant variant,
        int prefabIndex,
        float grassExclusionRadius = -1f)
    {
        this.localPosition = localPosition;
        this.localRotation = localRotation;
        this.localScale = localScale;
        this.variant = variant;
        this.prefabIndex = prefabIndex;
        this.grassExclusionRadius = grassExclusionRadius;
        grassExclusionHull = null;
        grassContactPadding = grassContactHeight = groundEmbed = 0f;
        grassHullBounds = default;
    }
}
