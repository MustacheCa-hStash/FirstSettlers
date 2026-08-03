using System.Collections.Generic;
using UnityEngine;

public static class FoliageGenerator
{
    public static void GenerateGrassForChunk(
        ChunkRecord record,
        GrassSettings grassSettings,
        TreeSettings treeSettings,
        int worldSeed,
        int chunkSize,
        float worldScale,
        float meshHeightMultiplier)
    {
        if (record.FoliageData == null)
        {
            record.FoliageData = new ChunkFoliageData();
        }

        ChunkFoliageData foliageData = record.FoliageData;

        int subChunksPerChunk = Mathf.Max(1, grassSettings.subChunksPerChunk);

        if (foliageData.nearGrassInstancesBySubChunk == null ||
            foliageData.subChunksPerChunk != subChunksPerChunk)
        {
            foliageData.InitializeNearGrass(subChunksPerChunk);
        }
        else
        {
            foliageData.ClearNearGrass();
        }

        if (record.SurfaceTypeMap == null || record.HeightMap == null)
            return;

        int cellsPerAxis = Mathf.Max(1, grassSettings.cellsPerAxis);
        float cellSize = (float)chunkSize / cellsPerAxis;
        float subChunkSize = (float)chunkSize / subChunksPerChunk;

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;

        float treeExclusionRadiusSqr =
            treeSettings.grassExclusionRadius * treeSettings.grassExclusionRadius;

        for (int cellZ = 0; cellZ < cellsPerAxis; cellZ++)
        {
            for (int cellX = 0; cellX < cellsPerAxis; cellX++)
            {
                int cellHash = Hash(
                    worldSeed,
                    grassSettings.seedOffset,
                    record.ChunkCoord.x,
                    record.ChunkCoord.z,
                    cellX,
                    cellZ,
                    713);

                float jitterStrength = Mathf.Clamp01(grassSettings.cellJitter);
                float jitterX = Mathf.Lerp(0.5f, Hash01(cellHash + 31), jitterStrength);
                float jitterZ = Mathf.Lerp(0.5f, Hash01(cellHash + 67), jitterStrength);

                float sampleX = (cellX + jitterX) * cellSize;
                float sampleZ = (cellZ + jitterZ) * cellSize;

                int mapX = Mathf.Clamp(Mathf.RoundToInt(sampleX), 0, chunkSize);
                int mapZ = Mathf.Clamp(Mathf.RoundToInt(sampleZ), 0, chunkSize);

                int paddedX = mapX + 1;
                int paddedZ = mapZ + 1;

                if (record.SurfaceTypeMap[paddedX, paddedZ] != SurfaceType.Grass)
                    continue;

                float localX = (topLeftX + sampleX) * worldScale;
                float localZ = (bottomLeftZ + sampleZ) * worldScale;

                if (IsInsideTreeExclusion(
                    localX,
                    localZ,
                    foliageData.treeCubeInstances,
                    treeExclusionRadiusSqr))
                {
                    continue;
                }

                float height = SampleHeightBilinear(
                    record.HeightMap,
                    sampleX,
                    sampleZ,
                    chunkSize);

                float localY = height * meshHeightMultiplier * worldScale;

                float yaw = 0f;
                if (grassSettings.randomizeYaw)
                {
                    yaw = GetDeterministicYaw(
                        worldSeed,
                        grassSettings.seedOffset,
                        record.ChunkCoord,
                        cellX,
                        cellZ);
                }

                float uniformScale = GetDeterministicScale(
                    worldSeed,
                    grassSettings.seedOffset,
                    record.ChunkCoord,
                    cellX,
                    cellZ,
                    grassSettings.uniformScaleRange);

                uint selectionRank = GetDeterministicSelectionRank(
                    worldSeed,
                    grassSettings.seedOffset,
                    record.ChunkCoord,
                    cellX,
                    cellZ);

                Vector3 localPosition = new Vector3(localX, localY, localZ);
                Quaternion localRotation = Quaternion.Euler(0f, yaw, 0f);
                Vector3 localScale = Vector3.one * uniformScale;

                int subChunkX = Mathf.Clamp(
                    Mathf.FloorToInt(sampleX / subChunkSize),
                    0,
                    subChunksPerChunk - 1);

                int subChunkZ = Mathf.Clamp(
                    Mathf.FloorToInt(sampleZ / subChunkSize),
                    0,
                    subChunksPerChunk - 1);

                foliageData.nearGrassInstancesBySubChunk[subChunkX, subChunkZ].Add(
                    new FoliageInstanceData(
                        localPosition,
                        localRotation,
                        localScale,
                        selectionRank));
            }
        }

        SortSubChunkBucketsBySelectionRank(foliageData);
        foliageData.nearGrassGenerated = true;
    }

