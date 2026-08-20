using UnityEngine;

public struct RockInstanceData
{
    public Vector3 localPosition;
    public Quaternion localRotation;
    public Vector3 localScale;
    public WorldFeatureVariant variant;
    public int prefabIndex;

    public RockInstanceData(
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale,
        WorldFeatureVariant variant,
        int prefabIndex)
    {
        this.localPosition = localPosition;
        this.localRotation = localRotation;
        this.localScale = localScale;
        this.variant = variant;
        this.prefabIndex = prefabIndex;
    }
}
