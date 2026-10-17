using UnityEngine;

public struct FlowerInstanceData
{
    public Vector3 localPosition;
    public Quaternion localRotation;
    public Vector3 localScale;
    public Color32 petalColor;
    public bool isTallFlower;
    public bool isDaisyWeed;
    public bool isPoppy;
    public bool isCornflower;

    public FlowerInstanceData(
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale,
        Color32 petalColor,
        bool isTallFlower = false,
        bool isDaisyWeed = false,
        bool isPoppy = false, bool isCornflower = false)
    {
        this.localPosition = localPosition;
        this.localRotation = localRotation;
        this.localScale = localScale;
        this.petalColor = petalColor;
        this.isTallFlower = isTallFlower;
        this.isDaisyWeed = isDaisyWeed;
        this.isPoppy = isPoppy;
        this.isCornflower = isCornflower;
    }
}
