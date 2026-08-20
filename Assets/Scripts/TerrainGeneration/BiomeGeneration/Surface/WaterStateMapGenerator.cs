using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

public static class WaterStateMapGenerator
{
    public static WaterState[,] GenerateWaterStateMap(float[,] heightMap, float[,] riverMaskMap)
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
        [ReadOnly] public NativeArray<float> heights;
        [ReadOnly] public NativeArray<float> riverMasks;

        [WriteOnly] public NativeArray<WaterState> waterStates;

        public void Execute(int index)
        {
            waterStates[index] = ClassifyWaterState(heights[index], riverMasks[index]);
        }

        private static WaterState ClassifyWaterState(float height, float riverMask)
        {
            const float oceanWaterLevel = TerrainWaterSettings.WaterLevel;
            const float oceanShallowBand = 0.015f;
            const float oceanWetBand = TerrainWaterSettings.WetBand;
            const float riverShallowThreshold = 0.40f;
            const float riverDeepThreshold = 0.65f;
            const float riverWetBand = 0.15f;

            if (height < oceanWaterLevel)
            {
                float oceanDepth = oceanWaterLevel - height;
                return oceanDepth > oceanShallowBand ? WaterState.Deep : WaterState.Shallow;
            }

            if (riverMask >= riverShallowThreshold)
            {
                float riverStrength = InverseLerp(riverShallowThreshold, 1f, riverMask);
                float riverSurfaceHeight = oceanWaterLevel + math.lerp(0.01f, 0.05f, riverStrength);

                if (height <= riverSurfaceHeight)
                    return riverMask >= riverDeepThreshold ? WaterState.Deep : WaterState.Shallow;

                if (height <= riverSurfaceHeight + riverWetBand)
                    return WaterState.Wet;
            }

            if (height <= oceanWaterLevel + oceanWetBand)
                return WaterState.Wet;

            return WaterState.Dry;
        }

        private static float InverseLerp(float a, float b, float value)
        {
            return math.clamp((value - a) / (b - a), 0f, 1f);
        }
    }
}
