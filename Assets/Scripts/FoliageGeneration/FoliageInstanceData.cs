using UnityEngine;

public struct FoliageInstanceData
{
    public Vector3 localPosition;
    public Quaternion localRotation;
    public Vector3 localScale;
    public uint selectionRank;
    public BiomeType biome;

    public FoliageInstanceData(
        Vector3 localPosition,
        Quaternion localRotation,
        Vector3 localScale,
        uint selectionRank,
        BiomeType biome)
    {
        this.localPosition = localPosition;
        this.localRotation = localRotation;
        this.localScale = localScale;
        this.selectionRank = selectionRank;
        this.biome = biome;
    }
}
