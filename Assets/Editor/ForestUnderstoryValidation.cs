using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Mathematics;

public static class ForestUnderstoryValidation
{
    private static void Check(bool ok,string message) {if(!ok) throw new Exception(message);}
    private static void Set(ChunkRecord record,string field,object value)=>
        typeof(ChunkRecord).GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(record,value);
    private static LeafClusterGeneration Generate(ChunkRecord record,LeafClusterSettings settings,int seed=1937)
    {
        var result=new LeafClusterGeneration(record,settings,seed,128,.3f,10,settings.IsFern?.7f:.65f);
        while(!result.Complete) result.Step(117);
        return result;
    }
    [MenuItem("Tools/Foliage/Validate Forest Understory")]
    public static void Run()
    {
        ValidateOwnership();ValidateMossGrass();ValidateFernAndLeaves();ValidateRim();
        ForestMossTreeValidation.Run();ButterflyValidation.Run();
        Debug.Log("FOREST UNDERSTORY PASS: grass/moss edge exclusion, near/far/owned-buffer parity, cached chunk biome majority, fern habitat/assets, increased leaves, forest rock-edge substrate blending.");
    }
    private static void ValidateOwnership()
    {
        var map=new BiomeType[131,131];
        for(int x=0;x<131;x++) for(int z=0;z<131;z++) map[x,z]=BiomeType.Grassland;
        Check(ChunkBiomeOwnership.Classify(map)==BiomeType.Grassland,"Meadow chunk lost ownership.");
        // All four corners meadow; interior/edges forest. Corner-only sampling would misclassify this.
        for(int x=1;x<=129;x++) for(int z=1;z<=129;z++)
            if((x!=1 && x!=129) || (z!=1 && z!=129)) map[x,z]=BiomeType.Forest;
        Check(ChunkBiomeOwnership.Classify(map)==BiomeType.Forest,"Interior forest was missed by corner samples.");
        using var record=new ChunkRecord(new ChunkCoord(-2,3));Set(record,"biomeMap",map);
        Check(record.DominantBiome==BiomeType.Forest,"Record ownership is incorrect.");
        var meadow=new BiomeType[131,131];for(int x=0;x<131;x++) for(int z=0;z<131;z++) meadow[x,z]=BiomeType.Grassland;
        Set(record,"biomeMap",meadow);Check(record.DominantBiome==BiomeType.Grassland,"Replaced biome map did not invalidate ownership.");
        for(int x=0;x<5;x++) for(int z=0;z<5;z++)
            meadow[1+128*x/4,1+128*z/4]=(BiomeType)(2+(x*5+z)%3);
        Check(ChunkBiomeOwnership.Classify(meadow)==null,"Mixed chunk acquired a false owner.");
        Check(ChunkBiomeOwnership.Classify(null)==null,"Unloaded chunk acquired insects.");
        Debug.Log("OWNERSHIP PASS: 25-read cached majority, four-corner trap, mixed chunks, replaced maps.");
    }
    private static void ValidateMossGrass()
    {
        using var record=ForestFloorPreview.Fixture(false);
        var floor=record.WorldFeaturePlan.ForestStructure.FloorEcologyMap;
        for(int x=0;x<131;x++) for(int z=0;z<131;z++)
        {
            floor[x,z]=new float4(.8f,0,x>=55 && x<=75?.4f:0,0);
            if(x<55) {record.BiomeMap[x,z]=BiomeType.Grassland;record.GroundCoverMap[x,z]=GroundCoverType.Default;}
        }
        record.NativeData.Dispose();Set(record,"nativeTerrainData",new ChunkRecord.NativeTerrainData(record.HeightMap,record.SlopeMap,
            record.BiomeMap,record.SurfaceTypeMap,null,record.GroundCoverMap,null,floor));
        var grass=new GrassSettings {cellsPerAxis=144,subChunksPerChunk=10};
        FoliageGenerator.GenerateGrassForChunk(record,grass,null,null,1937,128,.3f,10);
        var ranks=new HashSet<uint>();
        foreach(var bucket in record.FoliageData.nearGrassInstancesBySubChunk) foreach(var i in bucket)
        {
            float sampleX=(i.localPosition.x+19.2f)/.3f;
            Check(sampleX<52 || sampleX>76,"Grass poked through the moss transition.");
            ranks.Add(i.selectionRank);
        }
        Check(ranks.Count>100,"Moss removed grass outside its footprint.");
        FoliageGenerator.GenerateBillboardGrassForChunk(record,grass,null,null,1937,128,.3f,10);
        Check(record.FoliageData.billboardGrassInstances.Count==ranks.Count,"Moss near/far counts differ.");
        foreach(var i in record.FoliageData.billboardGrassInstances) Check(ranks.Contains(i.selectionRank),"Moss near/far identity differs.");
        record.NativeData.Dispose();Set(record,"nativeTerrainData",null);record.FoliageData.ClearBillboards();
        FoliageGenerator.GenerateBillboardGrassForChunk(record,grass,null,null,1937,128,.3f,10);
        Check(record.FoliageData.billboardGrassInstances.Count==ranks.Count,"Owned density fallback ignored moss.");
        Debug.Log($"MOSS GRASS PASS: hard partial-moss stripe exclusion with edge margin; {ranks.Count} grasses outside moss, near/far/fallback parity.");
    }
    private static void ValidateFernAndLeaves()
    {
        using var wet=ForestFloorPreview.Fixture(false);
        var floor=wet.WorldFeaturePlan.ForestStructure.FloorEcologyMap;
        for(int x=0;x<131;x++) for(int z=0;z<131;z++)
        {floor[x,z]=new float4(.05f,.05f,0,.3f);wet.MoistureMap[x,z]=.8f;wet.WorldFeaturePlan.ForestStructure.CanopyIntentMap[x,z]=.85f;}
        var ferns=Generate(wet,new FernSettings());var repeat=Generate(wet,new FernSettings());
        Check(ferns.Instances.Count>10 && ferns.Instances.Count==repeat.Instances.Count,"Suitable forest had no deterministic ferns.");
        for(int i=0;i<ferns.Instances.Count;i++) Check(ferns.Instances[i].position==repeat.Instances[i].position,"Fern positions changed across slices.");
        var oldFernSettings=new FernSettings {cellSize=2.5f,density=.30f,minMoisture=.45f,sizeMultiplier=1};
        var sparseFerns=Generate(wet,oldFernSettings);
        Check(ferns.Instances.Count>sparseFerns.Instances.Count*4,"Fern placement did not increase substantially.");
        var smallFerns=Generate(wet,new FernSettings {sizeMultiplier=1});
        Check(smallFerns.Instances.Count==ferns.Instances.Count,"Fern size changed unobstructed habitat density.");
        for(int i=0;i<ferns.Instances.Count;i++)
            Check(Math.Abs(ferns.Instances[i].scale-smallFerns.Instances[i].scale*2)<.0001f,"Fern size multiplier did not reach instance matrices.");
        var original=new LeafClusterSettings {placementMultiplier=1};var increased=new LeafClusterSettings();
        int a=Generate(wet,original).Instances.Count,b=Generate(wet,increased).Instances.Count;
        Check(b>=a*5,"Leaf placement increase is below +400% in the forest fixture.");
        for(int x=0;x<131;x++) for(int z=0;z<131;z++) {var ecology=floor[x,z];ecology.z=1;floor[x,z]=ecology;}
        Check(Generate(wet,new FernSettings()).Instances.Count==0,"Ferns grew through a moss carpet.");
        Check(Generate(wet,increased).Instances.Count>0,"Moss prevented fallen leaves accumulating.");
        for(int x=0;x<131;x++) for(int z=0;z<131;z++) {var ecology=floor[x,z];ecology.z=0;floor[x,z]=ecology;wet.MoistureMap[x,z]=.1f;}
        Check(Generate(wet,new FernSettings()).Instances.Count==0,"Dry ground retained ferns.");
        for(int x=0;x<131;x++) for(int z=0;z<131;z++) {wet.MoistureMap[x,z]=.8f;wet.BiomeMap[x,z]=BiomeType.Grassland;}
        Check(Generate(wet,new FernSettings()).Instances.Count==0,"Fern forest rule leaked into meadows.");
        foreach(string path in new[]{ForestFernPrefabBuilder.NearPath,ForestFernPrefabBuilder.FarPath,ForestFernPrefabBuilder.CoarsePath})
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);Check(prefab!=null,"Missing fern prefab.");
            var mesh=prefab.GetComponent<MeshFilter>().sharedMesh;var renderer=prefab.GetComponent<MeshRenderer>();
            Check(mesh!=null && mesh.subMeshCount==1 && mesh.triangles.Length/3<=300,"Fern mesh budget exceeded.");
            Check(Math.Abs(mesh.bounds.min.y)<.001f && mesh.bounds.size.y>.25f,"Fern is floating at its pivot or lost its arched silhouette.");
            Check(renderer.sharedMaterial.enableInstancing && !ShaderUtil.ShaderHasError(renderer.sharedMaterial.shader),"Fern material cannot be instanced.");
            Check(renderer.shadowCastingMode==ShadowCastingMode.Off && prefab.GetComponentsInChildren<Collider>().Length==0,"Ferns added shadow or collider overhead.");
            Check(renderer.sharedMaterial.FindPass("DepthNormals")>=0,"Fern lacks depth-normal support.");
        }
        Debug.Log($"FERN/LEAF PASS: {ferns.Instances.Count} forest ferns, dry/meadow/moss exclusions; leaf increase {a}->{b} ({b/(float)a:F2}x). Near/far instanced assets available.");
    }
    private static void ValidateRim()
    {
        var surfaces=new SurfaceType[19,19];var covers=new GroundCoverType[19,19];
        for(int x=0;x<19;x++) for(int z=0;z<19;z++) {surfaces[x,z]=x<9?SurfaceType.Grass:SurfaceType.Rock;covers[x,z]=x<9?GroundCoverType.LeafLitter:GroundCoverType.Default;}
        var maps=TerrainControlMapBuilder.BuildRaw(surfaces,covers);
        bool transition=false;
        foreach(int x in new[]{7,8})
        {
            int index=8*maps.Width+x;float grass=maps.Maps[0][index].b/255f, litter=maps.Maps[2][index].g/255f;
            Check(Math.Abs(litter/Mathf.Max(.001f,grass)-1)<.001f,"Litter did not retain full substrate at the rock transition.");
            transition|=grass>0 && grass<1;
        }
        Check(transition,"Rock transition fixture did not exercise blending.");
    }
    public static void BuildAndRunBatch()
    {
        try {LeafClusterPrefabBuilder.Build();ForestFernPrefabBuilder.Build();Run();ForestMossPreview.Run();ForestMossPreview.RunRockEdge();EditorApplication.Exit(0);}
        catch(Exception e) {Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
