using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

public static class BiomeMapGenerator
{
    public static BiomeType[,] GenerateBiomeMap(float[,] heightMap, float[,] moistureMap, float[,] temperatureMap, 
        float[,] slopeMap, float[,] mountainMaskMap, float[,] riverMaskMap, float waterLevel)
    {
        NativeArray<float> heights = default;
        NativeArray<float> moistures = default;
        NativeArray<float> temperatures = default;
        NativeArray<float> slopes = default;
        NativeArray<float> mountainMasks = default;
        NativeArray<float> riverMasks = default;
        NativeArray<BiomeType> biomes = default;

        int width = heightMap.GetLength(0);
        int height = heightMap.GetLength(1);
        BiomeType[,] biomeMap = new BiomeType[width, height];

        try
        {
            heights = CopyFloatMapToNative(heightMap, Allocator.TempJob, out width, out height);
            moistures = CopyFloatMapToNative(moistureMap, Allocator.TempJob, out _, out _);
            temperatures = CopyFloatMapToNative(temperatureMap, Allocator.TempJob, out _, out _);
            slopes = CopyFloatMapToNative(slopeMap, Allocator.TempJob, out _, out _);
            mountainMasks = CopyFloatMapToNative(mountainMaskMap, Allocator.TempJob, out _, out _);
            riverMasks = CopyFloatMapToNative(riverMaskMap, Allocator.TempJob, out _, out _);
            biomes = new NativeArray<BiomeType>(width * height, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            BiomeMapJob job = new BiomeMapJob
            {
                waterLevel = waterLevel,
                heights = heights,
                moistures = moistures,
                temperatures = temperatures,
                slopes = slopes,
                mountainMasks = mountainMasks,
                riverMasks = riverMasks,
                biomes = biomes
            };

            JobHandle handle = job.Schedule(biomes.Length, 64);
            handle.Complete();

            CopyNativeToMap(biomes, biomeMap);
        }
        finally
        {
            if (heights.IsCreated)
                heights.Dispose();
            if (moistures.IsCreated)
                moistures.Dispose();
            if (temperatures.IsCreated)
                temperatures.Dispose();
            if (slopes.IsCreated)
                slopes.Dispose();
            if (mountainMasks.IsCreated)
                mountainMasks.Dispose();
            if (riverMasks.IsCreated)
                riverMasks.Dispose();
            if (biomes.IsCreated)
                biomes.Dispose();
        }

        return biomeMap;
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

    private static void CopyNativeToMap(NativeArray<BiomeType> source, BiomeType[,] target)
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
    private struct BiomeMapJob : IJobParallelFor
    {
        public float waterLevel;
        [ReadOnly] public NativeArray<float> heights;
        [ReadOnly] public NativeArray<float> moistures;
        [ReadOnly] public NativeArray<float> temperatures;
        [ReadOnly] public NativeArray<float> slopes;
        [ReadOnly] public NativeArray<float> mountainMasks;
        [ReadOnly] public NativeArray<float> riverMasks;

        [WriteOnly] public NativeArray<BiomeType> biomes;

        public void Execute(int index)
        {
            float biomeSlope = slopes[index];
            float terrainHeight = heights[index];
            float moisture = moistures[index];
            float temperature = temperatures[index];
            float mountainMask = mountainMasks[index];
            float riverMask = riverMasks[index];

            biomes[index] = ClassifyBiome(terrainHeight, moisture, temperature, biomeSlope, mountainMask, riverMask, waterLevel);
        }

        private static BiomeType ClassifyBiome(
            float terrainHeight,
            float moisture,
            float temperature,
            float slope,
            float mountainMask,
            float riverMask,
            float waterLevel)
        {
            return TerrainSlopePolicy.ClassifyBiome(terrainHeight, moisture, temperature, slope, mountainMask, riverMask, waterLevel);
        }

        private static float InverseLerp(float a, float b, float value)
        {
            return math.clamp((value - a) / (b - a), 0f, 1f);
        }
    }
}
