using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public readonly struct TerrainHeightSample
{
    public readonly float Height;
    public readonly float MountainMask;
    public readonly float RiverMask;

    public TerrainHeightSample(float height, float mountainMask, float riverMask)
    {
        Height = height;
        MountainMask = mountainMask;
        RiverMask = riverMask;
    }
}

public readonly struct TerrainHeightSamplingContext
{
    public readonly int RiverSeed;
    public readonly float WaterLevel;
    public readonly float MountainHorizontalScale;
    public readonly WorldErosionSettings Erosion;

    public TerrainHeightSamplingContext(int seed, float waterLevel, float mountainHorizontalScale = 1f, WorldErosionSettings erosion = default)
    {
        WaterLevel = waterLevel;
        Erosion = erosion.Sanitized();
        MountainHorizontalScale = HeightMapGenerator.SanitizeMountainHorizontalScale(mountainHorizontalScale);
        RiverSeed = seed + 60000;
    }
}

public static partial class HeightMapGenerator
{
    private const int GameplaySlopeRadius = 4;
    private const int HeightFieldSampleBorder = GameplaySlopeRadius + 1;
    public static float SanitizeMountainHorizontalScale(float value)
    {
        return math.isfinite(value) ? math.clamp(value, 1f, 3f) : 1f;
    }

    public static TerrainHeightSamplingContext CreateSamplingContext(int seed, float waterLevel, float mountainHorizontalScale = 1f, WorldErosionSettings erosion = default)
    {
        return new TerrainHeightSamplingContext(seed, waterLevel, mountainHorizontalScale, erosion);
    }

    public static TerrainHeightSample SampleTerrainHeight(
        float worldX,
        float worldZ,
        float sampleScale,
        TerrainHeightSamplingContext context)
    {
        TerrainHeightSampleData sample = SampleTerrainHeight(
            worldX,
            worldZ,
            sampleScale,
            context.RiverSeed,
            context.WaterLevel, context.MountainHorizontalScale, context.Erosion);
        return new TerrainHeightSample(sample.Height, sample.MountainMask, sample.RiverMask);
    }

    public static TerrainHeightSample SampleTerrainHeightNative(
        float worldX,
        float worldZ,
        float sampleScale,
        int riverSeed,
        float waterLevel,
        float mountainHorizontalScale = 1f,
        WorldErosionSettings erosion = default)
    {
        TerrainHeightSampleData sample = SampleTerrainHeight(
            worldX,
            worldZ,
            sampleScale,
            riverSeed, waterLevel, SanitizeMountainHorizontalScale(mountainHorizontalScale), erosion);
        return new TerrainHeightSample(sample.Height, sample.MountainMask, sample.RiverMask);
    }

    public static HeightFieldResult GenerateTerrainHeightField(
        int chunkSize,
        int seed,
        float sampleScale,
        ChunkCoord chunkCoord,
        float waterLevel,
        float mountainHorizontalScale = 1f,
        float meshHeightMultiplier = 200f, WorldErosionSettings erosion = default)
    {
        return GenerateTerrainHeightFieldCore(chunkSize, seed, sampleScale, chunkCoord, waterLevel,
            mountainHorizontalScale, meshHeightMultiplier, erosion, false,
            out _, out _, out _);
    }

    public static HeightFieldResult GenerateTerrainHeightFieldForRequest(
        int chunkSize, int seed, float sampleScale, ChunkCoord chunkCoord, float waterLevel,
        float mountainHorizontalScale, float meshHeightMultiplier, WorldErosionSettings erosion,
        out NativeArray<float> nativeHeights, out NativeArray<float> nativeSlopes,
        out NativeArray<float> nativeRiverMasks)
    {
        return GenerateTerrainHeightFieldCore(chunkSize, seed, sampleScale, chunkCoord, waterLevel,
            mountainHorizontalScale, meshHeightMultiplier, erosion, true,
            out nativeHeights, out nativeSlopes, out nativeRiverMasks);
    }

