using Unity.Mathematics;

public enum BiomeBorder { None, ForestGrassland }

public readonly struct BiomeTransitionSample
{
    public readonly BiomeBorder Border;
    public readonly float PrimaryWeight;
    public float SecondaryWeight => 1f - PrimaryWeight;
    public float Edge => 4f * PrimaryWeight * SecondaryWeight;
    // The registered forest–grassland pair uses forest as its primary profile.
    public float Forest => PrimaryWeight;
    public float Meadow => SecondaryWeight;
    public BiomeTransitionSample(BiomeBorder border, float primaryWeight)
    { Border = border; PrimaryWeight = math.saturate(primaryWeight); }
}

// Stateless, Burst-compatible dispatch. Add other pair policies here without changing
// categorical biome ownership. Coordinates and existing habitat fields own transitions,
// never loaded neighbours or distance from a chunk edge.
public static class BiomeTransitionPolicy
{
    public const int GenerationVersion = 2;
    public const float ForestMoistureThreshold = .65f;
    public const float MoistureHalfWidth = .015f;

    public static BiomeTransitionSample Evaluate(BiomeType biome, SurfaceType surface,
        float moisture, float temperature, float height, float mountainMask,
        float slope, float river, float waterLevel)
    {
        float original = biome == BiomeType.Forest ? 1f : 0f;
        if (surface != SurfaceType.Grass ||
            (biome != BiomeType.Forest && biome != BiomeType.Grassland) ||
            height <= waterLevel || temperature < .30f || slope > TerrainSlopePolicy.GrassMaxDegrees ||
            mountainMask > .45f ||
            (mountainMask > .30f && height > math.lerp(.8f, .62f,
                math.saturate((mountainMask - .30f) / .15f))))
            return new BiomeTransitionSample(BiomeBorder.None, original);

        // Physical width follows the climate gradient, with existing habitat variation.
        // No boundary-distance transform, extra noise, or prescribed world-space band.
        float width = MoistureHalfWidth * math.lerp(.8f, 1.2f, math.saturate(river));
        return new BiomeTransitionSample(BiomeBorder.ForestGrassland,
            Membership(moisture, ForestMoistureThreshold, width));
    }

    // 0 = no registered border; 1..255 = meadow..forest. One byte per sample.
    public static byte Encode(BiomeTransitionSample sample) => sample.Border == BiomeBorder.ForestGrassland
        ? EncodeWeight(sample.PrimaryWeight) : (byte)0;
    public static float ForestWeight(byte membership, BiomeType biome) => membership == 0
        ? (biome == BiomeType.Forest ? 1f : 0f) : DecodeWeight(membership);
    public static bool IsMixed(byte membership) => membership > 1 && membership < 255;
    // Reusable math/packing for future pair-specific fields. Zero remains a sentinel.
    public static float Membership(float value, float threshold, float halfWidth) =>
        math.smoothstep(threshold - halfWidth, threshold + halfWidth, value);
    public static byte EncodeWeight(float weight) => (byte)(1 + (int)math.round(math.saturate(weight) * 254f));
    public static float DecodeWeight(byte membership) => membership == 0 ? 0f : (membership - 1) / 254f;
    public static bool SelectPrimary(float rank, float weight) => weight > 0f && (weight >= 1f || rank < weight);
    public static bool SelectForest(float rank, float membership) => SelectPrimary(rank, membership);
    public static float Population(float meadow, float forest, float membership) =>
        math.lerp(meadow, forest, math.saturate(membership));

    public static float ForestWeight(WorldFeaturePlan plan, BiomeType biome, int x, int z) =>
        ForestWeight(plan?.ForestMembershipMap == null ? (byte)0 : plan.ForestMembershipMap[x, z], biome);

    // Ecology density is blended, substrate strength is attenuated. Existing assets and
    // control textures carry the result; there is no second grass population.
    public static float4 BlendFloor(float4 forestEcology, float membership) => new float4(
        Population(1f, forestEcology.x, membership), forestEcology.yzw * membership);
    public static float ForestGrassDensity(float blendedDensity, float membership) => membership > 0f
        ? math.saturate((blendedDensity - (1f - membership)) / membership) : 0f;

    public static float4 FloorControls(float4 blendedEcology, float membership)
    {
        float rawDensity = ForestGrassDensity(blendedEcology.x, membership);
        return new float4(membership, membership * (1f - rawDensity * .18f), blendedEcology.y, blendedEcology.z);
    }
}
