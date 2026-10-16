using System;
using System.Collections.Generic;
using Unity.Mathematics;

/// <summary>Must remain deterministic for the lifetime of a density world and its snapshots.</summary>
public interface ITerrainDensityGenerator
{
    double SampleDensity(double3 worldPosition);
}
/// <summary>Optional height-only baseline specialization. Cave generators must not implement
/// this unless their density is exactly worldY - SampleHeight(X,Z).</summary>
public interface ITerrainHeightDensityGenerator : ITerrainDensityGenerator
{
    double SampleHeight(double worldX,double worldZ);
}

/// <summary>Optional conservative bounds over the entire box, not merely its eight corners.</summary>
public interface ITerrainDensityBounds
{
    bool TryGetDensityBounds(double3 minimum, double3 maximum, out double low, out double high);
}

/// <summary>
/// Canonical full-resolution heightfield adapter. Cold reads generate native/Burst height maps,
/// not the slightly different managed point evaluator. Use cold sampling on a producer thread.
/// </summary>
public sealed class TerrainBaselineSampler : ITerrainHeightDensityGenerator, ITerrainDensityBounds
{
    private readonly WorldGenerationConfiguration generation;
    private readonly Func<ChunkCoord, float[,]> loadHeightMap;
    private readonly int capacity;
    private readonly bool generateOnProviderMiss;
    private readonly Dictionary<ChunkCoord, float[,]> heights = new Dictionary<ChunkCoord, float[,]>();
    private readonly Queue<ChunkCoord> insertionOrder = new Queue<ChunkCoord>();
    private readonly object gate = new object();
    public int CachedChunkCount { get { lock (gate) return heights.Count; } }
    public int GeneratedChunkCount { get; private set; }

    public TerrainBaselineSampler(WorldGenerationConfiguration configuration, int cacheCapacity = 4,
        Func<ChunkCoord, float[,]> canonicalHeightMapProvider = null, bool generateWhenProviderMisses = false)
    {
        if (configuration.ChunkSize <= 0 || !math.isfinite(configuration.WorldScale) || configuration.WorldScale <= 0 ||
            !math.isfinite(configuration.MeshHeightMultiplier) || configuration.MeshHeightMultiplier <= 0 ||
            !math.isfinite(configuration.SampleScale) || configuration.SampleScale <= 0)
            throw new ArgumentException("Valid generation scales and dimensions required.", nameof(configuration));
        if (cacheCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(cacheCapacity));
        generation = configuration; capacity = cacheCapacity;
        // Providers must return stable canonical data, not a mutable render LOD or pooled map.
        loadHeightMap = canonicalHeightMapProvider ?? GenerateHeightMap;
        generateOnProviderMiss=canonicalHeightMapProvider!=null&&generateWhenProviderMisses;
    }

    public double SampleDensity(double3 worldPosition) => worldPosition.y - SampleHeight(worldPosition.x, worldPosition.z);

    public bool TryGetDensityBounds(double3 minimum, double3 maximum, out double low, out double high)
    {
        int x0=TerrainDensityCoordinates.FloorIndex(minimum.x/generation.WorldScale);
        int z0=TerrainDensityCoordinates.FloorIndex(minimum.z/generation.WorldScale);
        int x1=checked(TerrainDensityCoordinates.FloorIndex(maximum.x/generation.WorldScale)+1);
        int z1=checked(TerrainDensityCoordinates.FloorIndex(maximum.z/generation.WorldScale)+1);
        double minHeight=double.PositiveInfinity,maxHeight=double.NegativeInfinity;
        lock(gate)for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
        {double h=Height(x,z);minHeight=Math.Min(minHeight,h);maxHeight=Math.Max(maxHeight,h);}
        // Triangle interpolation is a convex combination of these canonical lattice heights.
        low=minimum.y-maxHeight;high=maximum.y-minHeight;return true;
    }

    public double SampleHeight(double worldX, double worldZ)
    {
        double gx = worldX / generation.WorldScale, gz = worldZ / generation.WorldScale;
        int x = TerrainDensityCoordinates.FloorIndex(gx), z = TerrainDensityCoordinates.FloorIndex(gz);
        if (x == int.MaxValue || z == int.MaxValue) throw new ArgumentOutOfRangeException(nameof(worldX));
        double fx = Math.Max(0, Math.Min(1, gx - x)), fz = Math.Max(0, Math.Min(1, gz - z));
        lock (gate)
        {
            if(fx==0&&fz==0)return Height(x,z);
            if(fx==0){double a0=Height(x,z);return a0+fz*(Height(x,z+1)-a0);}
            if(fz==0){double a0=Height(x,z);return a0+fx*(Height(x+1,z)-a0);}
            double a = Height(x, z), c = Height(x + 1, z + 1);
            // Same a-c diagonal as MeshGenerator's full-resolution topology.
            if (fx >= fz) { double d = Height(x + 1, z); return a + fx * (d - a) + fz * (c - d); }
            double b = Height(x, z + 1); return a + fx * (c - b) + fz * (b - a);
        }
    }

    private double Height(int x, int z)
    {
        int size = generation.ChunkSize;
        var coord = new ChunkCoord(TerrainDensityCoordinates.FloorDiv(x, size), TerrainDensityCoordinates.FloorDiv(z, size));
        if (!heights.TryGetValue(coord, out var map))
        {
            map = loadHeightMap(coord);
            if(map==null&&generateOnProviderMiss)map=GenerateHeightMap(coord);
            if (map == null || map.GetLength(0) != size + 3 || map.GetLength(1) != size + 3)
                throw new InvalidOperationException("Canonical height map must retain the existing one-sample halo.");
            if (heights.Count >= capacity) heights.Remove(insertionOrder.Dequeue());
            heights.Add(coord, map); insertionOrder.Enqueue(coord);
        }
        int lx = (int)((long)x - (long)coord.x * size), lz = (int)((long)z - (long)coord.z * size);
        // Keep the production float multiplication order before promoting to double.
        float value = map[lx + 1, lz + 1] * generation.MeshHeightMultiplier * generation.WorldScale;
        if (!math.isfinite(value)) throw new InvalidOperationException("Invalid canonical terrain height.");
        return value;
    }

    private float[,] GenerateHeightMap(ChunkCoord coord)
    {
        GeneratedChunkCount++;
        return HeightMapGenerator.GenerateTerrainHeightField(generation.ChunkSize, generation.Seed,
            generation.SampleScale, coord, generation.Water.WaterLevel, generation.MountainHorizontalScale,
            generation.MeshHeightMultiplier, generation.Erosion).HeightMap;
    }
}