    public static void GenerateBillboardGrassForChunk(
        ChunkRecord record,
        GrassSettings grassSettings,
        int worldSeed,
        int chunkSize,
        float worldScale,
        float meshHeightMultiplier)
    {
        if (record.FoliageData == null)
        {
            record.FoliageData = new ChunkFoliageData();
        }

        ChunkFoliageData foliageData = record.FoliageData;
        foliageData.ClearBillboards();

        if (record.SurfaceTypeMap == null || record.HeightMap == null)
            return;

        int cellsPerAxis = Mathf.Max(1, grassSettings.billboardCellsPerAxis);
        float cellSize = (float)chunkSize / cellsPerAxis;

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;

        for (int cellZ = 0; cellZ < cellsPerAxis; cellZ++)
        {
            for (int cellX = 0; cellX < cellsPerAxis; cellX++)
            {
                int baseHash = Hash(
                    worldSeed,
                    grassSettings.billboardSeedOffset,
                    record.ChunkCoord.x,
                    record.ChunkCoord.z,
                    cellX,
                    cellZ,
                    211);

                float spawnRoll = Hash01(baseHash);
                if (spawnRoll > grassSettings.billboardSpawnChance)
                    continue;

                float offsetX = Hash01(baseHash + 31);
                float offsetZ = Hash01(baseHash + 67);

                float sampleX = (cellX + offsetX) * cellSize;
                float sampleZ = (cellZ + offsetZ) * cellSize;

                sampleX = Mathf.Clamp(sampleX, 0f, chunkSize);
                sampleZ = Mathf.Clamp(sampleZ, 0f, chunkSize);

                int mapX = Mathf.Clamp(Mathf.RoundToInt(sampleX), 0, chunkSize);
                int mapZ = Mathf.Clamp(Mathf.RoundToInt(sampleZ), 0, chunkSize);

                int paddedX = mapX + 1;
                int paddedZ = mapZ + 1;

                if (record.SurfaceTypeMap[paddedX, paddedZ] != SurfaceType.Grass)
                    continue;

                float height = SampleHeightBilinear(
                    record.HeightMap,
                    sampleX,
                    sampleZ,
                    chunkSize);

                float localX = (topLeftX + sampleX) * worldScale;
                float localZ = (bottomLeftZ + sampleZ) * worldScale;
                float localY = height * meshHeightMultiplier * worldScale;

                float yaw = 0f;
                if (grassSettings.randomizeBillboardYaw)
                {
                    yaw = Hash01(baseHash + 97) * 360f;
                }

                float uniformScale = Mathf.Lerp(
                    grassSettings.billboardUniformScaleRange.x,
                    grassSettings.billboardUniformScaleRange.y,
                    Hash01(baseHash + 131));

                foliageData.billboardGrassInstances.Add(
                    new BillboardFoliageInstanceData(
                        new Vector3(localX, localY, localZ),
                        Quaternion.Euler(0f, yaw, 0f),
                        Vector3.one * uniformScale));
            }
        }

        foliageData.billboardGenerated = true;
    }

