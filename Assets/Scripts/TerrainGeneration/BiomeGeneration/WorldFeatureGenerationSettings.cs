using UnityEngine;

public struct WorldFeatureGenerationSettings
{
    public Vector2 treeUniformScaleRange;
    // Main-thread datacard snapshot indexed by WorldFeatureVariant. Never mutated after dispatch.
    public Vector2[] treeExclusionRadiusRanges;
    // Zero entries mean no species definition: use the global compatibility fallback.
    public Vector2[] treeUniformScaleRanges;
    public int forestRockPrefabCount;
    public int maxForestRocksPerChunk;
    public Vector2 forestRockUniformScaleRange;
    public Vector2 forestRockPitchRange;
    public int grasslandRockPrefabCount;
    public int grasslandLargeRockPrefabCount;
    public int maxGrasslandRocksPerChunk;
    public Vector2 grasslandRockUniformScaleRange;
    public Vector2 grasslandLargeRockUniformScaleRange;
    public Vector2 grasslandRockPitchRange;
    public int maxGrasslandTreesPerChunk;

    public static bool IsGrasslandTree(WorldFeatureVariant variant) => TreeSpeciesCatalog.IsGrassland(variant);

    public Vector2 GetTreeUniformScaleRange(WorldFeatureVariant variant)
    {
        int index = (int)variant;
        if (treeUniformScaleRanges != null && index >= 0 && index < treeUniformScaleRanges.Length &&
            treeUniformScaleRanges[index] != Vector2.zero)
            return treeUniformScaleRanges[index];
        return treeUniformScaleRange;
    }

    public Vector2 GetTreeExclusionRadiusRange(WorldFeatureVariant variant)
    {
        int index = (int)variant;
        if (treeExclusionRadiusRanges != null && index >= 0 && index < treeExclusionRadiusRanges.Length)
            return treeExclusionRadiusRanges[index];
        // Legacy defaults for unconfigured scenes and standalone generation/validation.
        // Configured worlds use the species cards, including an explicitly authored zero radius.
        return variant switch
        {
            WorldFeatureVariant.BirchAspenTree => new Vector2(6.2f, 7.8f),
            WorldFeatureVariant.SpruceTree => new Vector2(6.4f, 8.2f),
            WorldFeatureVariant.WhitePineTree => new Vector2(8f, 10.5f),
            WorldFeatureVariant.OakTree => new Vector2(8.8f, 11.5f),
            WorldFeatureVariant.MapleTree or WorldFeatureVariant.SugarMapleTree or WorldFeatureVariant.BeechTree => new Vector2(7.2f, 9.4f),
            WorldFeatureVariant.GrasslandOakTree => new Vector2(11.5f, 15.5f),
            WorldFeatureVariant.GrasslandWhitePineTree => new Vector2(8.5f, 11.5f),
            WorldFeatureVariant.GrasslandWillowTree => new Vector2(9f, 12.4f),
            WorldFeatureVariant.GrasslandBirchAspenTree => new Vector2(7.2f, 9.4f),
            WorldFeatureVariant.GrasslandMapleTree => new Vector2(8f, 10.8f),
            _ => Vector2.zero
        };
    }

    public float GetTreeExclusionRadius(WorldFeatureVariant variant, float roll)
    {
        Vector2 range = GetTreeExclusionRadiusRange(variant);
        return Mathf.Lerp(range.x, range.y, roll);
    }

    public static WorldFeatureGenerationSettings Default => new WorldFeatureGenerationSettings
    {
        treeUniformScaleRange = new Vector2(2f, 2f),
        forestRockPrefabCount = 0,
        maxForestRocksPerChunk = 2,
        forestRockUniformScaleRange = new Vector2(0.75f, 1.45f),
        forestRockPitchRange = new Vector2(-15f, 15f),
        grasslandRockPrefabCount = 0,
        grasslandLargeRockPrefabCount = 0,
        maxGrasslandRocksPerChunk = 7,
        grasslandRockUniformScaleRange = new Vector2(60f, 95f),
        grasslandLargeRockUniformScaleRange = new Vector2(95f, 135f),
        grasslandRockPitchRange = new Vector2(-12f, 12f),
        maxGrasslandTreesPerChunk = 8
    };
}
