using UnityEngine;

public struct FlowerInstanceData
{
    public Vector3 localPosition;
    public Quaternion localRotation;
    public Vector3 localScale;
    public Color32 petalColor;
    public bool isTallFlower;

    public FlowerInstanceData(
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale,
        Color32 petalColor,
        bool isTallFlower = false)
    {
        this.localPosition = localPosition;
        this.localRotation = localRotation;
        this.localScale = localScale;
        this.petalColor = petalColor;
        this.isTallFlower = isTallFlower;
    }
}
