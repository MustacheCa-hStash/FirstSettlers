using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

/// <summary>Static snow retention, in terrain coordinates. Shared by near and Burst far terrain.</summary>
public static class MountainSnow
{
    public const int SampleRadius = 4;
    public const float DefaultRenderCoverageGamma = 0.72f;

    // Generated height units (60 world units at the scene's current scales).
    public static float Snowline(float temperature, float moisture)
    {
        float t = math.saturate(temperature);
        float line = t < 0.3f ? math.lerp(0.7f, 2f, t / 0.3f)
            : t < 0.65f ? math.lerp(2f, 4f, (t - 0.3f) / 0.35f)
            : math.lerp(4f, 7f, (t - 0.65f) / 0.35f);
        return line + (0.5f - math.saturate(moisture)) * 1.2f;
    }

    // x = retained coverage, y = influence over the legacy material classification.
    public static float2 Evaluate(float2 position, int seed, float height, float mountainMask,
        float riverMask, float waterLevel, float temperature, float moisture,
        float2 gradient, float neighborMean, float heightMultiplier)
    {
        float influence = math.smoothstep(0.25f, 0.45f, mountainMask);
        // The mountain mask also overlaps lowland tundra. Preserve its existing snow/dusting below the foothills.
        influence *= math.smoothstep(waterLevel + 0.6f, waterLevel + 1.5f, height);
        if (influence <= 0f) return float2.zero;
        if (height <= waterLevel + TerrainWaterSettings.ShoreBand || riverMask >= TerrainWaterSettings.RiverBankThreshold)
            return new float2(0f, influence);

        float3 normal = math.normalize(new float3(-gradient.x * heightMultiplier, 1f, -gradient.y * heightMultiplier));
        // A fixed climatic north (+Z) and prevailing wind toward +X; independent of time-of-day lighting.
        float shade = normal.z * 0.35f;
        float lee = normal.x * 0.18f;
        float shelter = math.clamp((neighborMean - height) * heightMultiplier / SampleRadius, -1f, 1f);
        float2 offset = new float2(seed % 10007, seed % 7919);
        float broadNoise = noise.cnoise(position / 180f + offset);
        float edgeNoise = noise.cnoise(position / 24f + offset + 31f);
        float line = Snowline(temperature, moisture) - shade - lee - shelter * 0.55f + broadNoise * 0.4f;
        float altitude = math.smoothstep(line - 0.55f, line + 1.9f, height);
        // Edge breakup disappears in the upper snowfields.
        altitude = math.saturate(altitude + edgeNoise * 0.22f * (4f * altitude * (1f - altitude)));
        // Artistically generous retention on the vertically exaggerated mountains: taper from 55 to 85 degrees.
        float retention = math.sqrt(math.smoothstep(0.0872f, 0.5736f, normal.y));
        float exposed = 1f - math.saturate(-shelter) * 0.16f;
        return new float2(math.saturate(altitude * retention * exposed), influence);
    }

    public static float2[,] Generate(float[,] heights, float[,] masks, float[,] rivers,
        float[,] temperatures, float[,] moistures, int chunkSize, ChunkCoord coord, int seed,
        float sampleScale, TerrainWaterSettings water, float mountainScale, WorldErosionSettings erosion = default)
    {
        int width = heights.GetLength(0), depth = heights.GetLength(1);
        var result = new float2[width, depth];
        var context = HeightMapGenerator.CreateSamplingContext(seed, water.WaterLevel, mountainScale, erosion);
        using var terrainStorage = new NativeArray<float4>(width * depth, Allocator.TempJob);
        using var climateStorage = new NativeArray<float2>(width * depth, Allocator.TempJob);
        var terrain = terrainStorage;
        var climate = climateStorage;
        using var output = new NativeArray<float2>(width * depth, Allocator.TempJob);
        using var baseOffsets = Offsets(context.BaseLandOffsets);
        using var maskOffsets = Offsets(context.MountainMaskOffsets);
        using var mountainOffsets = Offsets(context.MountainTerrainOffsets);
        using var detailOffsets = Offsets(context.MountainRuggedOffsets);
        float2 origin = new float2(coord.x * chunkSize, coord.z * chunkSize);
        using var anchors = new NativeArray<MountainExpansionAnchor>(HeightMapGenerator.GetMountainAnchors(
            origin - SampleRadius, origin + chunkSize + SampleRadius, sampleScale, context), Allocator.TempJob);
        for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
            {
                int i = x * depth + z;
                terrain[i] = new float4(heights[x, z], masks[x, z], rivers[x, z], 0f);
                climate[i] = new float2(temperatures[x, z], moistures[x, z]);
            }
        new CoverageJob
        {
            width = width, depth = depth, origin = origin, seed = seed, sampleScale = sampleScale,
            waterLevel = water.WaterLevel, heightMultiplier = water.HeightMultiplier,
            mountainScale = context.MountainHorizontalScale, riverSeed = context.RiverSeed, erosion = context.Erosion,
            terrain = terrain, climate = climate, output = output, baseOffsets = baseOffsets,
            maskOffsets = maskOffsets, mountainOffsets = mountainOffsets, detailOffsets = detailOffsets, anchors = anchors
        }.Schedule(output.Length, 64).Complete();
        for (int x = 1; x < width - 1; x++)
            for (int z = 1; z < depth - 1; z++)
                result[x, z] = output[x * depth + z];
        return result;
    }