    public static void GenerateFlowersForChunk(
        ChunkRecord record,
        FlowerSettings flowerSettings,
        int worldSeed,
        int chunkSize,
        float worldScale,
        float meshHeightMultiplier)
    {
        if (record.FoliageData == null)
        {
            record.FoliageData = new ChunkFoliageData();
        }

        ChunkFoliageData foliageData = record.FoliageData;
        foliageData.ClearFlowers();

        if (flowerSettings == null || !flowerSettings.enableFlowers)
            return;

        if (record.SurfaceTypeMap == null || record.HeightMap == null || record.BiomeMap == null)
            return;

        float chunkSampleMinX = record.ChunkCoord.x * chunkSize;
        float chunkSampleMinZ = record.ChunkCoord.z * chunkSize;
        float chunkSampleMaxX = chunkSampleMinX + chunkSize;
        float chunkSampleMaxZ = chunkSampleMinZ + chunkSize;

        float patchCellSize = Mathf.Max(0.1f, flowerSettings.patchCellSize);
        float maxPatchRadius = Mathf.Max(
            Mathf.Max(flowerSettings.patchRadiusRange.x, flowerSettings.patchRadiusRange.y),
            0f);
        float padding = maxPatchRadius + patchCellSize;

        float expandedMinX = chunkSampleMinX - padding;
        float expandedMaxX = chunkSampleMaxX + padding;
        float expandedMinZ = chunkSampleMinZ - padding;
        float expandedMaxZ = chunkSampleMaxZ + padding;

        int globalCellMinX = Mathf.FloorToInt(expandedMinX / patchCellSize);
        int globalCellMaxX = Mathf.FloorToInt(expandedMaxX / patchCellSize);
        int globalCellMinZ = Mathf.FloorToInt(expandedMinZ / patchCellSize);
        int globalCellMaxZ = Mathf.FloorToInt(expandedMaxZ / patchCellSize);

        int maxPatchCentersPerCell = Mathf.Max(1, flowerSettings.maxPatchCentersPerCell);
        int minFlowersPerPatch = Mathf.Max(1, flowerSettings.minFlowersPerPatch);
        int maxFlowersPerPatch = Mathf.Max(minFlowersPerPatch, flowerSettings.maxFlowersPerPatch);
        float patchNoiseScale = Mathf.Max(0.0001f, flowerSettings.patchNoiseScale);
        float patchNoiseThreshold = Mathf.Clamp01(flowerSettings.patchNoiseThreshold);

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;

        float treeExclusionRadiusSqr =
            flowerSettings.treeExclusionRadius * flowerSettings.treeExclusionRadius;

        for (int globalCellZ = globalCellMinZ; globalCellZ <= globalCellMaxZ; globalCellZ++)
        {
            for (int globalCellX = globalCellMinX; globalCellX <= globalCellMaxX; globalCellX++)
            {
                for (int patchIndex = 0; patchIndex < maxPatchCentersPerCell; patchIndex++)
                {
                    int patchHash = Hash(
                        worldSeed,
                        flowerSettings.seedOffset,
                        globalCellX,
                        globalCellZ,
                        patchIndex,
                        421);

                    float centerGlobalSampleX =
                        (globalCellX + Hash01(patchHash + 31)) * patchCellSize;
                    float centerGlobalSampleZ =
                        (globalCellZ + Hash01(patchHash + 67)) * patchCellSize;

                    NoiseSample2D patchNoise = AnalyticValueNoise2D.Sample(
                        centerGlobalSampleX * patchNoiseScale,
                        centerGlobalSampleZ * patchNoiseScale,
                        worldSeed + flowerSettings.seedOffset);

                    float patchNoise01 = Mathf.Clamp01((patchNoise.Value + 1f) * 0.5f);
                    if (patchNoise01 < patchNoiseThreshold)
                        continue;

                    float patchStrength = patchNoiseThreshold >= 0.999f
                        ? 1f
                        : Mathf.InverseLerp(patchNoiseThreshold, 1f, patchNoise01);

                    float spawnChance = Mathf.Clamp01(flowerSettings.patchSpawnChance * patchStrength);
                    if (Hash01(patchHash + 103) > spawnChance)
                        continue;

                    float patchRadius = Mathf.Lerp(
                        flowerSettings.patchRadiusRange.x,
                        flowerSettings.patchRadiusRange.y,
                        Hash01(patchHash + 139));

                    patchRadius = Mathf.Max(0f, patchRadius);

                    if (centerGlobalSampleX + patchRadius < chunkSampleMinX ||
                        centerGlobalSampleX - patchRadius > chunkSampleMaxX ||
                        centerGlobalSampleZ + patchRadius < chunkSampleMinZ ||
                        centerGlobalSampleZ - patchRadius > chunkSampleMaxZ)
                    {
                        continue;
                    }

                    int flowersInPatch = GetDeterministicCount(
                        minFlowersPerPatch,
                        maxFlowersPerPatch,
                        patchHash + 173);

                    for (int flowerIndex = 0; flowerIndex < flowersInPatch; flowerIndex++)
                    {
                        int flowerHash = Hash(
                            worldSeed,
                            flowerSettings.seedOffset,
                            globalCellX,
                            globalCellZ,
                            patchIndex,
                            flowerIndex,
                            557);

                        float angle = Hash01(flowerHash + 19) * Mathf.PI * 2f;
                        float radius = Mathf.Sqrt(Hash01(flowerHash + 41)) * patchRadius;

                        float globalSampleX = centerGlobalSampleX + Mathf.Cos(angle) * radius;
                        float globalSampleZ = centerGlobalSampleZ + Mathf.Sin(angle) * radius;

                        if (globalSampleX < chunkSampleMinX || globalSampleX > chunkSampleMaxX ||
                            globalSampleZ < chunkSampleMinZ || globalSampleZ > chunkSampleMaxZ)
                        {
                            continue;
                        }

                        float localSampleX = globalSampleX - chunkSampleMinX;
                        float localSampleZ = globalSampleZ - chunkSampleMinZ;

                        int mapX = Mathf.Clamp(Mathf.RoundToInt(localSampleX), 0, chunkSize);
                        int mapZ = Mathf.Clamp(Mathf.RoundToInt(localSampleZ), 0, chunkSize);
                        int paddedX = mapX + 1;
                        int paddedZ = mapZ + 1;

                        if (!IsValidFlowerSample(record, flowerSettings, paddedX, paddedZ))
                            continue;

                        float localX = (topLeftX + localSampleX) * worldScale;
                        float localZ = (bottomLeftZ + localSampleZ) * worldScale;

                        if (flowerSettings.treeExclusionRadius > 0f &&
                            IsInsideTreeExclusion(
                                localX,
                                localZ,
                                foliageData.treeCubeInstances,
                                treeExclusionRadiusSqr))
                        {
                            continue;
                        }

                        float height = SampleHeightBilinear(
                            record.HeightMap,
                            localSampleX,
                            localSampleZ,
                            chunkSize);

                        float localY = height * meshHeightMultiplier * worldScale;

                        float yaw = flowerSettings.randomizeYaw
                            ? Hash01(flowerHash + 83) * 360f
                            : 0f;

                        float uniformScale = Mathf.Lerp(
                            flowerSettings.uniformScaleRange.x,
                            flowerSettings.uniformScaleRange.y,
                            Hash01(flowerHash + 127));

                        BiomeType biome = record.BiomeMap[paddedX, paddedZ];
                        Color32 petalColor = GetDeterministicPetalColor(
                            flowerSettings,
                            biome,
                            flowerHash);

                        foliageData.flowerInstances.Add(new FlowerInstanceData(
                            new Vector3(localX, localY, localZ),
                            Quaternion.Euler(0f, yaw, 0f),
                            Vector3.one * uniformScale,
                            petalColor));
                    }
                }
            }
        }

        foliageData.flowersGenerated = true;
    }

