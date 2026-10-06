using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class TreeDatacardValidation
{
    private const string Cards = "Assets/ScriptableObjects/WorldObjects/";
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private static WorldFeatureGenerationSettings Snapshot(TreeSettings settings) =>
        (WorldFeatureGenerationSettings)typeof(ChunkManager).GetMethod("BuildWorldFeatureGenerationSettings",
            BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { settings });

    private static WorldObjectDefinition Card(string name) =>
        AssetDatabase.LoadAssetAtPath<WorldObjectDefinition>(Cards + name + "Tree.asset");

    private static TreeSettings AuthoredSettings() => new TreeSettings
    {
        mapleTreeDefinition = Card("Maple"), sugarMapleTreeDefinition = Card("SugarMaple"),
        birchAspenTreeDefinition = Card("BirchAspen"), beechTreeDefinition = Card("Beech"),
        spruceTreeDefinition = Card("Spruce"), whitePineTreeDefinition = Card("WhitePine"),
        oakTreeDefinition = Card("Oak"), willowTreeDefinition = Card("Willow")
    };

    [MenuItem("Tools/Terrain/Validate Tree Datacards")]
    public static void Run()
    {
        ValidateMigration();
        ValidateAuthoredSpacing();
        ValidateAuthoredScale();
        ValidatePooledQueries();
        TreeGameplayValidation.Run();
        Debug.Log("TREE DATACARD PASS: migrated ranges, authored spacing, main-thread snapshots, dense/sparse worker parity, pooled species queries and tree gameplay.");
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    [MenuItem("Tools/Terrain/Validate Tree Definition Scale")]
    public static void ValidateAuthoredScale()
    {
        var settings = AuthoredSettings();
        var clones = new System.Collections.Generic.List<WorldObjectDefinition>();
        try
        {
            // Distinct species ranges make accidental global-scale use detectable.
            foreach (var field in typeof(TreeSettings).GetFields().Where(f => f.FieldType == typeof(WorldObjectDefinition)))
            {
                var source = (WorldObjectDefinition)field.GetValue(settings);
                Check(source != null, "Missing authored scale card: " + field.Name);
                var card = Object.Instantiate(source); clones.Add(card); field.SetValue(settings, card);
                SetRanges(card, new Vector2(4, 5), new Vector2(4, 5));
                SetScale(card, new Vector2(2 + clones.Count, 2.5f + clones.Count));
            }
            settings.treeUniformScaleRange = new Vector2(20, 21);
            var worker = Snapshot(settings);
            Check(worker.GetTreeUniformScaleRange(WorldFeatureVariant.OakTree) ==
                worker.GetTreeUniformScaleRange(WorldFeatureVariant.GrasslandOakTree), "Habitat variants lost shared scale.");
            Check(Snapshot(new TreeSettings { treeUniformScaleRange = new Vector2(6, 7) })
                .GetTreeUniformScaleRange(WorldFeatureVariant.SugarMapleTree) == new Vector2(6, 7),
                "Trees without definitions lost the global fallback.");
            var fallbackPrefab = new GameObject("Scale definition fallback");
            try
            {
                var authoring = fallbackPrefab.AddComponent<TreeGameplayAuthoring>();
                var serialized = new SerializedObject(authoring);
                serialized.FindProperty("queryDefinition").objectReferenceValue = settings.sugarMapleTreeDefinition;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Check(Snapshot(new TreeSettings { treeLOD0GameObjectPrefab = fallbackPrefab })
                    .GetTreeUniformScaleRange(WorldFeatureVariant.SugarMapleTree) ==
                    settings.sugarMapleTreeDefinition.TreeUniformScaleRange, "Prefab definition fallback lost scale.");
            }
            finally { Object.DestroyImmediate(fallbackPrefab); }

            // Editing Unity assets after dispatch must not change a worker's snapshot.
            foreach (var card in clones) SetScale(card, new Vector2(90, 91));
            const int size = 128, n = size + 3;
            int checkedTrees = 0;
            var species = new System.Collections.Generic.HashSet<WorldFeatureVariant>();
            foreach (var biome in new[] { BiomeType.Forest, BiomeType.Grassland, BiomeType.Taiga, BiomeType.Snow })
            {
                var b = new BiomeType[n, n]; var s = new SurfaceType[n, n];
                var m = new float[n, n]; var t = new float[n, n]; var h = new float[n, n];
                var slopes = new float[n, n]; var rivers = new float[n, n];
                for (int x = 0; x < n; x++) for (int z = 0; z < n; z++)
                {
                    b[x, z] = biome; s[x, z] = biome == BiomeType.Snow ? SurfaceType.Snow : SurfaceType.Grass;
                    m[x, z] = biome == BiomeType.Grassland ? .55f : .75f;
                    t[x, z] = biome == BiomeType.Snow ? .17f : .5f; h[x, z] = .4f;
                }
                for (int seed = 1937; seed < 1945; seed++)
                {
                    var coord = new ChunkCoord(seed - 1941, 1940 - seed);
                    var dense = Task.Run(() => WorldFeaturePlanGenerator.Generate(coord, size, seed,
                        b, s, m, t, slopes, rivers, worker, h, .24f)).GetAwaiter().GetResult();
                    var sparse = Task.Run(() => WorldFeaturePlanGenerator.GenerateTreePlacements(coord, size, seed,
                        b, s, m, t, slopes, rivers, worker, (x, z) => { },
                        sampleHeight: (x, z) => h[x, z], waterLevel: .24f)).GetAwaiter().GetResult();
                    var baseline = worker; baseline.treeUniformScaleRanges = null; baseline.treeUniformScaleRange = Vector2.one;
                    var original = WorldFeaturePlanGenerator.Generate(coord, size, seed,
                        b, s, m, t, slopes, rivers, baseline, h, .24f).Placements
                        .Where(p => p.featureType == WorldFeatureType.Tree).ToArray();
                    var near = dense.Placements.Where(p => p.featureType == WorldFeatureType.Tree).ToArray();
                    var far = sparse.Placements.Where(p => p.featureType == WorldFeatureType.Tree).ToArray();
                    Check(near.Length == far.Length && near.Length == original.Length, "Scale changed placement counts.");
                    for (int i = 0; i < near.Length; i++)
                    {
                        var p = near[i]; var range = worker.GetTreeUniformScaleRange(p.variant);
                        Check(p.scale.x >= range.x && p.scale.x <= range.y && p.scale == Vector3.one * p.scale.x &&
                            p.scale.x < 20, "Species scale or immutable snapshot was ignored: " + p.variant);
                        Check(p.scale == far[i].scale && p.treeId == far[i].treeId, "Near/far species scale mismatch.");
                        Check(p.treeId == original[i].treeId && p.variant == original[i].variant &&
                            p.sampleX == original[i].sampleX && p.sampleZ == original[i].sampleZ &&
                            p.exclusionRadius == original[i].exclusionRadius, "Scale changed tree identity or spacing.");
                        species.Add(p.variant); checkedTrees++;
                    }
                    using var record = new ChunkRecord(coord);
                    foreach (var pair in new (string name, object value)[] { ("worldFeaturePlan", dense),
                        ("heightMap", h), ("biomeMap", b), ("surfaceTypeMap", s) })
                        typeof(ChunkRecord).GetField(pair.name, BindingFlags.Instance | BindingFlags.NonPublic)
                            .SetValue(record, pair.value);
                    FoliageGenerator.GenerateTreeCubesForChunk(record, settings, seed, size, .3f, 10);
                    Check(record.FoliageData.treeCubeInstances.Count == near.Length, "Near conversion dropped trees.");
                    for (int i = 0; i < near.Length; i++)
                        Check(record.FoliageData.treeCubeInstances[i].localScale == near[i].scale,
                            "Near instance dropped authored species scale.");
                    if (near.Length > 0 && seed == 1937) ValidateScaledProxy(coord, record.FoliageData.treeCubeInstances[0]);
                }
            }
            Check(checkedTrees > 20 && species.Contains(WorldFeatureVariant.SugarMapleTree) &&
                species.Contains(WorldFeatureVariant.SpruceTree) && species.Contains(WorldFeatureVariant.GrasslandOakTree),
                "Scale fixture coverage: " + checkedTrees + " trees, " + string.Join(", ", species));
            var test = clones[0];
            SetScale(test, new Vector2(5, 3)); Check(test.TreeUniformScaleRange == new Vector2(3, 5), "Reversed scale failed.");
            SetScale(test, new Vector2(-1, 0)); Check(test.TreeUniformScaleRange == Vector2.one * .01f, "Nonpositive scale failed.");
            SetScale(test, new Vector2(float.NaN, 3)); Check(test.TreeUniformScaleRange == Vector2.one * 2, "Nonfinite scale failed.");
            Debug.Log($"TREE DEFINITION SCALE PASS: {checkedTrees} trees, {species.Count} variants, immutable species ranges, fallback cards, near/distant parity, colliders and stable placement IDs/spacing.");
        }
        finally { foreach (var card in clones) Object.DestroyImmediate(card); }
    }

    public static void RunScaleBatch()
    {
        try { ValidateAuthoredScale(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    private static void SetScale(WorldObjectDefinition card, Vector2 range)
    {
        var serialized = new SerializedObject(card);
        serialized.FindProperty("treeUniformScaleRange").vector2Value = range;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ValidateScaledProxy(ChunkCoord coord, TreeInstanceData tree)
    {
        var prefab = new GameObject("Species scale collider test"); prefab.SetActive(false);
        try
        {
            var collider = prefab.AddComponent<CapsuleCollider>(); collider.radius = .5f;
            var authoring = prefab.AddComponent<TreeGameplayAuthoring>();
            var serialized = new SerializedObject(authoring);
            var physical = serialized.FindProperty("physicalTrunkColliders"); physical.arraySize = 1;
            physical.GetArrayElementAtIndex(0).objectReferenceValue = collider;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var settings = new TreeSettings { treeLOD0GameObjectPrefab = prefab };
            settings.gameplay.activationBudgetMs = 0;
            var registry = new TreeRegistry(1937, 128 * .3f);
            registry.RegisterChunk(coord, new[] { tree }, TreePlacementDetail.Detailed);
            Check(registry.TryGet(tree.id, out var record), "Scaled record missing.");
            using var manager = new TreeGameplayManager(registry, settings, 128 * .3f);
            manager.Update(record.WorldPosition, 0);
            Check(manager.TryGetProxy(tree.id, out var proxy) && proxy.transform.localScale == tree.localScale,
                "Gameplay collider lost species scale.");
            Check(Mathf.Abs(proxy.PhysicalTrunkColliders[0].bounds.size.x - tree.localScale.x) < .001f,
                "Physical trunk bounds do not follow species scale.");
        }
        finally { Object.DestroyImmediate(prefab); }
    }

    private static void ValidateMigration()
    {
        var authored = AuthoredSettings();
        var worker = Snapshot(authored);
        var legacy = WorldFeatureGenerationSettings.Default;
        foreach (WorldFeatureVariant variant in Enum.GetValues(typeof(WorldFeatureVariant)))
        {
            if (legacy.GetTreeExclusionRadiusRange(variant) == Vector2.zero) continue;
            Check(authored.GetDefinition(variant) != null, "Missing species card: " + variant);
            Check(worker.GetTreeExclusionRadiusRange(variant) == legacy.GetTreeExclusionRadiusRange(variant),
                "Migration changed spacing: " + variant);
        }
        Check(authored.GetDefinition(WorldFeatureVariant.OakTree) ==
            authored.GetDefinition(WorldFeatureVariant.GrasslandOakTree), "Habitat variants must share a card.");
        Check(authored.GetDefinition(WorldFeatureVariant.Boulder) == null, "Non-tree resolved a tree card.");
        ComparePlans(worker, legacy, false);
    }

    private static void SetRanges(WorldObjectDefinition card, Vector2 forest, Vector2 grassland)
    {
        var serialized = new SerializedObject(card);
        serialized.FindProperty("forestTreeExclusionRadiusRange").vector2Value = forest;
        serialized.FindProperty("grasslandTreeExclusionRadiusRange").vector2Value = grassland;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ValidateAuthoredSpacing()
    {
        var card = ScriptableObject.CreateInstance<WorldObjectDefinition>();
        try
        {
            SetRanges(card, new Vector2(40, 30), new Vector2(-2, 0));
            Check(card.ForestTreeExclusionRadiusRange == new Vector2(30, 40) &&
                card.GrasslandTreeExclusionRadiusRange == Vector2.zero, "Invalid range sanitization failed.");
            var settings = new TreeSettings
            {
                mapleTreeDefinition = card, sugarMapleTreeDefinition = card, birchAspenTreeDefinition = card,
                beechTreeDefinition = card, spruceTreeDefinition = card, whitePineTreeDefinition = card,
                oakTreeDefinition = card, willowTreeDefinition = card
            };
            var worker = Snapshot(settings);
            Check(worker.GetTreeExclusionRadius(WorldFeatureVariant.OakTree, 0) == 30 &&
                worker.GetTreeExclusionRadius(WorldFeatureVariant.OakTree, 1) == 40 &&
                worker.GetTreeExclusionRadius(WorldFeatureVariant.GrasslandOakTree, .5f) == 0,
                "Authored ranges or zero radius lost in worker transport.");
            SetRanges(card, Vector2.one, Vector2.one);
            Check(worker.GetTreeExclusionRadiusRange(WorldFeatureVariant.SpruceTree) == new Vector2(30, 40),
                "Worker snapshot changed when the source card was edited.");
            ComparePlans(worker, WorldFeatureGenerationSettings.Default, true);
        }
        finally { Object.DestroyImmediate(card); }
    }

    private static void ComparePlans(WorldFeatureGenerationSettings worker, WorldFeatureGenerationSettings legacy, bool edited)
    {
        const int size = 128, n = size + 3;
        int trees = 0, snowTrees = 0, changed = 0;
        foreach (var biome in new[] { BiomeType.Forest, BiomeType.Grassland, BiomeType.Taiga, BiomeType.Snow })
        {
            var b = new BiomeType[n, n]; var s = new SurfaceType[n, n];
            var m = new float[n, n]; var t = new float[n, n]; var h = new float[n, n];
            var slopes = new float[n, n]; var rivers = new float[n, n];
            for (int x = 0; x < n; x++) for (int z = 0; z < n; z++)
            {
                b[x, z] = biome; s[x, z] = biome == BiomeType.Snow ? SurfaceType.Snow : SurfaceType.Grass;
                m[x, z] = .75f; t[x, z] = biome == BiomeType.Snow ? .17f : .5f; h[x, z] = .4f;
            }
            for (int seed = 1937; seed < 1945; seed++)
            {
                var coord = new ChunkCoord(-2, 3);
                var dense = Task.Run(() => WorldFeaturePlanGenerator.Generate(coord, size, seed,
                    b, s, m, t, slopes, rivers, worker, h, .24f)).GetAwaiter().GetResult();
                var sparse = Task.Run(() => WorldFeaturePlanGenerator.GenerateTreePlacements(coord, size, seed,
                    b, s, m, t, slopes, rivers, worker, (x, z) => { },
                    sampleHeight: (x, z) => h[x, z], waterLevel: .24f)).GetAwaiter().GetResult();
                var expected = dense.Placements.Where(p => p.featureType == WorldFeatureType.Tree).ToArray();
                var distant = sparse.Placements.Where(p => p.featureType == WorldFeatureType.Tree).ToArray();
                var original = WorldFeaturePlanGenerator.Generate(coord, size, seed,
                    b, s, m, t, slopes, rivers, legacy, h, .24f).Placements
                    .Where(p => p.featureType == WorldFeatureType.Tree).ToArray();
                Check(expected.Length == distant.Length, "Near/far tree count mismatch: " + biome);
                if (!expected.Select(p => p.treeId).SequenceEqual(original.Select(p => p.treeId))) changed++;
                if (!edited) Check(expected.Length == original.Length, "Migration changed tree count.");
                for (int i = 0; i < expected.Length; i++)
                {
                    var p = expected[i]; var far = distant[i];
                    Check(p.treeId == far.treeId && p.variant == far.variant && p.sampleX == far.sampleX &&
                        p.sampleZ == far.sampleZ && p.exclusionRadius == far.exclusionRadius,
                        "Near/far tree mismatch: " + biome);
                    Vector2 range = worker.GetTreeExclusionRadiusRange(p.variant);
                    Check(p.exclusionRadius >= range.x && p.exclusionRadius <= range.y, "Tree ignored authored radius.");
                    if (!edited) Check(p.treeId == original[i].treeId && p.exclusionRadius == original[i].exclusionRadius,
                        "Migration changed deterministic placement.");
                    for (int j = 0; j < i; j++)
                    {
                        var delta = new Vector2(p.sampleX - expected[j].sampleX, p.sampleZ - expected[j].sampleZ);
                        float radius = p.exclusionRadius + expected[j].exclusionRadius;
                        Check(delta.sqrMagnitude >= radius * radius, "Tree exclusion circles overlap.");
                    }
                    trees++;
                    if (biome == BiomeType.Snow) snowTrees++;
                }
            }
        }
        Check(trees > 20 && snowTrees > 0, "Fixture did not exercise forest, grassland, Taiga and snow trees.");
        if (edited) Check(changed > 0, "Editing card radii did not affect accepted trees.");
    }

    private static void ValidatePooledQueries()
    {
        var settings = AuthoredSettings();
        var prefab = new GameObject("Datacard fallback tree"); prefab.SetActive(false);
        try
        {
            var collider = prefab.AddComponent<CapsuleCollider>();
            var authoring = prefab.AddComponent<TreeGameplayAuthoring>();
            var serialized = new SerializedObject(authoring);
            var physical = serialized.FindProperty("physicalTrunkColliders"); physical.arraySize = 1;
            physical.GetArrayElementAtIndex(0).objectReferenceValue = collider;
            serialized.FindProperty("queryDefinition").objectReferenceValue = settings.spruceTreeDefinition;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            settings.treeLOD0GameObjectPrefab = prefab;
            settings.gameplay.activationBudgetMs = 0;
            var registry = new TreeRegistry(1937, 10);
            using var manager = new TreeGameplayManager(registry, settings, 10);
            var spruce = QueryTree(0, WorldFeatureVariant.SpruceTree);
            registry.RegisterChunk(default, new[] { spruce }, TreePlacementDetail.Detailed);
            manager.Update(Vector3.zero, 0);
            Check(manager.TryGetProxy(spruce.id, out var first) && first.TryGetInfo(out var firstInfo) &&
                firstInfo.Definition == settings.spruceTreeDefinition, "Spruce query lost its card.");
            registry.RegisterChunk(default, Array.Empty<TreeInstanceData>(), TreePlacementDetail.Detailed);
            manager.Update(Vector3.zero, .1);
            var maple = QueryTree(1, WorldFeatureVariant.SugarMapleTree);
            registry.RegisterChunk(default, new[] { maple }, TreePlacementDetail.Detailed);
            manager.Update(Vector3.zero, .2);
            Check(manager.TryGetProxy(maple.id, out var second) && second == first && second.TryGetInfo(out var info) &&
                info.Definition == settings.sugarMapleTreeDefinition && info.DisplayName == settings.sugarMapleTreeDefinition.DisplayName,
                "Pooled fallback prefab retained the previous species' query card.");
        }
        finally { Object.DestroyImmediate(prefab); }
    }

    private static TreeInstanceData QueryTree(int cell, WorldFeatureVariant variant) => new(
        new Vector3(-5, 0, -5), Quaternion.identity, Vector3.one, variant,
        new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255), 0,
        TreeId.Generated(1937, default, TreePlacementSource.Forest, cell));
}
