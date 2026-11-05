using Unity.Mathematics;

public enum WorldRockClass { Sandstone, Limestone, Granite, DesertBedrock }

/// <summary>Value and world-XZ derivatives. Fields are continuous across chunks and biome boundaries.</summary>
public readonly struct WorldLandformRegion
{
    public readonly float3 Aridity, Sediment, Plateau, Sand, Basin;
    public WorldLandformRegion(float3 aridity, float3 sediment, float3 plateau, float3 sand, float3 basin)
    { Aridity = aridity; Sediment = sediment; Plateau = plateau; Sand = sand; Basin = basin; }
}

/// <summary>
/// Stateless integer-seeded fields, evaluated identically by near/far terrain and cold density reads.
/// All scales use terrain coordinates, never chunk-local coordinates or LOD. No mutable random state.
/// float3 throughout this class means (value, d/dX, d/dZ).
/// </summary>
public static class WorldLandforms
{
    public const int GeneratorSchema = 4;
    // Relative to the water datum, in canonical height units (60 world metres at scene scale).
    public static float Upland(float height, float waterLevel) => math.smoothstep(waterLevel + 1.4f, waterLevel + 3f, height);
    public static float DryProvince(float aridity) => math.smoothstep(0.18f, 0.60f, aridity);
    public static float WaterExclusion(float2 p, int seed, float scale, in WorldLandformSettings s) =>
        s.enabled && s.dryDeserts ? DryProvince(Desert(p, seed, scale, s.desertRegionScale).x) : 0f;

    public static float DryFloor(float2 p, int seed, float scale, float waterLevel) =>
        waterLevel + 0.06f + Noise(p, math.max(scale, 1f) * 1.7f, seed + 86003).x * 0.012f;
    public static float3 Constant(float v) => new float3(v, 0f, 0f);
    public static float3 Multiply(float3 a, float3 b) => new float3(a.x * b.x, a.yz * b.x + b.yz * a.x);
    public static float3 Smooth(float low, float high, float3 a)
    {
        float t = math.saturate((a.x - low) / (high - low));
        return new float3(t * t * (3f - 2f * t), a.yz * (6f * t * (1f - t) / (high - low)));
    }
    private static float3 Abs(float3 a) => new float3(math.abs(a.x), a.yz * math.sign(a.x));
    private static float3 Divide(float3 a, float3 b) => new float3(a.x / b.x, (a.yz * b.x - b.yz * a.x) / (b.x * b.x));
    private static float3 SoftEdge(float threshold, float3 width, float3 field) => Smooth(0f, 1f, Divide(field - Constant(threshold), width));
    private static float3 RoundedAbs(float3 a)
    {
        const float rounding = 0.025f;
        float length = math.sqrt(a.x * a.x + rounding * rounding);
        return new float3(length - rounding, a.yz * (a.x / length));
    }