    public static void GenerateTreeCubesForChunk(
    ChunkRecord record,
    TreeSettings treeSettings,
    int worldSeed,
    int chunkSize,
    float worldScale,
    float meshHeightMultiplier)
    {
        if (record.FoliageData == null)
        {
            record.FoliageData = new ChunkFoliageData();
        }

        ChunkFoliageData foliageData = record.FoliageData;
        foliageData.ClearTreeCubes();

        if (record.SurfaceTypeMap == null || record.HeightMap == null)
            return;

        float chunkSampleMinX = record.ChunkCoord.x * chunkSize;
        float chunkSampleMinZ = record.ChunkCoord.z * chunkSize;
        float chunkSampleMaxX = chunkSampleMinX + chunkSize;
        float chunkSampleMaxZ = chunkSampleMinZ + chunkSize;

        float minDistanceSampleUnits = treeSettings.treeMinDistance / worldScale;
        float padding = minDistanceSampleUnits + treeSettings.treeCellSize;

        float expandedMinX = chunkSampleMinX - padding;
        float expandedMaxX = chunkSampleMaxX + padding;
        float expandedMinZ = chunkSampleMinZ - padding;
        float expandedMaxZ = chunkSampleMaxZ + padding;

        int globalCellMinX = Mathf.FloorToInt(expandedMinX / treeSettings.treeCellSize);
        int globalCellMaxX = Mathf.FloorToInt(expandedMaxX / treeSettings.treeCellSize);
        int globalCellMinZ = Mathf.FloorToInt(expandedMinZ / treeSettings.treeCellSize);
        int globalCellMaxZ = Mathf.FloorToInt(expandedMaxZ / treeSettings.treeCellSize);

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;

        List<TreeCandidateData> candidates = new List<TreeCandidateData>();

        for (int globalCellZ = globalCellMinZ; globalCellZ <= globalCellMaxZ; globalCellZ++)
        {
            for (int globalCellX = globalCellMinX; globalCellX <= globalCellMaxX; globalCellX++)
            {
                int baseHash = Hash(
                    worldSeed,
                    treeSettings.seedOffset,
                    globalCellX,
                    globalCellZ,
                    311);

                float spawnRoll = Hash01(baseHash);
                if (spawnRoll > treeSettings.treeSpawnChance)
                    continue;

                float offsetX = Hash01(baseHash + 37);
                float offsetZ = Hash01(baseHash + 73);

                float globalSampleX = (globalCellX + offsetX) * treeSettings.treeCellSize;
                float globalSampleZ = (globalCellZ + offsetZ) * treeSettings.treeCellSize;

                if (globalSampleX < expandedMinX || globalSampleX > expandedMaxX ||
                    globalSampleZ < expandedMinZ || globalSampleZ > expandedMaxZ)
                {
                    continue;
                }

                float localSampleX = globalSampleX - chunkSampleMinX;
                float localSampleZ = globalSampleZ - chunkSampleMinZ;

                float localX = (topLeftX + localSampleX) * worldScale;
                float localZ = (bottomLeftZ + localSampleZ) * worldScale;

                float globalUnityX = globalSampleX * worldScale;
                float globalUnityZ = globalSampleZ * worldScale;

                float yaw = Hash01(baseHash + 109) * 360f;

                float uniformScale = Mathf.Lerp(
                    treeSettings.treeUniformScaleRange.x,
                    treeSettings.treeUniformScaleRange.y,
                    Hash01(baseHash + 151));

                uint priority = (uint)Hash(
                    worldSeed,
                    treeSettings.seedOffset,
                    globalCellX,
                    globalCellZ,
                    919);

                candidates.Add(new TreeCandidateData(
                    new Vector3(localX, 0f, localZ),
                    globalUnityX,
                    globalUnityZ,
                    Quaternion.Euler(0f, yaw, 0f),
                    Vector3.one * uniformScale,
                    priority,
                    localSampleX,
                    localSampleZ));
            }
        }

        candidates.Sort((a, b) => a.priority.CompareTo(b.priority));

        List<TreeCandidateData> acceptedCandidates = new List<TreeCandidateData>();
        float minDistanceSqr = treeSettings.treeMinDistance * treeSettings.treeMinDistance;

        for (int i = 0; i < candidates.Count; i++)
        {
            TreeCandidateData candidate = candidates[i];

            bool tooClose = false;

            for (int j = 0; j < acceptedCandidates.Count; j++)
            {
                TreeCandidateData accepted = acceptedCandidates[j];

                float dx = candidate.globalUnityX - accepted.globalUnityX;
                float dz = candidate.globalUnityZ - accepted.globalUnityZ;

                float distSqr = dx * dx + dz * dz;

                if (distSqr < minDistanceSqr)
                {
                    tooClose = true;
                    break;
                }
            }

            if (tooClose)
                continue;

            acceptedCandidates.Add(candidate);
        }

        for (int i = 0; i < acceptedCandidates.Count; i++)
        {
            TreeCandidateData candidate = acceptedCandidates[i];

            if (candidate.localSampleX < 0f || candidate.localSampleX > chunkSize ||
                candidate.localSampleZ < 0f || candidate.localSampleZ > chunkSize)
            {
                continue;
            }

            int mapX = Mathf.Clamp(Mathf.RoundToInt(candidate.localSampleX), 0, chunkSize);
            int mapZ = Mathf.Clamp(Mathf.RoundToInt(candidate.localSampleZ), 0, chunkSize);

            int paddedX = mapX + 1;
            int paddedZ = mapZ + 1;

            if (record.SurfaceTypeMap[paddedX, paddedZ] != SurfaceType.Grass)
                continue;

            float height = SampleHeightBilinear(
                record.HeightMap,
                candidate.localSampleX,
                candidate.localSampleZ,
                chunkSize);

            Vector3 finalLocalPosition = new Vector3(
                candidate.localPosition.x,
                height * meshHeightMultiplier * worldScale,
                candidate.localPosition.z);

            foliageData.treeCubeInstances.Add(new TreeInstanceData(
                finalLocalPosition,
                candidate.localRotation,
                candidate.localScale));
        }

        foliageData.treeCubesGenerated = true;
    }

