using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

public static class WaterStateMapGenerator
{
    public static WaterState[,] GenerateWaterStateMap(float[,] heightMap, float[,] riverMaskMap, float waterLevel)
    {
        NativeArray<float> heights = default;
        NativeArray<float> riverMasks = default;
        NativeArray<WaterState> waterStates = default;

        int width = heightMap.GetLength(0);
        int height = heightMap.GetLength(1);
        WaterState[,] map = new WaterState[width, height];

        try
        {
            heights = CopyFloatMapToNative(heightMap, Allocator.TempJob, out width, out height);
            riverMasks = CopyFloatMapToNative(riverMaskMap, Allocator.TempJob, out _, out _);
            waterStates = new NativeArray<WaterState>(width * height, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            WaterStateMapJob job = new WaterStateMapJob
            {
                waterLevel = waterLevel,
                heights = heights,
                riverMasks = riverMasks,
                waterStates = waterStates
            };

            JobHandle handle = job.Schedule(waterStates.Length, 64);
            handle.Complete();

            CopyNativeToMap(waterStates, map);
        }
        finally
        {
            if (heights.IsCreated)
                heights.Dispose();
            if (riverMasks.IsCreated)
                riverMasks.Dispose();
            if (waterStates.IsCreated)
                waterStates.Dispose();
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

    private static void CopyNativeToMap(NativeArray<WaterState> source, WaterState[,] target)
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
    private struct WaterStateMapJob : IJobParallelFor
    {
        public float waterLevel;
        [ReadOnly] public NativeArray<float> heights;
        [ReadOnly] public NativeArray<float> riverMasks;

        [WriteOnly] public NativeArray<WaterState> waterStates;

        public void Execute(int index)
        {
            waterStates[index] = WaterStateClassifier.Classify(heights[index], riverMasks[index], waterLevel);
        }

    }
}
