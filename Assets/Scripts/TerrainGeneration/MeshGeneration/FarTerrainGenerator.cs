using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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
        float waterLevel,
        bool isMacroTile = false)
    {
        long totalStart = TerrainGenerationProfiler.GetTimestamp();
        int safeHeightGridResolution = Mathf.Clamp(heightGridResolution, 2, chunkSize + 1);
        int safeControlMapResolution = Mathf.Clamp(controlMapResolution, 2, 128);

        TerrainHeightSamplingContext samplingContext = HeightMapGenerator.CreateSamplingContext(seed, waterLevel);

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
        SurfaceType[,] meshSurfaceMap = BuildSurfaceMap(heightGrid, slopeGrid, mountainMaskGrid, riverMaskGrid, waterLevel);
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
            riverMaskGrid,
            waterLevel);
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
        int sampleCount = resolution * resolution;

        NativeArray<float> heights = default;
        NativeArray<float> mountainMasks = default;
        NativeArray<float> riverMasks = default;
        NativeArray<float2> baseLandOffsets = default;
        NativeArray<float2> mountainMaskOffsets = default;
        NativeArray<float2> mountainTerrainOffsets = default;
        NativeArray<float2> mountainRuggedOffsets = default;

        try
        {
            heights = new NativeArray<float>(sampleCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            mountainMasks = new NativeArray<float>(sampleCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            riverMasks = new NativeArray<float>(sampleCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            baseLandOffsets = CreateNativeOffsets(samplingContext.BaseLandOffsets);
            mountainMaskOffsets = CreateNativeOffsets(samplingContext.MountainMaskOffsets);
            mountainTerrainOffsets = CreateNativeOffsets(samplingContext.MountainTerrainOffsets);
            mountainRuggedOffsets = CreateNativeOffsets(samplingContext.MountainRuggedOffsets);

            FarHeightGridSampleJob job = new FarHeightGridSampleJob
            {
                resolution = resolution,
                chunkSize = chunkSize,
                chunkX = chunkCoord.x,
                chunkZ = chunkCoord.z,
                sampleScale = sampleScale,
                riverSeed = samplingContext.RiverSeed,
                waterLevel = samplingContext.WaterLevel,
                baseLandOffsets = baseLandOffsets,
                mountainMaskOffsets = mountainMaskOffsets,
                mountainTerrainOffsets = mountainTerrainOffsets,
                mountainRuggedOffsets = mountainRuggedOffsets,
                heights = heights,
                mountainMasks = mountainMasks,
                riverMasks = riverMasks
            };

            JobHandle handle = job.Schedule(sampleCount, 64);
            handle.Complete();

            CopyNativeGridToManaged(heights, heightGrid);
            CopyNativeGridToManaged(mountainMasks, mountainMaskGrid);
            CopyNativeGridToManaged(riverMasks, riverMaskGrid);
        }
        finally
        {
            if (heights.IsCreated)
                heights.Dispose();
            if (mountainMasks.IsCreated)
                mountainMasks.Dispose();
            if (riverMasks.IsCreated)
                riverMasks.Dispose();
            if (baseLandOffsets.IsCreated)
                baseLandOffsets.Dispose();
            if (mountainMaskOffsets.IsCreated)
                mountainMaskOffsets.Dispose();
            if (mountainTerrainOffsets.IsCreated)
                mountainTerrainOffsets.Dispose();
            if (mountainRuggedOffsets.IsCreated)
                mountainRuggedOffsets.Dispose();
        }

        return heightGrid;
    }

    private static float[,] BuildSlopeGrid(float[,] heightGrid, int chunkSize)
    {
        int resolution = heightGrid.GetLength(0);
        float[,] slopeGrid = new float[resolution, resolution];
        float sampleSpacing = resolution <= 1 ? chunkSize : chunkSize / (float)(resolution - 1);

        NativeArray<float> heightSamples = default;
        NativeArray<float> slopes = default;

        try
        {
            heightSamples = CopyGridToNative(heightGrid);
            slopes = new NativeArray<float>(heightSamples.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            FarSlopeGridJob job = new FarSlopeGridJob
            {
                resolution = resolution,
                sampleSpacing = sampleSpacing,
                heightSamples = heightSamples,
                slopes = slopes
            };

            JobHandle handle = job.Schedule(slopes.Length, 64);
            handle.Complete();

            CopyNativeGridToManaged(slopes, slopeGrid);
        }
        finally
        {
            if (heightSamples.IsCreated)
                heightSamples.Dispose();
            if (slopes.IsCreated)
                slopes.Dispose();
        }

        return slopeGrid;
    }

    private static SurfaceType[,] BuildSurfaceMap(
        float[,] heightGrid,
        float[,] slopeGrid,
        float[,] mountainMaskGrid,
        float[,] riverMaskGrid,
        float waterLevel)
    {
        int resolution = heightGrid.GetLength(0);
        SurfaceType[,] surfaceMap = new SurfaceType[resolution, resolution];

        NativeArray<float> heightSamples = default;
        NativeArray<float> slopeSamples = default;
        NativeArray<float> mountainMaskSamples = default;
        NativeArray<float> riverMaskSamples = default;
        NativeArray<SurfaceType> surfaces = default;

        try
        {
            heightSamples = CopyGridToNative(heightGrid);
            slopeSamples = CopyGridToNative(slopeGrid);
            mountainMaskSamples = CopyGridToNative(mountainMaskGrid);
            riverMaskSamples = CopyGridToNative(riverMaskGrid);
            surfaces = new NativeArray<SurfaceType>(heightSamples.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            FarSurfaceMapJob job = new FarSurfaceMapJob
            {
                waterLevel = waterLevel,
                heightSamples = heightSamples,
                slopeSamples = slopeSamples,
                mountainMaskSamples = mountainMaskSamples,
                riverMaskSamples = riverMaskSamples,
                surfaces = surfaces
            };

            JobHandle handle = job.Schedule(surfaces.Length, 64);
            handle.Complete();

            CopyNativeGridToManaged(surfaces, surfaceMap);
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
            if (surfaces.IsCreated)
                surfaces.Dispose();
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
        int mainTriangleCount = Mathf.Max(0, resolution - 1) * Mathf.Max(0, resolution - 1) * 6;

        NativeArray<float> heightSamples = default;
        NativeArray<SurfaceType> surfaceSamples = default;
        NativeArray<float3> vertices = default;
        NativeArray<float3> normals = default;
        NativeArray<float2> uvs = default;
        NativeArray<float4> colors = default;
        NativeArray<int> triangles = default;

        try
        {
            heightSamples = CopyGridToNative(heightGrid);
            surfaceSamples = CopyGridToNative(surfaceMap);
            vertices = new NativeArray<float3>(mainVertexCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            normals = new NativeArray<float3>(mainVertexCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            uvs = new NativeArray<float2>(mainVertexCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            colors = new NativeArray<float4>(mainVertexCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            triangles = new NativeArray<int>(mainTriangleCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            FarMeshVertexJob vertexJob = new FarMeshVertexJob
            {
                resolution = resolution,
                chunkSize = chunkSize,
                worldScale = worldScale,
                meshHeightMultiplier = meshHeightMultiplier,
                heightSamples = heightSamples,
                surfaceSamples = surfaceSamples,
                vertices = vertices,
                normals = normals,
                uvs = uvs,
                colors = colors
            };
            JobHandle vertexHandle = vertexJob.Schedule(mainVertexCount, 64);

            FarMeshTriangleJob triangleJob = new FarMeshTriangleJob
            {
                resolution = resolution,
                triangles = triangles
            };
            JobHandle triangleHandle = triangleJob.Schedule(mainTriangleCount / 6, 64);
            JobHandle.CombineDependencies(vertexHandle, triangleHandle).Complete();

            return CreateFarMeshData(
                vertices,
                normals,
                uvs,
                colors,
                triangles,
                heightGrid,
                surfaceMap,
                skirtDepth,
                mainVertexCount + skirtVertexEstimate);
        }
        finally
        {
            if (heightSamples.IsCreated)
                heightSamples.Dispose();
            if (surfaceSamples.IsCreated)
                surfaceSamples.Dispose();
            if (vertices.IsCreated)
                vertices.Dispose();
            if (normals.IsCreated)
                normals.Dispose();
            if (uvs.IsCreated)
                uvs.Dispose();
            if (colors.IsCreated)
                colors.Dispose();
            if (triangles.IsCreated)
                triangles.Dispose();
        }
    }

    private static MeshData CreateFarMeshData(
        NativeArray<float3> nativeVertices,
        NativeArray<float3> nativeNormals,
        NativeArray<float2> nativeUvs,
        NativeArray<float4> nativeColors,
        NativeArray<int> nativeTriangles,
        float[,] heightGrid,
        SurfaceType[,] surfaceMap,
        float skirtDepth,
        int vertexCapacity)
    {
        int resolution = heightGrid.GetLength(0);
        MeshData meshData = new MeshData(vertexCapacity);
        int[,] vertexIndices = new int[resolution, resolution];

        for (int x = 0; x < resolution; x++)
        {
            for (int z = 0; z < resolution; z++)
            {
                int index = x * resolution + z;
                float3 vertex = nativeVertices[index];
                float3 normal = nativeNormals[index];
                float2 uv = nativeUvs[index];
                float4 color = nativeColors[index];

                vertexIndices[x, z] = meshData.AddVertex(
                    new Vector3(vertex.x, vertex.y, vertex.z),
                    new Vector3(normal.x, normal.y, normal.z),
                    new Vector2(uv.x, uv.y),
                    new Color(color.x, color.y, color.z, color.w));
            }
        }

        for (int i = 0; i < nativeTriangles.Length; i += 3)
        {
            meshData.AddTriangle(nativeTriangles[i], nativeTriangles[i + 1], nativeTriangles[i + 2]);
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
        float[,] riverMaskGrid,
        float waterLevel)
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
                waterLevel = waterLevel,
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

    private static NativeArray<T> CopyGridToNative<T>(T[,] grid)
        where T : unmanaged
    {
        int width = grid.GetLength(0);
        int height = grid.GetLength(1);
        NativeArray<T> nativeGrid =
            new NativeArray<T>(width * height, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

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

    private static void CopyNativeGridToManaged(NativeArray<float> source, float[,] target)
    {
        int width = target.GetLength(0);
        int height = target.GetLength(1);

        for (int x = 0; x < width; x++)
        {
            int rowOffset = x * height;

            for (int z = 0; z < height; z++)
            {
                target[x, z] = source[rowOffset + z];
            }
        }
    }

    private static void CopyNativeGridToManaged<T>(NativeArray<T> source, T[,] target)
        where T : unmanaged
    {
        int width = target.GetLength(0);
        int height = target.GetLength(1);

        for (int x = 0; x < width; x++)
        {
            int rowOffset = x * height;

            for (int z = 0; z < height; z++)
            {
                target[x, z] = source[rowOffset + z];
            }
        }
    }

    private static NativeArray<float2> CreateNativeOffsets(Vector2[] source)
    {
        NativeArray<float2> result =
            new NativeArray<float2>(source.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        for (int i = 0; i < source.Length; i++)
            result[i] = new float2(source[i].x, source[i].y);

        return result;
    }

    [BurstCompile]
    private struct FarHeightGridSampleJob : IJobParallelFor
    {
        public int resolution;
        public int chunkSize;
        public int chunkX;
        public int chunkZ;
        public float sampleScale;
        public int riverSeed;
        public float waterLevel;

        [ReadOnly] public NativeArray<float2> baseLandOffsets;
        [ReadOnly] public NativeArray<float2> mountainMaskOffsets;
        [ReadOnly] public NativeArray<float2> mountainTerrainOffsets;
        [ReadOnly] public NativeArray<float2> mountainRuggedOffsets;

        [WriteOnly] public NativeArray<float> heights;
        [WriteOnly] public NativeArray<float> mountainMasks;
        [WriteOnly] public NativeArray<float> riverMasks;

        public void Execute(int index)
        {
            int x = index / resolution;
            int z = index - x * resolution;
            float tx = resolution == 1 ? 0f : x / (float)(resolution - 1);
            float tz = resolution == 1 ? 0f : z / (float)(resolution - 1);

            float localSampleX = tx * chunkSize;
            float localSampleZ = tz * chunkSize;
            float worldX = chunkX * chunkSize + localSampleX;
            float worldZ = chunkZ * chunkSize + localSampleZ;

            TerrainHeightSample sample = HeightMapGenerator.SampleTerrainHeightNative(
                worldX,
                worldZ,
                sampleScale,
                baseLandOffsets,
                mountainMaskOffsets,
                mountainTerrainOffsets,
                mountainRuggedOffsets,
                riverSeed, waterLevel);

            heights[index] = sample.Height;
            mountainMasks[index] = sample.MountainMask;
            riverMasks[index] = sample.RiverMask;
        }
    }

    [BurstCompile]
    private struct FarSlopeGridJob : IJobParallelFor
    {
        public int resolution;
        public float sampleSpacing;

        [ReadOnly] public NativeArray<float> heightSamples;
        [WriteOnly] public NativeArray<float> slopes;

        public void Execute(int index)
        {
            int x = index / resolution;
            int z = index - x * resolution;
            int x0 = math.max(x - 1, 0);
            int x1 = math.min(x + 1, resolution - 1);
            int z0 = math.max(z - 1, 0);
            int z1 = math.min(z + 1, resolution - 1);

            float dx = (heightSamples[x1 * resolution + z] - heightSamples[x0 * resolution + z]) /
                       math.max(sampleSpacing, 0.0001f);
            float dz = (heightSamples[x * resolution + z1] - heightSamples[x * resolution + z0]) /
                       math.max(sampleSpacing, 0.0001f);

            slopes[index] = math.sqrt(dx * dx + dz * dz);
        }
    }

    [BurstCompile]
    private struct FarSurfaceMapJob : IJobParallelFor
    {
        public float waterLevel;
        [ReadOnly] public NativeArray<float> heightSamples;
        [ReadOnly] public NativeArray<float> slopeSamples;
        [ReadOnly] public NativeArray<float> mountainMaskSamples;
        [ReadOnly] public NativeArray<float> riverMaskSamples;

        [WriteOnly] public NativeArray<SurfaceType> surfaces;

        public void Execute(int index)
        {
            float height = heightSamples[index];
            float slope = slopeSamples[index];
            float riverMask = riverMaskSamples[index];
            BiomeType biome = ClassifyCrudeBiome(
                height,
                slope,
                mountainMaskSamples[index],
                riverMask, waterLevel);

            surfaces[index] = SurfaceTypeClassifier.Classify(height, slope, riverMask, biome, waterLevel);
        }
    }

    [BurstCompile]
    private struct FarMeshVertexJob : IJobParallelFor
    {
        public int resolution;
        public int chunkSize;
        public float worldScale;
        public float meshHeightMultiplier;

        [ReadOnly] public NativeArray<float> heightSamples;
        [ReadOnly] public NativeArray<SurfaceType> surfaceSamples;

        [WriteOnly] public NativeArray<float3> vertices;
        [WriteOnly] public NativeArray<float3> normals;
        [WriteOnly] public NativeArray<float2> uvs;
        [WriteOnly] public NativeArray<float4> colors;

        public void Execute(int index)
        {
            int x = index / resolution;
            int z = index - x * resolution;
            float tx = resolution == 1 ? 0f : x / (float)(resolution - 1);
            float tz = resolution == 1 ? 0f : z / (float)(resolution - 1);

            vertices[index] = new float3(
                math.lerp(chunkSize / -2f, chunkSize / 2f, tx) * worldScale,
                heightSamples[index] * meshHeightMultiplier * worldScale,
                math.lerp(chunkSize / -2f, chunkSize / 2f, tz) * worldScale);
            normals[index] = CalculateNormal(heightSamples, resolution, x, z, chunkSize, meshHeightMultiplier);
            uvs[index] = new float2(tx, tz);
            colors[index] = GenerateColor(surfaceSamples[index]);
        }

        private static float3 CalculateNormal(
            NativeArray<float> heightSamples,
            int resolution,
            int x,
            int z,
            int chunkSize,
            float meshHeightMultiplier)
        {
            float sampleSpacing = resolution <= 1 ? chunkSize : chunkSize / (float)(resolution - 1);
            float left = heightSamples[math.max(x - 1, 0) * resolution + z];
            float right = heightSamples[math.min(x + 1, resolution - 1) * resolution + z];
            float down = heightSamples[x * resolution + math.max(z - 1, 0)];
            float up = heightSamples[x * resolution + math.min(z + 1, resolution - 1)];

            float dx = (right - left) * meshHeightMultiplier / math.max(sampleSpacing, 0.0001f);
            float dz = (up - down) * meshHeightMultiplier / math.max(sampleSpacing, 0.0001f);

            return math.normalize(new float3(-dx, 2f, -dz));
        }

        private static float4 GenerateColor(SurfaceType surfaceType)
        {
            switch (surfaceType)
            {
                case SurfaceType.Sand:
                    return new float4(0.80f, 0.75f, 0.55f, 1f);
                case SurfaceType.Mud:
                    return new float4(0.42f, 0.32f, 0.22f, 1f);
                case SurfaceType.Grass:
                    return new float4(0.1255f, 0.5451f, 0.1569f, 1f);
                case SurfaceType.Rock:
                    return new float4(0.45f, 0.45f, 0.45f, 1f);
                case SurfaceType.Snow:
                    return new float4(0.92f, 0.94f, 0.98f, 1f);
                case SurfaceType.Cliff:
                    return new float4(0.30f, 0.30f, 0.30f, 1f);
                case SurfaceType.Riverbed:
                    return new float4(0.35f, 0.30f, 0.24f, 1f);
                default:
                    return new float4(1f, 0f, 1f, 1f);
            }
        }
    }

    [BurstCompile]
    private struct FarMeshTriangleJob : IJobParallelFor
    {
        public int resolution;

        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<int> triangles;

        public void Execute(int cellIndex)
        {
            int cellsPerLine = resolution - 1;
            int x = cellIndex % cellsPerLine;
            int z = cellIndex / cellsPerLine;
            int a = x * resolution + z;
            int b = x * resolution + z + 1;
            int c = (x + 1) * resolution + z + 1;
            int d = (x + 1) * resolution + z;
            int triangleIndex = cellIndex * 6;

            triangles[triangleIndex] = a;
            triangles[triangleIndex + 1] = b;
            triangles[triangleIndex + 2] = c;
            triangles[triangleIndex + 3] = a;
            triangles[triangleIndex + 4] = c;
            triangles[triangleIndex + 5] = d;
        }
    }

    [BurstCompile]
    private struct FarControlMapBuildJob : IJobParallelFor
    {
        public float waterLevel;
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

            BiomeType biome = ClassifyCrudeBiome(height, slope, mountainMask, riverMask, waterLevel);
            SurfaceType surfaceType = SurfaceTypeClassifier.Classify(height, slope, riverMask, biome, waterLevel);
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

    private static BiomeType ClassifyCrudeBiome(float height, float slope, float mountainMask, float riverMask, float waterLevel)
    {
        if (height <= waterLevel)
            return BiomeType.Water;

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
