using UnityEngine;

public struct BillboardFoliageInstanceData
{
    public Vector3 localPosition;
    public Quaternion localRotation;
    public Vector3 localScale;

    public BillboardFoliageInstanceData(
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale)
    {
        this.localPosition = localPosition;
        this.localRotation = localRotation;
        this.localScale = localScale;
    }
}