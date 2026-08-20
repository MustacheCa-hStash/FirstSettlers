using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

public static class ClimateGenerator
{
    public static float[,] GenerateTerrainMoistureMap(int chunkSize, int seed, float sampleScale, int octaves, float persistence,
        float lacunarity, ChunkCoord chunkCoord)
    {
        return GenerateTerrainClimateMap(chunkSize, seed + 1000, sampleScale * 10f, octaves, persistence, lacunarity, chunkCoord);
    }

    public static float[,] GenerateTerrainTemperatureMap(int chunkSize, int seed, float sampleScale, int octaves, float persistence,
        float lacunarity, ChunkCoord chunkCoord)
    {
        return GenerateTerrainClimateMap(chunkSize, seed + 2000, sampleScale * 12f, octaves, persistence, lacunarity, chunkCoord);
    }

    private static float[,] GenerateTerrainClimateMap(int chunkSize, int seed, float sampleScale, int octaves, float persistence,
        float lacunarity, ChunkCoord chunkCoord)
    {
        float noiseSampleScale = sampleScale;
        int noiseOctaves = System.Math.Max(1, octaves - 2);
        float noisePersistence = persistence;
        float noiseLacunarity = lacunarity;

        int size = chunkSize + 3;
        float[,] terrainNoiseMap = new float[size, size];

        float maxPossibleNoise = 0f;
        float amplitude = 1f;

        for (int i = 0; i < noiseOctaves; i++)
        {
            maxPossibleNoise += amplitude;
            amplitude *= noisePersistence;
        }

        if (noiseSampleScale <= 0f)
            noiseSampleScale = 0.0001f;

        if (noiseLacunarity < 1f)
            noiseLacunarity = 1f;

        NativeArray<float2> octaveOffsets = default;
        NativeArray<float> samples = default;

        try
        {
            octaveOffsets = CreateOctaveOffsets(seed, noiseOctaves);
            samples = new NativeArray<float>(size * size, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            ClimateMapJob job = new ClimateMapJob
            {
                size = size,
                chunkSize = chunkSize,
                chunkX = chunkCoord.x,
                chunkZ = chunkCoord.z,
                seed = seed,
                sampleScale = noiseSampleScale,
                persistence = noisePersistence,
                lacunarity = noiseLacunarity,
                maxPossibleNoise = maxPossibleNoise,
                octaveOffsets = octaveOffsets,
                samples = samples
            };

            JobHandle handle = job.Schedule(samples.Length, 64);
            handle.Complete();

            CopyNativeToMap(samples, terrainNoiseMap);
        }
        finally
        {
            if (octaveOffsets.IsCreated)
                octaveOffsets.Dispose();
            if (samples.IsCreated)
                samples.Dispose();
        }

        return terrainNoiseMap;
    }

    private static NativeArray<float2> CreateOctaveOffsets(int seed, int octaves)
    {
        NativeArray<float2> octaveOffsets =
            new NativeArray<float2>(octaves, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        System.Random prng = new System.Random(seed);

        for (int i = 0; i < octaves; i++)
        {
            float offsetX = prng.Next(-100000, 100000);
            float offsetZ = prng.Next(-100000, 100000);
            octaveOffsets[i] = new float2(offsetX, offsetZ);
        }

        return octaveOffsets;
    }

    private static void CopyNativeToMap(NativeArray<float> source, float[,] target)
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
    private struct ClimateMapJob : IJobParallelFor
    {
        public int size;
        public int chunkSize;
        public int chunkX;
        public int chunkZ;
        public int seed;
        public float sampleScale;
        public float persistence;
        public float lacunarity;
        public float maxPossibleNoise;

        [ReadOnly] public NativeArray<float2> octaveOffsets;
        [WriteOnly] public NativeArray<float> samples;

        public void Execute(int index)
        {
            int x = index / size;
            int z = index - x * size;

            int localSampleX = x - 1;
            int localSampleZ = z - 1;

            float worldX = chunkX * chunkSize + localSampleX;
            float worldZ = chunkZ * chunkSize + localSampleZ;

            float amplitude = 1f;
            float frequency = 1f;
            float noise = 0f;

            for (int o = 0; o < octaveOffsets.Length; o++)
            {
                float2 offset = octaveOffsets[o];
                float sampleX = (worldX / sampleScale + offset.x) * frequency;
                float sampleZ = (worldZ / sampleScale + offset.y) * frequency;

                float value = SampleValueNoise(sampleX, sampleZ, seed + o * 1009);

                noise += value * amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            samples[index] = Normalize01(noise, maxPossibleNoise);
        }

        private static float Normalize01(float raw, float maxPossible)
        {
            return math.clamp((raw + maxPossible) / (2f * maxPossible), 0f, 1f);
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
