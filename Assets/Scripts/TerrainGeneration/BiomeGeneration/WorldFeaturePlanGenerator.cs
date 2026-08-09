using UnityEngine;

public static class WorldFeaturePlanGenerator
{
    private const float ForestCanopyMacroScale = 0.0075f;
    private const float ForestTreeClusterScale = 0.024f;
    private const float ForestClearingScale = 0.012f;
    private const float ForestRockinessScale = 0.018f;
    private const float ForestFineBreakupScale = 0.067f;
    private const float ForestOrganicFloorBlobScale = 0.014f;
    private const float ForestOrganicFloorBreakupScale = 0.043f;

    private const int TreeCandidateCellsPerAxis = 9;
    private const int MaxForestTreesPerChunk = 18;
    private const int BoulderCandidateCellsPerAxis = 4;
    private const int MaxForestBouldersPerChunk = 2;

    public static WorldFeaturePlan Generate(
        ChunkCoord chunkCoord,
        int chunkSize,
        int seed,
        BiomeType[,] biomeMap,
        SurfaceType[,] surfaceTypeMap,
        float[,] moistureMap,
        float[,] temperatureMap,
        float[,] slopeMap,
        float[,] riverMaskMap)
    {
        int width = biomeMap.GetLength(0);
        int height = biomeMap.GetLength(1);
        WorldFeaturePlan plan = new WorldFeaturePlan(width, height);

        BuildForestStructureFields(
            plan,
            chunkCoord,
            chunkSize,
            seed,
            biomeMap,
            surfaceTypeMap,
            moistureMap,
            slopeMap,
            riverMaskMap);

        AddForestBoulders(
            plan,
            chunkCoord,
            chunkSize,
            seed,
            biomeMap,
            surfaceTypeMap,
            slopeMap,
            riverMaskMap);

        BuildRockInfluenceMap(plan, chunkSize);

        AddForestTrees(
            plan,
            chunkCoord,
            chunkSize,
            seed,
            biomeMap,
            surfaceTypeMap,
            moistureMap,
            temperatureMap,
            slopeMap,
            riverMaskMap);

        BuildCanopyDensityMap(plan, chunkCoord, chunkSize, seed);
        BuildOrganicFloorIntentMap(plan, chunkCoord, chunkSize, seed);

        return plan;
    }

