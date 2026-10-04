using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TaigaTreeValidation
{
    private const int Size = 128, N = Size + 3;
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    public static void InvestigateBatch()
    {
        try { ReportSynthetic(); ReportScene(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    [MenuItem("Tools/Terrain/Validate Taiga Trees")]
    public static void Run()
    {
        ReportSynthetic(true);
        ValidateExclusions();
        BiomeTransitionValidation.Run();
        SpruceSnowValidation.Run();
        Debug.Log("TAIGA TREE PASS: suitable Taiga generates spruce; unsuitable land is excluded; dense/sparse identities and existing forest, transition and snow checks passed.");
    }

    public static void RunBatch()
    {
        try { ShaderUtil.allowAsyncCompilation = false; Run(); ReportScene(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    private static void ReportSynthetic(bool validate = false)
    {
        var b = new BiomeType[N, N]; var s = new SurfaceType[N, N];
        var m = new float[N, N]; var t = new float[N, N]; var h = new float[N, N];
        var slope = new float[N, N]; var river = new float[N, N];
        int forest = 0, taiga = 0;
        foreach (var biome in new[] { BiomeType.Forest, BiomeType.Taiga })
        {
            for (int x = 0; x < N; x++) for (int z = 0; z < N; z++)
            { b[x,z] = biome; s[x,z] = SurfaceType.Grass; m[x,z] = .8f; t[x,z] = .24f; h[x,z] = .5f; slope[x,z] = 8f; }
            for (int i = 0; i < 8; i++)
            {
                var p = WorldFeaturePlanGenerator.Generate(new ChunkCoord(i-4, 2-i), Size, 1456789,
                    b, s, m, t, slope, river, WorldFeatureGenerationSettings.Default, h, .24f);
                int count = p.Placements.Count(a => a.featureType == WorldFeatureType.Tree);
                if (biome == BiomeType.Forest) forest += count; else taiga += count;
                if (validate && biome == BiomeType.Taiga)
                {
                    Check(count <= 18, "Taiga exceeds forest tree budget.");
                    var sparse = WorldFeaturePlanGenerator.GenerateTreePlacements(new ChunkCoord(i-4, 2-i), Size, 1456789,
                        b, s, m, t, slope, river, WorldFeatureGenerationSettings.Default, (x,z) => { },
                        sampleHeight: (x,z) => h[x,z], waterLevel: .24f);
                    var trees = p.Placements.Where(a => a.featureType == WorldFeatureType.Tree).ToArray();
                    Check(trees.Length == sparse.Placements.Count, "Synthetic Taiga dense/sparse count mismatch.");
                    for (int j = 0; j < trees.Length; j++)
                    {
                        var a = trees[j]; var c = sparse.Placements[j];
                        Check(a.variant == WorldFeatureVariant.SpruceTree && a.snowCoverage == 0f,
                            "Grass-surface Taiga should use bare spruce.");
                        Check(a.sampleX == c.sampleX && a.sampleZ == c.sampleZ && a.rotation == c.rotation &&
                            a.scale == c.scale && a.exclusionRadius == c.exclusionRadius && a.influenceRadius == c.influenceRadius,
                            "Synthetic Taiga dense/sparse identity mismatch.");
                    }
                    Check(p.ForestMembershipMap.Cast<byte>().All(v => v == 0), "Taiga entered the Forest/Grassland blend.");
                }
            }
        }
        Debug.Log($"TAIGA INVESTIGATION: identical gentle wet grass habitat across eight chunks: Forest={forest} trees, Taiga={taiga} trees. Only the biome label differs.");
        if (validate) { Check(forest == 93, "Existing forest fixture placement changed."); Check(taiga > 0, "Suitable Taiga is still treeless."); }
    }

    private static void ValidateExclusions()
    {
        foreach (string habitat in new[] { "water", "rock", "tundra", "steep", "river", "snowSurface" })
        {
            var b = new BiomeType[N,N]; var s = new SurfaceType[N,N];
            var m = new float[N,N]; var t = new float[N,N]; var slope = new float[N,N]; var river = new float[N,N];
            for (int x = 0; x < N; x++) for (int z = 0; z < N; z++)
            {
                b[x,z] = habitat == "water" ? BiomeType.Water : habitat == "rock" ? BiomeType.Rock :
                    habitat == "tundra" ? BiomeType.Tundra : BiomeType.Taiga;
                s[x,z] = habitat == "snowSurface" ? SurfaceType.Snow : SurfaceType.Grass;
                m[x,z] = .8f; t[x,z] = .24f;
                slope[x,z] = habitat == "steep" ? TerrainSlopePolicy.ForestMaxDegrees : 8f;
                river[x,z] = habitat == "river" ? .64f : 0f;
            }
            for (int i = 0; i < 8; i++)
            {
                var coord = new ChunkCoord(i-4, 2-i);
                var full = WorldFeaturePlanGenerator.Generate(coord, Size, 1456789,b,s,m,t,slope,river,WorldFeatureGenerationSettings.Default);
                var sparse = WorldFeaturePlanGenerator.GenerateTreePlacements(coord,Size,1456789,b,s,m,t,slope,river,
                    WorldFeatureGenerationSettings.Default,(x,z) => { });
                Check(!full.Placements.Any(a => a.featureType == WorldFeatureType.Tree) && sparse.Placements.Count == 0,
                    "Taiga pass accepted unsuitable habitat: " + habitat);
            }
        }
    }

    // Read-only reconstruction of the saved scene. Runtime viewer movement isn't stored here.
    private static void ReportScene()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SmearScene.unity");
        var world = UnityEngine.Object.FindAnyObjectByType<WorldManager>();
        Check(world != null, "Saved scene has no WorldManager.");
        var so = new SerializedObject(world);
        int seed = so.FindProperty("worldSeed").intValue, size = so.FindProperty("chunkSize").intValue;
        Check(size == Size, "Scene diagnostic expects 128-sample logical chunks.");
        int octaves = so.FindProperty("octaves").intValue;
        float scale = so.FindProperty("sampleScale").floatValue, ws = so.FindProperty("worldScale").floatValue;
        float hm = so.FindProperty("meshHeightMultiplier").floatValue;
        float water = so.FindProperty("globalWaterY").floatValue / (ws * hm);
        float mountain = so.FindProperty("mountainWidth").floatValue;
        float persistence = so.FindProperty("persistence").floatValue, lacunarity = so.FindProperty("lacunarity").floatValue;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var erosion = ((WorldErosionSettings)typeof(WorldManager).GetField("erosion", flags).GetValue(world)).Sanitized();
        var settings = (TreeSettings)typeof(WorldManager).GetField("treeSettings", flags).GetValue(world);
        var placement = (WorldFeatureGenerationSettings)typeof(ChunkManager).GetMethod("BuildWorldFeatureGenerationSettings",
            BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { settings });
        var viewer = (Transform)so.FindProperty("viewer").objectReferenceValue;
        var center = new ChunkCoord(Mathf.FloorToInt(viewer.position.x / (size * ws)), Mathf.FloorToInt(viewer.position.z / (size * ws)));
        Debug.Log($"TAIGA SCENE: seed={seed}, saved viewer={viewer.position}, center=({center.x},{center.z}), distant enabled={settings.enableDistantTrees}, instance radius={settings.gameObjectTreeChunkRingRadius}, distant radius={settings.distantTreeDistanceChunks}. Legacy billboard start={settings.billboardTreeChunkStartRingRadius} is ignored while distant trees are enabled.");
        var context = HeightMapGenerator.CreateSamplingContext(seed, water, mountain, erosion);
        int climateOctaves = ClimateGenerator.GetClimateOctaveCount(octaves);
        float max = ClimateGenerator.GetMaxPossibleNoise(climateOctaves, persistence);
        using var mo = ClimateGenerator.CreateOctaveOffsets(seed + 1000, climateOctaves, Allocator.Persistent);
        using var te = ClimateGenerator.CreateOctaveOffsets(seed + 2000, climateOctaves, Allocator.Persistent);
        var totals = new int[Enum.GetValues(typeof(BiomeType)).Length];
        var csv = new StringBuilder("chunkX,chunkZ,biome,temperature,moisture,height,slopeDegrees\n");
        var nearest = new System.Collections.Generic.List<ChunkCoord>();
        for (int dz = -15; dz <= 15; dz++) for (int dx = -15; dx <= 15; dx++)
        {
            var coord = new ChunkCoord(center.x + dx, center.z + dz);
            int x = coord.x * size + size/2, z = coord.z * size + size/2;
            TerrainHeightSample Sample(int sx, int sz) => HeightMapGenerator.SampleTerrainHeightNative(sx, sz,
                scale, context.RiverSeed, water, mountain, context.Erosion);
            var a = Sample(x,z);
            float gx = (Sample(x+4,z).Height - Sample(x-4,z).Height)/8f;
            float gz = (Sample(x,z+4).Height - Sample(x,z-4).Height)/8f;
            float slope = TerrainSlopePolicy.FromGradient(math.sqrt(gx*gx + gz*gz), hm);
            float m = ClimateGenerator.SampleClimate01(x,z,seed+1000,scale*10f,persistence,lacunarity,max,mo);
            float t = ClimateGenerator.SampleClimate01(x,z,seed+2000,scale*12f,persistence,lacunarity,max,te);
            var biome = BiomeClassifier.Classify(a.Height,m,t,slope,a.MountainMask,a.RiverMask,water);
            totals[(int)biome]++;
            csv.AppendLine(FormattableString.Invariant($"{coord.x},{coord.z},{biome},{t:F4},{m:F4},{a.Height:F4},{slope:F2}"));
            if (biome == BiomeType.Taiga && slope < 25f && a.RiverMask < .32f) nearest.Add(coord);
        }
        nearest.Sort((a,b) => ((a.x-center.x)*(a.x-center.x)+(a.z-center.z)*(a.z-center.z)).CompareTo(
            (b.x-center.x)*(b.x-center.x)+(b.z-center.z)*(b.z-center.z)));
        Directory.CreateDirectory("Logs/TaigaTreeValidation");
        File.WriteAllText("Logs/TaigaTreeValidation/saved-scene-biomes.csv",csv.ToString());
        Debug.Log("TAIGA SCENE sampled chunk centers: " + string.Join(", ", Enum.GetValues(typeof(BiomeType)).Cast<BiomeType>().Select(b => b+"="+totals[(int)b])));
        int tested = 0, generatedTaiga = 0;
        foreach (var coord in nearest.Take(4))
        {
            var heights = HeightMapGenerator.GenerateTerrainHeightField(size,seed,scale,coord,water,mountain,hm,erosion);
            var m = ClimateGenerator.GenerateTerrainMoistureMap(size,seed,scale,octaves,persistence,lacunarity,coord);
            var t = ClimateGenerator.GenerateTerrainTemperatureMap(size,seed,scale,octaves,persistence,lacunarity,coord);
            var b = BiomeMapGenerator.GenerateBiomeMap(heights.HeightMap,m,t,heights.SlopeMap,heights.MountainMaskMap,heights.RiverMaskMap,water);
            var s = SurfaceMapGenerator.GenerateSurfaceTypeMap(heights.HeightMap,heights.SlopeMap,heights.RiverMaskMap,b,water);
            var full = WorldFeaturePlanGenerator.Generate(coord,size,seed,b,s,m,t,heights.SlopeMap,heights.RiverMaskMap,
                placement,heights.HeightMap,water,heights.MountainMaskMap);
            var trees = full.Placements.Where(p => p.featureType == WorldFeatureType.Tree).ToArray();
            int taigaSamples = b.Cast<BiomeType>().Count(v => v == BiomeType.Taiga);
            int taigaTrees = trees.Count(p => b[Mathf.RoundToInt(p.sampleX)+1,Mathf.RoundToInt(p.sampleZ)+1] == BiomeType.Taiga);
            var sparse = DistantTreePlacement.Generate(coord,size,seed,scale,octaves,persistence,lacunarity,
                ws,hm,water,mountain,settings.seedOffset,placement,erosion);
            using var record = new ChunkRecord(coord);
            int version = record.BeginTerrainDataRequest();
            record.TryCompleteTerrainDataRequest(version, heights.HeightMap, heights.SlopeMap, m, t, b, s,
                new WaterState[N,N], new GroundCoverType[N,N], full, heights.RiverMaskMap, null);
            FoliageGenerator.GenerateTreeCubesForChunk(record,settings,seed,size,ws,hm);
            Check(record.FoliageData.treeCubeInstances.Count == trees.Length, "Near instances dropped Taiga placements.");
            Check(trees.Length == sparse.Length, "Saved scene dense/sparse tree counts differ at " + coord);
            for (int i = 0; i < trees.Length; i++)
            {
                Check(trees[i].variant == sparse[i].variant && trees[i].rotation == sparse[i].localRotation &&
                    trees[i].scale == sparse[i].localScale && trees[i].snowCoverage == sparse[i].snowCoverage &&
                    Mathf.Abs(sparse[i].localPosition.x - (trees[i].sampleX-size*.5f)*ws) < .0001f &&
                    Mathf.Abs(sparse[i].localPosition.z - (trees[i].sampleZ-size*.5f)*ws) < .0001f,
                    "Saved scene handoff identity differs.");
                var near = record.FoliageData.treeCubeInstances[i]; var far = sparse[i];
                // Full terrain runs in Burst; the sparse managed sampler can differ by millimetres.
                Check((near.localPosition-far.localPosition).sqrMagnitude < .000025f && near.leafTint.Equals(far.leafTint) &&
                    near.barkTint.Equals(far.barkTint), $"Near/distant terrain seating or tint differs at ({coord.x},{coord.z}) tree {i}: " +
                    $"position delta={(near.localPosition-far.localPosition).ToString("F6")}, leaf={near.leafTint}/{far.leafTint}, bark={near.barkTint}/{far.barkTint}.");
            }
            // Counterfactual only in scratch maps: does removing the Taiga exclusion restore trees?
            for (int x = 0; x < N; x++) for (int z = 0; z < N; z++) if (b[x,z] == BiomeType.Taiga) b[x,z] = BiomeType.Forest;
            var counterfactual = WorldFeaturePlanGenerator.Generate(coord,size,seed,b,s,m,t,heights.SlopeMap,heights.RiverMaskMap,
                placement,heights.HeightMap,water,heights.MountainMaskMap);
            Debug.Log($"TAIGA REAL CHUNK ({coord.x},{coord.z}): {taigaSamples}/{N*N} Taiga samples, {trees.Length} trees total, {taigaTrees} in Taiga; changing only Taiga to Forest in scratch maps yields {counterfactual.Placements.Count(p => p.featureType == WorldFeatureType.Tree)} trees. Near/distant agree.");
            generatedTaiga += taigaTrees;
            tested++;
        }
        Debug.Log($"TAIGA SCENE investigation complete: tested {tested} real chunks; saved-scene-biomes.csv records 961 chunk-center habitat samples.");
        Check(tested == 4 && generatedTaiga > 0, "Saved scene Taiga failed to generate trees.");
    }
}