    // Fixed uint hashing also has a matching shader implementation for regional rock appearance.
    public static uint Hash(int x, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)seed ^ (uint)x * 374761393u ^ (uint)z * 668265263u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
    }
    private static float Signed(int x, int z, int seed) => (Hash(x, z, seed) & 0x00ffffffu) / 8388607.5f - 1f;

    public static float3 Noise(float2 position, float scale, int seed)
    {
        float frequency = 1f / math.max(scale, 1f);
        float2 p = position * frequency;
        int2 cell = (int2)math.floor(p);
        float2 f = p - cell;
        float2 u = f * f * f * (f * (f * 6f - 15f) + 10f);
        float2 du = 30f * f * f * (f - 1f) * (f - 1f) * frequency;
        float a = Signed(cell.x, cell.y, seed), b = Signed(cell.x + 1, cell.y, seed);
        float c = Signed(cell.x, cell.y + 1, seed), d = Signed(cell.x + 1, cell.y + 1, seed);
        float cross = a - b - c + d;
        return new float3(a + (b - a) * u.x + (c - a) * u.y + cross * u.x * u.y,
            (b - a + cross * u.y) * du.x, (c - a + cross * u.x) * du.y);
    }

    public static float3 Desert(float2 p, int seed, float sampleScale, float regionScale)
    {
        float scale = math.max(sampleScale, 1f) * regionScale;
        float3 broad = Noise(p, scale, seed + 81001);
        float3 edge = Noise(p, scale * 0.37f, seed + 81002) * 0.12f;
        return Smooth(0.04f, 0.42f, broad + edge);
    }

    public static WorldLandformRegion Region(float2 p, int seed, float sampleScale, in WorldLandformSettings s)
    {
        float scale = math.max(sampleScale, 1f);
        float3 dry = Desert(p, seed, scale, s.desertRegionScale);
        float3 sediment = Smooth(-0.35f, 0.30f, Noise(p, scale * 5f, seed + 82001));
        float3 province = Smooth(-0.12f, 0.32f, Noise(p, scale * 7f, seed + 82002));
        float3 plateau = Multiply(sediment, Multiply(province, Constant(0.24f) + dry * 0.76f));
        float3 sand = Multiply(dry, Multiply(Constant(1f) - plateau,
            Smooth(-0.18f, 0.38f, Noise(p, scale * 2.8f, seed + 82003))));
        float3 basin = Multiply(dry, Multiply(Constant(1f) - plateau,
            Smooth(0.42f, 0.72f, Noise(p, scale * 2.2f, seed + 82004))));
        return new WorldLandformRegion(dry, sediment, plateau, sand, basin);
    }

    public static WorldRockClass RockClass(float2 p, int seed, float scale, in WorldLandformSettings s)
    {
        var r = Region(p, seed, scale, s);
        if (r.Sediment.x >= 0.5f) return r.Aridity.x >= 0.5f ? WorldRockClass.Sandstone : WorldRockClass.Limestone;
        return r.Aridity.x >= 0.5f ? WorldRockClass.DesertBedrock : WorldRockClass.Granite;
    }

    public static WorldBaseSample ShapeBase(float2 p, WorldBaseSample input, float scale, int seed,
        float waterLevel, in WorldLandformSettings s)
    {
        if (!s.enabled) return input;
        var r = Region(p, seed, scale, s);
        float3 original = new float3(input.Height, input.Gradient);
        float3 high = Smooth(waterLevel + 0.9f, waterLevel + 1.8f, original);
        float3 edgeWidth = Constant(s.cliffWidth) + high * (s.cliffWidth * 4f);
        // Keep submerged terrain and the immediate shoreline on the original generator.
        float3 land = Smooth(waterLevel + 0.08f, waterLevel + 0.38f, original);
        float3 low = Constant(1f) - Smooth(1.6f + waterLevel, 4f + waterLevel, original);
        float3 suitable = Multiply(land, low);
        float plateauScale = math.max(scale, 1f) * s.plateauScale;
        float3 broad = Noise(p, plateauScale, seed + 83001);
        float3 breakup = Noise(p, plateauScale * 0.39f, seed + 83002) * 0.13f;
        float3 shape = broad + breakup;
        float3 plateau = SoftEdge(0.08f, edgeWidth, shape);
        // Detached remnants share their parent province instead of being scattered unrelated props.
        float3 buttes = Multiply(SoftEdge(0.48f, edgeWidth, Noise(p, plateauScale * 0.43f, seed + 83003)),
            Constant(1f) - plateau);
        float3 upland = plateau + buttes * 0.72f;
        float3 heightVariation = Constant(0.85f) + Noise(p, plateauScale * 2f, seed + 83004) * 0.25f;
        // A second ledge gives long escarpments and stepped shoulders without terracing all mountains.
        upland += SoftEdge(-0.22f, edgeWidth * 1.3f, shape) * 0.22f;
        // Elevated provinces become broad raised benches. Merely widening each small
        // cliff would replace their flat tops with long steep ramps instead.
        upland = Multiply(upland, Constant(1f) - high) + high * 0.9f;
        float3 uplift = Multiply(Multiply(Multiply(suitable, r.Plateau), upland), heightVariation) * s.plateauRelief;
        // Connected warped zero-contours, with tributaries, cut through plateau interiors.
        float3 drainage = Noise(p, plateauScale * 0.78f, seed + 84001) + Noise(p, plateauScale * 0.27f, seed + 84002) * 0.18f;
        float3 canyon = Multiply(Constant(1f) - SoftEdge(0.025f, Constant(0.085f) + high * 0.18f, Abs(drainage)),
            Constant(1f) - high);
        float3 cutUplift = Multiply(uplift, Constant(1f) - canyon * 0.92f);
        float3 result = original + cutUplift;
        // Angular exposed ridges in dry mountain country. Uses the actual height gradient in the chain rule.
        float3 mountain = Smooth(waterLevel + 1.4f, waterLevel + 4f, original);
        float3 ridge = Constant(1f) - RoundedAbs(Noise(p, plateauScale * 0.40f, seed + 85001));
        result += Multiply(Multiply(Multiply(r.Aridity, mountain), Constant(1f) - high), ridge) * 0.42f;
        float protection = math.saturate(math.max(uplift.x / math.max(s.plateauRelief, 0.001f), input.LandformMask));
        return new WorldBaseSample(result.x, result.yz, input.MountainMask, input.MountainWeight,
            input.MountainContribution, protection);
    }

    private static float DryDrainage(float2 p, int seed, float scale)
    {
        return Noise(p, math.max(scale, 1f) * 0.9f, seed + 86001).x +
            Noise(p, math.max(scale, 1f) * 0.26f, seed + 86002).x * 0.20f;
    }
    public static float DryWash(float2 p, int seed, float scale) =>
        1f - math.smoothstep(0.018f, 0.095f, math.abs(DryDrainage(p, seed, scale)));

    public static float DuneProfile(float phase)
    {
        // Long windward rise, shorter slip face; rounded joins prevent needle crests.
        float t = math.frac(phase);
        float ramp = t < 0.72f ? t / 0.72f : (1f - t) / 0.28f;
        return ramp * ramp * (3f - 2f * ramp);
    }

    public static float FinishUplands(float2 p, float height, float baseHeight, float plateauMask, int seed, float scale,
        float waterLevel, in WorldLandformSettings s)
    {
        if (!s.enabled || height <= waterLevel + TerrainWaterSettings.ShoreBand) return height;
        float blend = Upland(baseHeight, waterLevel) * math.smoothstep(0.10f, 0.30f, plateauMask);
        if (blend <= 0f) return height;
        // Broad, gently inclined benches with rounded connecting shoulders. A slow
        // regional phase varies their levels; only raised plateau provinces receive
        // shelves, so ordinary rounded mountains do not acquire repeated bands.
        // The derivative stays positive through
        // every repeat, including the wrap. Low country receives no height remap.
        const float spacing = 0.75f;
        float offset = Noise(p, math.max(scale, 1f) * 2f, seed + 89001).x * 0.20f +
            Noise(p, math.max(scale, 1f) * 7f, seed + 89002).x * 0.15f;
        float phase = (height - waterLevel) / spacing + offset;
        float t = math.frac(phase);
        float profile = t * 0.12f + math.smoothstep(0.30f, 1f, t) * 0.88f + 0.132f;
        float terraced = waterLevel + (math.floor(phase) + profile - offset) * spacing;
        return math.lerp(height, terraced, blend);
    }

    public static float FinishSurface(float2 p, WorldBaseSample input, float height, float river, float scale,
        int seed, float waterLevel, in WorldLandformSettings s)
    {
        if (!s.enabled) return height;
        var r = Region(p, seed, scale, s);
        float exclusion = s.dryDeserts ? DryProvince(r.Aridity.x) : 0f;
        float dryFloor = DryFloor(p, seed, scale, waterLevel);
        // A geometric floor, shared by every consumer, prevents invisible water and wet
        // ecology in desert basins. The broad province transition preserves coast continuity.
        float above = height - waterLevel;
        // Smooth positive relief keeps the old shoreline from becoming a perfectly level
        // filled polygon. Low areas become basin floors; ordinary dry ground retains its hills.
        float dryHeight = dryFloor + 0.5f * (above + math.sqrt(above * above + 0.0064f));
        height = math.lerp(height, dryHeight, exclusion);
        if (height <= waterLevel) return height;
        float uplandDetail = 1f - Upland(input.Height, waterLevel);
        float dry = r.Aridity.x * math.smoothstep(waterLevel + 0.04f, waterLevel + 0.28f, height) * uplandDetail;
        float flat = 1f - math.smoothstep(0.0007f, 0.0025f, math.length(input.Gradient));
        float low = 1f - math.smoothstep(0.06f, 0.25f, input.MountainContribution);
        float channelProtection = 1f - math.smoothstep(0.08f, 0.4f, river);
        float wash = DryWash(p, seed, scale) * dry * low * channelProtection;
        // An ephemeral wash follows local relief; never lower its bed into the shared water plane.
        float washFloor = math.lerp(waterLevel + 0.045f, dryFloor, exclusion);
        height -= math.min(s.dryWashDepth, math.max(0f, height - washFloor)) * wash;
        // Lowland basin floors must not drag an elevated bench down toward the sea-level clay pan.
        float lowBasin = 1f - math.smoothstep(waterLevel + 0.30f, waterLevel + 0.75f, input.Height);
        float basin = r.Basin.x * flat * low * channelProtection * uplandDetail * lowBasin;
        float basinFloor = math.max(waterLevel + 0.09f, dryFloor + 0.025f) + (1f-r.Basin.x) * 0.06f;
        height = math.lerp(height, math.min(height, basinFloor), basin * 0.94f);
        // Dunes taper away over a broad apron around washes; using the narrow channel mask
        // here would make sand walls along the wash banks steeper than a slip face.
        float washApron = math.smoothstep(0.09f, 0.45f, math.abs(DryDrainage(p, seed, scale)));
        // Broad river-valley shoulders are dry ground too. Reserve only the immediate
        // shoreline; the separate river mask keeps dunes out of active channels.
        float drySand = r.Aridity.x * math.smoothstep(waterLevel + 0.012f, waterLevel + 0.04f, height) * uplandDetail;
        float sand = r.Sand.x * flat * low * channelProtection * washApron * (1f - r.Basin.x) *
            (1f - math.saturate(input.LandformMask * 2f)) * drySand;
        float2 direction = math.normalizesafe(new float2(1f, 0.32f), new float2(1f, 0f));
        // Seed chooses a stable quadrant. Low-frequency phase warp bends and breaks the crests.
        uint wind = Hash(0, 0, seed + 87000);
        if ((wind & 1u) != 0) direction.x = -direction.x;
        if ((wind & 2u) != 0) direction = direction.yx;
        float phase = math.dot(p, direction) / s.duneWavelength +
            Noise(p, s.duneWavelength * 3f, seed + 87001).x * 0.35f;
        float variation = 0.75f + Noise(p, s.duneWavelength * 4f, seed + 87002).x * 0.18f;
        height += DuneProfile(phase) * s.duneHeight * sand * variation;
        // Short eroded ridges and gullies on dry sedimentary shoulders, away from loose sand.
        float badland = dry * r.Sediment.x * low * (1f - flat) * (1f - input.LandformMask) * channelProtection;
        float ribs = math.abs(Noise(p, math.max(scale, 1f) * 0.13f, seed + 88001).x);
        height += ribs * 0.09f * badland;
        return height;
    }

    public static SurfaceType Surface(float2 p, int seed, float scale, float height, float slope, float river,
        BiomeType biome, float waterLevel, in WorldLandformSettings s)
    {
        var surface = SurfaceTypeClassifier.Classify(height, slope, river, biome, waterLevel);
        if (!s.enabled || height <= waterLevel + TerrainWaterSettings.ShoreBand ||
            river >= TerrainWaterSettings.RiverBankThreshold || slope > TerrainSlopePolicy.LooseSurfaceMaxDegrees ||
            surface == SurfaceType.Snow || surface == SurfaceType.Cliff) return surface;
        var r = Region(p, seed, scale, s);
        if (biome != BiomeType.Desert && !(s.dryDeserts && r.Aridity.x >= 0.60f)) return surface;
        if (r.Aridity.x < 0.18f) return surface;
        float floor = DryFloor(p,seed,scale,waterLevel);
        // Pale low basins and clay pans replace the former exposed submerged bed.
        if (height < waterLevel + 0.8f && slope < 16f && (r.Basin.x > 0.40f ||
            s.dryDeserts && r.Sand.x < 0.45f && height < floor + 0.055f))
            return SurfaceType.CrackedClay;
        if (height < waterLevel + 0.8f && DryWash(p, seed, scale) > 0.32f) return SurfaceType.DryWash;
        if (r.Plateau.x > 0.4f || r.Sand.x < 0.40f) return SurfaceType.Rock;
        return SurfaceType.Sand;
    }
}
