using UnityEngine;

public struct RockInstanceData
{
    public Vector3 localPosition;
    public Quaternion localRotation;
    public Vector3 localScale;
    public int prefabIndex;

    public RockInstanceData(
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale,
        int prefabIndex)
    {
        this.localPosition = localPosition;
        this.localRotation = localRotation;
        this.localScale = localScale;
        this.prefabIndex = prefabIndex;
    }
}
