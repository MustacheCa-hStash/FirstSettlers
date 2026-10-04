using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class TreeRegistryValidation
{
    private const int Seed = 1937, Size = 128, N = Size + 3;
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Terrain/Validate Tree Registry")]
    public static void Run()
    {
        ValidateSnapshotsAndState();
        ValidateSpatialLookup();
        ValidatePlacementIdentity();
        ValidateManagerRegistration();
        // Also exercises the real worker sampler against full terrain generation.
        DistantTreeValidation.Run();
        Debug.Log("TREE REGISTRY PASS: deterministic IDs, dense/sparse and near/worker parity, " +
            "registration, state retention, source authority, invalid snapshots and XZ lookup.");
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    private static TreeInstanceData Instance(ChunkCoord coord, int cell, Vector3 position)
        => new TreeInstanceData(position, Quaternion.identity, Vector3.one, WorldFeatureVariant.SpruceTree,
            new Color32(255, 255, 255, 255), new Color32(255, 255, 255, 255),
            id: TreeId.Generated(Seed, coord, TreePlacementSource.Forest, cell));

    private static void ValidateSnapshotsAndState()
    {
        var coord = new ChunkCoord(-2, 3);
        var a = Instance(coord, 4, Vector3.zero);
        var b = Instance(coord, 8, Vector3.right * 5);
        var registry = new TreeRegistry(Seed, Size);
        int changes = 0;
        registry.ChunkChanged += _ => changes++;
        var distant = new[] { a, b };
        Check(registry.RegisterChunk(coord, distant, TreePlacementDetail.Distant), "First registration was ignored.");
        Check(!registry.RegisterChunk(coord, distant, TreePlacementDetail.Distant) && changes == 1,
            "Identical registration caused duplicate work.");
        Check(registry.Count == 2 && registry.ChunkCount == 1, "Duplicate records.");
        registry.TryGet(a.id, out var original);
        Check(registry.TrySetState(a.id, TreeState.Cut), "State change failed.");
        a.localPosition.y = 90; a.localScale *= 2; a.snowCoverage = 1;
        Check(registry.RegisterChunk(coord, new[] { b, a }, TreePlacementDetail.Detailed), "Detailed snapshot was ignored.");
        registry.TryGet(a.id, out var refreshed);
        Check(ReferenceEquals(original, refreshed) && refreshed.State == TreeState.Cut && refreshed.WorldPosition.y == 90,
            "Refresh lost record identity, state or authoritative height.");
        Check(!registry.RegisterChunk(coord, distant, TreePlacementDetail.Distant) && registry.Count == 2,
            "Late worker result overwrote detailed data.");
        Check(registry.GetChunk(coord)[1].Id == a.id, "ID depends on current list order.");
        ExpectArgument(() => registry.RegisterChunk(coord, new[] { a, a }, TreePlacementDetail.Detailed));
        ExpectArgument(() => registry.RegisterChunk(coord, new[] { default(TreeInstanceData) }, TreePlacementDetail.Detailed));
        var foreign = a; foreign.id = TreeId.Generated(Seed + 1, coord, TreePlacementSource.Forest, 4);
        ExpectArgument(() => registry.RegisterChunk(coord, new[] { foreign }, TreePlacementDetail.Detailed));
        var wrongChunk = a; wrongChunk.id = TreeId.Generated(Seed, new ChunkCoord(0, 0), TreePlacementSource.Forest, 4);
        ExpectArgument(() => registry.RegisterChunk(coord, new[] { wrongChunk }, TreePlacementDetail.Detailed));
        Check(registry.Count == 2 && registry.GetChunk(coord).Count == 2, "Invalid snapshot partially changed registry.");
        registry.RegisterChunk(coord, Array.Empty<TreeInstanceData>(), TreePlacementDetail.Detailed);
        Check(registry.Count == 0 && registry.GetChunk(coord).Count == 0, "Empty snapshot left stale records.");
        registry.RegisterChunk(coord, new[] { a }, TreePlacementDetail.Detailed);
        Check(registry.TryGet(a.id, out refreshed) && refreshed.State == TreeState.Cut,
            "Re-registration lost modified state.");
        var mutable = new List<TreeInstanceData> { a };
        registry.RegisterChunk(coord, mutable, TreePlacementDetail.Detailed, 1);
        mutable.Add(b);
        registry.RegisterChunk(coord, mutable, TreePlacementDetail.Detailed, 2);
        Check(registry.Count == 2, "In-place generation revision was ignored.");
        var view = registry.GetChunk(coord);
        registry.Clear();
        Check(registry.Count == 0 && registry.ChunkCount == 0 && view.Count == 0, "World disposal left records.");
        registry.RegisterChunk(coord, new[] { a }, TreePlacementDetail.Detailed);
        Check(registry.GetChunk(coord)[0].State == TreeState.Standing, "State leaked across world reset.");
        Check(!default(TreeId).IsValid && a.id != TreeId.Generated(Seed, coord, TreePlacementSource.Snow, 4),
            "Default or source identity invalid.");
        Check(JsonUtility.FromJson<TreeId>(JsonUtility.ToJson(a.id)) == a.id, "Serialized ID changed.");
    }

    private static void ValidateSpatialLookup()
    {
        var registry = new TreeRegistry(Seed, Size);
        var negative = new ChunkCoord(-1, 0);
        var positive = new ChunkCoord(0, 0);
        var a = Instance(negative, 0, new Vector3(63, 9, -64)); // world (-1, 9, 0)
        var b = Instance(positive, 0, new Vector3(-63, 200, -64)); // world (1, 200, 0)
        var outside = Instance(positive, 1, new Vector3(-61, 0, -64));
        registry.RegisterChunk(negative, new[] { a }, TreePlacementDetail.Detailed);
        registry.RegisterChunk(positive, new[] { b, outside }, TreePlacementDetail.Detailed);
        var results = new List<TreeRecord>();
        registry.CollectOriginsInRadiusXZ(Vector3.zero, 1f, results);
        Check(results.Count == 2 && results.Any(r => r.Id == a.id) && results.Any(r => r.Id == b.id),
            "XZ lookup missed negative chunks, boundary points or ignored height incorrectly.");
    }

    private static void ValidatePlacementIdentity()
    {
        int checkedTrees = 0;
        var sources = new HashSet<TreePlacementSource>();
        foreach (var biome in new[] { BiomeType.Forest, BiomeType.Grassland, BiomeType.Taiga, BiomeType.Snow })
            for (int scenario = 0; scenario < 8; scenario++)
            {
                var coord = new ChunkCoord(scenario - 4, 2 - scenario);
                var b = new BiomeType[N, N]; var s = new SurfaceType[N, N];
                var m = new float[N, N]; var t = new float[N, N]; var h = new float[N, N];
                var slopes = new float[N, N]; var rivers = new float[N, N];
                for (int x = 0; x < N; x++) for (int z = 0; z < N; z++)
                { b[x, z] = biome; s[x, z] = biome == BiomeType.Snow ? SurfaceType.Snow : SurfaceType.Grass;
                    m[x, z] = biome == BiomeType.Grassland ? .55f : .8f;
                    t[x, z] = biome == BiomeType.Snow ? .17f : .5f; h[x, z] = .5f; }
                var settings = WorldFeatureGenerationSettings.Default;
                var full = WorldFeaturePlanGenerator.Generate(coord, Size, Seed, b, s, m, t, slopes, rivers, settings, h, .24f);
                var sparse = WorldFeaturePlanGenerator.GenerateTreePlacements(coord, Size, Seed, b, s, m, t, slopes,
                    rivers, settings, (x, z) => { }, sampleHeight: (x, z) => h[x, z], waterLevel: .24f);
                var trees = full.Placements.Where(p => p.featureType == WorldFeatureType.Tree).ToArray();
                var farTrees = sparse.Placements.Where(p => p.featureType == WorldFeatureType.Tree).ToArray();
                Check(trees.Select(p => p.treeId).SequenceEqual(farTrees.Select(p => p.treeId)), "Dense/sparse IDs disagree.");
                Check(trees.All(p => p.treeId.IsValid) && trees.Select(p => p.treeId).Distinct().Count() == trees.Length,
                    "Generated IDs are invalid or duplicated.");
                settings.treeUniformScaleRange = new Vector2(4, 5);
                var scaled = WorldFeaturePlanGenerator.Generate(coord, Size, Seed, b, s, m, t, slopes, rivers, settings, h, .24f);
                Check(trees.Select(p => p.treeId).SequenceEqual(scaled.Placements.Where(p => p.featureType == WorldFeatureType.Tree).Select(p => p.treeId)),
                    "Changing tree scale changed IDs.");
                using var record = new ChunkRecord(coord);
                Set(record, "worldFeaturePlan", full); Set(record, "heightMap", h); Set(record, "surfaceTypeMap", s);
                // Reordering input placements must not change their identity.
                full.Placements.Reverse();
                FoliageGenerator.GenerateTreeCubesForChunk(record, new TreeSettings(), Seed, Size, .3f, 10f);
                Check(new HashSet<TreeId>(record.FoliageData.treeCubeInstances.Select(p => p.id)).SetEquals(trees.Select(p => p.treeId)),
                    "Near conversion or reordered placements changed IDs.");
                foreach (var tree in trees) sources.Add(tree.treeId.Source);
                checkedTrees += trees.Length;
            }
        Check(checkedTrees > 30 && sources.Count == 3,
            "Fixtures did not exercise all candidate sources: trees=" + checkedTrees + ", sources=" + string.Join(",", sources));
        Debug.Log("Tree IDs verified across " + checkedTrees + " synthetic forest, meadow, Taiga and snow placements.");
    }

    private static void ValidateManagerRegistration()
    {
        var coord = new ChunkCoord(-2, 4);
        var tree = Instance(coord, 5, Vector3.zero);
        var registry = new TreeRegistry(Seed, Size);
        var foliage = new FoliageManager(null, new GrassSettings(), new FlowerSettings { enableFlowers = false },
            null, null, null, null, new TreeSettings(), Seed, Size, 1, 10, new TerrainWaterSettings(2.4f, 10, 1), registry);
        using var record = new ChunkRecord(coord);
        record.FoliageData = new ChunkFoliageData { treeCubesGenerated = true };
        record.FoliageData.treeCubeInstances.Add(tree);
        try
        {
            typeof(FoliageManager).GetMethod("EnsureTreesGenerated", Private).Invoke(foliage, new object[] { record });
            Check(registry.TryGet(tree.id, out _), "Detailed manager did not register cached placements.");
            record.FoliageData.ClearTreeCubes();
            record.FoliageData.treeCubesGenerated = true;
            typeof(FoliageManager).GetMethod("EnsureTreesGenerated", Private).Invoke(foliage, new object[] { record });
            Check(registry.Count == 0, "Detailed revision left stale records.");
        }
        finally { foliage.Dispose(); }
        var distantCoord = new ChunkCoord(4, -2);
        var distantTree = Instance(distantCoord, 1, Vector3.zero);
        using (var distant = new DistantTreeManager(new TreeSettings(), Seed, Size, 100, 5, .5f, 2,
            1, 10, .24f, 1.5f, WorldFeatureGenerationSettings.Default, treeRegistry: registry))
        {
            var build = typeof(DistantTreeManager).GetMethod("Build", Private);
            build.Invoke(distant, new object[] { distantCoord, new[] { distantTree }, TreePlacementDetail.Distant, true });
            Check(registry.TryGet(distantTree.id, out _), "Distant manager did not register placements.");
            build.Invoke(distant, new object[] { new ChunkCoord(99, 99), Array.Empty<TreeInstanceData>(), TreePlacementDetail.Distant, false });
            Check(registry.ChunkCount == 2, "Failed generation registered an empty authoritative result.");
        }
        Check(registry.TryGet(distantTree.id, out _), "Disposing a rendering consumer removed world data.");
    }

    private static void Set(object target, string name, object value)
        => target.GetType().GetField(name, Private).SetValue(target, value);
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void ExpectArgument(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Invalid snapshot was accepted.");
    }
}
