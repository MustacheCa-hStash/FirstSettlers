using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

public static class MountainSnowValidation
{
    [MenuItem("Tools/Terrain/Validate Mountain Snow")]
    public static void Run()
    {
        ValidateRules();
        foreach (int seed in new[] { 7, 42, 12345 })
            ValidateTerrain(seed);
        ValidateRegionalCoverage();
        Debug.Log("Mountain snow validation passed: climate, altitude, retention, shelter, weights, water protection, X/Z seams, and near/far snow agreement for three seeds.");
    }

    public static void RunBatch()
    {
        try { Run(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static float Coverage(float height, float temperature = 0.45f, float moisture = 0.5f,
        float2 gradient = default, float shelter = 0f, float river = 0f)
        => MountainSnow.Evaluate(new float2(172f, -93f), 42, height, 0.8f, river, 0.24f,
            temperature, moisture, gradient, height + shelter, 200f).x;

    private static void ValidateRules()
    {
        for (int t = 1; t <= 100; t++)
            Require(MountainSnow.Snowline(t / 100f, 0.5f) >= MountainSnow.Snowline((t - 1) / 100f, 0.5f), "Warmer climates lower the snowline.");
        Require(Coverage(3f, 0.2f) > Coverage(3f, 0.8f), "Cold mountains should retain more snow.");
        Require(Coverage(3f, moisture: 0.9f) > Coverage(3f, moisture: 0.1f), "Moisture should increase accumulation.");
        Require(Coverage(12f, 0.9f) > 0.95f, "Hot climates cannot snow at high elevations.");
        Require(Coverage(9f, gradient: new float2(0.2f, 0f)) < 0.01f, "Vertical faces retain snow.");
        Require(Coverage(3f, shelter: 0.02f) > Coverage(3f, shelter: -0.02f), "Gullies should retain more snow than exposed ridges.");
        Require(Coverage(0.24f) == 0f && Coverage(6f, river: 1f) == 0f, "Snow covers protected water.");
        Require(MountainSnow.Evaluate(float2.zero, 42, 0.6f, 0.4f, 0f, 0.24f, 0.1f, 0.2f,
            float2.zero, 0.6f, 200f).y == 0f, "Mountain masks overlapping lowland tundra must preserve dusting.");
        float previous = 0f;
        for (int h = 0; h <= 160; h++)
        {
            float coverage = Coverage(h * 0.1f);
            Require(math.isfinite(coverage) && coverage >= previous - 0.00001f && coverage <= 1f, "Altitude response is invalid.");
            previous = coverage;
        }
        for (int i = 0; i <= 100; i++)
        {
            Color32 a = new Color32(20, 30, 40, 50), b = new Color32(60, 25, 30, 0);
            MountainSnow.Apply(ref a, ref b, new float2(i / 100f, 0.7f));
            int sum = a.r + a.g + a.b + a.a + b.r + b.g + b.b;
            Require(Math.Abs(sum - 255) <= 4, "Snow changes total material weight.");
        }
        Color32 lowA = new Color32(0, 0, 191, 0), lowB = new Color32(64, 0, 0, 0);
        MountainSnow.Apply(ref lowA, ref lowB, float2.zero);
        Require(lowA.b == 191 && lowB.r == 64, "Lowland tundra dusting changed.");
    }

    private static void ValidateTerrain(int seed)
    {
        const int size = 32;
        var water = new TerrainWaterSettings(14.4f, 200f, 0.3f);
        var context = HeightMapGenerator.CreateSamplingContext(seed, water.WaterLevel, 1.3f);
        ChunkCoord coord = default;
        bool found = false;
        for (int x = -24; x <= 24 && !found; x++)
            for (int z = -24; z <= 24 && !found; z++)
            {
                var sample = HeightMapGenerator.SampleTerrainHeight(x * 512f, z * 512f, 600f, context);
                if (sample.Height > 5f && sample.MountainMask > 0.5f)
                { coord = new ChunkCoord(x * 16, z * 16); found = true; }
            }
        Require(found, "Could not find a mountain for validation.");
        float2[,] Generate(ChunkCoord c)
        {
            var field = HeightMapGenerator.GenerateTerrainHeightField(size, seed, 600f, c, water.WaterLevel, 1.3f);
            var temp = ClimateGenerator.GenerateTerrainTemperatureMap(size, seed, 600f, 2, 0.05f, 10f, c);
            var moisture = ClimateGenerator.GenerateTerrainMoistureMap(size, seed, 600f, 2, 0.05f, 10f, c);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var result = MountainSnow.Generate(field.HeightMap, field.MountainMaskMap, field.RiverMaskMap,
                temp, moisture, size, c, seed, 600f, water, 1.3f);
            timer.Stop();
            if (c == coord)
            {
                float oldSum = 0f, newSum = 0f;
                int count = 0;
                for (int x = 1; x <= size + 1; x++)
                    for (int z = 1; z <= size + 1; z++)
                    {
                        if (result[x, z].y < 0.999f) continue;
                        var biome = BiomeClassifier.Classify(field.HeightMap[x, z], moisture[x, z], temp[x, z],
                            field.SlopeMap[x, z], field.MountainMaskMap[x, z], field.RiverMaskMap[x, z], water.WaterLevel);
                        var surface = SurfaceTypeClassifier.Classify(field.HeightMap[x, z], field.SlopeMap[x, z],
                            field.RiverMaskMap[x, z], biome, water.WaterLevel);
                        oldSum += surface == SurfaceType.Snow ? 1f : 0f;
                        newSum += result[x, z].x;
                        count++;
                    }
                Debug.Log($"Snow seed {seed}: old/new sample coverage {oldSum / math.max(1, count):P0}/{newSum / math.max(1, count):P0}; near snow generation {timer.Elapsed.TotalMilliseconds:F2} ms ({size}-unit chunk, editor).");
            }
            return result;
        }
        var near = Generate(coord);
        var nextX = Generate(new ChunkCoord(coord.x + 1, coord.z));
        var nextZ = Generate(new ChunkCoord(coord.x, coord.z + 1));
        for (int i = 1; i <= size + 1; i++)
        {
            Require(math.distance(near[size + 1, i], nextX[1, i]) < 0.003f, "X snow seam.");
            Require(math.distance(near[i, size + 1], nextZ[i, 1]) < 0.003f, "Z snow seam.");
        }
        var far = FarTerrainGenerator.Generate(coord, 1, size, seed, 600f, 200f, 0.3f, 5, 9, 0f,
            water.WaterLevel, mountainHorizontalScale: 1.3f, climateOctaves: 2, climatePersistence: 0.05f, climateLacunarity: 10f);
        int compared = 0;
        float sum = 0f;
        for (int z = 0; z < 9; z++)
            for (int x = 0; x < 9; x++)
            {
                float2 snow = near[x * 4 + 1, z * 4 + 1];
                if (snow.y < 0.999f) continue;
                float rendered = far.ControlMapsRawData.Maps[1][z * 9 + x].r / 255f;
                Require(math.abs(rendered - snow.x) < 0.008f, "Near/far snow mismatch.");
                sum += rendered;
                compared++;
            }
        Require(compared > 0, "Near/far validation did not exercise mountain snow.");
        Debug.Log($"Snow seed {seed}, chunk {coord.x},{coord.z}: {compared} near/far comparisons, average coverage {sum / compared:P0}.");
    }

    private static void ValidateRegionalCoverage()
    {
        const int seed = 42;
        var context = HeightMapGenerator.CreateSamplingContext(seed, 0.24f, 1.3f);
        using var tempOffsets = ClimateGenerator.CreateOctaveOffsets(seed + 2000, 1, Allocator.TempJob);
        using var moistureOffsets = ClimateGenerator.CreateOctaveOffsets(seed + 1000, 1, Allocator.TempJob);
        float previous = 0f, current = 0f;
        int count = 0;
        float Height(float x, float z) => HeightMapGenerator.SampleTerrainHeight(x, z, 600f, context).Height;
        for (int x = -24; x <= 24; x++)
            for (int z = -24; z <= 24; z++)
            {
                float wx = x * 512f, wz = z * 512f;
                var center = HeightMapGenerator.SampleTerrainHeight(wx, wz, 600f, context);
                if (center.MountainMask < 0.45f || center.Height < 2f) continue;
                float t = ClimateGenerator.SampleClimate01(wx, wz, seed + 2000, 7200f, 0.05f, 10f, 1f, tempOffsets);
                float m = ClimateGenerator.SampleClimate01(wx, wz, seed + 1000, 6000f, 0.05f, 10f, 1f, moistureOffsets);
                float left = Height(wx - 4f, wz), right = Height(wx + 4f, wz);
                float down = Height(wx, wz - 4f), up = Height(wx, wz + 4f);
                float2 gradient = new float2(right - left, up - down) / 8f;
                float slope = math.length(gradient);
                var biome = BiomeClassifier.Classify(center.Height, m, t, slope, center.MountainMask, center.RiverMask, 0.24f);
                var surface = SurfaceTypeClassifier.Classify(center.Height, slope, center.RiverMask, biome, 0.24f);
                previous += surface == SurfaceType.Snow ? 1f : 0f;
                current += MountainSnow.Evaluate(new float2(wx, wz), seed, center.Height, center.MountainMask,
                    center.RiverMask, 0.24f, t, m, gradient, (left + right + down + up) / 4f, 200f).x;
                count++;
            }
        Require(count > 100 && current > previous, "Regional mountain coverage did not increase.");
        Debug.Log($"Regional mountain coverage, seed {seed}: {count} samples; old {previous / count:P1}, new {current / count:P1}.");
    }
}
