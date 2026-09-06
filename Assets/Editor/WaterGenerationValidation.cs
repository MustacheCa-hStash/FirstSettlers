using System;
using UnityEditor;
using UnityEngine;

public static class WaterGenerationValidation
{
    [MenuItem("Tools/Terrain/Validate Global Water")]
    public static void Run()
    {
        ValidateClassificationAndMeshes();
        ValidateSamplingAndCoverage();
        Debug.Log("Global water validation passed: classification, placement margins, mesh LODs, chunk borders, near/far sampling, and coverage.");
    }

    public static void RunBatch()
    {
        try
        {
            Run();
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void ValidateClassificationAndMeshes()
    {
        const int size = 16;
        foreach (float surfaceY in new[] { -6f, 14.4f, 30f })
        {
            TerrainWaterSettings settings = new TerrainWaterSettings(surfaceY, 200f, 0.3f);
            float level = settings.WaterLevel;
            float[,] heights = new float[size + 3, size + 3];
            float[,] masks = new float[size + 3, size + 3];
            float[,] slopes = new float[size + 3, size + 3];
            float[,] moisture = new float[size + 3, size + 3];
            float[,] temperature = new float[size + 3, size + 3];
            float[,] mountains = new float[size + 3, size + 3];
            for (int x = 0; x < size + 3; x++)
            {
                for (int z = 0; z < size + 3; z++)
                {
                    heights[x, z] = level + (x - 9) * 0.01f;
                    masks[x, z] = z / (float)(size + 2);
                    slopes[x, z] = z % 2 == 0 ? 0.7f : 0f;
                    moisture[x, z] = 0.8f;
                    temperature[x, z] = 0.5f;
                }
            }

            BiomeType[,] biomes = BiomeMapGenerator.GenerateBiomeMap(heights, moisture, temperature, slopes, mountains, masks, level);
            SurfaceType[,] surfaces = SurfaceMapGenerator.GenerateSurfaceTypeMap(heights, slopes, masks, biomes, level);
            WaterState[,] states = WaterStateMapGenerator.GenerateWaterStateMap(heights, masks, level);
            for (int x = 1; x <= size + 1; x++)
            {
                for (int z = 1; z <= size + 1; z++)
                {
                    BiomeType helperBiome = BiomeClassifier.Classify(heights[x, z], moisture[x, z], temperature[x, z], slopes[x, z], 0f, masks[x, z], level);
                    Require(biomes[x, z] == helperBiome, $"Biome helper/job mismatch at {x},{z}: height={heights[x, z]:R}, level={level:R}, helper={helperBiome}, job={biomes[x, z]}.");
                    Require(states[x, z] == WaterStateClassifier.Classify(heights[x, z], masks[x, z], level), "Water helper/job mismatch.");
                    bool submerged = heights[x, z] <= level;
                    Require((biomes[x, z] == BiomeType.Water) == submerged, "Water biome does not match the plane.");
                    Require((states[x, z] == WaterState.Shallow || states[x, z] == WaterState.Deep) == submerged, "Water state does not match the plane.");
                    if (submerged)
                        Require(surfaces[x, z] == SurfaceType.Riverbed, "Submerged terrain permits land placement.");
                    if (surfaces[x, z] == SurfaceType.Grass)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                            for (int dz = -1; dz <= 1; dz++)
                                Require(heights[x + dx, z + dz] > level, "Rounded placement could interpolate underwater.");
                    }
                }
            }

            foreach (int step in new[] { 1, 2, 4, 8, 16 })
            {
                ValidatePlane(LakeMeshGenerator.GenerateLakeMesh(heights, states, masks, 200f, step, 0.3f, surfaceY), surfaceY);
                ValidatePlane(RiverMeshGenerator.GenerateRiverMesh(heights, states, masks, 200f, step, 0.3f, surfaceY), surfaceY);
            }

            // A steep shoreline exercises the interpolation exclusion independently of the beach band.
            heights[8, 8] = level - 0.01f;
            heights[9, 8] = level + 0.1f;
            slopes[9, 8] = 0f;
            masks[9, 8] = 0f;
            biomes[9, 8] = BiomeType.Forest;
            surfaces = SurfaceMapGenerator.GenerateSurfaceTypeMap(heights, slopes, masks, biomes, level);
            Require(surfaces[9, 8] != SurfaceType.Grass, "Steep shore has no placement margin.");
        }
    }

    private static void ValidatePlane(WaterMeshData data, float waterY)
    {
        Require(data.VertexCount > 0, "Expected water mesh is empty.");
        Mesh mesh = data.CreateMesh();
        try
        {
            foreach (Vector3 vertex in mesh.vertices)
                Require(vertex.y == waterY, "Water vertex is not at the exact Inspector Y.");
            foreach (int index in mesh.triangles)
                Require(index >= 0 && index < mesh.vertexCount, "Invalid water triangle index.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    private static void ValidateSamplingAndCoverage()
    {
        const int chunkSize = 32;
        const float level = TerrainWaterSettings.DefaultWaterLevel;
        const float scale = 600f;
        foreach (int seed in new[] { 7, 42, 12345 })
        {
            TerrainHeightSamplingContext context = HeightMapGenerator.CreateSamplingContext(seed, level);
            int wet = 0;
            int total = 0;
            ChunkCoord riverChunk = default;
            bool foundRiver = false;
            for (int x = -32; x <= 32; x++)
            {
                for (int z = -32; z <= 32; z++)
                {
                    float wx = x * 751f;
                    float wz = z * 751f;
                    TerrainHeightSample sample = HeightMapGenerator.SampleTerrainHeight(wx, wz, scale, context);
                    Require(!float.IsNaN(sample.Height) && !float.IsInfinity(sample.Height), "Invalid generated height.");
                    if (sample.Height <= level) wet++;
                    total++;
                    if (!foundRiver && sample.RiverMask > 0.99f)
                    {
                        foundRiver = true;
                        riverChunk = new ChunkCoord(Mathf.FloorToInt(wx / chunkSize), Mathf.FloorToInt(wz / chunkSize));
                        Require(sample.Height <= level - 0.02f, "River core is not below the global surface.");
                    }
                }
            }

            float coverage = wet / (float)total;
            Debug.Log($"Global water coverage seed={seed}: {coverage:P2} ({wet}/{total} samples over a 48064 x 48064 terrain-unit region).");
            Require(coverage > 0f && coverage < 0.5f, "Water coverage is empty or dominates the world.");
            Require(foundRiver, "No river core found in coverage sample.");

            foreach (float waterLevel in new[] { level, 0.4f })
            {
                context = HeightMapGenerator.CreateSamplingContext(seed, waterLevel);
                HeightFieldResult near = HeightMapGenerator.GenerateTerrainHeightField(chunkSize, seed, scale, riverChunk, waterLevel);
                HeightFieldResult neighbor = HeightMapGenerator.GenerateTerrainHeightField(chunkSize, seed, scale, new ChunkCoord(riverChunk.x + 1, riverChunk.z), waterLevel);
                FarTerrainRequestResult far = FarTerrainGenerator.Generate(riverChunk, 1, chunkSize, seed, scale, 200f, 0.3f, 9, 16, 0f, waterLevel);
                for (int x = 0; x <= chunkSize; x += 4)
                {
                    for (int z = 0; z <= chunkSize; z += 4)
                    {
                        float managed = HeightMapGenerator.SampleTerrainHeight(riverChunk.x * chunkSize + x, riverChunk.z * chunkSize + z, scale, context).Height;
                        Require(Mathf.Abs(managed - near.HeightMap[x + 1, z + 1]) < 0.0002f, "Managed/native terrain mismatch.");
                    }
                    Require(near.HeightMap[chunkSize + 1, x + 1] == neighbor.HeightMap[1, x + 1], "Chunk-border height mismatch.");
                }
                Mesh farMesh = far.TerrainMeshData.CreateMesh();
                try
                {
                    foreach (Vector3 vertex in farMesh.vertices)
                    {
                        int x = Mathf.RoundToInt(vertex.x / 0.3f + chunkSize / 2f);
                        int z = Mathf.RoundToInt(vertex.z / 0.3f + chunkSize / 2f);
                        Require(Mathf.Abs(vertex.y - near.HeightMap[x + 1, z + 1] * 200f * 0.3f) < 0.001f, "Near/far terrain mismatch.");
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(farMesh);
                }
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