    private static void BuildForestStructureFields(
        WorldFeaturePlan plan,
        ChunkCoord chunkCoord,
        int chunkSize,
        int seed,
        BiomeType[,] biomeMap,
        SurfaceType[,] surfaceTypeMap,
        float[,] moistureMap,
        float[,] slopeMap,
        float[,] riverMaskMap)
    {
        ForestStructureFields fields = plan.ForestStructure;
        int width = biomeMap.GetLength(0);
        int height = biomeMap.GetLength(1);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (biomeMap[x, z] != BiomeType.Forest || surfaceTypeMap[x, z] != SurfaceType.Grass)
                    continue;

                float worldX = chunkCoord.x * chunkSize + (x - 1);
                float worldZ = chunkCoord.z * chunkSize + (z - 1);

                float canopyMacro = Sample01(worldX, worldZ, ForestCanopyMacroScale, seed + 6100);
                float treeCluster = Sample01(worldX, worldZ, ForestTreeClusterScale, seed + 6101);
                float clearingNoise = Sample01(worldX, worldZ, ForestClearingScale, seed + 6102);
                float rockinessNoise = Sample01(worldX, worldZ, ForestRockinessScale, seed + 6103);
                float fineBreakup = Sample01(worldX, worldZ, ForestFineBreakupScale, seed + 6104);

                float slopeSuitability = Mathf.InverseLerp(0.14f, 0.03f, slopeMap[x, z]);
                float riverSuitability = 1f - SmoothStep(0.52f, 0.76f, riverMaskMap[x, z]);
                float clearing = Mathf.Clamp01((clearingNoise - 0.62f) * 2.65f);

                float canopyIntent = canopyMacro * 0.52f + treeCluster * 0.36f + fineBreakup * 0.12f;
                canopyIntent = Mathf.Clamp01(canopyIntent * slopeSuitability * riverSuitability);
                canopyIntent *= 1f - clearing * 0.72f;

                float rockiness = Mathf.Clamp01((rockinessNoise - 0.55f) * 2.2f);
                rockiness = Mathf.Clamp01(rockiness + Mathf.InverseLerp(0.055f, 0.13f, slopeMap[x, z]) * 0.42f);
                rockiness *= riverSuitability;

                float dampShade = Mathf.Clamp01(moistureMap[x, z] * 0.5f + canopyIntent * 0.34f + riverMaskMap[x, z] * 0.16f);
                float understory = Mathf.Clamp01((0.35f + dampShade * 0.55f + fineBreakup * 0.1f) * (1f - clearing * 0.6f));
                understory *= 1f - rockiness * 0.35f;

                fields.CanopyIntentMap[x, z] = canopyIntent;
                fields.ClearingMap[x, z] = clearing;
                fields.TreeClusterMap[x, z] = treeCluster;
                fields.RockinessMap[x, z] = rockiness;
                fields.DampShadeMap[x, z] = dampShade;
                fields.UnderstoryDensityMap[x, z] = understory;
            }
        }
    }

    private static void AddForestBoulders(
        WorldFeaturePlan plan,
        ChunkCoord chunkCoord,
        int chunkSize,
        int seed,
        BiomeType[,] biomeMap,
        SurfaceType[,] surfaceTypeMap,
        float[,] slopeMap,
        float[,] riverMaskMap)
    {
        int placed = 0;
        float cellSize = chunkSize / (float)BoulderCandidateCellsPerAxis;
        int totalCandidates = BoulderCandidateCellsPerAxis * BoulderCandidateCellsPerAxis;
        int candidateOffset = Mathf.Abs(Hash(seed, chunkCoord.x, chunkCoord.z, 6211)) % totalCandidates;

        for (int candidateIndex = 0; candidateIndex < totalCandidates; candidateIndex++)
        {
            if (placed >= MaxForestBouldersPerChunk)
                return;

            int shuffledIndex = (candidateIndex * 5 + candidateOffset) % totalCandidates;
            int cellX = shuffledIndex % BoulderCandidateCellsPerAxis;
            int cellZ = shuffledIndex / BoulderCandidateCellsPerAxis;

            int hash = Hash(seed, chunkCoord.x, chunkCoord.z, cellX, cellZ, 6201);
            float sampleX = Mathf.Clamp((cellX + Hash01(hash + 17)) * cellSize, 4f, chunkSize - 4f);
            float sampleZ = Mathf.Clamp((cellZ + Hash01(hash + 31)) * cellSize, 4f, chunkSize - 4f);

            int paddedX = Mathf.Clamp(Mathf.RoundToInt(sampleX), 0, chunkSize) + 1;
            int paddedZ = Mathf.Clamp(Mathf.RoundToInt(sampleZ), 0, chunkSize) + 1;

            if (!IsValidForestLandSample(biomeMap, surfaceTypeMap, slopeMap, riverMaskMap, paddedX, paddedZ, 0.15f, 0.68f))
                continue;

            float rockiness = plan.ForestStructure.RockinessMap[paddedX, paddedZ];
            float chance = Mathf.InverseLerp(0.44f, 0.88f, rockiness) * 0.55f;

            if (Hash01(hash + 53) > chance)
                continue;

            float uniformScale = Mathf.Lerp(0.75f, 1.45f, Hash01(hash + 79));
            float exclusionRadius = Mathf.Lerp(4.8f, 7.4f, Mathf.InverseLerp(0.75f, 1.45f, uniformScale));
            float yaw = Hash01(hash + 97) * 360f;

            if (IntersectsExistingPlacement(plan, sampleX, sampleZ, exclusionRadius))
                continue;

            plan.Placements.Add(new WorldFeaturePlacement(
                WorldFeatureType.Boulder,
                WorldFeatureVariant.Boulder,
                sampleX,
                sampleZ,
                Quaternion.Euler(0f, yaw, 0f),
                Vector3.one * uniformScale,
                exclusionRadius,
                exclusionRadius + 8.5f));

            placed++;
        }
    }

    private static void AddForestTrees(
        WorldFeaturePlan plan,
        ChunkCoord chunkCoord,
        int chunkSize,
        int seed,
        BiomeType[,] biomeMap,
        SurfaceType[,] surfaceTypeMap,
        float[,] moistureMap,
        float[,] temperatureMap,
        float[,] slopeMap,
        float[,] riverMaskMap)
    {
        int placed = 0;
        float cellSize = chunkSize / (float)TreeCandidateCellsPerAxis;
        int totalCandidates = TreeCandidateCellsPerAxis * TreeCandidateCellsPerAxis;
        int candidateOffset = Mathf.Abs(Hash(seed, chunkCoord.x, chunkCoord.z, 6311)) % totalCandidates;

        for (int candidateIndex = 0; candidateIndex < totalCandidates; candidateIndex++)
        {
            if (placed >= MaxForestTreesPerChunk)
                return;

            int shuffledIndex = (candidateIndex * 37 + candidateOffset) % totalCandidates;
            int cellX = shuffledIndex % TreeCandidateCellsPerAxis;
            int cellZ = shuffledIndex / TreeCandidateCellsPerAxis;

            int hash = Hash(seed, chunkCoord.x, chunkCoord.z, cellX, cellZ, 6301);
            float sampleX = Mathf.Clamp((cellX + Hash01(hash + 17)) * cellSize, 5f, chunkSize - 5f);
            float sampleZ = Mathf.Clamp((cellZ + Hash01(hash + 31)) * cellSize, 5f, chunkSize - 5f);

            int paddedX = Mathf.Clamp(Mathf.RoundToInt(sampleX), 0, chunkSize) + 1;
            int paddedZ = Mathf.Clamp(Mathf.RoundToInt(sampleZ), 0, chunkSize) + 1;

            if (!IsValidForestLandSample(biomeMap, surfaceTypeMap, slopeMap, riverMaskMap, paddedX, paddedZ, 0.12f, 0.64f))
                continue;

            ForestStructureFields fields = plan.ForestStructure;
            float canopyIntent = fields.CanopyIntentMap[paddedX, paddedZ];
            float clearing = fields.ClearingMap[paddedX, paddedZ];
            float cluster = fields.TreeClusterMap[paddedX, paddedZ];
            float rockiness = fields.RockinessMap[paddedX, paddedZ];

            float treeChance = Mathf.InverseLerp(0.30f, 0.84f, canopyIntent);
            treeChance *= Mathf.Lerp(0.72f, 1.18f, cluster);
            treeChance *= 1f - clearing * 0.82f;
            treeChance *= 1f - rockiness * 0.28f;
            treeChance = Mathf.Clamp01(treeChance);

            if (Hash01(hash + 53) > treeChance)
                continue;

            WorldFeatureVariant variant = ChooseForestTreeVariant(
                fields.DampShadeMap[paddedX, paddedZ],
                moistureMap[paddedX, paddedZ],
                temperatureMap[paddedX, paddedZ],
                cluster,
                hash);

            float yaw = Hash01(hash + 79) * 360f;
            float uniformScale = variant == WorldFeatureVariant.SpruceTree
                ? Mathf.Lerp(1.85f, 2.45f, Hash01(hash + 97))
                : Mathf.Lerp(1.65f, 2.2f, Hash01(hash + 97));

            float exclusionRadius = variant == WorldFeatureVariant.SpruceTree
                ? Mathf.Lerp(6.4f, 8.2f, Hash01(hash + 131))
                : Mathf.Lerp(7.4f, 9.2f, Hash01(hash + 131));

            if (IntersectsExistingPlacement(plan, sampleX, sampleZ, exclusionRadius))
                continue;

            float influenceRadius = variant == WorldFeatureVariant.SpruceTree
                ? Mathf.Lerp(16f, 22f, Hash01(hash + 149))
                : Mathf.Lerp(18f, 25f, Hash01(hash + 149));

            plan.Placements.Add(new WorldFeaturePlacement(
                WorldFeatureType.Tree,
                variant,
                sampleX,
                sampleZ,
                Quaternion.Euler(0f, yaw, 0f),
                Vector3.one * uniformScale,
                exclusionRadius,
                influenceRadius));

            placed++;
        }
    }

    private static WorldFeatureVariant ChooseForestTreeVariant(
        float dampShade,
        float moisture,
        float temperature,
        float cluster,
        int hash)
    {
        float spruceAffinity = dampShade * 0.45f + moisture * 0.25f + (1f - temperature) * 0.2f + cluster * 0.1f;
        float spruceThreshold = Mathf.Lerp(0.42f, 0.68f, Hash01(hash + 173));
        return spruceAffinity > spruceThreshold
            ? WorldFeatureVariant.SpruceTree
            : WorldFeatureVariant.MapleTree;
    }

    private static bool IsValidForestLandSample(
        BiomeType[,] biomeMap,
        SurfaceType[,] surfaceTypeMap,
        float[,] slopeMap,
        float[,] riverMaskMap,
        int paddedX,
        int paddedZ,
        float maxSlope,
        float maxRiverMask)
    {
        return biomeMap[paddedX, paddedZ] == BiomeType.Forest &&
               surfaceTypeMap[paddedX, paddedZ] == SurfaceType.Grass &&
               slopeMap[paddedX, paddedZ] < maxSlope &&
               riverMaskMap[paddedX, paddedZ] < maxRiverMask;
    }

    private static bool IntersectsExistingPlacement(
        WorldFeaturePlan plan,
        float sampleX,
        float sampleZ,
        float exclusionRadius)
    {
        for (int i = 0; i < plan.Placements.Count; i++)
        {
            WorldFeaturePlacement placement = plan.Placements[i];
            float minDistance = exclusionRadius + placement.exclusionRadius;
            float dx = sampleX - placement.sampleX;
            float dz = sampleZ - placement.sampleZ;

            if (dx * dx + dz * dz < minDistance * minDistance)
                return true;
        }

        return false;
    }

    private static void BuildRockInfluenceMap(WorldFeaturePlan plan, int chunkSize)
    {
        ForestStructureFields fields = plan.ForestStructure;
        int width = fields.RockInfluenceMap.GetLength(0);
        int height = fields.RockInfluenceMap.GetLength(1);

        for (int i = 0; i < plan.Placements.Count; i++)
        {
            WorldFeaturePlacement placement = plan.Placements[i];
            if (placement.featureType != WorldFeatureType.Boulder)
                continue;

            ApplyRadialInfluence(
                fields.RockInfluenceMap,
                chunkSize,
                placement.sampleX,
                placement.sampleZ,
                placement.influenceRadius,
                1f);
        }

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                fields.RockInfluenceMap[x, z] = Mathf.Max(
                    fields.RockInfluenceMap[x, z],
                    fields.RockinessMap[x, z] * 0.35f);
            }
        }
    }

    private static void BuildCanopyDensityMap(WorldFeaturePlan plan, ChunkCoord chunkCoord, int chunkSize, int seed)
    {
        int width = plan.CanopyDensityMap.GetLength(0);
        int height = plan.CanopyDensityMap.GetLength(1);
        ForestStructureFields fields = plan.ForestStructure;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                plan.CanopyDensityMap[x, z] = fields.CanopyIntentMap[x, z] * 0.26f;
            }
        }

        for (int i = 0; i < plan.Placements.Count; i++)
        {
            WorldFeaturePlacement placement = plan.Placements[i];
            if (placement.featureType != WorldFeatureType.Tree)
                continue;

            ApplyRadialInfluence(
                plan.CanopyDensityMap,
                chunkSize,
                placement.sampleX,
                placement.sampleZ,
                placement.influenceRadius,
                0.92f);
        }

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                float worldX = chunkCoord.x * chunkSize + (x - 1);
                float worldZ = chunkCoord.z * chunkSize + (z - 1);
                float breakup = Sample01(worldX, worldZ, ForestFineBreakupScale, seed + 6105);

                float density = plan.CanopyDensityMap[x, z];
                density *= Mathf.Lerp(0.78f, 1.12f, breakup);
                density *= 1f - fields.ClearingMap[x, z] * 0.42f;
                plan.CanopyDensityMap[x, z] = Mathf.Clamp01(density);
            }
        }
    }

    private static void BuildOrganicFloorIntentMap(WorldFeaturePlan plan, ChunkCoord chunkCoord, int chunkSize, int seed)
    {
        ForestStructureFields fields = plan.ForestStructure;
        int width = fields.OrganicFloorIntentMap.GetLength(0);
        int height = fields.OrganicFloorIntentMap.GetLength(1);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (fields.CanopyIntentMap[x, z] <= 0f &&
                    fields.DampShadeMap[x, z] <= 0f &&
                    plan.CanopyDensityMap[x, z] <= 0f)
                {
                    continue;
                }

                float worldX = chunkCoord.x * chunkSize + (x - 1);
                float worldZ = chunkCoord.z * chunkSize + (z - 1);
                float broadBlob = Sample01(worldX, worldZ, ForestOrganicFloorBlobScale, seed + 6110);
                float breakup = Sample01(worldX, worldZ, ForestOrganicFloorBreakupScale, seed + 6111);
                float fineVariation = Sample01(worldX, worldZ, ForestFineBreakupScale, seed + 6112);

                float organicIntent =
                    broadBlob * 0.5f +
                    breakup * 0.16f +
                    fields.CanopyIntentMap[x, z] * 0.16f +
                    fields.DampShadeMap[x, z] * 0.12f +
                    plan.CanopyDensityMap[x, z] * 0.06f;

                organicIntent *= Mathf.Lerp(0.82f, 1.12f, fineVariation);
                organicIntent *= 1f - fields.ClearingMap[x, z] * 0.62f;
                organicIntent *= 1f - fields.RockInfluenceMap[x, z] * 0.26f;

                fields.OrganicFloorIntentMap[x, z] = Mathf.Clamp01(organicIntent);
            }
        }

        for (int i = 0; i < plan.Placements.Count; i++)
        {
            WorldFeaturePlacement placement = plan.Placements[i];
            if (placement.featureType != WorldFeatureType.Tree)
                continue;

            float radius = placement.variant == WorldFeatureVariant.MapleTree
                ? placement.influenceRadius * 0.72f
                : placement.influenceRadius * 0.52f;

            float strength = placement.variant == WorldFeatureVariant.MapleTree ? 0.16f : 0.08f;

            AddOrganicFloorTreeInfluence(
                fields,
                chunkCoord,
                chunkSize,
                seed,
                placement.sampleX,
                placement.sampleZ,
                radius,
                strength);
        }
    }

    private static void AddOrganicFloorTreeInfluence(
        ForestStructureFields fields,
        ChunkCoord chunkCoord,
        int chunkSize,
        int seed,
        float sampleX,
        float sampleZ,
        float radius,
        float strength)
    {
        float radiusSqr = radius * radius;
        int width = fields.OrganicFloorIntentMap.GetLength(0);
        int height = fields.OrganicFloorIntentMap.GetLength(1);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                float mapSampleX = Mathf.Clamp(x - 1, 0, chunkSize);
                float mapSampleZ = Mathf.Clamp(z - 1, 0, chunkSize);
                float dx = mapSampleX - sampleX;
                float dz = mapSampleZ - sampleZ;
                float distSqr = dx * dx + dz * dz;

                if (distSqr > radiusSqr)
                    continue;

                float worldX = chunkCoord.x * chunkSize + mapSampleX;
                float worldZ = chunkCoord.z * chunkSize + mapSampleZ;
                float breakup = Sample01(worldX, worldZ, ForestOrganicFloorBreakupScale, seed + 6113);
                float falloff = 1f - Mathf.Sqrt(distSqr) / radius;
                falloff = falloff * falloff * (3f - 2f * falloff);

                float contribution = falloff * strength * Mathf.Lerp(0.18f, 0.9f, breakup);
                contribution *= 1f - fields.ClearingMap[x, z] * 0.55f;
                contribution *= 1f - fields.RockInfluenceMap[x, z] * 0.25f;

                fields.OrganicFloorIntentMap[x, z] = Mathf.Clamp01(
                    fields.OrganicFloorIntentMap[x, z] + contribution);
            }
        }
    }

    private static void ApplyRadialInfluence(
        float[,] targetMap,
        int chunkSize,
        float sampleX,
        float sampleZ,
        float radius,
        float strength)
    {
        float radiusSqr = radius * radius;
        int width = targetMap.GetLength(0);
        int height = targetMap.GetLength(1);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                float mapSampleX = Mathf.Clamp(x - 1, 0, chunkSize);
                float mapSampleZ = Mathf.Clamp(z - 1, 0, chunkSize);
                float dx = mapSampleX - sampleX;
                float dz = mapSampleZ - sampleZ;
                float distSqr = dx * dx + dz * dz;

                if (distSqr > radiusSqr)
                    continue;

                float falloff = 1f - Mathf.Sqrt(distSqr) / radius;
                falloff = falloff * falloff * (3f - 2f * falloff);
                targetMap[x, z] = Mathf.Max(targetMap[x, z], Mathf.Clamp01(falloff * strength));
            }
        }
    }

    private static float Sample01(float worldX, float worldZ, float scale, int seed)
    {
        NoiseSample2D sample = AnalyticValueNoise2D.Sample(worldX * scale, worldZ * scale, seed);
        return Mathf.Clamp01((sample.Value + 1f) * 0.5f);
    }

    private static float SmoothStep(float edge0, float edge1, float value)
    {
        float t = Mathf.Clamp01((value - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }

    private static int Hash(params int[] values)
    {
        unchecked
        {
            uint hash = 2166136261u;

            for (int i = 0; i < values.Length; i++)
            {
                hash ^= (uint)values[i];
                hash *= 16777619u;

                hash ^= hash >> 13;
                hash *= 1274126177u;
                hash ^= hash >> 16;
            }

            return (int)hash;
        }
    }

    private static float Hash01(int hash)
    {
        unchecked
        {
            uint value = (uint)hash;

            value ^= value >> 17;
            value *= 0xed5ad4bbu;
            value ^= value >> 11;
            value *= 0xac4c1b51u;
            value ^= value >> 15;
            value *= 0x31848babu;
            value ^= value >> 14;

            return value / 4294967295f;
        }
    }
}
