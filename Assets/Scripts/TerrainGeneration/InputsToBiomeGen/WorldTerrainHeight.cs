using LPMGames.Terrain.Erosion;
using Unity.Mathematics;

public readonly struct WorldBaseSample
{
    public readonly float Height, MountainMask, MountainWeight, MountainContribution;
    public readonly float2 Gradient;
    public WorldBaseSample(float h, float2 gradient, float mask, float weight, float contribution)
    { Height = h; Gradient = gradient; MountainMask = mask; MountainWeight = weight; MountainContribution = contribution; }
}

/// <summary>One smooth base surface and one world-space erosion pass. No overlapping eroded mountain copies.</summary>
public static class WorldTerrainHeight
{
    private static float2 LatticeGradient(int2 cell, int seed)
    {
        uint a = math.hash(new uint3((uint2)cell, (uint)seed));
        uint b = math.hash(new uint3((uint2)cell, (uint)seed ^ 0x9e3779b9u));
        return math.normalizesafe(new float2(a & 65535u, b & 65535u) / 32767.5f - 1f, new float2(1f, 0f));
    }

    // C2-continuous gradient noise with exact derivatives and integer-seeded lattice gradients.
    // No seed-dependent float translations or finite-difference input slopes.
    private static float3 Noise(float2 p, int seed)
    {
        int2 cell = (int2)math.floor(p);
        float2 f = math.frac(p);
        float2 u = f * f * f * (f * (f * 6f - 15f) + 10f);
        float2 du = 30f * f * f * (f - 1f) * (f - 1f);
        float2 g00 = LatticeGradient(cell, seed), g10 = LatticeGradient(cell + new int2(1, 0), seed);
        float2 g01 = LatticeGradient(cell + new int2(0, 1), seed), g11 = LatticeGradient(cell + 1, seed);
        float n00 = math.dot(g00, f), n10 = math.dot(g10, f - new float2(1, 0));
        float n01 = math.dot(g01, f - new float2(0, 1)), n11 = math.dot(g11, f - 1f);
        float a = math.lerp(n00, n10, u.x), b = math.lerp(n01, n11, u.x);
        float2 gradient = math.lerp(math.lerp(g00, g10, u.x), math.lerp(g01, g11, u.x), u.y) +
            new float2(du.x * math.lerp(n10 - n00, n11 - n01, u.y), du.y * (b - a));
        return new float3(math.lerp(a, b, u.y), gradient) * 1.41421356f;
    }

    private static float3 Fbm(float2 p, float scale, int seed, int octaves, float gain)
    {
        float3 sum = 0f;
        float frequency = 1f / scale, amplitude = 1f, total = 0f;
        for (int i = 0; i < octaves; i++)
        {
            math.sincos(0.6435f + i * 2.39996f, out float sin, out float cos);
            float2 q = new float2(cos * p.x - sin * p.y, sin * p.x + cos * p.y) * frequency;
            float3 sample = Noise(q, seed + i * 1009);
            float2 gradient = new float2(cos * sample.y + sin * sample.z, -sin * sample.y + cos * sample.z) * frequency;
            sum += new float3(sample.x, gradient) * amplitude;
            total += amplitude; amplitude *= gain; frequency *= 2f;
        }
        return sum / total;
    }
    public static WorldBaseSample Base(float2 p, float sampleScale, int seed, float coverage, in WorldErosionSettings s)
    {
        float scale = math.max(sampleScale, 0.0001f);
        float3 land = Fbm(p, scale * 1.6f, seed + 20000, s.baseOctaves, s.baseRoughness);
        float3 maskNoise = Fbm(p, scale * 6f, seed + 30000, 2, 0.3f);
        float lower = 0.05f - (math.clamp(coverage, 1f, 3f) - 1f) * 0.15f;
        float t = math.saturate((maskNoise.x - lower) / (0.65f - lower));
        float mask = t * t * (3f - 2f * t);
        float2 maskGradient = maskNoise.yz * (6f * t * (1f - t) / (0.65f - lower));
        float3 profile = Fbm(p, scale * 3f, seed + 40000, s.baseOctaves, s.baseRoughness);
        float v = math.saturate(0.5f + 0.5f * profile.x);
        float shaped = math.pow(v, s.mountainShape);
        float2 shapedGradient = v > 0f && v < 1f ? profile.yz * (0.5f * s.mountainShape * math.pow(v, s.mountainShape - 1f)) : float2.zero;
        float contribution = mask * shaped;
        float h = s.baseElevation + land.x * s.lowlandRelief + contribution * s.mountainRelief;
        float2 gradient = land.yz * s.lowlandRelief + (maskGradient * shaped + mask * shapedGradient) * s.mountainRelief;
        return new WorldBaseSample(h, gradient, mask, mask, contribution);
    }

    public static float Erode(float2 position, WorldBaseSample input, float sampleScale, int seed, float coverage, in WorldErosionSettings s)
    {
        if (!s.enabled || s.amplitude <= 0f) return input.Height;
        int count = 0; float weight = 1f, total = 0f, wave = s.wavelength * math.min(s.stretch.x, s.stretch.y);
        for (int i = 0; i < s.octaves && wave >= s.minimumWavelength; i++)
        { count++; total += weight; weight *= s.persistence; wave /= s.lacunarity; }
        if (count == 0) return input.Height;
        float target;
        if (s.fadeTarget == ErosionFadeTarget.Altitude)
            target = math.clamp((input.Height - s.valleyAndPeakHeights.x) / (s.valleyAndPeakHeights.y - s.valleyAndPeakHeights.x) * 2f - 1f, -1f, 1f);
        else
        {
            float2 x = new float2(s.reliefRadius, 0f), z = new float2(0f, s.reliefRadius);
            float mean = (Base(position + x, sampleScale, seed, coverage, s).Height + Base(position - x, sampleScale, seed, coverage, s).Height +
                Base(position + z, sampleScale, seed, coverage, s).Height + Base(position - z, sampleScale, seed, coverage, s).Height) * 0.25f;
            float contrast = (input.Height - mean) / s.reliefContrast;
            target = contrast / math.sqrt(1f + contrast * contrast);
        }
        math.sincos(math.radians(s.rotation), out float sin, out float cos);
        float2 p = position - new float2(s.offset.x, s.offset.y);
        float2 domain = new float2(cos * p.x + sin * p.y, -sin * p.x + cos * p.y) / new float2(s.stretch.x, s.stretch.y) / s.wavelength;
        // Chain rule: height derivatives in domain coordinates, including stretch and rotation.
        float2 gradient = new float2(cos * input.Gradient.x + sin * input.Gradient.y, -sin * input.Gradient.x + cos * input.Gradient.y) *
            new float2(s.stretch.x, s.stretch.y) * (s.wavelength / s.heightScale);
        float4 erosion = AdvancedTerrainErosion.ErosionFilter(domain, new float3(input.Height / s.heightScale, gradient), target,
            s.amplitude / (s.heightScale * total), s.gullyWeight, s.branching,
            new float4(s.ridgeRounding, s.valleyRounding, 1f, s.lacunarity), new float4(s.slopeResponse, 1.25f, s.slopeResponse, 1.5f),
            new float2(0.7f, 0f), 1f, count, s.lacunarity, s.persistence, s.cellSize, s.normalization,
            seed + s.seedOffset, out _, s.directionSmoothing);
        return input.Height + erosion.x * s.heightScale;
    }
}
