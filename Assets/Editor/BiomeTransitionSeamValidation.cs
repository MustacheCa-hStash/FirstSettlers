using System;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

public static class BiomeTransitionSeamValidation
{
    private const int Size=128,N=Size+3;
    private static void Check(bool ok,string message) {if(!ok) throw new Exception(message);}
    public static void Run()
    {
        var coord=new ChunkCoord(-2,-1);
        WorldFeaturePlan Plan(ChunkCoord c,out ControlMapPixelData controls)
        {
            var b=new BiomeType[N,N];var s=new SurfaceType[N,N];var cover=new GroundCoverType[N,N];
            var m=new float[N,N];var t=new float[N,N];var slope=new float[N,N];var river=new float[N,N];var heights=new float[N,N];
            for(int x=0;x<N;x++) for(int z=0;z<N;z++)
            {
                float wx=c.x*Size+x-1,wz=c.z*Size+z-1;
                m[x,z]=.65f+(wx+128)*.00018f+Mathf.Sin(wz*.025f)*.005f;
                t[x,z]=.5f;slope[x,z]=8;heights[x,z]=1;
                b[x,z]=m[x,z]>.65f?BiomeType.Forest:BiomeType.Grassland;s[x,z]=SurfaceType.Grass;
                cover[x,z]=b[x,z]==BiomeType.Forest?GroundCoverType.LeafLitter:GroundCoverType.Default;
            }
            var p=WorldFeaturePlanGenerator.Generate(c,Size,1937,b,s,m,t,slope,river,WorldFeatureGenerationSettings.Default,heights);
            controls=TerrainControlMapBuilder.BuildRaw(s,cover,forestStructure:p.ForestStructure,biomeMap:b,forestMembership:p.ForestMembershipMap);
            return p;
        }
        var a=Plan(coord,out var ca);var east=Plan(new ChunkCoord(-1,-1),out var ce);var north=Plan(new ChunkCoord(-2,0),out var cn);
        for(int z=0;z<N;z++)
        {
            Check(a.ForestMembershipMap[Size+1,z]==east.ForestMembershipMap[1,z],"Membership X seam differs.");
            Check(math.cmax(math.abs(a.ForestStructure.FloorEcologyMap[Size+1,z]-east.ForestStructure.FloorEcologyMap[1,z]))<.000001f,"Floor X seam differs.");
            Check(math.cmax(math.abs(ForestFloorPolicy.GrassSampleAt(a.ForestStructure.FloorEcologyMap,Size+1,z)-
                ForestFloorPolicy.GrassSampleAt(east.ForestStructure.FloorEcologyMap,1,z)))<.000001f,"Grass habitat/moss X seam differs.");
        }
        for(int x=0;x<N;x++)
        {
            Check(a.ForestMembershipMap[x,Size+1]==north.ForestMembershipMap[x,1],"Membership Z seam differs.");
            Check(math.cmax(math.abs(a.ForestStructure.FloorEcologyMap[x,Size+1]-north.ForestStructure.FloorEcologyMap[x,1]))<.000001f,"Floor Z seam differs.");
            Check(math.cmax(math.abs(ForestFloorPolicy.GrassSampleAt(a.ForestStructure.FloorEcologyMap,x,Size+1)-
                ForestFloorPolicy.GrassSampleAt(north.ForestStructure.FloorEcologyMap,x,1)))<.000001f,"Grass habitat/moss Z seam differs.");
        }
        for(int i=0;i<=Size;i++) for(int map=0;map<3;map++)
        {
            Check(ca.Maps[map][i*(Size+1)+Size].Equals(ce.Maps[map][i*(Size+1)]),"Blurred control X seam differs.");
            Check(ca.Maps[map][Size*(Size+1)+i].Equals(cn.Maps[map][i]),"Blurred control Z seam differs.");
        }
        Debug.Log("TRANSITION SEAMS PASS: negative-coordinate membership, weighted ecology and all three blurred control textures on both axes.");
    }

    public static void ValidateRealChunks(int seed,float sampleScale,int octaves,float persistence,float lacunarity,
        float water,float mountain,float heightMultiplier,WorldErosionSettings erosion,ChunkCoord center)
    {
        int count=0,mixed=0;
        var settings=WorldFeatureGenerationSettings.Default;
        foreach(var c in new[]{center,new ChunkCoord(center.x-1,center.z),new ChunkCoord(center.x+1,center.z),
            new ChunkCoord(center.x,center.z-1),new ChunkCoord(center.x,center.z+1)})
        {
            var h=HeightMapGenerator.GenerateTerrainHeightField(Size,seed,sampleScale,c,water,mountain,heightMultiplier,erosion);
            var m=ClimateGenerator.GenerateTerrainMoistureMap(Size,seed,sampleScale,octaves,persistence,lacunarity,c);
            var t=ClimateGenerator.GenerateTerrainTemperatureMap(Size,seed,sampleScale,octaves,persistence,lacunarity,c);
            var b=BiomeMapGenerator.GenerateBiomeMap(h.HeightMap,m,t,h.SlopeMap,h.MountainMaskMap,h.RiverMaskMap,water);
            var s=SurfaceMapGenerator.GenerateSurfaceTypeMap(h.HeightMap,h.SlopeMap,h.RiverMaskMap,b,water);
            var plan=WorldFeaturePlanGenerator.Generate(c,Size,seed,b,s,m,t,h.SlopeMap,h.RiverMaskMap,settings,h.HeightMap,water,h.MountainMaskMap);
            var expected=plan.Placements.Where(p=>p.featureType==WorldFeatureType.Tree).ToArray();
            var distant=DistantTreePlacement.Generate(c,Size,seed,sampleScale,octaves,persistence,lacunarity,1,heightMultiplier,
                water,mountain,12000,settings,erosion);
            Check(expected.Length==distant.Length,"Real border near/distant count mismatch at "+c);
            for(int i=0;i<expected.Length;i++)
            {
                var a=expected[i];var d=distant[i];
                Check(a.variant==d.variant && a.rotation==d.localRotation && a.scale==d.localScale &&
                    Mathf.Abs(a.sampleX-Size*.5f-d.localPosition.x)<.00001f &&
                    Mathf.Abs(a.sampleZ-Size*.5f-d.localPosition.z)<.00001f,"Real border near/distant identity mismatch.");
                count++;
                if(BiomeTransitionPolicy.IsMixed(plan.ForestMembershipMap[Mathf.RoundToInt(a.sampleX)+1,Mathf.RoundToInt(a.sampleZ)+1])) mixed++;
            }
        }
        Check(count>0 && mixed>0,"Real border fixtures did not exercise mixed trees.");
        Debug.Log($"TRANSITION REAL WORLD PASS: {count} identical near/distant trees, {mixed} mixed, five chunks around {center}.");
    }
}
