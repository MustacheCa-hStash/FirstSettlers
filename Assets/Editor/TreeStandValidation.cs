using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class TreeStandValidation
{
    private const int Size=128, Seed=14567;
    [Serializable] private class TreePoint { public float x,z,r,scale; public string species; public uint stand; }
    [Serializable] private class Map
    {
        public string habitat;
        public int chunks=8,chunkSize=Size,axis=256,seed=Seed,capped,candidates,habitatQueries,competitionTests;
        public float worldScale=.3f,milliseconds,density,coreDensity,openingDensity;
        public float[] intent=new float[256*256];
        public int[] counts=new int[64];
        public List<TreePoint> trees=new();
    }
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static WorldObjectDefinition Card(string name)=>AssetDatabase.LoadAssetAtPath<WorldObjectDefinition>("Assets/ScriptableObjects/WorldObjects/"+name+"Tree.asset");
    public static TreeSettings AuthoredSettings()=>new TreeSettings
    {
        mapleTreeDefinition=Card("Maple"),sugarMapleTreeDefinition=Card("SugarMaple"),birchAspenTreeDefinition=Card("BirchAspen"),
        beechTreeDefinition=Card("Beech"),spruceTreeDefinition=Card("Spruce"),whitePineTreeDefinition=Card("WhitePine"),
        oakTreeDefinition=Card("Oak"),willowTreeDefinition=Card("Willow"),maxForestRocksPerChunk=0,maxGrasslandRocksPerChunk=0,
        maxSnowRocksPerChunk=0,maxGrasslandTreesPerChunk=24
    };
    private static TreeHabitatSample Flat(BiomeType biome)=>new TreeHabitatSample
    {Biome=biome,Surface=biome==BiomeType.Snow?SurfaceType.Snow:SurfaceType.Grass,Height=.5f,
        Moisture=biome==BiomeType.Grassland?.5f:.8f,Temperature=biome==BiomeType.Taiga?.25f:biome==BiomeType.Snow?.17f:.5f};

    [MenuItem("Tools/Terrain/Validate Layered Tree Stands")]
    public static void Run()
    {
        var authored=AuthoredSettings();
        Check(authored.mapleTreeDefinition!=null,"Authored species cards missing.");
        var settings=TreeGenerationSnapshot.Create(authored,.3f);
        Check(settings.treeDistribution.Enabled,"Default stand profile was not loaded.");
        foreach(var biome in new[]{BiomeType.Forest,BiomeType.Grassland,BiomeType.Taiga,BiomeType.Snow})
            ValidateRegion(settings,biome);
        ValidatePipeline(settings);
        ValidateEcotone(settings);
        ValidateHabitatGates(settings);
        ValidateWorldSampler(settings);
        Debug.Log("TREE_STANDS_PASS: authored radii, world-space stands, negative coordinates, exact ownership, cross-chunk spacing, order independence, dominant palettes, scale independence, density contrast, full/sparse/worker agreement and water/biome gates.");
    }
    private static void ValidateRegion(WorldFeatureGenerationSettings settings,BiomeType biome)
    {
        var habitat=Flat(biome);
        var records=new List<(ChunkCoord coord,WorldFeaturePlacement placement)>();
        var map=new Map{habitat=biome.ToString()};
        var field=new TreeStandField.Evaluator(Seed,settings.treeDistribution);
        var watch=Stopwatch.StartNew();
        for(int z=-4;z<4;z++)for(int x=-4;x<4;x++)
        {
            var coord=new ChunkCoord(x,z);
            var plan=new WorldFeaturePlan(3,3);
            var generator=new LayeredTreeDistribution(Seed,settings,(wx,wz)=>habitat,.24f);
            generator.Append(plan,coord,Size);
            Check(generator.Stats.Candidates<6000,"Neighbour work exceeded the bounded default fixture allowance.");
            map.counts[(z+4)*8+x+4]=plan.Placements.Count;
            map.capped+=generator.Stats.Capped;map.candidates+=generator.Stats.Candidates;
            map.habitatQueries+=generator.Stats.HabitatQueries;map.competitionTests+=generator.Stats.CompetitionTests;
            foreach(var p in plan.Placements)
            {
                Check(p.sampleX>=0 && p.sampleX<Size && p.sampleZ>=0 && p.sampleZ<Size,"Tree ownership extends beyond its chunk.");
                Check(p.treeId.IsValid && p.treeId.Source==TreePlacementSource.Stand,"World-lattice identity is invalid.");
                Check(p.variant!=WorldFeatureVariant.GrasslandWhitePineTree,"Grassland white pine returned.");
                if(biome==BiomeType.Grassland)Check(TreeSpeciesCatalog.IsGrassland(p.variant),"Forest species in pure grassland.");
                if(biome==BiomeType.Taiga || biome==BiomeType.Snow)Check(p.variant==WorldFeatureVariant.SpruceTree,"Cold habitat emitted non-spruce.");
                if(biome==BiomeType.Snow)Check(p.snowCoverage>=.8f && p.snowCoverage<=1f,"Snow tree lost the near-render/surface snow state.");
                var range=settings.GetTreeExclusionRadiusRange(p.variant);
                Check(p.exclusionRadius>=range.x && p.exclusionRadius<=range.y,"Stand generation ignored authored exclusion radii.");
                records.Add((coord,p));
                float wx=x*Size+p.sampleX,wz=z*Size+p.sampleZ;
                map.trees.Add(new TreePoint{x=(wx+4*Size)*.3f,z=(wz+4*Size)*.3f,r=p.exclusionRadius*.3f,
                    scale=p.scale.x,species=p.variant.ToString(),stand=field.Evaluate(wx,wz).Palette});
            }
        }
        map.milliseconds=(float)watch.Elapsed.TotalMilliseconds;
        Check(records.Select(p=>p.placement.treeId).Distinct().Count()==records.Count,"Duplicate tree identities across chunks.");
        ValidateConversion(records,biome);
        for(int i=0;i<records.Count;i++)for(int j=0;j<i;j++)
        {
            var a=records[i];var b=records[j];
            float dx=(a.coord.x-b.coord.x)*Size+a.placement.sampleX-b.placement.sampleX;
            float dz=(a.coord.z-b.coord.z)*Size+a.placement.sampleZ-b.placement.sampleZ;
            float radius=a.placement.exclusionRadius+b.placement.exclusionRadius;
            Check(dx*dx+dz*dz+.001f>=radius*radius,"Exclusion circles overlap across a chunk edge or corner.");
        }
        float area=Mathf.Pow(8*Size*.3f,2)/10000f;
        map.density=records.Count/area;
        Check(map.capped==0,"Density is being limited by a chunk quota.");
        if(biome==BiomeType.Forest)Check(map.density>=5f && map.density<=25f,"Forest density outside the mature-tree tuning envelope.");
        if(biome==BiomeType.Grassland)Check(map.density>=.1f && map.density<=5f,"Grassland density outside the sparse-grove tuning envelope.");
        if(biome==BiomeType.Taiga || biome==BiomeType.Snow)Check(records.Count>0,"Cold habitat fixture generated no trees.");

        int core=0,opening=0,coreTrees=0,openingTrees=0;
        for(int z=0;z<map.axis;z++)for(int x=0;x<map.axis;x++)
        {
            var stand=field.Evaluate(-4*Size+x*4,-4*Size+z*4);
            float cover=biome==BiomeType.Grassland?stand.Meadow:stand.Forest;
            map.intent[z*map.axis+x]=cover;
            if(cover>.7f)core++;if(cover<.2f)opening++;
        }
        foreach(var tree in map.trees)
        {
            var stand=field.Evaluate(tree.x/.3f-4*Size,tree.z/.3f-4*Size);
            float cover=biome==BiomeType.Grassland?stand.Meadow:stand.Forest;
            if(cover>.7f)coreTrees++;if(cover<.2f)openingTrees++;
        }
        map.coreDensity=coreTrees/Mathf.Max(.0001f,area*core/(map.axis*map.axis));
        map.openingDensity=openingTrees/Mathf.Max(.0001f,area*opening/(map.axis*map.axis));
        if(biome==BiomeType.Forest || biome==BiomeType.Grassland)
            Check(map.coreDensity>map.openingDensity*1.4f,"Stand cores do not read denser than openings.");
        if(biome==BiomeType.Forest)
        {
            var groups=map.trees.GroupBy(p=>p.stand).Where(g=>g.Count()>=6).ToArray();
            Check(groups.Length>=3,"Not enough coherent stands in the species fixture.");
            float dominance=groups.Average(g=>g.GroupBy(p=>p.species).Max(s=>s.Count())/(float)g.Count());
            Check(dominance>.55f,"Species palettes are too mixed to read as dominant stands.");
            Debug.Log($"STAND_PALETTE dominant species mean={dominance:P1}, stands={groups.Length}");
        }
        // Reverse request order and execute on workers. Neighbour state is never a dependency.
        foreach(var coord in records.Select(r=>r.coord).Distinct().Reverse().Take(8))
        {
            var again=Task.Run(()=>{var p=new WorldFeaturePlan(3,3);new LayeredTreeDistribution(Seed,settings,(wx,wz)=>habitat,.24f).Append(p,coord,Size);return p;}).GetAwaiter().GetResult();
            Check(again.Placements.Select(p=>p.treeId).SequenceEqual(records.Where(r=>r.coord.Equals(coord)).Select(r=>r.placement.treeId)),"Generation order changed placement.");
        }
        string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../ValidationOutputs"));Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"Stands"+biome+".json"),JsonUtility.ToJson(map));
        Debug.Log($"STAND_DENSITY {biome}: {records.Count} trees, {map.density:F2}/ha, core={map.coreDensity:F2}/ha, opening={map.openingDensity:F2}/ha; {map.milliseconds/64:F2} ms/chunk, {map.habitatQueries/64f:F1} habitat queries/chunk, {map.candidates/64f:F0} cached candidates/chunk; capped={map.capped}");
    }
    private static void ValidateConversion(List<(ChunkCoord coord,WorldFeaturePlacement placement)> records,BiomeType biome)
    {
        Check(records.Count>0,"No placements to exercise the near conversion.");
        var coord=records[0].coord;
        var plan=new WorldFeaturePlan(3,3);
        plan.Placements.AddRange(records.Where(r=>r.coord.Equals(coord)).Select(r=>r.placement));
        using var record=new ChunkRecord(coord);
        var heights=new float[Size+3,Size+3];var surfaces=new SurfaceType[Size+3,Size+3];
        for(int x=0;x<Size+3;x++)for(int z=0;z<Size+3;z++)
        {heights[x,z]=.5f;surfaces[x,z]=biome==BiomeType.Snow?SurfaceType.Snow:SurfaceType.Grass;}
        void Set(string field,object value)=>typeof(ChunkRecord).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(record,value);
        Set("worldFeaturePlan",plan);Set("heightMap",heights);Set("surfaceTypeMap",surfaces);
        FoliageGenerator.GenerateTreeCubesForChunk(record,AuthoredSettings(),Seed,Size,.3f,200f);
        Check(record.FoliageData.treeCubeInstances.Select(p=>p.id).SequenceEqual(plan.Placements.Select(p=>p.treeId)),"Near conversion dropped layered trees or their IDs.");
    }
    private static void ValidateEcotone(WorldFeatureGenerationSettings settings)
    {
        TreeHabitatSample Sample(float x,float z)
        {
            var site=Flat(x<0?BiomeType.Forest:BiomeType.Grassland);site.Moisture=x<0?.655f:.645f;return site;
        }
        var trees=new List<(float x,float z,WorldFeaturePlacement tree)>();
        for(int z=-4;z<4;z++)for(int x=-1;x<=0;x++)
        {
            var plan=new WorldFeaturePlan(3,3);new LayeredTreeDistribution(Seed,settings,Sample,.24f).Append(plan,new ChunkCoord(x,z),Size);
            foreach(var tree in plan.Placements)
            {
                float wx=x*Size+tree.sampleX,wz=z*Size+tree.sampleZ;
                Check(tree.variant!=WorldFeatureVariant.GrasslandWhitePineTree,"Meadow white pine in ecotone.");
                if(wx>=0)Check(tree.variant!=WorldFeatureVariant.WhitePineTree,"Forest stand leaked pine onto the grassland side.");
                trees.Add((wx,wz,tree));
            }
        }
        Check(trees.Count>0,"Ecotone fixture produced no trees.");
        for(int i=0;i<trees.Count;i++)for(int j=0;j<i;j++)
        {
            float dx=trees[i].x-trees[j].x,dz=trees[i].z-trees[j].z,r=trees[i].tree.exclusionRadius+trees[j].tree.exclusionRadius;
            Check(dx*dx+dz*dz+.001f>=r*r,"Competition fails across a biome boundary.");
        }
    }
    private static void ValidateHabitatGates(WorldFeatureGenerationSettings settings)
    {
        foreach(string kind in new[]{"water","steep","alpine meadow","tundra","summit","frozen snow"})
        {
            var site=Flat(BiomeType.Forest);
            if(kind=="water")site.Height=.2f;
            if(kind=="steep")site.Slope=50f;
            if(kind=="alpine meadow"){site.Biome=BiomeType.Grassland;site.Mountain=.4f;site.Height=1f;}
            if(kind=="tundra")site.Biome=BiomeType.Tundra;
            if(kind=="summit" || kind=="frozen snow")
            {site=Flat(BiomeType.Snow);if(kind=="summit")site.Height=1f;else site.Temperature=.05f;}
            var plan=new WorldFeaturePlan(3,3);
            new LayeredTreeDistribution(Seed,settings,(x,z)=>site,.24f).Append(plan,new ChunkCoord(-1,-1),Size);
            Check(plan.Placements.Count==0,"Layered habitat gate failed: "+kind);
        }
    }
    private static void ValidatePipeline(WorldFeatureGenerationSettings settings)
    {
        const int n=Size+3;
        var b=new BiomeType[n,n];var s=new SurfaceType[n,n];var m=new float[n,n];var t=new float[n,n];
        var h=new float[n,n];var slope=new float[n,n];var river=new float[n,n];
        for(int x=0;x<n;x++)for(int z=0;z<n;z++)
        {b[x,z]=BiomeType.Forest;s[x,z]=SurfaceType.Grass;m[x,z]=.8f;t[x,z]=.5f;h[x,z]=.5f;}
        var coord=new ChunkCoord(-1,-1);
        TreeHabitatSample Sample(float x,float z)=>Flat(BiomeType.Forest);
        var watch=Stopwatch.StartNew();
        var full=WorldFeaturePlanGenerator.Generate(coord,Size,Seed,b,s,m,t,slope,river,settings,h,.24f,treeHabitatSampler:Sample);
        Debug.Log($"STAND_FULL_PLAN time={watch.Elapsed.TotalMilliseconds:F2} ms (includes terrain ecology maps)");
        var legacy=settings;legacy.treeDistribution.Enabled=false;
        var fullTimes=new List<double>();var legacyTimes=new List<double>();
        for(int repeat=0;repeat<3;repeat++)
        {
            watch.Restart();WorldFeaturePlanGenerator.Generate(coord,Size,Seed,b,s,m,t,slope,river,settings,h,.24f,treeHabitatSampler:Sample);
            fullTimes.Add(watch.Elapsed.TotalMilliseconds);
            watch.Restart();WorldFeaturePlanGenerator.Generate(coord,Size,Seed,b,s,m,t,slope,river,legacy,h,.24f);
            legacyTimes.Add(watch.Elapsed.TotalMilliseconds);
        }
        fullTimes.Sort();legacyTimes.Sort();
        Debug.Log($"STAND_FULL_PLAN_WARM new median={fullTimes[1]:F2} ms; legacy median={legacyTimes[1]:F2} ms (flat controlled field, includes understory)");
        var sparse=WorldFeaturePlanGenerator.GenerateTreePlacements(coord,Size,Seed,b,s,m,t,slope,river,settings,(x,z)=>{},sampleHeight:(x,z)=>.5f,waterLevel:.24f,treeHabitatSampler:Sample);
        Compare(full,sparse);
        var scaled=settings;
        scaled.treeUniformScaleRanges=(Vector2[])settings.treeUniformScaleRanges.Clone();
        for(int i=0;i<scaled.treeUniformScaleRanges.Length;i++)scaled.treeUniformScaleRanges[i]=new Vector2(.5f,2f);
        var resized=new WorldFeaturePlan(3,3);new LayeredTreeDistribution(Seed,scaled,Sample,.24f).Append(resized,coord,Size);
        Compare(sparse,resized); // Visual scales never drive competition/identity.
        Check(sparse.Placements.Count>0,"No tree for boulder clearance test.");
        var blocked=new WorldFeaturePlan(3,3);var anchor=sparse.Placements[0];
        blocked.Placements.Add(new WorldFeaturePlacement(WorldFeatureType.Boulder,WorldFeatureVariant.Boulder,
            anchor.sampleX,anchor.sampleZ,Quaternion.identity,Vector3.one,5f,10f));
        new LayeredTreeDistribution(Seed,settings,Sample,.24f).Append(blocked,coord,Size);
        foreach(var tree in blocked.Placements.Where(p=>p.featureType==WorldFeatureType.Tree))
        {
            float dx=tree.sampleX-anchor.sampleX,dz=tree.sampleZ-anchor.sampleZ,r=tree.exclusionRadius+5f;
            Check(dx*dx+dz*dz>=r*r,"Layered tree overlaps planned boulder clearance.");
        }
        // Changing chunk partitioning must not change spatial distribution.
        var large=new WorldFeaturePlan(3,3);new LayeredTreeDistribution(Seed,settings,Sample,.24f).Append(large,new ChunkCoord(-1,-1),256);
        var parts=new List<Vector2>();
        for(int z=-2;z<0;z++)for(int x=-2;x<0;x++)
        {
            var part=new WorldFeaturePlan(3,3);new LayeredTreeDistribution(Seed,settings,Sample,.24f).Append(part,new ChunkCoord(x,z),Size);
            parts.AddRange(part.Placements.Select(p=>new Vector2(x*Size+p.sampleX,z*Size+p.sampleZ)));
        }
        Check(parts.Count==large.Placements.Count,"Chunk partition changed tree counts.");
        foreach(var p in large.Placements)Check(parts.Any(q=>(q-new Vector2(-256+p.sampleX,-256+p.sampleZ)).sqrMagnitude<.0001f),"Chunk partition shifted a tree.");
    }
    private static void Compare(WorldFeaturePlan a,WorldFeaturePlan b)
    {
        var aa=a.Placements.Where(p=>p.featureType==WorldFeatureType.Tree).ToArray();var bb=b.Placements.Where(p=>p.featureType==WorldFeatureType.Tree).ToArray();
        Check(aa.Length==bb.Length,"Full/sparse tree counts differ.");
        for(int i=0;i<aa.Length;i++)Check(aa[i].treeId==bb[i].treeId && aa[i].variant==bb[i].variant && aa[i].sampleX==bb[i].sampleX && aa[i].sampleZ==bb[i].sampleZ && aa[i].exclusionRadius==bb[i].exclusionRadius,"Full/sparse tree identity or spacing differs.");
    }
    private static void ValidateWorldSampler(WorldFeatureGenerationSettings settings)
    {
        int trees=0;
        var times=new List<double>();var oldTimes=new List<double>();
        var coords=new List<ChunkCoord>{new ChunkCoord(0,0),new ChunkCoord(5,-3),new ChunkCoord(-8,9),new ChunkCoord(20,12),new ChunkCoord(-20,-12)};
        using(var probe=new TreeHabitatSampler(Seed,600f,2,.05f,10f,200f,.24f,1f,WorldErosionSettings.Default))
        {
            int forests=0;
            var field=new TreeStandField.Evaluator(Seed,settings.treeDistribution);
            for(int z=-60;z<=60 && forests<4;z+=10)for(int x=-60;x<=60 && forests<4;x+=10)
            {
                var site=probe.Sample(x*Size+64,z*Size+64);
                if(site.Biome!=BiomeType.Forest || site.Surface!=SurfaceType.Grass || site.Slope>20f ||
                    field.Evaluate(x*Size+64,z*Size+64).Forest<.5f)continue;
                coords.Add(new ChunkCoord(x,z));forests++;
            }
            Check(forests>=2,"World sampler benchmark did not find suitable forest cores.");
        }
        foreach(var coord in coords)
        {
            using var sampler=new TreeHabitatSampler(Seed,600f,2,.05f,10f,200f,.24f,1f,WorldErosionSettings.Default);
            var expected=new WorldFeaturePlan(3,3);
            var generator=new LayeredTreeDistribution(Seed,settings,sampler.Sample,.24f);generator.Append(expected,coord,Size);
            var timer=Stopwatch.StartNew();
            var worker=DistantTreePlacement.Generate(coord,Size,Seed,600f,2,.05f,10f,.3f,200f,.24f,1f,12000,settings,WorldErosionSettings.Default);
            double newMs=timer.Elapsed.TotalMilliseconds;times.Add(newMs);
            Check(expected.Placements.Count==worker.Length,"Canonical world sampler and distant path differ.");
            for(int i=0;i<worker.Length;i++)Check(expected.Placements[i].treeId==worker[i].id && expected.Placements[i].variant==worker[i].variant,"Canonical world identity differs from distant worker.");
            trees+=worker.Length;
            var legacy=settings;legacy.treeDistribution.Enabled=false;
            timer.Restart();DistantTreePlacement.Generate(coord,Size,Seed,600f,2,.05f,10f,.3f,200f,.24f,1f,12000,legacy,WorldErosionSettings.Default);
            double oldMs=timer.Elapsed.TotalMilliseconds;oldTimes.Add(oldMs);
            Debug.Log($"STAND_WORLD {coord.x},{coord.z}: trees={worker.Length}, terrain samples={sampler.HeightSampleCount}, habitat samples={sampler.HabitatSampleCount}, new={newMs:F2} ms, legacy={oldMs:F2} ms");
        }
        Check(trees>=6,"World-worker parity did not exercise enough actual forest trees.");
        times.Sort();oldTimes.Sort();
        Debug.Log($"STAND_WORKER_TIMING new median={times[times.Count/2]:F2} ms; legacy median={oldTimes[oldTimes.Count/2]:F2} ms; world trees checked={trees} ({coords.Count} mixed/forest chunks, editor CPU)");
    }
    public static void ReportDimensions()
    {
        string scene=Path.GetFullPath(Path.Combine(Application.dataPath,"../ValidationSceneBindings.txt"));
        if(!File.Exists(scene))scene=Path.Combine(Application.dataPath,"Scenes/SmearScene.unity");
        if(!File.Exists(scene))return;
        string text=File.ReadAllText(scene);
        var settings=TreeGenerationSnapshot.Create(AuthoredSettings());
        foreach(var pair in new[]{("mapleTreePrefab",WorldFeatureVariant.MapleTree),("sugarMapleTreePrefab",WorldFeatureVariant.SugarMapleTree),
            ("birchAspenTreePrefab",WorldFeatureVariant.BirchAspenTree),("beechTreePrefab",WorldFeatureVariant.BeechTree),
            ("spruceTreePrefab",WorldFeatureVariant.SpruceTree),("whitePineTreePrefab",WorldFeatureVariant.WhitePineTree),
            ("oakTreePrefab",WorldFeatureVariant.OakTree),("grasslandWillowTreePrefab",WorldFeatureVariant.GrasslandWillowTree)})
        {
            var match=Regex.Match(text,@"    "+pair.Item1+@": \{fileID: [^,}\r\n]+, guid: (\w+)");
            if(!match.Success)match=Regex.Match(text,@"    treeLOD0GameObjectPrefab: \{fileID: [^,}\r\n]+, guid: (\w+)");
            if(!match.Success)continue;
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(match.Groups[1].Value));
            if(prefab==null)continue;
            bool valid=false;var bounds=new Bounds();
            foreach(var filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if(filter.sharedMesh==null)continue;
                var b=filter.sharedMesh.bounds;
                var matrix=prefab.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix;
                for(int corner=0;corner<8;corner++)
                {
                    var point=matrix.MultiplyPoint3x4(b.center+Vector3.Scale(b.extents,new Vector3((corner&1)==0?-1:1,(corner&2)==0?-1:1,(corner&4)==0?-1:1)));
                    if(!valid){bounds=new Bounds(point,Vector3.zero);valid=true;}else bounds.Encapsulate(point);
                }
            }
            if(!valid)continue;
            var range=settings.GetTreeUniformScaleRange(pair.Item2);float scale=(range.x+range.y)*.5f;
            Debug.Log($"TREE_WORLD_SIZE {pair.Item2}: prefab={prefab.name}, height={bounds.size.y*scale:F1}m, crown span={Mathf.Max(bounds.size.x,bounds.size.z)*scale:F1}m; player=1.8m");
        }
    }
    public static void RunBatch()
    {
        try{Run();ReportDimensions();TreeRegistryValidation.Run();TreeGameplayValidation.Run();EditorApplication.Exit(0);}
        catch(Exception exception){UnityEngine.Debug.LogException(exception);EditorApplication.Exit(1);}
    }
}
