using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

public static class BiomeTransitionValidation
{
    private const int Size = 128, N = Size + 3, Seed = 1937;
    private const float Scale = .3f;
    private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    private static void Set(ChunkRecord record, string name, object value) =>
        typeof(ChunkRecord).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(record, value);

    [MenuItem("Tools/Terrain/Validate Forest–Grassland Transitions")]
    public static void Run()
    {
        ValidatePolicy();
        ValidatePlanner();
        ValidateGrassAndLifetime();
        ValidateGrassProfileSubsets();
        ValidateGrassMoss();
        ValidatePlants();
        ValidateTerrain();
        BiomeTransitionSeamValidation.Run();
        DistantTreeValidation.Run();
        // Existing substrate, negative-coordinate seam and far-terrain regressions.
        ForestFloorValidation.Run();
        WritePreview();
        var preview=ScriptableObject.CreateInstance<BiomeTransitionPreview>();
        try {preview.ReadSceneSettings();preview.FindBorder();preview.ValidateWorldConsumers();preview.Generate();preview.Export();}
        finally {UnityEngine.Object.DestroyImmediate(preview);}
        Debug.Log("BIOME TRANSITION PASS: scoped membership, variable width, pooled full/sparse parity, shared tree budget, " +
            "near/billboard/fallback grass identities, plant substitution/toggles/moss, lease lifetime, terrain endpoints and previews.");
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    public static void RunGraphicsBatch()
    {
        try
        {
            Run();
            ForestGrassValidation.Run();
            Debug.Log("BIOME TRANSITION GPU PASS: existing resident grass arena and complementary family/LOD lists validated.");
            EditorApplication.Exit(0);
        }
        catch(Exception ex) {Debug.LogException(ex);EditorApplication.Exit(1);}
    }

    public static void RunGrassMossBatch()
    {
        try
        {
            Unity.Burst.BurstCompiler.Options.EnableBurstCompileSynchronously = true;
            ValidateGrassMoss();
            ValidateGrassAndLifetime();
            ValidateGrassProfileSubsets();
            BiomeTransitionSeamValidation.Run();
            ForestFloorValidation.Run();
            ForestUnderstoryValidation.Run();
            Debug.Log("GRASS MOSS FIX PASS: gradual visible-moss retention, transition profiles, near/far/fallback identity, seams and existing floor/understory regressions.");
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    private static void ValidateGrassMoss()
    {
        var options = new GrassSettings { cellsPerAxis = 112, subChunksPerChunk = 5 };
        foreach (float membership in new[] { 0f, .25f, .5f, 1f })
        {
            Dictionary<uint, FoliageInstanceData> baseline = null;
            foreach (float rawMoss in new[] { 0f, .05f, .3f, .58f, 1f })
            {
                using var record = Fixture(membership);
                var plan = record.WorldFeaturePlan;
                float weight = BiomeTransitionPolicy.ForestWeight(plan.ForestMembershipMap[1,1], record.BiomeMap[1,1]);
                for (int x = 0; x < N; x++) for (int z = 0; z < N; z++)
                    plan.ForestStructure.FloorEcologyMap[x,z] = BiomeTransitionPolicy.BlendFloor(new float4(.2f,0f,rawMoss,.3f),weight);
                RebuildNative(record);
                FoliageGenerator.GenerateGrassForChunk(record,options,null,null,Seed,Size,Scale,10);
                var near = new Dictionary<uint,FoliageInstanceData>();
                foreach (var bucket in record.FoliageData.nearGrassInstancesBySubChunk)
                    foreach (var g in bucket) Check(near.TryAdd(g.selectionRank,g), "Moss duplicated a grass candidate.");
                if (baseline == null) baseline = near;
                Check(near.All(p => baseline.TryGetValue(p.Key,out var b) && p.Value.forestBlend == b.forestBlend),
                    "Moss added candidates or changed their chosen grass family.");
                if (rawMoss <= .05f) Check(near.Count >= baseline.Count * .98f, "Faint moss removed a grass patch.");
                if (membership == 0f) Check(near.Count == baseline.Count, "Unweighted moss reduced pure meadow grass.");
                if (membership == .25f && rawMoss == 1f)
                    Check(near.Values.Count(g => g.forestBlend == 0f) > 1000, "Faint weighted moss vetoed the meadow share.");
                if (membership == 1f && rawMoss >= .58f) Check(near.Count == 0, "Dense visible moss retained grass.");
                if (membership == 1f && rawMoss == .3f)
                    Check(near.Count > baseline.Count * .3f && near.Count < baseline.Count * .7f,
                        "Partial moss did not gradually thin grass.");
                void CheckBillboards()
                {
                    FoliageGenerator.GenerateBillboardGrassForChunk(record,options,null,null,Seed,Size,Scale,10);
                    Check(record.FoliageData.billboardGrassInstances.Count == near.Count,
                        "Moss near/far/fallback counts disagree.");
                    foreach (var g in record.FoliageData.billboardGrassInstances)
                        Check(near.TryGetValue(g.selectionRank,out var n) && g.forestBlend == n.forestBlend && g.localPosition == n.localPosition,
                            "Moss near/far/fallback changed candidate identity.");
                }
                CheckBillboards();
                record.NativeData.Dispose(); Set(record,"nativeTerrainData",null); record.FoliageData.ClearBillboards();
                CheckBillboards();
                Debug.Log($"GRASS MOSS retention: forest={weight:F3}, visible coverage={rawMoss*weight:F3}, candidates={near.Count}/{baseline.Count}.");
            }
        }
        // A faint neighboring colony must blur into coverage, never mark a whole cell as excluded.
        var floor = new float4[5,5];
        for (int x = 0; x < 5; x++) for (int z = 0; z < 5; z++) floor[x,z] = new float4(1,0,0,0);
        floor[3,2].z = .05f;
        var sample = ForestFloorPolicy.GrassSampleAt(floor,2,2);
        Check(sample.x == 1f && ForestFloorPolicy.GrassRetention(sample.y) == 1f,
            "A faint neighboring colony expanded into a grass exclusion.");
        Debug.Log("GRASS MOSS PASS: faint edges survive, partial cover thins, dense cores clear, meadow share retained, and near/far/fallback parity.");
    }

    private static BiomeTransitionSample Sample(float moisture, BiomeType biome = BiomeType.Grassland,
        float temperature = .5f, float mountain = 0f, SurfaceType surface = SurfaceType.Grass) =>
        BiomeTransitionPolicy.Evaluate(biome, surface, moisture, temperature, 1f, mountain, 0f, 0f, 0f);

    private static void ValidatePolicy()
    {
        foreach (BiomeType biome in Enum.GetValues(typeof(BiomeType)))
            Check((Sample(.65f, biome).Border != BiomeBorder.None) ==
                (biome == BiomeType.Forest || biome == BiomeType.Grassland), "Another biome entered this transition.");
        Check(Sample(.65f, temperature: .25f).Border == BiomeBorder.None, "Cold grassland entered forest transition.");
        Check(Sample(.65f, mountain: .4f).Border == BiomeBorder.None, "Mountain grassland entered forest transition.");
        Check(Sample(.65f, surface: SurfaceType.Riverbed).Border == BiomeBorder.None, "Riverbed entered transition.");
        Check(BiomeTransitionPolicy.Evaluate(BiomeType.Grassland, SurfaceType.Grass, .65f, .5f, 0f, 0f, 0f, 0f, 0f).Border == BiomeBorder.None,
            "Submerged land entered transition.");
        Check(BiomeTransitionPolicy.SelectForest(1f,1f) && !BiomeTransitionPolicy.SelectForest(0f,0f),"Asset-family endpoints are not exact.");
        float previous = -1f;
        for (int i = 0; i <= 100; i++)
        {
            var sample = Sample(.62f + i * .0006f);
            float forest = BiomeTransitionPolicy.ForestWeight(BiomeTransitionPolicy.Encode(sample), BiomeType.Grassland);
            Check(forest >= previous && Mathf.Abs(forest - sample.Forest) <= 1f / 508f + .00001f, "Membership encoding is not monotonic/accurate.");
            Check(Mathf.Abs(sample.Forest + sample.Meadow - 1f) < .00001f, "Weights do not partition population.");
            Check(BiomeTransitionPolicy.Population(8, 18, forest) >= 8 && BiomeTransitionPolicy.Population(8, 18, forest) <= 18,
                "Weighted density stacked both populations.");
            previous = forest;
        }
        int Width(float gradient)
        {
            int count = 0;
            for (int x = -100; x <= 100; x++) if (Sample(.65f + x * gradient).Edge > .1f) count++;
            return count;
        }
        Check(Width(.0002f) > Width(.001f) * 3, "Transition became an even distance band.");
        Debug.Log($"TRANSITION POLICY PASS: measured field widths {Width(.0002f)} / {Width(.001f)} samples for different climate gradients.");
    }

    private static void ValidatePlanner()
    {
        var scratch = new WorldFeaturePlan(N, N); var prepared = new bool[N, N];
        var sb = new BiomeType[N,N]; var ss = new SurfaceType[N,N];
        var sm = new float[N,N]; var st = new float[N,N]; var sl = new float[N,N]; var sr = new float[N,N];
        int totalTrees = 0, mixedTrees = 0, totalRocks = 0, totalBushes = 0;
        var settings = WorldFeatureGenerationSettings.Default;
        settings.grasslandLargeRockPrefabCount = 2;
        for (int scenario = 0; scenario < 16; scenario++)
        {
            var coord = new ChunkCoord(scenario - 8, 5 - scenario);
            var b = new BiomeType[N,N]; var s = new SurfaceType[N,N]; var m = new float[N,N];
            var t = new float[N,N]; var slopes = new float[N,N]; var r = new float[N,N];
            var h = new float[N,N]; var mountain = new float[N,N];
            for (int x = 0; x < N; x++) for (int z = 0; z < N; z++)
            {
                m[x,z] = scenario % 4 == 0 ? .75f : scenario % 4 == 1 ? .55f :
                    .65f + (x - 65f) * .00035f + Mathf.Sin(z * .07f) * .004f;
                t[x,z] = .5f; h[x,z] = 1f; slopes[x,z] = 8f; r[x,z] = .05f;
                mountain[x,z] = scenario % 4 == 3 && z < 20 ? .4f : 0f;
                b[x,z] = BiomeClassifier.Classify(h[x,z], m[x,z], t[x,z], slopes[x,z], mountain[x,z], r[x,z], 0f);
                s[x,z] = SurfaceType.Grass;
            }
            var full = WorldFeaturePlanGenerator.Generate(coord, Size, Seed, b, s, m, t, slopes, r, settings, h, 0f, mountain);
            var sparse = WorldFeaturePlanGenerator.GenerateTreePlacements(coord, Size, Seed, sb, ss, sm, st, sl, sr, settings,
                (x,z) => { sb[x,z]=b[x,z];ss[x,z]=s[x,z];sm[x,z]=m[x,z];st[x,z]=t[x,z];sl[x,z]=slopes[x,z];sr[x,z]=r[x,z]; },
                scratch, prepared, (x,z)=>h[x,z], 0f, (x,z)=>mountain[x,z]);
            var expected = full.Placements.Where(p => p.featureType != WorldFeatureType.Bush).ToArray();
            Check(expected.Length == sparse.Placements.Count, $"Full/sparse counts differ in scenario {scenario}.");
            for (int i = 0; i < expected.Length; i++)
            {
                var a = expected[i]; var c = sparse.Placements[i];
                Check(a.featureType == c.featureType && a.variant == c.variant && a.sampleX == c.sampleX &&
                    a.sampleZ == c.sampleZ && a.scale == c.scale && a.rotation == c.rotation,
                    $"Pooled sparse identity differs at {scenario}/{i}.");
            }
            int trees = expected.Count(p => p.featureType == WorldFeatureType.Tree);
            Check(trees <= Mathf.Max(18, settings.maxGrasslandTreesPerChunk), "Shared tree budget exceeded.");
            totalTrees += trees;
            int rocks=expected.Count(p=>p.featureType==WorldFeatureType.Boulder);
            Check(rocks<=Mathf.Max(settings.maxForestRocksPerChunk,settings.maxGrasslandRocksPerChunk),"Shared rock budget exceeded.");
            totalRocks+=rocks;
            foreach(var bush in full.Placements.Where(p=>p.featureType==WorldFeatureType.Bush))
            {
                totalBushes++;
                Check(BiomeTransitionPolicy.ForestWeight(full,b[Mathf.RoundToInt(bush.sampleX)+1,Mathf.RoundToInt(bush.sampleZ)+1],
                    Mathf.RoundToInt(bush.sampleX)+1,Mathf.RoundToInt(bush.sampleZ)+1)>0f,"Berry bush escaped forest membership.");
            }
            foreach (var p in expected.Where(p => p.featureType == WorldFeatureType.Tree))
                if (BiomeTransitionPolicy.IsMixed(full.ForestMembershipMap[Mathf.RoundToInt(p.sampleX)+1,Mathf.RoundToInt(p.sampleZ)+1])) mixedTrees++;
        }
        Check(totalRocks > 0 && totalBushes > 0 && totalTrees > 20 && mixedTrees > 0, "Planner fixtures did not exercise transition trees.");
        Debug.Log($"TRANSITION PLANNER PASS: 16 reused sparse workspaces, {totalTrees} trees, {mixedTrees} in mixed habitat, {totalRocks} rocks and {totalBushes} weighted berry bushes.");
    }

    // Controlled profile isolates substitution from independently varying moss colonies.
    private static ChunkRecord Fixture(float forest, bool varying = false)
    {
        var record = ForestFloorPreview.Fixture(false);
        var plan = record.WorldFeaturePlan;
        for (int x = 0; x < N; x++) for (int z = 0; z < N; z++)
        {
            float w = varying ? Mathf.Clamp01((x - 16f - Mathf.Sin(z*.08f)*8f) / 100f) : forest;
            record.BiomeMap[x,z] = w > .5f ? BiomeType.Forest : BiomeType.Grassland;
            record.GroundCoverMap[x,z] = GroundCoverType.DarkGrass;
            record.MoistureMap[x,z] = .65f;
            plan.ForestMembershipMap[x,z] = BiomeTransitionPolicy.Encode(new BiomeTransitionSample(BiomeBorder.ForestGrassland,w));
            w = BiomeTransitionPolicy.ForestWeight(plan.ForestMembershipMap[x,z], record.BiomeMap[x,z]);
            plan.ForestStructure.FloorEcologyMap[x,z] = BiomeTransitionPolicy.BlendFloor(new float4(.2f,0f,0f,.3f),w);
            plan.ForestStructure.CanopyIntentMap[x,z] = .65f;
        }
        RebuildNative(record);
        return record;
    }

    private static void RebuildNative(ChunkRecord record)
    {
        record.NativeData?.Dispose();
        Set(record,"nativeTerrainData",new ChunkRecord.NativeTerrainData(record.HeightMap,record.SlopeMap,record.BiomeMap,
            record.SurfaceTypeMap,null,record.GroundCoverMap,null,record.WorldFeaturePlan.ForestStructure.FloorEcologyMap,
            record.WorldFeaturePlan.ForestMembershipMap));
    }

    private static void ValidateGrassAndLifetime()
    {
        using var record = Fixture(.5f, true);
        var options = new GrassSettings { cellsPerAxis = 112, subChunksPerChunk = 5 };
        FoliageGenerator.GenerateGrassForChunk(record,options,null,null,Seed,Size,Scale,10);
        var near = new Dictionary<uint,FoliageInstanceData>();
        int forest = 0, meadow = 0;
        foreach (var bucket in record.FoliageData.nearGrassInstancesBySubChunk) foreach (var g in bucket)
        {
            Check(near.TryAdd(g.selectionRank,g),"Grass candidate duplicated across subchunks.");
            Check(g.forestBlend == 0f || g.forestBlend == 1f,"Grass candidate did not choose one asset family.");
            if (g.forestBlend == 1) forest++; else meadow++;
        }
        Check(forest > 100 && meadow > 100 && near.Count <= options.cellsPerAxis * options.cellsPerAxis, "Grass did not substitute within one population.");
        FoliageGenerator.GenerateBillboardGrassForChunk(record,options,null,null,Seed,Size,Scale,10);
        Check(record.FoliageData.billboardGrassInstances.Count == near.Count,"Near/far grass counts disagree.");
        foreach (var g in record.FoliageData.billboardGrassInstances)
            Check(near.TryGetValue(g.selectionRank,out var n) && g.forestBlend == n.forestBlend && g.localPosition == n.localPosition,
                "Billboard grass changed position or family.");
        var data = record.NativeData;
        using (var lease = data.AcquireLease())
        {
            data.Dispose();
            Check(data.AcquireLease() == null && lease.Data.ForestMembershipMap[50*N+50] == record.WorldFeaturePlan.ForestMembershipMap[50,50],
                "Native membership outlived its lease incorrectly.");
        }
        Set(record,"nativeTerrainData",null); record.FoliageData.ClearBillboards();
        FoliageGenerator.GenerateBillboardGrassForChunk(record,options,null,null,Seed,Size,Scale,10);
        Check(record.FoliageData.billboardGrassInstances.Count == near.Count,"Owned-buffer fallback changed transition density.");
        foreach (var g in record.FoliageData.billboardGrassInstances)
            Check(near[g.selectionRank].forestBlend == g.forestBlend,"Owned-buffer fallback changed grass family.");
        Debug.Log($"TRANSITION GRASS PASS: {near.Count} single-population candidates ({forest} forest / {meadow} meadow), near/far/fallback parity and lease lifetime.");
    }

    private static void ValidateGrassProfileSubsets()
    {
        var options=new GrassSettings {cellsPerAxis=112,subChunksPerChunk=5};
        HashSet<uint> ForestRanks(float w,out int total)
        {
            using var record=Fixture(w);
            FoliageGenerator.GenerateGrassForChunk(record,options,null,null,Seed,Size,Scale,10);
            var ranks=new HashSet<uint>();total=0;
            foreach(var bucket in record.FoliageData.nearGrassInstancesBySubChunk) foreach(var g in bucket)
            {total++;if(g.forestBlend==1f) ranks.Add(g.selectionRank);}
            return ranks;
        }
        var full=ForestRanks(1f,out int forestCount);var mixed=ForestRanks(.5f,out int mixedCount);
        var meadow=ForestRanks(0f,out int meadowCount);
        Check(mixed.Count>0 && mixed.IsSubsetOf(full) && meadow.Count==0,"Mixed grass inflated the forest profile's population.");
        Check(forestCount<mixedCount && mixedCount<meadowCount,"Single grass population did not interpolate between profile densities.");
        Debug.Log($"TRANSITION GRASS PROFILES PASS: pure forest {forestCount}, mixed {mixedCount} ({mixed.Count} forest subset), meadow {meadowCount}.");
    }

    private static void ValidatePlants()
    {
        var clover = new CloverSettings { patchSpawnChance=1,patchNoiseThreshold=0,patchCellSize=12,forestPatchChance=.5f };
        var dandelion = new DandelionSettings { patchSpawnChance=1,patchNoiseThreshold=0,patchCellSize=12 };
        var flower = new FlowerSettings { allowedBiomes=new[]{BiomeType.Grassland},patchSpawnChance=1,patchNoiseThreshold=0,patchCellSize=12 };
        var counts = new List<int[]>();
        foreach (float w in new[]{0f,.5f,1f})
        {
            using var record = Fixture(w);
            FoliageGenerator.GenerateCloverForChunk(record,clover,3,Seed,Size,Scale,10);
            FoliageGenerator.GenerateDandelionsForChunk(record,dandelion,Seed,Size,Scale,10);
            FoliageGenerator.GenerateFlowersForChunk(record,flower,Seed,Size,Scale,10);
            var leaves = new LeafClusterGeneration(record,new LeafClusterSettings(),Seed,Size,Scale,10);
            var ferns = new LeafClusterGeneration(record,new FernSettings(),Seed,Size,Scale,10);
            while(!leaves.Complete) leaves.Step(113); while(!ferns.Complete) ferns.Step(127);
            counts.Add(new[]{record.FoliageData.cloverInstances.Count,record.FoliageData.dandelionInstances.Count,
                record.FoliageData.flowerInstances.Count,leaves.Instances.Count,ferns.Instances.Count});
        }
        for(int species=1;species<=2;species++)
            Check(counts[0][species]>counts[1][species] && counts[1][species]>0 && counts[2][species]==0,"Meadow plants did not taper across forest membership.");
        for(int species=3;species<=4;species++)
            Check(counts[0][species]==0 && counts[1][species]>0 && counts[2][species]>=counts[1][species],"Forest scatter did not taper into meadow.");
        Check(counts[0][0]>counts[1][0] && counts[1][0]>=counts[2][0],"Clover stacked forest and meadow populations.");
        using(var record=Fixture(.5f))
        {
            clover.enableForestClover=false;
            FoliageGenerator.GenerateCloverForChunk(record,clover,3,Seed,Size,Scale,10);
            Check(record.FoliageData.cloverInstances.Count>0 && record.FoliageData.cloverInstances.Count<counts[1][0],"Forest clover toggle removed meadow clover or failed to remove forest share.");
            for(int x=0;x<N;x++) for(int z=0;z<N;z++) record.WorldFeaturePlan.ForestStructure.FloorEcologyMap[x,z]=new float4(.2f,0,.6f,0);
            RebuildNative(record);
            FoliageGenerator.GenerateFlowersForChunk(record,flower,Seed,Size,Scale,10);
            FoliageGenerator.GenerateDandelionsForChunk(record,dandelion,Seed,Size,Scale,10);
            Check(record.FoliageData.flowerInstances.Count==0 && record.FoliageData.dandelionInstances.Count==0,"Transition plants ignored moss.");
        }
        // A forest litter label must not create a clover cliff at the inner edge.
        int previous=int.MaxValue;
        foreach(float w in new[]{.5f,.98f,1f})
        {
            using var closed=Fixture(w);
            for(int x=0;x<N;x++) for(int z=0;z<N;z++) closed.GroundCoverMap[x,z]=GroundCoverType.LeafLitter;
            RebuildNative(closed);clover.enableForestClover=true;
            FoliageGenerator.GenerateCloverForChunk(closed,clover,3,Seed,Size,Scale,10);
            int count=closed.FoliageData.cloverInstances.Count;
            Check(count<previous && (w<1f?count>0:count==0),"Margin clover did not reach the closed-forest habitat smoothly.");
            previous=count;
        }
        ValidateOptionalFlowers();
        Debug.Log("TRANSITION PLANTS PASS: meadow/mixed/forest counts (clover, dandelion, flower, leaf, fern) " +
            string.Join("; ",counts.Select(c=>string.Join("/",c))));
    }

    private static void ValidateOptionalFlowers()
    {
        var asset=new GameObject("Transition optional-flower fixture");
        try
        {
            var options=new FlowerSettings {patchSpawnChance=0,tallFlowerPrefab=asset,daisyWeedPrefab=asset,
                tallFlowerPatchSpawnChance=1,tallFlowerPatchNoiseThreshold=0,tallFlowerMeadowSpawnChance=1,
                tallFlowerMeadowNoiseThreshold=0,daisyWeedPatchSpawnChance=1,daisyWeedPatchNoiseThreshold=0};
            int previous=int.MaxValue;
            foreach(float w in new[]{0f,.5f,1f})
            {
                using var record=Fixture(w);
                FoliageGenerator.GenerateFlowersForChunk(record,options,Seed,Size,Scale,10);
                int count=record.FoliageData.flowerInstances.Count;
                Check(count<previous && (w<1f?count>0:count==0),"Optional lupine/daisy drifts ignored meadow membership.");
                previous=count;
            }
        }
        finally {UnityEngine.Object.DestroyImmediate(asset);}
    }

    private static void ValidateTerrain()
    {
        using var meadow=Fixture(0); using var forest=Fixture(1); using var mixed=Fixture(.5f);
        ControlMapPixelData Controls(ChunkRecord record) => TerrainControlMapBuilder.BuildRaw(record.SurfaceTypeMap,record.GroundCoverMap,
            forestStructure:record.WorldFeaturePlan.ForestStructure,biomeMap:record.BiomeMap,forestMembership:record.WorldFeaturePlan.ForestMembershipMap);
        var a=Controls(meadow);var b=Controls(forest);var c=Controls(mixed);int center=64*a.Width+64;
        Check(a.Maps[2][center].Equals(new Color32(0,0,0,0)),"Pure meadow retained forest overlay.");
        Check(b.Maps[2][center].r==255 && c.Maps[2][center].r>120 && c.Maps[2][center].r<135,"Continuous floor overlay did not follow membership.");
        // A registered pair sentinel must leave unrelated covers unchanged.
        for(int x=0;x<N;x++) for(int z=0;z<N;z++)
        {mixed.BiomeMap[x,z]=BiomeType.Taiga;mixed.WorldFeaturePlan.ForestMembershipMap[x,z]=0;}
        var expected=TerrainControlMapBuilder.BuildRaw(mixed.SurfaceTypeMap,mixed.GroundCoverMap,biomeMap:mixed.BiomeMap);
        Check(Controls(mixed).Maps[2].SequenceEqual(expected.Maps[2]),"Taiga ground changed through this pair policy.");
        Debug.Log("TRANSITION TERRAIN PASS: meadow/forest endpoints, blended overlays, unrelated biome control preservation.");
    }

    private static void WritePreview()
    {
        const int width=512,height=256;
        var texture=new Texture2D(width,height,TextureFormat.RGBA32,false);
        try
        {
            for(int x=0;x<width;x++) for(int z=0;z<height;z++)
            {
                float boundary=256+Mathf.Sin(z*.025f)*50;
                float gradient=Mathf.Lerp(.00017f,.0007f,(Mathf.Sin(z*.018f)+1)*.5f);
                float moisture=.65f+(x-boundary)*gradient;
                float w=Sample(moisture).Forest;
                Color color=Color.Lerp(new Color(.72f,.78f,.32f),new Color(.08f,.30f,.16f),w);
                if(Mathf.Abs(w-.5f)<.015f) color=new Color(.95f,.93f,.8f);
                texture.SetPixel(x,z,color);
            }
            texture.Apply(); Directory.CreateDirectory("ArtReferences/BiomeTransitions");
            File.WriteAllBytes("ArtReferences/BiomeTransitions/Membership.png",texture.EncodeToPNG());
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    }
}
