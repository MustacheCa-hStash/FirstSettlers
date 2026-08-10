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
        float skirtDepth)
    {
        int safeHeightGridResolution = Mathf.Clamp(heightGridResolution, 2, chunkSize + 1);
        int safeControlMapResolution = Mathf.Clamp(controlMapResolution, 2, 128);

        TerrainHeightSamplingContext samplingContext = HeightMapGenerator.CreateSamplingContext(seed);

        float[,] heightGrid = BuildHeightGrid(
            chunkCoord,
            chunkSize,
            sampleScale,
            safeHeightGridResolution,
            samplingContext,
            out float[,] mountainMaskGrid,
            out float[,] riverMaskGrid);

        float[,] slopeGrid = BuildSlopeGrid(heightGrid, chunkSize);
        SurfaceType[,] meshSurfaceMap = BuildSurfaceMap(heightGrid, slopeGrid, mountainMaskGrid, riverMaskGrid);

        MeshData meshData = BuildMesh(
            chunkSize,
            worldScale,
            meshHeightMultiplier,
            heightGrid,
            meshSurfaceMap,
            Mathf.Max(0f, skirtDepth));

        ControlMapPixelData controlMaps = BuildControlMaps(
            chunkCoord,
            chunkSize,
            sampleScale,
            safeControlMapResolution,
            samplingContext);

        return new FarTerrainRequestResult(chunkCoord, requestVersion, meshData, controlMaps);
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

                Vector3 normal = CalculateNormal(heightGrid, x, z, meshHeightMultiplier);
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
        ChunkCoord chunkCoord,
        int chunkSize,
        float sampleScale,
        int resolution,
        TerrainHeightSamplingContext samplingContext)
    {
        float[,] heightGrid = BuildHeightGrid(
            chunkCoord,
            chunkSize,
            sampleScale,
            resolution,
            samplingContext,
            out float[,] mountainMaskGrid,
            out float[,] riverMaskGrid);

        float[,] slopeGrid = BuildSlopeGrid(heightGrid, chunkSize);
        SurfaceType[,] surfaceMap = BuildSurfaceMap(heightGrid, slopeGrid, mountainMaskGrid, riverMaskGrid);
        GroundCoverType[,] groundCoverMap = new GroundCoverType[resolution, resolution];

        return TerrainControlMapBuilder.BuildRaw(surfaceMap, groundCoverMap);
    }

    private static Vector3 CalculateNormal(float[,] heightGrid, int x, int z, float meshHeightMultiplier)
    {
        int resolution = heightGrid.GetLength(0);

        float left = heightGrid[Mathf.Max(x - 1, 0), z];
        float right = heightGrid[Mathf.Min(x + 1, resolution - 1), z];
        float down = heightGrid[x, Mathf.Max(z - 1, 0)];
        float up = heightGrid[x, Mathf.Min(z + 1, resolution - 1)];

        float dx = (right - left) * meshHeightMultiplier;
        float dz = (up - down) * meshHeightMultiplier;

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
