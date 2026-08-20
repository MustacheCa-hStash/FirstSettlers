using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

public static class SurfaceMapGenerator
{
    public static SurfaceType[,] GenerateSurfaceTypeMap(float[,] heightMap, float[,] slopeMap, float[,] riverMaskMap, 
        BiomeType[,] biomeMap)
    {
        NativeArray<float> heights = default;
        NativeArray<float> slopes = default;
        NativeArray<float> riverMasks = default;
        NativeArray<BiomeType> biomes = default;
        NativeArray<SurfaceType> surfaces = default;

        int width = heightMap.GetLength(0);
        int height = heightMap.GetLength(1);
        SurfaceType[,] map = new SurfaceType[width, height];

        try
        {
            heights = CopyFloatMapToNative(heightMap, Allocator.TempJob, out width, out height);
            slopes = CopyFloatMapToNative(slopeMap, Allocator.TempJob, out _, out _);
            riverMasks = CopyFloatMapToNative(riverMaskMap, Allocator.TempJob, out _, out _);
            biomes = CopyMapToNative(biomeMap, Allocator.TempJob, out _, out _);
            surfaces = new NativeArray<SurfaceType>(width * height, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            SurfaceMapJob job = new SurfaceMapJob
            {
                heights = heights,
                slopes = slopes,
                riverMasks = riverMasks,
                biomes = biomes,
                surfaces = surfaces
            };

            JobHandle handle = job.Schedule(surfaces.Length, 64);
            handle.Complete();

            CopyNativeToMap(surfaces, map);
        }
        finally
        {
            if (heights.IsCreated)
                heights.Dispose();
            if (slopes.IsCreated)
                slopes.Dispose();
            if (riverMasks.IsCreated)
                riverMasks.Dispose();
            if (biomes.IsCreated)
                biomes.Dispose();
            if (surfaces.IsCreated)
                surfaces.Dispose();
        }

        return map;
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

    private static void CopyNativeToMap(NativeArray<SurfaceType> source, SurfaceType[,] target)
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
    private struct SurfaceMapJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> heights;
        [ReadOnly] public NativeArray<float> slopes;
        [ReadOnly] public NativeArray<float> riverMasks;
        [ReadOnly] public NativeArray<BiomeType> biomes;

        [WriteOnly] public NativeArray<SurfaceType> surfaces;

        public void Execute(int index)
        {
            surfaces[index] = ClassifySurface(heights[index], slopes[index], riverMasks[index], biomes[index]);
        }

        private static SurfaceType ClassifySurface(float height, float slope, float riverMask, BiomeType biome)
        {
            const float oceanWaterLevel = TerrainWaterSettings.WaterLevel;
            const float beachBand = TerrainWaterSettings.BeachLevel - TerrainWaterSettings.WaterLevel;
            const float riverBankThreshold = 0.69f;
            const float riverCoreThreshold = 0.72f;
            const float cliffSlopeThreshold = 0.6f;
            const float rockSlopeThreshold = 0.42f;

            if (slope >= cliffSlopeThreshold)
                return SurfaceType.Cliff;

            if (height <= oceanWaterLevel + beachBand)
                return SurfaceType.Sand;

            if (biome == BiomeType.Rock)
                return SurfaceType.Rock;

            if (riverMask >= riverCoreThreshold)
                return SurfaceType.Riverbed;

            if (riverMask >= riverBankThreshold)
                return slope >= rockSlopeThreshold ? SurfaceType.Rock : SurfaceType.Mud;

            switch (biome)
            {
                case BiomeType.Beach:
                case BiomeType.Desert:
                    return SurfaceType.Sand;
                case BiomeType.Forest:
                case BiomeType.Grassland:
                    return SurfaceType.Grass;
                case BiomeType.Rock:
                    return SurfaceType.Rock;
                case BiomeType.Snow:
                    return SurfaceType.Snow;
                case BiomeType.Water:
                    return SurfaceType.Riverbed;
                default:
                    return SurfaceType.Grass;
            }
        }
    }
}