    private static bool IsInsideTreeExclusion(
        float localX,
        float localZ,
        List<TreeInstanceData> treeInstances,
        float exclusionRadiusSqr)
    {
        for (int i = 0; i < treeInstances.Count; i++)
        {
            TreeInstanceData tree = treeInstances[i];

            float dx = localX - tree.localPosition.x;
            float dz = localZ - tree.localPosition.z;

            float distSqr = dx * dx + dz * dz;

            if (distSqr < exclusionRadiusSqr)
                return true;
        }

        return false;
    }

    private static bool IsValidFlowerSample(
        ChunkRecord record,
        FlowerSettings flowerSettings,
        int paddedX,
        int paddedZ)
    {
        if (record.SurfaceTypeMap[paddedX, paddedZ] != SurfaceType.Grass)
            return false;

        BiomeType biome = record.BiomeMap[paddedX, paddedZ];
        if (!flowerSettings.AllowsBiome(biome))
            return false;

        if (record.SlopeMap != null &&
            record.SlopeMap[paddedX, paddedZ] > flowerSettings.maxSlope)
        {
            return false;
        }

        return true;
    }

    private static int GetDeterministicCount(int minCount, int maxCount, int hash)
    {
        if (maxCount <= minCount)
            return minCount;

        int range = maxCount - minCount + 1;
        int offset = Mathf.FloorToInt(Hash01(hash) * range);
        return Mathf.Clamp(minCount + offset, minCount, maxCount);
    }

