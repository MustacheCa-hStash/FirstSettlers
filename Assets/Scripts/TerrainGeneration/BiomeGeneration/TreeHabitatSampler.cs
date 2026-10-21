using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public struct TreeHabitatSample
{
    public BiomeType Biome;
    public SurfaceType Surface;
    public float Height, Moisture, Temperature, Slope, River, Mountain;
}

/// <summary>Canonical lazy world sampler for both detailed and distant placement, including unloaded neighbours.</summary>
public sealed class TreeHabitatSampler : IDisposable
{
    private readonly int seed;
    private readonly float scale,heightMultiplier,water,persistence,lacunarity,climateMax;
    private readonly TerrainHeightSamplingContext context;
    private NativeArray<float2> moistureOffsets,temperatureOffsets;
    private readonly Dictionary<long,TerrainHeightSample> heights=new();
    private readonly Dictionary<long,TreeHabitatSample> habitats=new();
    public int HeightSampleCount => heights.Count;
    public int HabitatSampleCount => habitats.Count;
    public TreeHabitatSampler(int seed,float sampleScale,int octaves,float persistence,float lacunarity,
        float heightMultiplier,float waterLevel,float mountainScale,WorldErosionSettings erosion)
    {
        this.seed=seed;scale=sampleScale;this.heightMultiplier=heightMultiplier;water=waterLevel;
        this.persistence=persistence;this.lacunarity=lacunarity;
        context=HeightMapGenerator.CreateSamplingContext(seed,waterLevel,mountainScale,erosion);
        int count=ClimateGenerator.GetClimateOctaveCount(octaves);
        climateMax=ClimateGenerator.GetMaxPossibleNoise(count,persistence);
        moistureOffsets=ClimateGenerator.CreateOctaveOffsets(seed+1000,count,Allocator.Persistent);
        temperatureOffsets=ClimateGenerator.CreateOctaveOffsets(seed+2000,count,Allocator.Persistent);
    }
    private static long Key(int x,int z) => ((long)x<<32)|(uint)z;
    private TerrainHeightSample Height(int x,int z)
    {
        long key=Key(x,z);
        if(!heights.TryGetValue(key,out var value))
        {
            value=HeightMapGenerator.SampleTerrainHeightNative(x,z,scale,context.RiverSeed,water,context.MountainHorizontalScale,context.Erosion);
            heights.Add(key,value);
        }
        return value;
    }
    public TreeHabitatSample Sample(float worldSampleX,float worldSampleZ)
    {
        int x=Mathf.RoundToInt(worldSampleX),z=Mathf.RoundToInt(worldSampleZ);
        long key=Key(x,z);
        if(habitats.TryGetValue(key,out var value))return value;
        var center=Height(x,z);
        float dx=(Height(x+4,z).Height-Height(x-4,z).Height)/8f;
        float dz=(Height(x,z+4).Height-Height(x,z-4).Height)/8f;
        float slope=TerrainSlopePolicy.FromGradient(Mathf.Sqrt(dx*dx+dz*dz),heightMultiplier);
        float moisture=ClimateGenerator.SampleClimate01(x,z,seed+1000,scale*10f,persistence,lacunarity,climateMax,moistureOffsets);
        float temperature=ClimateGenerator.SampleClimate01(x,z,seed+2000,scale*12f,persistence,lacunarity,climateMax,temperatureOffsets);
        var biome=BiomeClassifier.Classify(center.Height,moisture,temperature,slope,center.MountainMask,center.RiverMask,water);
        var surface=SurfaceTypeClassifier.Classify(center.Height,slope,center.RiverMask,biome,water);
        if(surface==SurfaceType.Grass)
            for(int ox=-1;ox<=1;ox++)for(int oz=-1;oz<=1;oz++)
                if(Height(x+ox,z+oz).Height<=water)surface=SurfaceType.Mud;
        value=new TreeHabitatSample{Biome=biome,Surface=surface,Height=center.Height,Moisture=moisture,Temperature=temperature,
            Slope=slope,River=center.RiverMask,Mountain=center.MountainMask};
        habitats.Add(key,value);
        return value;
    }
    public void Dispose(){if(moistureOffsets.IsCreated)moistureOffsets.Dispose();if(temperatureOffsets.IsCreated)temperatureOffsets.Dispose();}
}
