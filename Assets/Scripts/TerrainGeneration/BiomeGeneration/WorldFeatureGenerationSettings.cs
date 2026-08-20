using UnityEngine;

public struct WorldFeatureGenerationSettings
{
    public int forestRockPrefabCount;
    public int maxForestRocksPerChunk;
    public Vector2 forestRockUniformScaleRange;
    public Vector2 forestRockPitchRange;
    public int grasslandRockPrefabCount;
    public int maxGrasslandRocksPerChunk;
    public Vector2 grasslandRockUniformScaleRange;
    public Vector2 grasslandRockPitchRange;
    public int maxGrasslandTreesPerChunk;

    public static WorldFeatureGenerationSettings Default => new WorldFeatureGenerationSettings
    {
        forestRockPrefabCount = 0,
        maxForestRocksPerChunk = 2,
        forestRockUniformScaleRange = new Vector2(0.75f, 1.45f),
        forestRockPitchRange = new Vector2(-15f, 15f),
        grasslandRockPrefabCount = 0,
        maxGrasslandRocksPerChunk = 7,
        grasslandRockUniformScaleRange = new Vector2(0.55f, 1.35f),
        grasslandRockPitchRange = new Vector2(-12f, 12f),
        maxGrasslandTreesPerChunk = 8
    };
}
