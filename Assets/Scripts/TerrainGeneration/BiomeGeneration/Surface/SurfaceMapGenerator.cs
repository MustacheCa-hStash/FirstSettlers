using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

public static class SurfaceMapGenerator
{
    public static SurfaceType[,] GenerateSurfaceTypeMap(float[,] heightMap, float[,] slopeMap, float[,] riverMaskMap, 
        BiomeType[,] biomeMap, float waterLevel)
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
                width = width,
                height = height,
                waterLevel = waterLevel,
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
        public int width;
        public int height;
        public float waterLevel;
        [ReadOnly] public NativeArray<float> heights;
        [ReadOnly] public NativeArray<float> slopes;
        [ReadOnly] public NativeArray<float> riverMasks;
        [ReadOnly] public NativeArray<BiomeType> biomes;

        [WriteOnly] public NativeArray<SurfaceType> surfaces;

        public void Execute(int index)
        {
            SurfaceType surface = SurfaceTypeClassifier.Classify(heights[index], slopes[index], riverMasks[index], biomes[index], waterLevel);
            surfaces[index] = surface;
            if (surface != SurfaceType.Grass)
                return;

            // Placement rounds to a sample but interpolates height. Keep its surrounding cell dry too.
            int x = index / height;
            int z = index % height;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    int nx = x + dx;
                    int nz = z + dz;
                    if (nx >= 0 && nx < width && nz >= 0 && nz < height && heights[nx * height + nz] <= waterLevel)
                    {
                        surfaces[index] = SurfaceType.Mud;
                        return;
                    }
                }
            }
        }

    }
}
