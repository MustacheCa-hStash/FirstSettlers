using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

// Worker-only sampler. The returned manifest contains no Unity objects or full terrain maps.
public static class DistantTreePlacement
{
    private sealed class Scratch
    {
        public readonly int Size;
        public readonly BiomeType[,] Biomes;
        public readonly SurfaceType[,] Surfaces;
        public readonly float[,] Moisture, Temperature, Slopes, Rivers;
        public readonly bool[,] Sampled, Prepared;
        public readonly Dictionary<int, TerrainHeightSample> Heights = new Dictionary<int, TerrainHeightSample>();
        public readonly WorldFeaturePlan Plan;
        public Scratch(int size)
        {
            Size = size;
            Biomes = new BiomeType[size, size]; Surfaces = new SurfaceType[size, size];
            Moisture = new float[size, size]; Temperature = new float[size, size];
            Slopes = new float[size, size]; Rivers = new float[size, size];
            Sampled = new bool[size, size]; Prepared = new bool[size, size];
            Plan = new WorldFeaturePlan(size, size);
        }
    }
    // Reuse transient ecological grids rather than allocating megabytes on every chunk.
    // At most four idle workspaces are retained; a workspace is owned by only one worker.
    private static readonly ConcurrentBag<Scratch> scratchPool = new ConcurrentBag<Scratch>();
    public static TreeInstanceData[] Generate(ChunkCoord coord, int chunkSize, int seed,
        float sampleScale, int octaves, float persistence, float lacunarity,
        float worldScale, float heightMultiplier, float waterLevel, float mountainScale,
        int tintSeedOffset, WorldFeatureGenerationSettings settings, WorldErosionSettings erosion = default)
    {
        int size = chunkSize + 3;
        if (!scratchPool.TryTake(out var scratch) || scratch.Size != size) scratch = new Scratch(size);
        var biomes = scratch.Biomes; var surfaces = scratch.Surfaces;
        var moisture = scratch.Moisture; var temperature = scratch.Temperature;
        var slopes = scratch.Slopes; var rivers = scratch.Rivers;
        var sampled = scratch.Sampled; var heights = scratch.Heights;
        Array.Clear(sampled, 0, sampled.Length); heights.Clear();
        try
        {
            var context = HeightMapGenerator.CreateSamplingContext(seed, waterLevel, mountainScale, erosion);
            float2 minimum = new float2(coord.x * chunkSize - 1, coord.z * chunkSize - 1);
            using var anchors = new NativeArray<MountainExpansionAnchor>(
                HeightMapGenerator.GetMountainAnchors(minimum, minimum + chunkSize + 2, sampleScale, context), Allocator.Persistent);
            using var land = Offsets(context.BaseLandOffsets);
            using var mask = Offsets(context.MountainMaskOffsets);
            using var terrain = Offsets(context.MountainTerrainOffsets);
            using var rugged = Offsets(context.MountainRuggedOffsets);
            int climateOctaves = ClimateGenerator.GetClimateOctaveCount(octaves);
            float climateMax = ClimateGenerator.GetMaxPossibleNoise(climateOctaves, persistence);
            using var moistureOffsets = ClimateGenerator.CreateOctaveOffsets(seed + 1000, climateOctaves, Allocator.Persistent);
            using var temperatureOffsets = ClimateGenerator.CreateOctaveOffsets(seed + 2000, climateOctaves, Allocator.Persistent);

            TerrainHeightSample Height(int x, int z)
            {
                int key = x * size + z;
                if (!heights.TryGetValue(key, out var value))
                {
                    value = HeightMapGenerator.SampleTerrainHeightNative(coord.x * chunkSize + x - 1,
                        coord.z * chunkSize + z - 1, sampleScale, land, mask, terrain, rugged,
                        context.RiverSeed, waterLevel, mountainScale, anchors, context.Erosion);
                    heights.Add(key, value);
                }
                return value;
            }
            void Sample(int x, int z)
            {
                if (sampled[x, z]) return;
                var center = Height(x, z);
                int x0 = Mathf.Max(x - 4, 0), x1 = Mathf.Min(x + 4, size - 1);
                int z0 = Mathf.Max(z - 4, 0), z1 = Mathf.Min(z + 4, size - 1);
                float dx = (Height(x1, z).Height - Height(x0, z).Height) / Mathf.Max(1, x1 - x0);
                float dz = (Height(x, z1).Height - Height(x, z0).Height) / Mathf.Max(1, z1 - z0);
                slopes[x, z] = TerrainSlopePolicy.FromGradient(math.sqrt(dx * dx + dz * dz), heightMultiplier);
                float wx = coord.x * chunkSize + x - 1, wz = coord.z * chunkSize + z - 1;
                moisture[x, z] = ClimateGenerator.SampleClimate01(wx, wz, seed + 1000, sampleScale * 10f,
                    persistence, lacunarity, climateMax, moistureOffsets);
                temperature[x, z] = ClimateGenerator.SampleClimate01(wx, wz, seed + 2000, sampleScale * 12f,
                    persistence, lacunarity, climateMax, temperatureOffsets);
                rivers[x, z] = center.RiverMask;
                biomes[x, z] = BiomeClassifier.Classify(center.Height, moisture[x, z], temperature[x, z],
                    slopes[x, z], center.MountainMask, center.RiverMask, waterLevel);
                surfaces[x, z] = SurfaceTypeClassifier.Classify(center.Height, slopes[x, z], center.RiverMask, biomes[x, z], waterLevel);
                sampled[x, z] = true;
            }
            var plan = WorldFeaturePlanGenerator.GenerateTreePlacements(coord, chunkSize, seed,
                biomes, surfaces, moisture, temperature, slopes, rivers, settings, Sample, scratch.Plan, scratch.Prepared);
            var trees = new List<TreeInstanceData>();
            foreach (var p in plan.Placements)
            {
                if (p.featureType != WorldFeatureType.Tree) continue;
                // Identical bilinear height and tint sampling to the near representation.
                int x = Mathf.FloorToInt(p.sampleX), z = Mathf.FloorToInt(p.sampleZ);
                int x1 = Mathf.Min(x + 1, chunkSize), z1 = Mathf.Min(z + 1, chunkSize);
                float h = Mathf.Lerp(Mathf.Lerp(Height(x + 1, z + 1).Height, Height(x1 + 1, z + 1).Height, p.sampleX - x),
                    Mathf.Lerp(Height(x + 1, z1 + 1).Height, Height(x1 + 1, z1 + 1).Height, p.sampleX - x), p.sampleZ - z);
                FoliageGenerator.GetDeterministicTreeColors(p.variant, seed, tintSeedOffset, coord,
                    p.sampleX, p.sampleZ, out Color32 leaf, out Color32 bark);
                trees.Add(new TreeInstanceData(new Vector3((p.sampleX - chunkSize * 0.5f) * worldScale,
                    h * heightMultiplier * worldScale, (p.sampleZ - chunkSize * 0.5f) * worldScale),
                    p.rotation, p.scale, p.variant, leaf, bark));
            }
            return trees.ToArray();
        }
        finally
        {
            lock (scratchPool)
                if (scratchPool.Count < 4) scratchPool.Add(scratch);
        }
    }

    private static NativeArray<float2> Offsets(Vector2[] values)
    {
        var result = new NativeArray<float2>(values.Length, Allocator.Persistent);
        for (int i = 0; i < values.Length; i++) result[i] = new float2(values[i].x, values[i].y);
        return result;
    }
}
