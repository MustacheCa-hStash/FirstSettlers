using System;
using UnityEditor;
using UnityEngine;

public static class ForestFloorRedesignValidation
{
    private static void Check(bool value,string message) {if(!value) throw new Exception(message);}
    [MenuItem("Tools/Foliage/Validate Forest Floor Redesign")]
    public static void Run()
    {
        LeafClusterValidation.Run();
        var grass=new GrassSettings {activeRingRadius=1,billboardRingRadius=3,subChunksPerChunk=10};
        var settings=new LeafClusterSettings();
        using(var system=new LeafClusterSystem(settings,145678,128,.3f,10,grass))
        {
            Check(Math.Abs(system.RenderDistance-92.16f)<.001f,"Leaf range is not 80% of grass.");
            grass.billboardRingRadius=5;
            Check(Math.Abs(system.RenderDistance-153.6f)<.001f,"Changed grass range did not update leaves.");
            settings.matchGrassRenderDistance=false;
            Check(system.RenderDistance==settings.renderDistance,"Manual leaf range override failed.");
        }
        for(uint rank=0;rank<1024;rank++)
        {
            Check(LeafClusterSystem.SelectLod(rank,0,18,30)==0 && LeafClusterSystem.SelectLod(rank,31,18,30)==1,"Leaf LOD endpoints incorrect.");
            int previous=0;
            for(float distance=18;distance<=30;distance+=.25f)
            {
                int selected=LeafClusterSystem.SelectLod(rank,distance,18,30);
                Check(selected>=previous,"Leaf LOD transition reverses or duplicates."); previous=selected;
            }
        }
        CloverSettings options=ForestFloorPreview.CloverOptions();
        using var opening=ForestFloorPreview.Fixture(true,GroundCoverType.DarkGrass);
        FoliageGenerator.GenerateCloverForChunk(opening,options,3,145678,128,.3f,10);
        int count=opening.FoliageData.cloverInstances.Count;
        Check(count>0,"Suitable forest opening received no clover.");
        options.enableForestClover=false;
        FoliageGenerator.GenerateCloverForChunk(opening,options,3,145678,128,.3f,10);
        Check(opening.FoliageData.cloverInstances.Count==0,"Forest clover toggle failed.");
        options.enableForestClover=true;
        using var deep=ForestFloorPreview.Fixture(false,GroundCoverType.LeafLitter);
        FoliageGenerator.GenerateCloverForChunk(deep,options,3,145678,128,.3f,10);
        Check(deep.FoliageData.cloverInstances.Count==0,"Clover covered deep forest litter.");
        using var steep=ForestFloorPreview.Fixture(true,GroundCoverType.DarkGrass);
        for(int x=0;x<131;x++) for(int z=0;z<131;z++) steep.SlopeMap[x,z]=30;
        steep.NativeData.Dispose(); // Retained map references match; refresh contents for the fixture.
        typeof(ChunkRecord).GetField("nativeTerrainData",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(steep,null);
        FoliageGenerator.GenerateCloverForChunk(steep,options,3,145678,128,.3f,10);
        Check(steep.FoliageData.cloverInstances.Count==0,"Clover escaped forest slope limit.");
        Debug.Log($"FOREST FLOOR REDESIGN PASS: matched/dynamic/manual range, complementary leaf LODs, solid-color scatter assets, {count} opening clover clumps, deep litter/toggle/slope exclusions.");
    }
}
