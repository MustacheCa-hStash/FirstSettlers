using UnityEngine;

public struct BillboardFoliageInstanceData
{
    public Vector3 localPosition;
    public Quaternion localRotation;
    public Vector3 localScale;
    public BiomeType biome;

    public BillboardFoliageInstanceData(
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale,
        BiomeType biome)
    {
        this.localPosition = localPosition;
        this.localRotation = localRotation;
        this.localScale = localScale;
        this.biome = biome;
    }
}