    private static HeightFieldResult GenerateTerrainHeightFieldCore(
        int chunkSize, int seed, float sampleScale, ChunkCoord chunkCoord, float waterLevel,
        float mountainHorizontalScale, float meshHeightMultiplier, WorldErosionSettings erosion,
        bool retainNativeMaps, out NativeArray<float> nativeHeights,
        out NativeArray<float> nativeSlopes, out NativeArray<float> nativeRiverMasks)
    {
        nativeHeights = default;
        nativeSlopes = default;
        nativeRiverMasks = default;
        bool success = false;
        // Keep the public one-sample halo, but generate four extra samples on each side
        // so the four-unit gameplay slope stencil is identical across chunk boundaries.
        int mapSize = chunkSize + 3;
        int width = mapSize + GameplaySlopeRadius * 2;
        int height = mapSize + GameplaySlopeRadius * 2;

        float[,] finalHeightMap = new float[mapSize, mapSize];
        float[,] mountainMaskMap = new float[mapSize, mapSize];
        float[,] riverMaskMap = new float[mapSize, mapSize];
        float[,] slopeMap = new float[mapSize, mapSize];

        if (sampleScale <= 0f)
            sampleScale = 0.0001f;

        TerrainHeightSamplingContext samplingContext = CreateSamplingContext(seed, waterLevel, mountainHorizontalScale, erosion);
        int sampleCount = width * height;

        NativeArray<float> finalHeights = default;
        NativeArray<float> mountainMasks = default;
        NativeArray<float> riverMasks = default;
        NativeArray<float> slopes = default;

        try
        {
            finalHeights = new NativeArray<float>(sampleCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            mountainMasks = new NativeArray<float>(sampleCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            riverMasks = new NativeArray<float>(sampleCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            slopes = new NativeArray<float>(sampleCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            HeightFieldSampleJob sampleJob = new HeightFieldSampleJob
            {
                width = width,
                height = height,
                chunkSize = chunkSize,
                chunkX = chunkCoord.x,
                chunkZ = chunkCoord.z,
                sampleScale = sampleScale,
                riverSeed = samplingContext.RiverSeed,
                waterLevel = waterLevel,
                mountainHorizontalScale = samplingContext.MountainHorizontalScale,
                erosion = samplingContext.Erosion,
                finalHeights = finalHeights,
                mountainMasks = mountainMasks,
                riverMasks = riverMasks
            };

            JobHandle sampleHandle = sampleJob.Schedule(sampleCount, 64);

            HeightSlopeJob slopeJob = new HeightSlopeJob
            {
                heightMultiplier = meshHeightMultiplier,
                width = width,
                height = height,
                finalHeights = finalHeights,
                slopes = slopes
            };

            JobHandle slopeHandle = slopeJob.Schedule(sampleCount, 64, sampleHandle);
            slopeHandle.Complete();

            if (retainNativeMaps)
            {
                int interiorCount = mapSize * mapSize;
                nativeHeights = new NativeArray<float>(interiorCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                nativeSlopes = new NativeArray<float>(interiorCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                nativeRiverMasks = new NativeArray<float>(interiorCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }

            CopyHeightFieldInteriorToMap(finalHeights, finalHeightMap, nativeHeights);
            CopyHeightFieldInteriorToMap(mountainMasks, mountainMaskMap);
            CopyHeightFieldInteriorToMap(riverMasks, riverMaskMap, nativeRiverMasks);
            CopyHeightFieldInteriorToMap(slopes, slopeMap, nativeSlopes);
            success = true;
        }
        finally
        {
            if (!success)
            {
                if (nativeHeights.IsCreated) nativeHeights.Dispose();
                if (nativeSlopes.IsCreated) nativeSlopes.Dispose();
                if (nativeRiverMasks.IsCreated) nativeRiverMasks.Dispose();
                nativeHeights = default;
                nativeSlopes = default;
                nativeRiverMasks = default;
            }
            if (finalHeights.IsCreated)
                finalHeights.Dispose();
            if (mountainMasks.IsCreated)
                mountainMasks.Dispose();
            if (riverMasks.IsCreated)
                riverMasks.Dispose();
            if (slopes.IsCreated)
                slopes.Dispose();
        }

        return new HeightFieldResult(finalHeightMap, slopeMap, mountainMaskMap, riverMaskMap);
    }

    public static float[,] ApplyBiomeHeightModifiers(float[,] rawHeightMap, BiomeType[,] biomeMap)
    {
        return rawHeightMap;
    }

    private static void CopyHeightFieldInteriorToMap(
        NativeArray<float> source, float[,] target, NativeArray<float> retained = default)
    {
        int width = target.GetLength(0);
        int height = target.GetLength(1);

        for (int x = 0; x < width; x++)
        {
            int rowOffset = (x + GameplaySlopeRadius) * (height + GameplaySlopeRadius * 2);

            for (int z = 0; z < height; z++)
            {
                float value = source[rowOffset + z + GameplaySlopeRadius];
                target[x, z] = value;
                if (retained.IsCreated)
                    retained[x * height + z] = value;
            }
        }
    }

    private static float ApplyHeightPipeline(float normalizedHeight)
    {
        return normalizedHeight;
    }

    private static TerrainHeightSampleData SampleTerrainHeight(
        float worldX,
        float worldZ,
        float sampleScale,
        int riverSeed,
        float waterLevel,
        float mountainHorizontalScale = 1f,
        WorldErosionSettings erosion = default)
    {
        return SampleWorldHeight(new float2(worldX, worldZ), sampleScale, riverSeed, waterLevel, mountainHorizontalScale, erosion.Sanitized());
    }

    private static TerrainHeightSampleData SampleWorldHeight(float2 position, float sampleScale, int riverSeed,
        float waterLevel, float coverage, in WorldErosionSettings erosion)
    {
        int seed = riverSeed - 60000;
        WorldBaseSample input = WorldTerrainHeight.Base(position, sampleScale, seed, coverage, erosion);
        float height = WorldTerrainHeight.Erode(position, input, sampleScale, seed, coverage, erosion);
        height = WorldTerrainHeight.ShapeForAccess(position, input, height, waterLevel, seed, erosion);
        float carvedRiverMask = 0f;
        if (erosion.carveRivers)
        {
            float scale = math.max(sampleScale, 0.0001f) * 10f;
            float riverMask = SampleRiverMask(position.x / scale, position.y / scale, riverSeed, out float basin);
            float eligibility = (1f - math.smoothstep(0.012f, 0.03f, input.MountainContribution)) *
                (1f - math.smoothstep(0.20f, 0.55f, input.MountainWeight));
            carvedRiverMask = riverMask * eligibility;
            height = CarveRiverBasin(height, basin * eligibility, carvedRiverMask, waterLevel,
                erosion.riverValleyWidth, erosion.riverValleyFlattening);
        }
        return new TerrainHeightSampleData(height, input.MountainMask, carvedRiverMask);
    }
    private static float CarveRiverBasin(float originalHeight, float basinInfluence, float riverMask, float waterLevel,
        float valleyWidth = 1f, float valleyFlattening = 1f)
    {
        // The broad valley stays dry; only the narrow channel crosses the water plane.
        float shoulder = waterLevel + TerrainWaterSettings.RiverShoulderHeight;
        float basinBlend = math.smoothstep(0f, 1f, math.pow(math.saturate(basinInfluence), 1f / valleyWidth)) * valleyFlattening;
        float basinHeight = math.lerp(originalHeight, math.min(originalHeight, shoulder), basinBlend);
        float channelBlend = math.smoothstep(TerrainWaterSettings.RiverCarveStart, 1f, riverMask);
        float bed = waterLevel - TerrainWaterSettings.RiverBedDepth;
        return math.lerp(basinHeight, math.min(basinHeight, bed), channelBlend);
    }

    private static float SampleRiverMask(float sampleX, float sampleZ, int seed, out float basinInfluence)
    {
        const float siteCellSize = 1.8f;
        const float siteJitter = 0.6f;
        const float riverHalfWidth = 0.014f;
        const float bankFalloffWidth = 0.025f;
        const float pairAdjacencyFadeWidth = 0.06f;

        GetWarpedRiverSample(sampleX, sampleZ, seed, out float warpedX, out float warpedZ);

        int baseCellX = (int)math.floor(warpedX / siteCellSize);
        int baseCellZ = (int)math.floor(warpedZ / siteCellSize);

        float2 site0 = default;
        float2 site1 = default;
        float2 site2 = default;
        float2 site3 = default;
        float2 site4 = default;
        float2 site5 = default;
        float2 site6 = default;
        float2 site7 = default;
        float2 site8 = default;

        float distSq0 = 0f;
        float distSq1 = 0f;
        float distSq2 = 0f;
        float distSq3 = 0f;
        float distSq4 = 0f;
        float distSq5 = 0f;
        float distSq6 = 0f;
        float distSq7 = 0f;
        float distSq8 = 0f;

        int siteCount = 0;

        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = baseCellX + dx;
                int cz = baseCellZ + dz;

                float2 site = GetRiverSite(cx, cz, seed, siteCellSize, siteJitter);
                float dxp = warpedX - site.x;
                float dzp = warpedZ - site.y;
                float distSq = dxp * dxp + dzp * dzp;

                SetRiverSite(siteCount, site, distSq,
                    ref site0, ref site1, ref site2, ref site3, ref site4, ref site5, ref site6, ref site7, ref site8,
                    ref distSq0, ref distSq1, ref distSq2, ref distSq3, ref distSq4, ref distSq5, ref distSq6, ref distSq7, ref distSq8);

                siteCount++;
            }
        }

        float riverMask = 0f;
        basinInfluence = 0f;

        for (int i = 0; i < siteCount; i++)
        {
            GetRiverSiteByIndex(i,
                site0, site1, site2, site3, site4, site5, site6, site7, site8,
                distSq0, distSq1, distSq2, distSq3, distSq4, distSq5, distSq6, distSq7, distSq8,
                out float2 a, out float distSqA);

            for (int j = i + 1; j < siteCount; j++)
            {
                GetRiverSiteByIndex(j,
                    site0, site1, site2, site3, site4, site5, site6, site7, site8,
                    distSq0, distSq1, distSq2, distSq3, distSq4, distSq5, distSq6, distSq7, distSq8,
                    out float2 b, out float distSqB);

                float siteDeltaX = b.x - a.x;
                float siteDeltaZ = b.y - a.y;
                float siteSeparation = math.sqrt(siteDeltaX * siteDeltaX + siteDeltaZ * siteDeltaZ);

                if (siteSeparation < 0.0001f)
                    continue;

                float pairNearness = math.max(distSqA, distSqB);
                float closestThirdGap = float.MaxValue;

                for (int k = 0; k < siteCount; k++)
                {
                    if (k == i || k == j)
                        continue;

                    GetRiverSiteByIndex(k,
                        site0, site1, site2, site3, site4, site5, site6, site7, site8,
                        distSq0, distSq1, distSq2, distSq3, distSq4, distSq5, distSq6, distSq7, distSq8,
                        out _, out float distSqK);

                    float thirdGap = distSqK - pairNearness;

                    if (thirdGap < closestThirdGap)
                        closestThirdGap = thirdGap;
                }

                float adjacencyGate = InverseLerp(-pairAdjacencyFadeWidth, 0f, closestThirdGap);

                adjacencyGate = math.saturate(adjacencyGate);
                adjacencyGate = Smooth01(adjacencyGate);
                adjacencyGate = Smooth01(adjacencyGate);

                if (adjacencyGate <= 0f)
                    continue;

                float borderDistance = math.abs(distSqB - distSqA) / (2f * siteSeparation);
                float basinEdge = 1f - InverseLerp(
                    riverHalfWidth + bankFalloffWidth,
                    riverHalfWidth + bankFalloffWidth + TerrainWaterSettings.RiverBasinFalloffWidth,
                    borderDistance);
                basinInfluence = math.max(basinInfluence, basinEdge * adjacencyGate);
                float edgeMask = 1f - InverseLerp(
                    riverHalfWidth,
                    riverHalfWidth + bankFalloffWidth,
                    borderDistance);

                edgeMask = math.saturate(edgeMask);
                edgeMask *= adjacencyGate;

                riverMask = math.max(riverMask, edgeMask);
            }
        }

        return math.saturate(riverMask);
    }

    private static void GetWarpedRiverSample(float sampleX, float sampleZ, int seed, out float warpedX, out float warpedZ)
    {
        const float warpScale = 0.45f;
        const float warpStrength = 0.30f;

        float seedOffsetX1 = Hash01(seed, 0, 10) * 2000f - 1000f;
        float seedOffsetZ1 = Hash01(seed, 0, 11) * 2000f - 1000f;
        float seedOffsetX2 = Hash01(seed, 0, 12) * 2000f - 1000f;
        float seedOffsetZ2 = Hash01(seed, 0, 13) * 2000f - 1000f;

        float warpSampleX1 = sampleX * warpScale + 17.13f + seedOffsetX1;
        float warpSampleZ1 = sampleZ * warpScale + 41.27f + seedOffsetZ1;

        float warpSampleX2 = sampleX * warpScale + 73.91f + seedOffsetX2;
        float warpSampleZ2 = sampleZ * warpScale + 12.58f + seedOffsetZ2;

        float offsetX = noise.cnoise(new float2(warpSampleX1, warpSampleZ1)) * warpStrength;
        float offsetZ = noise.cnoise(new float2(warpSampleX2, warpSampleZ2)) * warpStrength;

        warpedX = sampleX + offsetX;
        warpedZ = sampleZ + offsetZ;
    }

    private static float2 GetRiverSite(int cellX, int cellZ, int seed, float siteCellSize, float siteJitter)
    {
        float jitterRange = siteCellSize * 0.5f * siteJitter;

        float ox = Hash01(cellX, cellZ, seed) * 2f - 1f;
        float oz = Hash01(cellX, cellZ, seed + 1) * 2f - 1f;

        float sx = (cellX + 0.5f) * siteCellSize + ox * jitterRange;
        float sz = (cellZ + 0.5f) * siteCellSize + oz * jitterRange;

        return new float2(sx, sz);
    }

    private static void SetRiverSite(
        int index,
        float2 site,
        float distSq,
        ref float2 site0,
        ref float2 site1,
        ref float2 site2,
        ref float2 site3,
        ref float2 site4,
        ref float2 site5,
        ref float2 site6,
        ref float2 site7,
        ref float2 site8,
        ref float distSq0,
        ref float distSq1,
        ref float distSq2,
        ref float distSq3,
        ref float distSq4,
        ref float distSq5,
        ref float distSq6,
        ref float distSq7,
        ref float distSq8)
    {
        switch (index)
        {
            case 0:
                site0 = site;
                distSq0 = distSq;
                break;
            case 1:
                site1 = site;
                distSq1 = distSq;
                break;
            case 2:
                site2 = site;
                distSq2 = distSq;
                break;
            case 3:
                site3 = site;
                distSq3 = distSq;
                break;
            case 4:
                site4 = site;
                distSq4 = distSq;
                break;
            case 5:
                site5 = site;
                distSq5 = distSq;
                break;
            case 6:
                site6 = site;
                distSq6 = distSq;
                break;
            case 7:
                site7 = site;
                distSq7 = distSq;
                break;
            case 8:
                site8 = site;
                distSq8 = distSq;
                break;
        }
    }

    private static void GetRiverSiteByIndex(
        int index,
        float2 site0,
        float2 site1,
        float2 site2,
        float2 site3,
        float2 site4,
        float2 site5,
        float2 site6,
        float2 site7,
        float2 site8,
        float distSq0,
        float distSq1,
        float distSq2,
        float distSq3,
        float distSq4,
        float distSq5,
        float distSq6,
        float distSq7,
        float distSq8,
        out float2 site,
        out float distSq)
    {
        switch (index)
        {
            case 0:
                site = site0;
                distSq = distSq0;
                break;
            case 1:
                site = site1;
                distSq = distSq1;
                break;
            case 2:
                site = site2;
                distSq = distSq2;
                break;
            case 3:
                site = site3;
                distSq = distSq3;
                break;
            case 4:
                site = site4;
                distSq = distSq4;
                break;
            case 5:
                site = site5;
                distSq = distSq5;
                break;
            case 6:
                site = site6;
                distSq = distSq6;
                break;
            case 7:
                site = site7;
                distSq = distSq7;
                break;
            default:
                site = site8;
                distSq = distSq8;
                break;
        }
    }

    private static float NormalizeSymmetric01(float value, float maxAbsValue)
    {
        return math.saturate((value + maxAbsValue) / (2f * maxAbsValue));
    }

    private static float ComputeMaxPossibleHeight(int octaves, float persistence)
    {
        float maxPossibleHeight = 0f;
        float amplitude = 1f;

        for (int i = 0; i < octaves; i++)
        {
            maxPossibleHeight += amplitude;
            amplitude *= persistence;
        }

        return maxPossibleHeight;
    }

    private static float InverseLerp(float a, float b, float value)
    {
        return math.saturate((value - a) / (b - a));
    }

    private static float Smooth01(float value)
    {
        return value * value * (3f - 2f * value);
    }

    private static float Quintic(float t)
    {
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    private static float HashToSignedValue(int x, int z, int seed)
    {
        float value01 = Hash01(x, z, seed);
        return value01 * 2f - 1f;
    }

    private static float Hash01(int x, int z, int channel)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)z * 668265263u + (uint)channel * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return h / (float)uint.MaxValue;
        }
    }

    private readonly struct TerrainHeightSampleData
    {
        public readonly float Height;
        public readonly float MountainMask;
        public readonly float RiverMask;

        public TerrainHeightSampleData(float height, float mountainMask, float riverMask)
        {
            Height = height;
            MountainMask = mountainMask;
            RiverMask = riverMask;
        }
    }

    [BurstCompile]
    private struct HeightFieldSampleJob : IJobParallelFor
    {
        public int width;
        public int height;
        public int chunkSize;
        public int chunkX;
        public int chunkZ;
        public float sampleScale;
        public int riverSeed;
        public float waterLevel;
        public float mountainHorizontalScale;
        public WorldErosionSettings erosion;
        [WriteOnly] public NativeArray<float> finalHeights;
        [WriteOnly] public NativeArray<float> mountainMasks;
        [WriteOnly] public NativeArray<float> riverMasks;

        public void Execute(int index)
        {
            int x = index / height;
            int z = index - x * height;
            int localSampleX = x - HeightFieldSampleBorder;
            int localSampleZ = z - HeightFieldSampleBorder;

            float worldX = chunkX * chunkSize + localSampleX;
            float worldZ = chunkZ * chunkSize + localSampleZ;

            TerrainHeightSampleData sample = SampleTerrainHeight(
                worldX,
                worldZ,
                sampleScale,
                riverSeed, waterLevel, mountainHorizontalScale, erosion);

            finalHeights[index] = sample.Height;
            mountainMasks[index] = sample.MountainMask;
            riverMasks[index] = sample.RiverMask;
        }
    }

    [BurstCompile]
    private struct HeightSlopeJob : IJobParallelFor
    {
        public float heightMultiplier;
        public int width;
        public int height;

        [ReadOnly] public NativeArray<float> finalHeights;
        [WriteOnly] public NativeArray<float> slopes;

        public void Execute(int index)
        {
            const int slopeRadius = GameplaySlopeRadius;

            int x = index / height;
            int z = index - x * height;

            int x0 = math.max(x - slopeRadius, 0);
            int x1 = math.min(x + slopeRadius, width - 1);
            int z0 = math.max(z - slopeRadius, 0);
            int z1 = math.min(z + slopeRadius, height - 1);

            float wideDx = (finalHeights[x1 * height + z] - finalHeights[x0 * height + z]) /
                           math.max(1f, x1 - x0);
            float wideDz = (finalHeights[x * height + z1] - finalHeights[x * height + z0]) /
                           math.max(1f, z1 - z0);

            slopes[index] = TerrainSlopePolicy.FromGradient(math.sqrt(wideDx * wideDx + wideDz * wideDz), heightMultiplier);
        }
    }
}
