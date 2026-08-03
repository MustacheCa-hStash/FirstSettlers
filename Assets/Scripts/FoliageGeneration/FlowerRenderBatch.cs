using UnityEngine;

public struct FlowerRenderBatch
{
    public Matrix4x4[] matrices;
    public Vector4[] petalColors;

    public FlowerRenderBatch(Matrix4x4[] matrices, Vector4[] petalColors)
    {
        this.matrices = matrices;
        this.petalColors = petalColors;
    }
}
