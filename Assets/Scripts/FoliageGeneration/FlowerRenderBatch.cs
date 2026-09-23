using UnityEngine;

public struct FlowerRenderBatch
{
    public Matrix4x4[] matrices;
    public Vector4[] petalColors;
    public bool isTallFlower;

    public FlowerRenderBatch(Matrix4x4[] matrices, Vector4[] petalColors, bool isTallFlower = false)
    {
        this.matrices = matrices;
        this.petalColors = petalColors;
        this.isTallFlower = isTallFlower;
    }
}
