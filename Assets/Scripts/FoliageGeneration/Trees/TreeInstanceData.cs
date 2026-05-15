using UnityEngine;

public struct TreeInstanceData
{
    public Vector3 localPosition;
    public Quaternion localRotation;
    public Vector3 localScale;
    
    public TreeInstanceData(Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
    {
        this.localPosition = localPosition;
        this.localRotation = localRotation;
        this.localScale = localScale;
    }
}
