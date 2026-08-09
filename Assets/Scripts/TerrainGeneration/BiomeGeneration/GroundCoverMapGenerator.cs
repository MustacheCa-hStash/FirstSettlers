using UnityEngine;

public static class GroundCoverMapGenerator
{
    public static GroundCoverType[,] GenerateGroundCoverMap(
        BiomeType[,] biomeMap,
        SurfaceType[,] surfaceTypeMap,
        float[,] moistureMap,
        float[,] slopeMap,
        float[,] riverMaskMap,
        WorldFeaturePlan worldFeaturePlan,
        int chunkSize,
        int seed,
        ChunkCoord chunkCoord)
    {
        int width = biomeMap.GetLength(0);
        int height = biomeMap.GetLength(1);
        GroundCoverType[,] groundCoverMap = new GroundCoverType[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                groundCoverMap[x, z] = Classify(
                    biomeMap[x, z],
                    surfaceTypeMap[x, z],
                    moistureMap[x, z],
                    slopeMap[x, z],
                    riverMaskMap[x, z],
                    worldFeaturePlan,
                    chunkSize,
                    seed,
                    chunkCoord,
                    x,
                    z);
            }
        }

        return groundCoverMap;
    }

    private static GroundCoverType Classify(
        BiomeType biome,
        SurfaceType surface,
        float moisture,
        float slope,
        float riverMask,
        WorldFeaturePlan worldFeaturePlan,
        int chunkSize,
        int seed,
        ChunkCoord chunkCoord,
        int x,
        int z)
    {
        if (surface != SurfaceType.Grass)
            return GroundCoverType.Default;

        switch (biome)
        {
            case BiomeType.Forest:
                return ClassifyForestCover(
                    moisture,
                    slope,
                    riverMask,
                    worldFeaturePlan,
                    chunkSize,
                    seed,
                    chunkCoord,
                    x,
                    z);

            case BiomeType.Taiga:
                return Sample01(seed + 8310, chunkCoord, chunkSize, x, z, 0.04f) > 0.48f
                    ? GroundCoverType.NeedleLitter
                    : GroundCoverType.DarkGrass;

            case BiomeType.Tundra:
                return GroundCoverType.SnowDusting;

            default:
                return GroundCoverType.Default;
        }
    }

    private static GroundCoverType ClassifyForestCover(
        float moisture,
        float slope,
        float riverMask,
        WorldFeaturePlan worldFeaturePlan,
        int chunkSize,
        int seed,
        ChunkCoord chunkCoord,
        int x,
        int z)
    {
        float patchNoise = Sample01(seed + 8300, chunkCoord, chunkSize, x, z, 0.055f);
        float broadPatchNoise = Sample01(seed + 8301, chunkCoord, chunkSize, x, z, 0.023f);
        float canopyDensity = worldFeaturePlan.CanopyDensityMap[x, z];
        ForestStructureFields fields = worldFeaturePlan.ForestStructure;
        float clearing = fields.ClearingMap[x, z];
        float rockInfluence = fields.RockInfluenceMap[x, z];
        float dampShade = fields.DampShadeMap[x, z];
        float understory = fields.UnderstoryDensityMap[x, z];
        float organicFloorIntent = fields.OrganicFloorIntentMap[x, z];
        bool nearRiver = riverMask > 0.64f;
        bool exposedOrDry = slope > 0.085f || moisture < 0.32f;

        if (nearRiver)
            return dampShade > 0.58f && patchNoise > 0.55f ? GroundCoverType.Moss : GroundCoverType.BareDirt;

        if (rockInfluence > 0.52f)
        {
            if (dampShade > 0.48f && patchNoise > 0.35f)
                return GroundCoverType.Moss;

            return organicFloorIntent > 0.66f && broadPatchNoise > 0.48f
                ? GroundCoverType.LeafLitter
                : GroundCoverType.BareDirt;
        }

        if (clearing > 0.56f)
        {
            if (organicFloorIntent > 0.74f && patchNoise > 0.62f)
                return GroundCoverType.LeafLitter;

            return GroundCoverType.DarkGrass;
        }

        if (organicFloorIntent > 0.58f && patchNoise > 0.24f)
        {
            if (dampShade > 0.68f && patchNoise < 0.34f)
                return GroundCoverType.Moss;

            if (exposedOrDry && patchNoise > 0.72f)
                return GroundCoverType.BareDirt;

            return GroundCoverType.LeafLitter;
        }

        if (canopyDensity > 0.62f)
        {
            if (exposedOrDry && patchNoise > 0.35f)
                return GroundCoverType.BareDirt;

            if (dampShade > 0.66f && patchNoise < 0.24f)
                return GroundCoverType.Moss;

            return organicFloorIntent > 0.48f && broadPatchNoise > 0.68f
                ? GroundCoverType.LeafLitter
                : GroundCoverType.DarkGrass;
        }

        if (canopyDensity > 0.28f)
        {
            if (organicFloorIntent > 0.52f && (broadPatchNoise > 0.72f || patchNoise > 0.86f))
                return GroundCoverType.LeafLitter;

            return GroundCoverType.DarkGrass;
        }

        return GroundCoverType.DarkGrass;
    }

    private static float Sample01(int seed, ChunkCoord chunkCoord, int chunkSize, int x, int z, float scale)
    {
        float worldX = chunkCoord.x * chunkSize + (x - 1);
        float worldZ = chunkCoord.z * chunkSize + (z - 1);
        NoiseSample2D sample = AnalyticValueNoise2D.Sample(worldX * scale, worldZ * scale, seed);
        return Mathf.Clamp01((sample.Value + 1f) * 0.5f);
    }
}
