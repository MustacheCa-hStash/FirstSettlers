using UnityEngine;

public struct DandelionInstanceData
{
    public Vector3 localPosition;
    public Quaternion localRotation;
    public Vector3 localScale;
    public uint selectionRank;

    public DandelionInstanceData(
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale,
        uint selectionRank)
    {
        this.localPosition = localPosition;
        this.localRotation = localRotation;
        this.localScale = localScale;
        this.selectionRank = selectionRank;
    }
}
