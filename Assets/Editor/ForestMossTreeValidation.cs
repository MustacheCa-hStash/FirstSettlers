using System;
using System.Linq;
using System.Reflection;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

public static class ForestMossTreeValidation
{
    private static void Check(bool condition,string message) { if(!condition) throw new Exception(message); }
    [MenuItem("Tools/Terrain/Validate Forest Moss and Tree Scale")]
    public static void Run()
    {
        ValidateTrees(); ValidateMoss();
        ForestFloorValidation.Run();
        ForestFloorRedesignValidation.Run();
        DistantTreeValidation.Run();
        Debug.Log("FOREST MOSS/TREE SCALE PASS: inspector settings transport, forest/meadow near/sparse scaling, deterministic sizes, moss dominance/habitat, native grass and clover/leaf suppression, seams and far terrain.");
    }
    private static void ValidateTrees()
    {
        var inspector=new TreeSettings {treeUniformScaleRange=new Vector2(4,5)};
        var worker=(WorldFeatureGenerationSettings)typeof(ChunkManager).GetMethod("BuildWorldFeatureGenerationSettings",
            BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{inspector});
        Check(worker.treeUniformScaleRange==inspector.treeUniformScaleRange,"Inspector scale was lost before tree planning.");
        const int size=128,n=size+3;
        int count=0;
        foreach(var biome in new[]{BiomeType.Forest,BiomeType.Grassland})
        {
            var b=new BiomeType[n,n]; var s=new SurfaceType[n,n]; var m=new float[n,n]; var t=new float[n,n];
            for(int x=0;x<n;x++) for(int z=0;z<n;z++) {b[x,z]=biome;s[x,z]=SurfaceType.Grass;m[x,z]=.75f;t[x,z]=.5f;}
            for(int seed=1937;seed<1941;seed++)
            {
                var coord=new ChunkCoord(-2,3);
                var slopes=new float[n,n]; var rivers=new float[n,n];
                var a=worker; a.treeUniformScaleRange=Vector2.one;
                var one=WorldFeaturePlanGenerator.Generate(coord,size,seed,b,s,m,t,slopes,rivers,a);
                var large=WorldFeaturePlanGenerator.Generate(coord,size,seed,b,s,m,t,slopes,rivers,worker);
                var sparse=WorldFeaturePlanGenerator.GenerateTreePlacements(coord,size,seed,b,s,m,t,slopes,rivers,worker,(x,z)=>{});
                var expected=large.Placements.Where(p=>p.featureType==WorldFeatureType.Tree).ToArray();
                var original=one.Placements.Where(p=>p.featureType==WorldFeatureType.Tree).ToArray();
                var distant=sparse.Placements.Where(p=>p.featureType==WorldFeatureType.Tree).ToArray();
                Check(expected.Length==original.Length && expected.Length==distant.Length,"Scale changed tree placement count or near/far parity.");
                for(int i=0;i<expected.Length;i++)
                {
                    var p=expected[i]; var o=original[i];
                    Check(p.scale.x>=4 && p.scale.x<=5 && p.scale==Vector3.one*p.scale.x,"Inspector range did not control uniform scale.");
                    Check(o.scale==Vector3.one && p.sampleX==o.sampleX && p.sampleZ==o.sampleZ && p.variant==o.variant,"Scale changed tree identity.");
                    Check(p.scale==distant[i].scale,"Distant planner used a different tree size.");
                    count++;
                }
                using var record=new ChunkRecord(coord);
                Set(record,"worldFeaturePlan",large);Set(record,"heightMap",new float[n,n]);
                Set(record,"biomeMap",b);Set(record,"surfaceTypeMap",s);
                FoliageGenerator.GenerateTreeCubesForChunk(record,inspector,seed,size,.3f,10);
                Check(record.FoliageData.treeCubeInstances.Count==expected.Length,"Near tree instances lost placements.");
                for(int i=0;i<expected.Length;i++) Check(record.FoliageData.treeCubeInstances[i].localScale==expected[i].scale,"Near instance dropped planned scale.");
                a.treeUniformScaleRange=new Vector2(5,4);
                var reversed=WorldFeaturePlanGenerator.Generate(coord,size,seed,b,s,m,t,slopes,rivers,a).Placements.Where(p=>p.featureType==WorldFeatureType.Tree).ToArray();
                Check(reversed.Select(p=>p.scale).SequenceEqual(expected.Select(p=>p.scale)),"Reversed range changed deterministic size.");
            }
        }
        Check(count>20,"Tree scale fixture did not exercise enough trees.");
        Debug.Log($"TREE SCALE PASS: {count} forest/meadow trees; 1x versus 4–5x, reversed endpoints, near and distant planner parity.");
    }
    private static void ValidateMoss()
    {
        int strong=0,dry=0; double total=0;
        for(int x=-128;x<128;x+=2) for(int z=-128;z<128;z+=2)
        {
            var wet=ForestFloorPolicy.Evaluate(new float2(x,z),1937,.85f,4,0,.85f,0);
            var low=ForestFloorPolicy.Evaluate(new float2(x,z),1937,.1f,4,0,.85f,0);
            if(wet.z>.58f) {strong++; Check(wet.x<.007f,"Dense moss retained meadow-like grass.");}
            if(low.z>0) dry++;
            total+=wet.z;
            Check(ForestFloorPolicy.Evaluate(new float2(x,z),1937,.85f,4,.9f,.85f,0).z==0,"Moss spread into open water.");
        }
        Check(strong>100 && dry==0,"Moss has no carpet cores or survived dry habitat.");
        Check(ForestFloorPolicy.MossDominance(.4f)>.7f && ForestFloorPolicy.MossDominance(1)==1,"Moss does not dominate mixed substrate.");
        var options=ForestFloorPreview.CloverOptions();
        using var clear=ForestFloorPreview.Fixture(true,GroundCoverType.DarkGrass);
        using var carpet=ForestFloorPreview.Fixture(true,GroundCoverType.DarkGrass);
        SetMoss(clear,0);SetMoss(carpet,1);
        FoliageGenerator.GenerateCloverForChunk(clear,options,3,145678,128,.3f,10);
        FoliageGenerator.GenerateCloverForChunk(carpet,options,3,145678,128,.3f,10);
        Check(clear.FoliageData.cloverInstances.Count>0 && carpet.FoliageData.cloverInstances.Count==0,"Visible moss did not exclude clover cores.");
        var leafSettings=new LeafClusterSettings {density=1};
        var a=new LeafClusterGeneration(clear,leafSettings,145678,128,.3f,10);
        var c=new LeafClusterGeneration(carpet,leafSettings,145678,128,.3f,10);
        while(!a.Complete) a.Step(256);while(!c.Complete) c.Step(256);
        Check(c.Instances.Count>0 && c.Instances.Count<a.Instances.Count*.4f,"Moss did not retain sparse fallen leaves.");
        // Native grass copies the density field after suppression, without adding a moss buffer.
        var grass=new GrassSettings {cellsPerAxis=144,subChunksPerChunk=10};
        FoliageGenerator.GenerateGrassForChunk(clear,grass,null,null,145678,128,.3f,10);
        FoliageGenerator.GenerateGrassForChunk(carpet,grass,null,null,145678,128,.3f,10);
        Check(carpet.FoliageData.GetTotalNearGrassInstanceCount()<clear.FoliageData.GetTotalNearGrassInstanceCount()*.05f,"Native grass ignored moss suppression.");
        Debug.Log($"MOSS PASS: {strong} carpet samples, mean coverage {total/16384:P1}; clover cores excluded, leaves {a.Instances.Count}->{c.Instances.Count}, grass {clear.FoliageData.GetTotalNearGrassInstanceCount()}->{carpet.FoliageData.GetTotalNearGrassInstanceCount()}.");
    }
    private static void SetMoss(ChunkRecord record,float moss)
    {
        var floor=record.WorldFeaturePlan.ForestStructure.FloorEcologyMap;
        for(int x=0;x<floor.GetLength(0);x++) for(int z=0;z<floor.GetLength(1);z++)
        {
            var ecology=floor[x,z]; ecology.z=moss;
            ecology.x=moss==0 ? .25f : .005f; floor[x,z]=ecology;
        }
        record.NativeData.Dispose();
        Set(record,"nativeTerrainData",new ChunkRecord.NativeTerrainData(record.HeightMap,record.SlopeMap,record.BiomeMap,
            record.SurfaceTypeMap,null,record.GroundCoverMap,null,floor));
    }
    private static void Set(object record,string field,object value)=>
        record.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(record,value);
    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch(Exception e) {Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