    private static Color32 GetDeterministicPetalColor(
        FlowerSettings flowerSettings,
        BiomeType biome,
        int hash)
    {
        Color baseColor = flowerSettings.GetBasePetalColor(biome, Hash01(hash + 271));
        float variation = Mathf.Max(0f, flowerSettings.petalColorVariation);
        float brightness = Mathf.Lerp(1f - variation, 1f + variation, Hash01(hash + 307));

        baseColor.r = Mathf.Clamp01(baseColor.r * brightness);
        baseColor.g = Mathf.Clamp01(baseColor.g * brightness);
        baseColor.b = Mathf.Clamp01(baseColor.b * brightness);
        baseColor.a = 1f;

        return (Color32)baseColor;
    }

    private static void SortSubChunkBucketsBySelectionRank(ChunkFoliageData foliageData)
    {
        int subChunksPerChunk = foliageData.subChunksPerChunk;

        for (int x = 0; x < subChunksPerChunk; x++)
        {
            for (int z = 0; z < subChunksPerChunk; z++)
            {
                foliageData.nearGrassInstancesBySubChunk[x, z].Sort((a, b) =>
                    a.selectionRank.CompareTo(b.selectionRank));
            }
        }
    }

    private static float GetDeterministicYaw(
        int worldSeed,
        int seedOffset,
        ChunkCoord chunkCoord,
        int cellX,
        int cellZ)
    {
        int hash = Hash(worldSeed, seedOffset, chunkCoord.x, chunkCoord.z, cellX, cellZ, 17);
        float t = Hash01(hash);
        return t * 360f;
    }

