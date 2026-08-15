using UnityEngine;

public struct TreeInstanceData
{
    public Vector3 localPosition;
    public Quaternion localRotation;
    public Vector3 localScale;
    public WorldFeatureVariant variant;
    public Color32 leafTint;
    public Color32 barkTint;
    
    public TreeInstanceData(
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale,
        WorldFeatureVariant variant)
        : this(
            localPosition,
            localRotation,
            localScale,
            variant,
            new Color32(255, 255, 255, 255),
            new Color32(255, 255, 255, 255))
    {
    }

    public TreeInstanceData(
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale,
        WorldFeatureVariant variant,
        Color32 leafTint,
        Color32 barkTint)
    {
        this.localPosition = localPosition;
        this.localRotation = localRotation;
        this.localScale = localScale;
        this.variant = variant;
        this.leafTint = leafTint;
        this.barkTint = barkTint;
    }
}
