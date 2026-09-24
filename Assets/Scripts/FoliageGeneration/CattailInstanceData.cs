using UnityEngine;

public struct CattailInstanceData
{
    public Vector3 localPosition;
    public Quaternion localRotation;
    public float uniformScale;

    public CattailInstanceData(Vector3 localPosition, Quaternion localRotation, float uniformScale)
    {
        this.localPosition = localPosition;
        this.localRotation = localRotation;
        this.uniformScale = uniformScale;
    }
}