    private static float GetDeterministicScale(
        int worldSeed,
        int seedOffset,
        ChunkCoord chunkCoord,
        int cellX,
        int cellZ,
        Vector2 scaleRange)
    {
        int hash = Hash(worldSeed, seedOffset, chunkCoord.x, chunkCoord.z, cellX, cellZ, 29);
        float t = Hash01(hash);
        return Mathf.Lerp(scaleRange.x, scaleRange.y, t);
    }

    private static uint GetDeterministicSelectionRank(
        int worldSeed,
        int seedOffset,
        ChunkCoord chunkCoord,
        int cellX,
        int cellZ)
    {
        unchecked
        {
            return (uint)Hash(worldSeed, seedOffset, chunkCoord.x, chunkCoord.z, cellX, cellZ, 101);
        }
    }

    private struct TreeCandidateData
    {
        public Vector3 localPosition;
        public float globalUnityX;
        public float globalUnityZ;
        public Quaternion localRotation;
        public Vector3 localScale;
        public uint priority;
        public float localSampleX;
        public float localSampleZ;

        public TreeCandidateData(
            Vector3 localPosition,
            float globalUnityX,
            float globalUnityZ,
            Quaternion localRotation,
            Vector3 localScale,
            uint priority,
            float localSampleX,
            float localSampleZ)
        {
            this.localPosition = localPosition;
            this.globalUnityX = globalUnityX;
            this.globalUnityZ = globalUnityZ;
            this.localRotation = localRotation;
            this.localScale = localScale;
            this.priority = priority;
            this.localSampleX = localSampleX;
            this.localSampleZ = localSampleZ;
        }
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

    private static float SampleHeightBilinear(
        float[,] heightMap,
        float sampleX,
        float sampleZ,
        int chunkSize)
    {
        float x = Mathf.Clamp(sampleX, 0f, chunkSize);
        float z = Mathf.Clamp(sampleZ, 0f, chunkSize);

        int x0 = Mathf.FloorToInt(x);
        int z0 = Mathf.FloorToInt(z);
        int x1 = Mathf.Min(x0 + 1, chunkSize);
        int z1 = Mathf.Min(z0 + 1, chunkSize);

        float tx = x - x0;
        float tz = z - z0;

        int px0 = x0 + 1;
        int pz0 = z0 + 1;
        int px1 = x1 + 1;
        int pz1 = z1 + 1;

        float h00 = heightMap[px0, pz0];
        float h10 = heightMap[px1, pz0];
        float h01 = heightMap[px0, pz1];
        float h11 = heightMap[px1, pz1];

        float hx0 = Mathf.Lerp(h00, h10, tx);
        float hx1 = Mathf.Lerp(h01, h11, tx);

        return Mathf.Lerp(hx0, hx1, tz);
    }
}
