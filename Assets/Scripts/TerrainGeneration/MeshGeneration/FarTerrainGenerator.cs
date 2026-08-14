using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public static class FarTerrainGenerator
{
    public static FarTerrainRequestResult Generate(
        ChunkCoord chunkCoord,
        int requestVersion,
        int chunkSize,
        int seed,
        float sampleScale,
        float meshHeightMultiplier,
        float worldScale,
        int heightGridResolution,
        int controlMapResolution,
        float skirtDepth,
        bool isMacroTile = false)
    {
        long totalStart = TerrainGenerationProfiler.GetTimestamp();
        int safeHeightGridResolution = Mathf.Clamp(heightGridResolution, 2, chunkSize + 1);
        int safeControlMapResolution = Mathf.Clamp(controlMapResolution, 2, 128);

        TerrainHeightSamplingContext samplingContext = HeightMapGenerator.CreateSamplingContext(seed);

        long stageStart = TerrainGenerationProfiler.GetTimestamp();
        float[,] heightGrid = BuildHeightGrid(
            chunkCoord,
            chunkSize,
            sampleScale,
            safeHeightGridResolution,
            samplingContext,
            out float[,] mountainMaskGrid,
            out float[,] riverMaskGrid);
        TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.FarHeightGrid, stageStart);

        stageStart = TerrainGenerationProfiler.GetTimestamp();
        float[,] slopeGrid = BuildSlopeGrid(heightGrid, chunkSize);
        TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.FarSlopeGrid, stageStart);

        stageStart = TerrainGenerationProfiler.GetTimestamp();
        SurfaceType[,] meshSurfaceMap = BuildSurfaceMap(heightGrid, slopeGrid, mountainMaskGrid, riverMaskGrid);
        TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.FarSurfaceMap, stageStart);

        stageStart = TerrainGenerationProfiler.GetTimestamp();
        MeshData meshData = BuildMesh(
            chunkSize,
            worldScale,
            meshHeightMultiplier,
            heightGrid,
            meshSurfaceMap,
            Mathf.Max(0f, skirtDepth));
        TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.FarMeshBuild, stageStart);

        stageStart = TerrainGenerationProfiler.GetTimestamp();
        ControlMapPixelData controlMaps = BuildControlMaps(
            safeControlMapResolution,
            heightGrid,
            slopeGrid,
            mountainMaskGrid,
            riverMaskGrid);
        TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.FarControlMapBuild, stageStart);
        TerrainGenerationProfiler.Record(TerrainGenerationProfileStage.FarTerrainTotal, totalStart);

        return new FarTerrainRequestResult(chunkCoord, requestVersion, isMacroTile, meshData, controlMaps);
    }

    private static float[,] BuildHeightGrid(
        ChunkCoord chunkCoord,
        int chunkSize,
        float sampleScale,
        int resolution,
        TerrainHeightSamplingContext samplingContext,
        out float[,] mountainMaskGrid,
        out float[,] riverMaskGrid)
    {
        float[,] heightGrid = new float[resolution, resolution];
        mountainMaskGrid = new float[resolution, resolution];
        riverMaskGrid = new float[resolution, resolution];

        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float tx = resolution == 1 ? 0f : x / (float)(resolution - 1);
                float tz = resolution == 1 ? 0f : z / (float)(resolution - 1);

                float localSampleX = tx * chunkSize;
                float localSampleZ = tz * chunkSize;
                float worldX = chunkCoord.x * chunkSize + localSampleX;
                float worldZ = chunkCoord.z * chunkSize + localSampleZ;

                TerrainHeightSample sample = HeightMapGenerator.SampleTerrainHeight(
                    worldX,
                    worldZ,
                    sampleScale,
                    samplingContext);

                heightGrid[x, z] = sample.Height;
                mountainMaskGrid[x, z] = sample.MountainMask;
                riverMaskGrid[x, z] = sample.RiverMask;
            }
        }

        return heightGrid;
    }

    private static float[,] BuildSlopeGrid(float[,] heightGrid, int chunkSize)
    {
        int resolution = heightGrid.GetLength(0);
        float[,] slopeGrid = new float[resolution, resolution];
        float sampleSpacing = resolution <= 1 ? chunkSize : chunkSize / (float)(resolution - 1);

        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                int x0 = Mathf.Max(x - 1, 0);
                int x1 = Mathf.Min(x + 1, resolution - 1);
                int z0 = Mathf.Max(z - 1, 0);
                int z1 = Mathf.Min(z + 1, resolution - 1);

                float dx = (heightGrid[x1, z] - heightGrid[x0, z]) / Mathf.Max(sampleSpacing, 0.0001f);
                float dz = (heightGrid[x, z1] - heightGrid[x, z0]) / Mathf.Max(sampleSpacing, 0.0001f);

                slopeGrid[x, z] = Mathf.Sqrt(dx * dx + dz * dz);
            }
        }

        return slopeGrid;
    }

    private static SurfaceType[,] BuildSurfaceMap(
        float[,] heightGrid,
        float[,] slopeGrid,
        float[,] mountainMaskGrid,
        float[,] riverMaskGrid)
    {
        int resolution = heightGrid.GetLength(0);
        SurfaceType[,] surfaceMap = new SurfaceType[resolution, resolution];

        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                BiomeType biome = ClassifyCrudeBiome(
                    heightGrid[x, z],
                    slopeGrid[x, z],
                    mountainMaskGrid[x, z],
                    riverMaskGrid[x, z]);

                surfaceMap[x, z] = SurfaceTypeClassifier.Classify(
                    heightGrid[x, z],
                    slopeGrid[x, z],
                    riverMaskGrid[x, z],
                    biome);
            }
        }

        return surfaceMap;
    }

    private static MeshData BuildMesh(
        int chunkSize,
        float worldScale,
        float meshHeightMultiplier,
        float[,] heightGrid,
        SurfaceType[,] surfaceMap,
        float skirtDepth)
    {
        int resolution = heightGrid.GetLength(0);
        int mainVertexCount = resolution * resolution;
        int skirtVertexEstimate = skirtDepth > 0f ? resolution * 8 : 0;
        MeshData meshData = new MeshData(mainVertexCount + skirtVertexEstimate);
        int[,] vertexIndices = new int[resolution, resolution];

        for (int z = 0; z < resolution; z++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float tx = resolution == 1 ? 0f : x / (float)(resolution - 1);
                float tz = resolution == 1 ? 0f : z / (float)(resolution - 1);

                Vector3 vertex = new Vector3(
                    Mathf.Lerp(chunkSize / -2f, chunkSize / 2f, tx) * worldScale,
                    heightGrid[x, z] * meshHeightMultiplier * worldScale,
                    Mathf.Lerp(chunkSize / -2f, chunkSize / 2f, tz) * worldScale);

                Vector3 normal = CalculateNormal(heightGrid, x, z, chunkSize, meshHeightMultiplier);
                Color color = SurfaceTypeClassifier.GenerateColor(surfaceMap[x, z], WaterState.Dry);

                vertexIndices[x, z] = meshData.AddVertex(vertex, normal, new Vector2(tx, tz), color);
            }
        }

        for (int z = 0; z < resolution - 1; z++)
        {
            for (int x = 0; x < resolution - 1; x++)
            {
                int a = vertexIndices[x, z];
                int b = vertexIndices[x, z + 1];
                int c = vertexIndices[x + 1, z + 1];
                int d = vertexIndices[x + 1, z];

                meshData.AddTriangle(a, b, c);
                meshData.AddTriangle(a, c, d);
            }
        }

        if (skirtDepth > 0f)
            AddSkirts(meshData, vertexIndices, heightGrid, surfaceMap, skirtDepth);

        return meshData;
    }

    private static void AddSkirts(
        MeshData meshData,
        int[,] vertexIndices,
        float[,] heightGrid,
        SurfaceType[,] surfaceMap,
        float skirtDepth)
    {
        int resolution = heightGrid.GetLength(0);

        for (int x = 0; x < resolution - 1; x++)
        {
            AddSkirtSegment(meshData, vertexIndices[x, 0], vertexIndices[x + 1, 0], surfaceMap[x, 0], skirtDepth);
            AddSkirtSegment(meshData, vertexIndices[x + 1, resolution - 1], vertexIndices[x, resolution - 1], surfaceMap[x, resolution - 1], skirtDepth);
        }

        for (int z = 0; z < resolution - 1; z++)
        {
            AddSkirtSegment(meshData, vertexIndices[0, z + 1], vertexIndices[0, z], surfaceMap[0, z], skirtDepth);
            AddSkirtSegment(meshData, vertexIndices[resolution - 1, z], vertexIndices[resolution - 1, z + 1], surfaceMap[resolution - 1, z], skirtDepth);
        }
    }

    private static void AddSkirtSegment(
        MeshData meshData,
        int topAIndex,
        int topBIndex,
        SurfaceType surfaceType,
        float skirtDepth)
    {
        Vector3 topA = meshData.GetVertex(topAIndex);
        Vector3 topB = meshData.GetVertex(topBIndex);
        Color color = SurfaceTypeClassifier.GenerateColor(surfaceType, WaterState.Dry);

        int bottomAIndex = meshData.AddVertex(
            new Vector3(topA.x, topA.y - skirtDepth, topA.z),
            Vector3.up,
            Vector2.zero,
            color);

        int bottomBIndex = meshData.AddVertex(
            new Vector3(topB.x, topB.y - skirtDepth, topB.z),
            Vector3.up,
            Vector2.zero,
            color);

        meshData.AddTriangle(topAIndex, bottomAIndex, bottomBIndex);
        meshData.AddTriangle(topAIndex, bottomBIndex, topBIndex);
    }

    private static ControlMapPixelData BuildControlMaps(
        int resolution,
        float[,] heightGrid,
        float[,] slopeGrid,
        float[,] mountainMaskGrid,
        float[,] riverMaskGrid)
    {
        ControlMapPixelData controlMaps = new ControlMapPixelData(resolution, resolution, 3);
        int sourceResolution = heightGrid.GetLength(0);
        int pixelCount = resolution * resolution;

        NativeArray<float> heightSamples = default;
        NativeArray<float> slopeSamples = default;
        NativeArray<float> mountainMaskSamples = default;
        NativeArray<float> riverMaskSamples = default;
        NativeArray<Color32> controlMap0 = default;
        NativeArray<Color32> controlMap1 = default;
        NativeArray<Color32> controlMap2 = default;

        try
        {
            heightSamples = CopyGridToNative(heightGrid);
            slopeSamples = CopyGridToNative(slopeGrid);
            mountainMaskSamples = CopyGridToNative(mountainMaskGrid);
            riverMaskSamples = CopyGridToNative(riverMaskGrid);
            controlMap0 = new NativeArray<Color32>(pixelCount, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            controlMap1 = new NativeArray<Color32>(pixelCount, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            controlMap2 = new NativeArray<Color32>(pixelCount, Allocator.TempJob, NativeArrayOptions.ClearMemory);

            FarControlMapBuildJob job = new FarControlMapBuildJob
            {
                sourceResolution = sourceResolution,
                controlMapResolution = resolution,
                heightSamples = heightSamples,
                slopeSamples = slopeSamples,
                mountainMaskSamples = mountainMaskSamples,
                riverMaskSamples = riverMaskSamples,
                controlMap0 = controlMap0,
                controlMap1 = controlMap1,
                controlMap2 = controlMap2
            };

            JobHandle handle = job.Schedule(pixelCount, 64);
            handle.Complete();

            controlMap0.CopyTo(controlMaps.Maps[0]);
            controlMap1.CopyTo(controlMaps.Maps[1]);
            controlMap2.CopyTo(controlMaps.Maps[2]);
        }
        finally
        {
            if (heightSamples.IsCreated)
                heightSamples.Dispose();
            if (slopeSamples.IsCreated)
                slopeSamples.Dispose();
            if (mountainMaskSamples.IsCreated)
                mountainMaskSamples.Dispose();
            if (riverMaskSamples.IsCreated)
                riverMaskSamples.Dispose();
            if (controlMap0.IsCreated)
                controlMap0.Dispose();
            if (controlMap1.IsCreated)
                controlMap1.Dispose();
            if (controlMap2.IsCreated)
                controlMap2.Dispose();
        }

        return controlMaps;
    }

    private static NativeArray<float> CopyGridToNative(float[,] grid)
    {
        int width = grid.GetLength(0);
        int height = grid.GetLength(1);
        NativeArray<float> nativeGrid =
            new NativeArray<float>(width * height, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        for (int x = 0; x < width; x++)
        {
            int rowOffset = x * height;

            for (int z = 0; z < height; z++)
            {
                nativeGrid[rowOffset + z] = grid[x, z];
            }
        }

        return nativeGrid;
    }

    [BurstCompile]
    private struct FarControlMapBuildJob : IJobParallelFor
    {
        public int sourceResolution;
        public int controlMapResolution;

        [ReadOnly] public NativeArray<float> heightSamples;
        [ReadOnly] public NativeArray<float> slopeSamples;
        [ReadOnly] public NativeArray<float> mountainMaskSamples;
        [ReadOnly] public NativeArray<float> riverMaskSamples;

        [WriteOnly] public NativeArray<Color32> controlMap0;
        [WriteOnly] public NativeArray<Color32> controlMap1;
        [WriteOnly] public NativeArray<Color32> controlMap2;

        public void Execute(int pixelIndex)
        {
            int z = pixelIndex / controlMapResolution;
            int x = pixelIndex - z * controlMapResolution;
            float tx = controlMapResolution == 1 ? 0f : x / (float)(controlMapResolution - 1);
            float tz = controlMapResolution == 1 ? 0f : z / (float)(controlMapResolution - 1);

            float height = SampleGrid(heightSamples, sourceResolution, tx, tz);
            float slope = SampleGrid(slopeSamples, sourceResolution, tx, tz);
            float mountainMask = SampleGrid(mountainMaskSamples, sourceResolution, tx, tz);
            float riverMask = SampleGrid(riverMaskSamples, sourceResolution, tx, tz);

            BiomeType biome = ClassifyCrudeBiome(height, slope, mountainMask, riverMask);
            SurfaceType surfaceType = ClassifySurface(height, slope, riverMask, biome);
            Color32 surfaceColor = SurfaceTypeToControlColor(surfaceType);

            if (UsesFirstControlMap(surfaceType))
                controlMap0[pixelIndex] = surfaceColor;
            else
                controlMap1[pixelIndex] = surfaceColor;

            controlMap2[pixelIndex] = new Color32(0, 0, 0, 0);
        }

        private static float SampleGrid(NativeArray<float> grid, int resolution, float tx, float tz)
        {
            float sourceX = tx * (resolution - 1);
            float sourceZ = tz * (resolution - 1);
            int x0 = (int)math.floor(sourceX);
            int z0 = (int)math.floor(sourceZ);
            int x1 = math.min(x0 + 1, resolution - 1);
            int z1 = math.min(z0 + 1, resolution - 1);
            float fx = sourceX - x0;
            float fz = sourceZ - z0;

            float a = grid[x0 * resolution + z0];
            float b = grid[x1 * resolution + z0];
            float c = grid[x0 * resolution + z1];
            float d = grid[x1 * resolution + z1];
            return math.lerp(math.lerp(a, b, fx), math.lerp(c, d, fx), fz);
        }

        private static BiomeType ClassifyCrudeBiome(float height, float slope, float mountainMask, float riverMask)
        {
            const float waterLevel = TerrainWaterSettings.WaterLevel;
            const float beachLevel = TerrainWaterSettings.BeachLevel;

            if (height < waterLevel || riverMask >= 0.82f)
                return BiomeType.Water;

            if (height < beachLevel)
                return BiomeType.Beach;

            if (mountainMask > 0.46f)
            {
                if (height > 5.5f && slope < 0.12f)
                    return BiomeType.Snow;

                return BiomeType.Rock;
            }

            if (mountainMask > 0.32f || slope > 0.08f)
                return BiomeType.Rock;

            return BiomeType.Grassland;
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
            {
                if (slope >= rockSlopeThreshold)
                    return SurfaceType.Rock;

                return SurfaceType.Mud;
            }

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

        private static bool UsesFirstControlMap(SurfaceType surfaceType)
        {
            switch (surfaceType)
            {
                case SurfaceType.Sand:
                case SurfaceType.Mud:
                case SurfaceType.Grass:
                case SurfaceType.Rock:
                    return true;
                case SurfaceType.Snow:
                case SurfaceType.Cliff:
                case SurfaceType.Riverbed:
                    return false;
                default:
                    return true;
            }
        }

        private static Color32 SurfaceTypeToControlColor(SurfaceType surfaceType)
        {
            const byte value = 255;

            switch (surfaceType)
            {
                case SurfaceType.Sand:
                    return new Color32(value, 0, 0, 0);
                case SurfaceType.Mud:
                    return new Color32(0, value, 0, 0);
                case SurfaceType.Grass:
                    return new Color32(0, 0, value, 0);
                case SurfaceType.Rock:
                    return new Color32(0, 0, 0, value);
                case SurfaceType.Snow:
                    return new Color32(value, 0, 0, 0);
                case SurfaceType.Cliff:
                    return new Color32(0, value, 0, 0);
                case SurfaceType.Riverbed:
                    return new Color32(0, 0, value, 0);
                default:
                    return new Color32(0, 0, 0, 0);
            }
        }
    }

    private static Vector3 CalculateNormal(float[,] heightGrid, int x, int z, int chunkSize, float meshHeightMultiplier)
    {
        int resolution = heightGrid.GetLength(0);
        float sampleSpacing = resolution <= 1 ? chunkSize : chunkSize / (float)(resolution - 1);

        float left = heightGrid[Mathf.Max(x - 1, 0), z];
        float right = heightGrid[Mathf.Min(x + 1, resolution - 1), z];
        float down = heightGrid[x, Mathf.Max(z - 1, 0)];
        float up = heightGrid[x, Mathf.Min(z + 1, resolution - 1)];

        float dx = (right - left) * meshHeightMultiplier / Mathf.Max(sampleSpacing, 0.0001f);
        float dz = (up - down) * meshHeightMultiplier / Mathf.Max(sampleSpacing, 0.0001f);

        return new Vector3(-dx, 2f, -dz).normalized;
    }

    private static BiomeType ClassifyCrudeBiome(float height, float slope, float mountainMask, float riverMask)
    {
        if (height < TerrainWaterSettings.WaterLevel || riverMask >= 0.82f)
            return BiomeType.Water;

        if (height < TerrainWaterSettings.BeachLevel)
            return BiomeType.Beach;

        if (mountainMask > 0.46f)
        {
            if (height > 5.5f && slope < 0.12f)
                return BiomeType.Snow;

            return BiomeType.Rock;
        }

        if (mountainMask > 0.32f || slope > 0.08f)
            return BiomeType.Rock;

        return BiomeType.Grassland;
    }
}
