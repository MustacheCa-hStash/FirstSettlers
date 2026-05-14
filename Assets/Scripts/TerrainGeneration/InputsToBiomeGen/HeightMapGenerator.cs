using UnityEngine;

public static class HeightMapGenerator
{
    public static HeightFieldResult GenerateTerrainHeightField(
        int chunkSize,
        int seed,
        float sampleScale,
        ChunkCoord chunkCoord)
    {
        int width = chunkSize + 3;
        int height = chunkSize + 3;

        float[,] finalHeightMap = new float[width, height];
        float[,] mountainMaskMap = new float[width, height];
        float[,] riverMaskMap = new float[width, height];

        if (sampleScale <= 0f)
            sampleScale = 0.0001f;

        Vector2[] baseLandOffsets = TerrainNoiseUtility.GenerateOctaveOffsets(seed + 20000, 2);
        Vector2[] mountainMaskOffsets = TerrainNoiseUtility.GenerateOctaveOffsets(seed + 30000, 3);
        Vector2[] mountainTerrainOffsets = TerrainNoiseUtility.GenerateOctaveOffsets(seed + 40000, 4);
        Vector2[] mountainRuggedOffsets = TerrainNoiseUtility.GenerateOctaveOffsets(seed + 50000, 3);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                int localSampleX = x - 1;
                int localSampleZ = z - 1;

                float worldX = chunkCoord.x * chunkSize + localSampleX;
                float worldZ = chunkCoord.z * chunkSize + localSampleZ;

                float baseLandSampleX = worldX / (sampleScale * 1.6f);
                float baseLandSampleZ = worldZ / (sampleScale * 1.6f);
                float baseLand = BaseLandGenerator.Sample(baseLandSampleX, baseLandSampleZ, baseLandOffsets);

                float mountainMaskSampleX = worldX / (sampleScale * 6.0f);
                float mountainMaskSampleZ = worldZ / (sampleScale * 6.0f);
                float mountainMask = MountainMaskGenerator.Sample(mountainMaskSampleX, mountainMaskSampleZ, mountainMaskOffsets);

                float gatedMask = Mathf.SmoothStep(0.12f, 0.9f, mountainMask);
                float mountainWeight = Mathf.Pow(gatedMask, 1.8f);

                float mountainTerrainSampleX = worldX / (sampleScale * 3f);
                float mountainTerrainSampleZ = worldZ / (sampleScale * 3f);
                float mountainTerrain = MountainTerrainGenerator.Sample(mountainTerrainSampleX, mountainTerrainSampleZ, mountainTerrainOffsets);

                float riverSampleX = worldX / (sampleScale * 10.0f);
                float riverSampleZ = worldZ / (sampleScale * 10.0f);
                float riverMask = RiverGenerator.Sample(riverSampleX, riverSampleZ);

                float mainMountainHeight = mountainTerrain * mountainWeight * 45.0f;

                float ruggedSampleX = worldX / (sampleScale * 0.3f);
                float ruggedSampleZ = worldZ / (sampleScale * 0.3f);

                float ruggedRaw = TerrainNoiseUtility.SampleBasicFbm(
                    ruggedSampleX,
                    ruggedSampleZ,
                    0,
                    3,
                    0.5f,
                    2.0f,
                    mountainRuggedOffsets
                );

                float ruggedNoise = Mathf.Max(0f, ruggedRaw);

                float ruggedMask = Mathf.SmoothStep(0.25f, 0.8f, mountainTerrain);

                float ruggedHeight = ruggedNoise * ruggedMask * mountainWeight * 2.0f;

                float finalHeight = baseLand + mainMountainHeight + ruggedHeight;
                finalHeight = ApplyHeightPipeline(finalHeight);

                float riverEligibility;
                float mountainContribution = mountainTerrain * mountainWeight;

                if (mountainContribution >= 0.03f)
                {
                    riverEligibility = 0f;
                }
                else
                {
                    riverEligibility = 1f - Mathf.SmoothStep(0.20f, 0.55f, mountainWeight);
                }

                float carvedRiverMask = riverMask * riverEligibility;

                float originalHeight = finalHeight;

                float riverDepth = 0.0015f;
                float riverReferenceHeight = ApplyHeightPipeline(baseLand);
                float riverbedTarget = riverReferenceHeight - riverDepth;

                float riverInclusionThreshold = 0.75f;

                float basinMask = Mathf.InverseLerp(0.03f, 0.95f, carvedRiverMask);
                basinMask = Mathf.SmoothStep(0f, 1f, basinMask);

                finalHeight = Mathf.Lerp(originalHeight, riverbedTarget, basinMask);

                float riverCoreMask = Mathf.InverseLerp(riverInclusionThreshold, 1.0f, carvedRiverMask);
                riverCoreMask = Mathf.SmoothStep(0f, 1f, riverCoreMask);

                float riverCoreExtraDepth = 0.5f;
                finalHeight -= riverCoreMask * riverCoreExtraDepth;

                finalHeightMap[x, z] = finalHeight;
                mountainMaskMap[x, z] = mountainMask;
                riverMaskMap[x, z] = carvedRiverMask;
            }
        }

        ComputeFinalGradients(finalHeightMap, out float[,] gradientXMap, out float[,] gradientZMap, out float[,] slopeMap);
        
        return new HeightFieldResult(finalHeightMap, gradientXMap, gradientZMap, slopeMap, mountainMaskMap, riverMaskMap);
    }

    public static float[,] ApplyBiomeHeightModifiers(float[,] rawHeightMap, BiomeType[,] biomeMap)
    {
        return rawHeightMap;
    }

    private static float ApplyHeightPipeline(float normalizedHeight)
    {
        return normalizedHeight;
    }

    private static void ComputeFinalGradients(float[,] finalHeightMap, out float[,] gradientXMap, out float[,] gradientZMap, out float[,] slopeMap)
    {
        int width = finalHeightMap.GetLength(0);
        int height = finalHeightMap.GetLength(1);

        gradientXMap = new float[width, height];
        gradientZMap = new float[width, height];
        slopeMap = new float[width, height];

        const int slopeRadius = 4;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                float left = finalHeightMap[Mathf.Max(x - 1, 0), z];
                float right = finalHeightMap[Mathf.Min(x + 1, width - 1), z];
                float down = finalHeightMap[x, Mathf.Max(z - 1, 0)];
                float up = finalHeightMap[x, Mathf.Min(z + 1, height - 1)];

                float dx;
                if (x == 0)
                    dx = right - finalHeightMap[x, z];
                else if (x == width - 1)
                    dx = finalHeightMap[x, z] - left;
                else
                    dx = (right - left) * 0.5f;

                float dz;
                if (z == 0)
                    dz = up - finalHeightMap[x, z];
                else if (z == height - 1)
                    dz = finalHeightMap[x, z] - down;
                else
                    dz = (up - down) * 0.5f;

                gradientXMap[x, z] = dx;
                gradientZMap[x, z] = dz;

                int x0 = Mathf.Max(x - slopeRadius, 0);
                int x1 = Mathf.Min(x + slopeRadius, width - 1);
                int z0 = Mathf.Max(z - slopeRadius, 0);
                int z1 = Mathf.Min(z + slopeRadius, height - 1);

                float wideDx = (finalHeightMap[x1, z] - finalHeightMap[x0, z]) / Mathf.Max(1f, x1 - x0);
                float wideDz = (finalHeightMap[x, z1] - finalHeightMap[x, z0]) / Mathf.Max(1f, z1 - z0);

                slopeMap[x, z] = Mathf.Sqrt(wideDx * wideDx + wideDz * wideDz);
            }
        }
    }
}