using UnityEngine;

public struct FlowerRenderBatch
{
    public Matrix4x4[] matrices;
    public Vector4[] petalColors;
    public bool isTallFlower;
    public bool isDaisyWeed;
    public bool isPoppy;
    public bool isCornflower;

    public FlowerRenderBatch(Matrix4x4[] matrices, Vector4[] petalColors, bool isTallFlower = false, bool isDaisyWeed = false, bool isPoppy = false, bool isCornflower = false)
    {
        this.matrices = matrices;
        this.petalColors = petalColors;
        this.isTallFlower = isTallFlower;
        this.isDaisyWeed = isDaisyWeed;
        this.isPoppy = isPoppy;
        this.isCornflower = isCornflower;
    }
}
