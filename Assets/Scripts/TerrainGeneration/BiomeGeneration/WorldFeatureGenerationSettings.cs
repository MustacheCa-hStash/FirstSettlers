using UnityEngine;

public struct WorldFeatureGenerationSettings
{
    public int forestRockPrefabCount;
    public int maxForestRocksPerChunk;
    public Vector2 forestRockUniformScaleRange;
    public Vector2 forestRockPitchRange;

    public static WorldFeatureGenerationSettings Default => new WorldFeatureGenerationSettings
    {
        forestRockPrefabCount = 0,
        maxForestRocksPerChunk = 2,
        forestRockUniformScaleRange = new Vector2(0.75f, 1.45f),
        forestRockPitchRange = new Vector2(-15f, 15f)
    };
}
