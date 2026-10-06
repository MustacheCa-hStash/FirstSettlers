using UnityEngine;

// Main-thread authored bindings become plain worker values. No Unity assets cross the worker boundary.
public static class TreeGenerationSnapshot
{
    public static WorldFeatureGenerationSettings Create(TreeSettings treeSettings)
    {
        WorldFeatureGenerationSettings settings = WorldFeatureGenerationSettings.Default;

        if (treeSettings == null)
            return settings;

        settings.treeUniformScaleRange = treeSettings.treeUniformScaleRange;
        // Snapshot Unity assets on the main thread; near/distant workers receive plain values only.
        int capacity = 0;
        foreach (var species in TreeSpeciesCatalog.All) capacity = Mathf.Max(capacity, (int)species.Variant + 1);
        var radiusRanges = new Vector2[capacity];
        var scaleRanges = new Vector2[radiusRanges.Length];
        foreach (var species in TreeSpeciesCatalog.All)
        {
            var variant = species.Variant;
            Vector2 range = settings.GetTreeExclusionRadiusRange(variant);
            WorldObjectDefinition definition = species.Definition(treeSettings);
            if (definition != null)
            {
                range = species.IsGrassland
                    ? definition.GrasslandTreeExclusionRadiusRange : definition.ForestTreeExclusionRadiusRange;
                scaleRanges[(int)variant] = definition.TreeUniformScaleRange;
            }
            radiusRanges[(int)variant] = range;
        }
        settings.treeExclusionRadiusRanges = radiusRanges;
        settings.treeUniformScaleRanges = scaleRanges;
        settings.forestRockPrefabCount =
            treeSettings.forestRockPrefabs != null ? treeSettings.forestRockPrefabs.Length : 0;
        settings.maxForestRocksPerChunk = Mathf.Max(0, treeSettings.maxForestRocksPerChunk);
        settings.forestRockUniformScaleRange = treeSettings.forestRockUniformScaleRange;
        settings.forestRockPitchRange = treeSettings.forestRockPitchRange;
        settings.grasslandRockPrefabCount =
            treeSettings.grasslandRockPrefabs != null ? treeSettings.grasslandRockPrefabs.Length : 0;
        settings.grasslandLargeRockPrefabCount =
            treeSettings.grasslandLargeRockPrefabs != null ? treeSettings.grasslandLargeRockPrefabs.Length : 0;
        if (settings.grasslandLargeRockPrefabCount == 0 && treeSettings.grasslandLargeRockFallbackPrefab != null)
            settings.grasslandLargeRockPrefabCount = 1;
        settings.maxGrasslandRocksPerChunk = Mathf.Max(0, treeSettings.maxGrasslandRocksPerChunk);
        settings.grasslandRockUniformScaleRange = treeSettings.grasslandRockUniformScaleRange;
        settings.grasslandLargeRockUniformScaleRange = treeSettings.grasslandLargeRockUniformScaleRange;
        settings.grasslandRockPitchRange = treeSettings.grasslandRockPitchRange;
        settings.maxGrasslandTreesPerChunk = Mathf.Max(0, treeSettings.maxGrasslandTreesPerChunk);

        return settings;
    }
}
