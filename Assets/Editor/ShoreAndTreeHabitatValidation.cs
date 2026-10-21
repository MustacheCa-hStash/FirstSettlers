using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class ShoreAndTreeHabitatValidation
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    [MenuItem("Tools/Terrain/Validate Narrow Shores and Tree Habitats")]
    public static void Run()
    {
        foreach (float degrees in new[] { .5f, 1f, 2f, 5f, 10f })
        {
            int oldCount=0, newCount=0;
            const float step=.025f, level=.24f;
            float rise = Mathf.Tan(degrees*Mathf.Deg2Rad)/60f; // heightMultiplier 200, worldScale .3.
            for (float x=step; x<100f; x+=step)
            {
                float height=level+x*rise;
                if(height<=level+.01f)oldCount++;
                if(SurfaceTypeClassifier.Classify(height,degrees,0,BiomeType.Grassland,level)==SurfaceType.Sand)newCount++;
            }
            float ratio=newCount/(float)oldCount;
            Check(ratio>.30f && ratio<.36f, "Flat-bank shore did not shrink by approximately two thirds.");
            Debug.Log($"SHORE_WIDTH slope={degrees:F1}: old={oldCount*step:F2}m, new={newCount*step:F2}m ({ratio:P1} retained)");
        }

        const int size=128, width=size+3;
        var b=new BiomeType[width,width]; var surface=new SurfaceType[width,width];
        var moisture=new float[width,width]; var temperature=new float[width,width];
        var slopes=new float[width,width]; var rivers=new float[width,width]; var heights=new float[width,width];
        int total=0, forestPines=0;
        var settings=WorldFeatureGenerationSettings.Default;
        settings.maxForestRocksPerChunk=settings.maxGrasslandRocksPerChunk=0;
        foreach (float wetness in new[] { .50f,.645f,.8f })
        {
            for(int x=0;x<width;x++)for(int z=0;z<width;z++)
            {
                b[x,z]=wetness>.65f?BiomeType.Forest:BiomeType.Grassland;
                surface[x,z]=SurfaceType.Grass;moisture[x,z]=wetness;temperature[x,z]=.5f;heights[x,z]=.5f;
            }
            for(int seed=1;seed<=24;seed++)
            {
                var coord=new ChunkCoord(seed%5-2,seed/5-2);
                var dense=WorldFeaturePlanGenerator.Generate(coord,size,seed*193,b,surface,moisture,temperature,slopes,rivers,settings,heights,.24f);
                var sparse=WorldFeaturePlanGenerator.GenerateTreePlacements(coord,size,seed*193,b,surface,moisture,temperature,slopes,rivers,
                    settings,(x,z)=>{},sampleHeight:(x,z)=>heights[x,z],waterLevel:.24f);
                var trees=dense.Placements.Where(p=>p.featureType==WorldFeatureType.Tree).ToArray();
                var far=sparse.Placements.Where(p=>p.featureType==WorldFeatureType.Tree).ToArray();
                Check(trees.Length==far.Length,"Near/distant tree habitat counts differ.");
                for(int i=0;i<trees.Length;i++)
                {
                    Check(trees[i].treeId==far[i].treeId && trees[i].variant==far[i].variant,"Near/distant species selection differs.");
                    Check(trees[i].variant!=WorldFeatureVariant.GrasslandWhitePineTree,"Grassland white pine was still generated.");
                    if(wetness<=.65f)Check(trees[i].variant!=WorldFeatureVariant.WhitePineTree,"Forest edge leaked white pine into grassland.");
                    else if(trees[i].variant==WorldFeatureVariant.WhitePineTree)forestPines++;
                    total++;
                }
            }
        }
        Check(total>100 && forestPines>0,"Habitat fixture did not exercise enough trees or retained forest white pine.");
        Debug.Log($"SHORE_HABITAT_PASS: five bank slopes, {total} near/distant tree placements, no grassland white pine, {forestPines} retained forest white pines.");
    }

    public static void RunBatch()
    {
        try
        {
            FoliageExclusionProfileAuthoring.Setup();
            FoliageExclusionValidation.Run();
            Run();
            WaterGenerationValidation.Run();
            EditorApplication.Exit(0);
        }
        catch(Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }
}
