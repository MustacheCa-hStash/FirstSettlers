using UnityEngine;

public struct BillboardFoliageInstanceData
{
    public Vector3 localPosition;
    public Quaternion localRotation;
    public Vector3 localScale;
    public uint selectionRank;
    public float forestBlend;

    public BillboardFoliageInstanceData(
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale,
        uint selectionRank,
        float forestBlend)
    {
        this.localPosition = localPosition;
        this.localRotation = localRotation;
        this.localScale = localScale;
        this.selectionRank = selectionRank;
        this.forestBlend = forestBlend;
    }
}
