using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

public static class GroundCoverMapGenerator
{
    public static GroundCoverType[,] GenerateGroundCoverMap(
        BiomeType[,] biomeMap,
        SurfaceType[,] surfaceTypeMap,
        float[,] moistureMap,
        float[,] slopeMap,
        float[,] riverMaskMap,
        WorldFeaturePlan worldFeaturePlan,
        int chunkSize,
        int seed,
        ChunkCoord chunkCoord)
    {
        NativeArray<BiomeType> biomes = default;
        NativeArray<SurfaceType> surfaces = default;
        NativeArray<float> moistures = default;
        NativeArray<float> slopes = default;
        NativeArray<float> riverMasks = default;
        NativeArray<float> canopyDensities = default;
        NativeArray<float> clearings = default;
        NativeArray<float> rockInfluences = default;
        NativeArray<float> dampShades = default;
        NativeArray<float> organicFloorIntents = default;
        NativeArray<GroundCoverType> groundCovers = default;

        int width = biomeMap.GetLength(0);
        int height = biomeMap.GetLength(1);
        GroundCoverType[,] groundCoverMap = new GroundCoverType[width, height];

        try
        {
            biomes = CopyMapToNative(biomeMap, Allocator.TempJob, out width, out height);
            surfaces = CopyMapToNative(surfaceTypeMap, Allocator.TempJob, out _, out _);
            moistures = CopyFloatMapToNative(moistureMap, Allocator.TempJob, out _, out _);
            slopes = CopyFloatMapToNative(slopeMap, Allocator.TempJob, out _, out _);
            riverMasks = CopyFloatMapToNative(riverMaskMap, Allocator.TempJob, out _, out _);
            canopyDensities = CopyFloatMapToNative(worldFeaturePlan.CanopyDensityMap, Allocator.TempJob, out _, out _);
            clearings = CopyFloatMapToNative(worldFeaturePlan.ForestStructure.ClearingMap, Allocator.TempJob, out _, out _);
            rockInfluences = CopyFloatMapToNative(worldFeaturePlan.ForestStructure.RockInfluenceMap, Allocator.TempJob, out _, out _);
            dampShades = CopyFloatMapToNative(worldFeaturePlan.ForestStructure.DampShadeMap, Allocator.TempJob, out _, out _);
            organicFloorIntents =
                CopyFloatMapToNative(worldFeaturePlan.ForestStructure.OrganicFloorIntentMap, Allocator.TempJob, out _, out _);
            groundCovers =
                new NativeArray<GroundCoverType>(width * height, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            GroundCoverMapJob job = new GroundCoverMapJob
            {
                height = height,
                chunkSize = chunkSize,
                seed = seed,
                chunkX = chunkCoord.x,
                chunkZ = chunkCoord.z,
                biomes = biomes,
                surfaces = surfaces,
                moistures = moistures,
                slopes = slopes,
                riverMasks = riverMasks,
                canopyDensities = canopyDensities,
                clearings = clearings,
                rockInfluences = rockInfluences,
                dampShades = dampShades,
                organicFloorIntents = organicFloorIntents,
                groundCovers = groundCovers
            };

            JobHandle handle = job.Schedule(groundCovers.Length, 64);
            handle.Complete();

            CopyNativeToMap(groundCovers, groundCoverMap);
        }
        finally
        {
            if (biomes.IsCreated)
                biomes.Dispose();
            if (surfaces.IsCreated)
                surfaces.Dispose();
            if (moistures.IsCreated)
                moistures.Dispose();
            if (slopes.IsCreated)
                slopes.Dispose();
            if (riverMasks.IsCreated)
                riverMasks.Dispose();
            if (canopyDensities.IsCreated)
                canopyDensities.Dispose();
            if (clearings.IsCreated)
                clearings.Dispose();
            if (rockInfluences.IsCreated)
                rockInfluences.Dispose();
            if (dampShades.IsCreated)
                dampShades.Dispose();
            if (organicFloorIntents.IsCreated)
                organicFloorIntents.Dispose();
            if (groundCovers.IsCreated)
                groundCovers.Dispose();
        }

        return groundCoverMap;
    }

    private static NativeArray<float> CopyFloatMapToNative(float[,] source, Allocator allocator, out int width, out int height)
    {
        width = source.GetLength(0);
        height = source.GetLength(1);
        NativeArray<float> result =
            new NativeArray<float>(width * height, allocator, NativeArrayOptions.UninitializedMemory);

        for (int x = 0; x < width; x++)
        {
            int rowOffset = x * height;
            for (int z = 0; z < height; z++)
                result[rowOffset + z] = source[x, z];
        }

        return result;
    }

    private static NativeArray<T> CopyMapToNative<T>(T[,] source, Allocator allocator, out int width, out int height)
        where T : unmanaged
    {
        width = source.GetLength(0);
        height = source.GetLength(1);
        NativeArray<T> result =
            new NativeArray<T>(width * height, allocator, NativeArrayOptions.UninitializedMemory);

        for (int x = 0; x < width; x++)
        {
            int rowOffset = x * height;
            for (int z = 0; z < height; z++)
                result[rowOffset + z] = source[x, z];
        }

        return result;
    }

    private static void CopyNativeToMap(NativeArray<GroundCoverType> source, GroundCoverType[,] target)
    {
        int width = target.GetLength(0);
        int height = target.GetLength(1);

        for (int x = 0; x < width; x++)
        {
            int rowOffset = x * height;
            for (int z = 0; z < height; z++)
                target[x, z] = source[rowOffset + z];
        }
    }

    [BurstCompile]
    private struct GroundCoverMapJob : IJobParallelFor
    {
        public int height;
        public int chunkSize;
        public int seed;
        public int chunkX;
        public int chunkZ;

        [ReadOnly] public NativeArray<BiomeType> biomes;
        [ReadOnly] public NativeArray<SurfaceType> surfaces;
        [ReadOnly] public NativeArray<float> moistures;
        [ReadOnly] public NativeArray<float> slopes;
        [ReadOnly] public NativeArray<float> riverMasks;
        [ReadOnly] public NativeArray<float> canopyDensities;
        [ReadOnly] public NativeArray<float> clearings;
        [ReadOnly] public NativeArray<float> rockInfluences;
        [ReadOnly] public NativeArray<float> dampShades;
        [ReadOnly] public NativeArray<float> organicFloorIntents;

        [WriteOnly] public NativeArray<GroundCoverType> groundCovers;

        public void Execute(int index)
        {
            int x = index / height;
            int z = index - x * height;

            groundCovers[index] = Classify(
                biomes[index],
                surfaces[index],
                moistures[index],
                slopes[index],
                riverMasks[index],
                index,
                x,
                z);
        }

        private GroundCoverType Classify(
            BiomeType biome,
            SurfaceType surface,
            float moisture,
            float slope,
            float riverMask,
            int index,
            int x,
            int z)
        {
            if (surface != SurfaceType.Grass)
                return GroundCoverType.Default;

            switch (biome)
            {
                case BiomeType.Forest:
                    return ClassifyForestCover(moisture, slope, riverMask, index, x, z);
                case BiomeType.Taiga:
                    return Sample01(seed + 8310, x, z, 0.04f) > 0.48f
                        ? GroundCoverType.NeedleLitter
                        : GroundCoverType.DarkGrass;
                case BiomeType.Tundra:
                    return GroundCoverType.SnowDusting;
                default:
                    return GroundCoverType.Default;
            }
        }

        private GroundCoverType ClassifyForestCover(
            float moisture,
            float slope,
            float riverMask,
            int index,
            int x,
            int z)
        {
            float patchNoise = Sample01(seed + 8300, x, z, 0.055f);
            float broadPatchNoise = Sample01(seed + 8301, x, z, 0.023f);
            float canopyDensity = canopyDensities[index];
            float clearing = clearings[index];
            float rockInfluence = rockInfluences[index];
            float dampShade = dampShades[index];
            float organicFloorIntent = organicFloorIntents[index];
            bool nearRiver = riverMask > 0.64f;
            bool exposedOrDry = slope > 0.085f || moisture < 0.32f;

            if (nearRiver)
                return dampShade > 0.58f && patchNoise > 0.55f ? GroundCoverType.Moss : GroundCoverType.BareDirt;

            if (rockInfluence > 0.52f)
            {
                if (dampShade > 0.48f && patchNoise > 0.35f)
                    return GroundCoverType.Moss;

                return organicFloorIntent > 0.66f && broadPatchNoise > 0.48f
                    ? GroundCoverType.LeafLitter
                    : GroundCoverType.BareDirt;
            }

            if (clearing > 0.56f)
            {
                if (organicFloorIntent > 0.74f && patchNoise > 0.62f)
                    return GroundCoverType.LeafLitter;

                return GroundCoverType.DarkGrass;
            }

            if (organicFloorIntent > 0.58f && patchNoise > 0.24f)
            {
                if (dampShade > 0.68f && patchNoise < 0.34f)
                    return GroundCoverType.Moss;

                if (exposedOrDry && patchNoise > 0.72f)
                    return GroundCoverType.BareDirt;

                return GroundCoverType.LeafLitter;
            }

            if (canopyDensity > 0.62f)
            {
                if (exposedOrDry && patchNoise > 0.35f)
                    return GroundCoverType.BareDirt;

                if (dampShade > 0.66f && patchNoise < 0.24f)
                    return GroundCoverType.Moss;

                return organicFloorIntent > 0.48f && broadPatchNoise > 0.68f
                    ? GroundCoverType.LeafLitter
                    : GroundCoverType.DarkGrass;
            }

            if (canopyDensity > 0.28f)
            {
                if (organicFloorIntent > 0.52f && (broadPatchNoise > 0.72f || patchNoise > 0.86f))
                    return GroundCoverType.LeafLitter;

                return GroundCoverType.DarkGrass;
            }

            return GroundCoverType.DarkGrass;
        }

        private float Sample01(int noiseSeed, int x, int z, float scale)
        {
            float worldX = chunkX * chunkSize + (x - 1);
            float worldZ = chunkZ * chunkSize + (z - 1);
            float sample = SampleValueNoise(worldX * scale, worldZ * scale, noiseSeed);
            return math.clamp((sample + 1f) * 0.5f, 0f, 1f);
        }

        private static float SampleValueNoise(float x, float z, int noiseSeed)
        {
            int ix = (int)math.floor(x);
            int iz = (int)math.floor(z);

            float fx = x - ix;
            float fz = z - iz;

            float u = Quintic(fx);
            float v = Quintic(fz);

            float a = HashToSignedValue(ix, iz, noiseSeed);
            float b = HashToSignedValue(ix + 1, iz, noiseSeed);
            float c = HashToSignedValue(ix, iz + 1, noiseSeed);
            float d = HashToSignedValue(ix + 1, iz + 1, noiseSeed);

            float k0 = a;
            float k1 = b - a;
            float k2 = c - a;
            float k3 = a - b - c + d;

            return k0 + k1 * u + k2 * v + k3 * u * v;
        }

        private static float Quintic(float t)
        {
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        private static float HashToSignedValue(int x, int z, int noiseSeed)
        {
            unchecked
            {
                uint h = (uint)noiseSeed;
                h ^= 374761393u * (uint)x;
                h ^= 668265263u * (uint)z;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;

                float value01 = (h & 0x00FFFFFFu) / 16777215f;
                return value01 * 2f - 1f;
            }
        }
    }
}
