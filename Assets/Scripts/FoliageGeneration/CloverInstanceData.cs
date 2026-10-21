using UnityEngine;

public struct CloverInstanceData
{
    public Vector3 localPosition;
    public Quaternion localRotation;
    public Vector3 localScale;
    public uint selectionRank;
    public float grassInfluenceRadius;
    public int prefabIndex;
    // Negative retains the legacy global fade width for prefabs without profiles.
    public float grassFadePadding;

    public CloverInstanceData(
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale,
        uint selectionRank,
        float grassInfluenceRadius,
        int prefabIndex,
        float grassFadePadding = -1f)
    {
        this.localPosition = localPosition;
        this.localRotation = localRotation;
        this.localScale = localScale;
        this.selectionRank = selectionRank;
        this.grassInfluenceRadius = grassInfluenceRadius;
        this.prefabIndex = prefabIndex;
        this.grassFadePadding = grassFadePadding;
    }
}