    private static NativeArray<float2> Offsets(Vector2[] source)
    {
        var result = new NativeArray<float2>(source.Length, Allocator.TempJob);
        for (int i = 0; i < source.Length; i++) result[i] = new float2(source[i].x, source[i].y);
        return result;
    }

    [BurstCompile]
    private struct CoverageJob : IJobParallelFor
    {
        public int width, depth, seed, riverSeed;
        public float2 origin;
        public float sampleScale, waterLevel, heightMultiplier, mountainScale;
        public WorldErosionSettings erosion;
        [ReadOnly] public NativeArray<float4> terrain;
        [ReadOnly] public NativeArray<float2> climate, baseOffsets, maskOffsets, mountainOffsets, detailOffsets;
        [ReadOnly] public NativeArray<MountainExpansionAnchor> anchors;
        [WriteOnly] public NativeArray<float2> output;

        public void Execute(int index)
        {
            int x = index / depth, z = index % depth;
            float4 center = terrain[index];
            if (x == 0 || z == 0 || x == width - 1 || z == depth - 1 || center.y <= 0.25f)
            { output[index] = float2.zero; return; }
            float left = Sample(x - SampleRadius, z), right = Sample(x + SampleRadius, z);
            float down = Sample(x, z - SampleRadius), up = Sample(x, z + SampleRadius);
            output[index] = Evaluate(origin + new float2(x - 1, z - 1), seed, center.x, center.y, center.z,
                waterLevel, climate[index].x, climate[index].y, new float2(right - left, up - down) / (2f * SampleRadius),
                (left + right + down + up) * 0.25f, heightMultiplier);
        }

        private float Sample(int x, int z)
        {
            if (x >= 0 && z >= 0 && x < width && z < depth) return terrain[x * depth + z].x;
            // Sample beyond the halo instead of clamping, so chunk borders have identical retention.
            return HeightMapGenerator.SampleTerrainHeightNative(origin.x + x - 1, origin.y + z - 1,
                sampleScale, baseOffsets, maskOffsets, mountainOffsets, detailOffsets, riverSeed,
                waterLevel, mountainScale, anchors, erosion).Height;
        }
    }

    public static void Apply(ref Color32 map0, ref Color32 map1, float2 snow, float renderCoverageGamma = DefaultRenderCoverageGamma)
    {
        if (snow.y <= 0f) return;
        float coverage = RenderCoverage(snow.x, renderCoverageGamma);
        float4 a = new float4(map0.r, map0.g, map0.b, map0.a) / 255f;
        float4 b = new float4(map1.r, map1.g, map1.b, map1.a) / 255f;
        float total = math.csum(a) + b.x + b.y + b.z;
        // Recover rock beneath old mountain snow, then coat the underlying materials continuously.
        float4 groundA = a;
        groundA.w += b.x;
        float4 groundB = b;
        groundB.x = 0f;
        groundA *= 1f - coverage;
        groundB *= 1f - coverage;
        groundB.x = coverage * total;
        a = math.lerp(a, groundA, snow.y);
        b = math.lerp(b, groundB, snow.y);
        map0 = new Color32(Byte(a.x), Byte(a.y), Byte(a.z), Byte(a.w));
        map1 = new Color32(Byte(b.x), Byte(b.y), Byte(b.z), Byte(b.w));
    }

    public static float RenderCoverage(float coverage, float gamma = DefaultRenderCoverageGamma)
        => math.pow(math.saturate(coverage), SanitizeRenderCoverageGamma(gamma));

    public static float SanitizeRenderCoverageGamma(float gamma)
        => math.isfinite(gamma) ? math.clamp(gamma, 0.35f, 1.25f) : DefaultRenderCoverageGamma;

    private static byte Byte(float value) => (byte)math.round(math.saturate(value) * 255f);
}
