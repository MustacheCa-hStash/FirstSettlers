using Unity.Mathematics;

// Continuous visual ecology, independent of the dominant ground-cover gameplay label.
// Packed sample: grass keep probability, soil blend, moss blend, mixed-litter blend.
public static class ForestFloorPolicy
{
    public static float4 Evaluate(float2 worldXZ, int seed, float moisture, float slope, float riverMask)
    {
        float clearing = math.saturate((Sample01(worldXZ * 0.012f, seed + 6102) - 0.62f) * 2.65f);
        float canopy = Sample01(worldXZ * 0.0075f, seed + 6100) * 0.52f +
            Sample01(worldXZ * 0.024f, seed + 6101) * 0.36f +
            Sample01(worldXZ * 0.067f, seed + 6104) * 0.12f;
        canopy *= math.saturate((TerrainSlopePolicy.ForestMaxDegrees - slope) /
            (TerrainSlopePolicy.ForestMaxDegrees - TerrainSlopePolicy.ForestFadeStartDegrees));
        canopy *= (1f - math.smoothstep(0.52f, 0.76f, riverMask)) * (1f - clearing * 0.72f);
        return Evaluate(worldXZ, seed, moisture, slope, riverMask, canopy, clearing);
    }

    public static float4 Evaluate(float2 worldXZ, int seed, float moisture, float slope,
        float riverMask, float canopyIntent, float clearing)
    {
        // Warp the colony field rather than thresholding circular tree footprints.
        float2 warp = new float2(Sample01(worldXZ * 0.018f, seed + 8450),
            Sample01(worldXZ * 0.018f, seed + 8451)) - 0.5f;
        float2 colonyXZ = worldXZ + warp * 24f;
        float colony = Sample01(colonyXZ * 0.045f, seed + 8452) * 0.72f +
            Sample01(colonyXZ * 0.13f, seed + 8453) * 0.28f;
        float colonyDensity = math.smoothstep(0.28f, 0.76f, colony);
        float opening = math.max(math.smoothstep(0.12f, 0.74f, clearing),
            1f - math.smoothstep(0.18f, 0.72f, canopyIntent));
        float moistureSuitability = math.smoothstep(0.22f, 0.52f, moisture) *
            (1f - math.smoothstep(0.82f, 0.98f, moisture) * 0.55f);
        float slopeSuitability = 1f - math.smoothstep(18f, TerrainSlopePolicy.GrassMaxDegrees, slope);
        float riverSuitability = 1f - math.smoothstep(0.48f, 0.72f, riverMask);
        float density = math.lerp(0.025f, 0.26f, colonyDensity) * math.lerp(0.55f, 1f, opening);
        density = math.lerp(density, 0.45f + colonyDensity * 0.35f, math.smoothstep(0.35f, 0.9f, clearing));
        density *= moistureSuitability * slopeSuitability * riverSuitability;

        float damp = math.saturate(moisture * 0.65f + canopyIntent * 0.35f);
        float soil = math.smoothstep(24f, TerrainSlopePolicy.GroundCoverExposedDegrees + 10f, slope) * 0.32f +
            (1f - math.smoothstep(0.2f, 0.4f, moisture)) * 0.16f +
            math.smoothstep(0.48f, 0.76f, riverMask) * 0.6f;
        // Independent, warped moss colonies have readable carpet cores instead of a green wash.
        // Keep their habitat field global so detailed/far maps and neighboring chunks agree.
        float mossNoise = Sample01(colonyXZ * 0.026f, seed + 8460) * 0.7f +
            Sample01(colonyXZ * 0.085f, seed + 8461) * 0.3f;
        float mossHabitat = math.smoothstep(0.4f, 0.76f, moisture * 0.75f + canopyIntent * 0.25f) *
            math.lerp(0.55f, 1f, canopyIntent) * (1f - math.saturate(clearing) * 0.8f) *
            (1f - math.smoothstep(0.62f, 0.82f, riverMask)) *
            (1f - math.smoothstep(38f, 60f, slope) * 0.65f);
        float moss = math.smoothstep(0.43f, 0.62f, mossNoise) * mossHabitat;
        density *= math.lerp(1f, 0.02f, MossDominance(moss));
        float mixedLitter = math.smoothstep(0.35f, 0.8f, damp) *
            math.lerp(0.2f, 0.7f, Sample01(worldXZ * 0.014f, seed + 8454));
        return math.saturate(new float4(density, soil, moss, mixedLitter));
    }

    // The terrain shader blends these channels in sequence; they are not exclusive proportions.
    public static float4 ControlWeights(float4 ecology) =>
        new float4(1f, 1f - ecology.x * 0.18f, ecology.y, ecology.z);

    // Mirrored in the terrain shader: moss has priority over the underlying substrate.
    public static float MossDominance(float coverage) => math.smoothstep(0.03f, 0.58f, coverage);
    public static float LeafRetention(float coverage) => math.lerp(1f, 0.25f, MossDominance(coverage));
    public static float CloverRetention(float coverage) => 1f - MossDominance(coverage);

    public static float SampleMoss(float4[,] map, float2 sample)
    {
        if (map == null) return 0;
        float2 p = math.clamp(sample + 1f, float2.zero, new float2(map.GetLength(0)-1,map.GetLength(1)-1));
        int2 a = (int2)math.floor(p), b = math.min(a+1,new int2(map.GetLength(0)-1,map.GetLength(1)-1));
        return math.lerp(math.lerp(map[a.x,a.y].z,map[b.x,a.y].z,p.x-a.x),
            math.lerp(map[a.x,b.y].z,map[b.x,b.y].z,p.x-a.x),p.y-a.y);
    }

    private static float Sample01(float2 position, int seed) =>
        math.saturate((AnalyticValueNoise2D.Sample(position.x, position.y, seed).Value + 1f) * 0.5f);
}
