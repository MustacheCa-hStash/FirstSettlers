using UnityEngine;

public struct LilyPadInstanceData
{
    public Vector3 localPosition;
    public Quaternion localRotation;
    public float uniformScale;

    public LilyPadInstanceData(Vector3 localPosition, Quaternion localRotation, float uniformScale)
    {
        this.localPosition = localPosition;
        this.localRotation = localRotation;
        this.uniformScale = uniformScale;
    }
}
