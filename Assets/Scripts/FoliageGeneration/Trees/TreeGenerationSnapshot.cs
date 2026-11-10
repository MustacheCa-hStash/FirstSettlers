using UnityEngine;

// Main-thread authored bindings become plain worker values. No Unity assets cross the worker boundary.
public static class TreeGenerationSnapshot
{
    public static WorldFeatureGenerationSettings Create(TreeSettings treeSettings, float worldScale = .3f)
    {
        WorldFeatureGenerationSettings settings = WorldFeatureGenerationSettings.Default;

        if (treeSettings == null)
            return settings;

        var distribution = treeSettings.distributionProfile != null ? treeSettings.distributionProfile :
            Resources.Load<TreeDistributionProfile>("Foliage/TreeDistribution");
        if (distribution != null) settings.treeDistribution = distribution.Snapshot(worldScale);

        settings.treeUniformScaleRange = treeSettings.treeUniformScaleRange;
        settings.disableBerryBushes = treeSettings.disableBerryBushes;
        // Snapshot Unity assets on the main thread; near/distant workers receive plain values only.
        int capacity = 0;
        foreach (var species in TreeSpeciesCatalog.All) capacity = Mathf.Max(capacity, (int)species.Variant + 1);
        var radiusRanges = new Vector2[capacity];
        var scaleRanges = new Vector2[radiusRanges.Length];
        if (settings.treeDistribution.WoodlandEdgeInfill > 0)
            settings.woodlandCrownProfiles = new Vector3[capacity];
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
            if (settings.treeDistribution.UseWoodlandSpeciesMix)
            {
                // These models were authored in metres. The legacy ranges
                // compensate for the scales of a different set of meshes.
                scaleRanges[(int)variant] = new Vector2(.76f, 1.13f);
                Vector2 meters = variant switch
                {
                    WorldFeatureVariant.OakTree or WorldFeatureVariant.GrasslandOakTree => new Vector2(4.0f, 5.6f),
                    WorldFeatureVariant.BeechTree or WorldFeatureVariant.SugarMapleTree => new Vector2(3.0f, 4.5f),
                    WorldFeatureVariant.BirchAspenTree or WorldFeatureVariant.GrasslandBirchAspenTree => new Vector2(2.2f, 3.4f),
                    _ => new Vector2(2.8f, 4.2f)
                };
                range = meters / settings.treeDistribution.WorldScale;
            }
            radiusRanges[(int)variant] = range;
            if (settings.woodlandCrownProfiles != null)
                settings.woodlandCrownProfiles[(int)variant] = MeasureCrown(species.NearPrefab(treeSettings));
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
        settings.maxSnowRocksPerChunk = Mathf.Max(0, treeSettings.maxSnowRocksPerChunk);
        settings.snowRockPrefabCount = treeSettings.snowRockPrefabs?.Length > 0 ? treeSettings.snowRockPrefabs.Length : 3;
        settings.snowLargeRockPrefabCount = treeSettings.snowLargeRockPrefabs?.Length > 0 ? treeSettings.snowLargeRockPrefabs.Length : 3;
        settings.snowRockUniformScaleRange = treeSettings.snowRockUniformScaleRange;
        settings.snowLargeRockUniformScaleRange = treeSettings.snowLargeRockUniformScaleRange;

        return settings;
    }

    private static Vector3 MeasureCrown(GameObject prefab)
    {
        if (prefab == null) return default;
        bool found = false;
        Bounds crown = default;
        foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>())
        {
            if (renderer.name != "Foliage" || !renderer.TryGetComponent<MeshFilter>(out var filter) ||
                filter.sharedMesh == null) continue;
            var local = filter.sharedMesh.bounds;
            var toRoot = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                var sign = new Vector3((corner&1)==0 ? -1 : 1, (corner&2)==0 ? -1 : 1, (corner&4)==0 ? -1 : 1);
                var point = toRoot.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, sign));
                if (found) crown.Encapsulate(point);
                else { crown = new Bounds(point, Vector3.zero); found = true; }
            }
        }
        return found ? new Vector3(Mathf.Max(crown.extents.x, crown.extents.z), Mathf.Max(0f, crown.min.y), crown.max.y) : default;
    }
}
