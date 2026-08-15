using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
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
        int subChunksPerChunk = Mathf.Max(1, grassSettings.subChunksPerChunk);
        EnsureNearGrassStorage(record, subChunksPerChunk);

        for (int localSubChunkX = 0; localSubChunkX < subChunksPerChunk; localSubChunkX++)
        {
            for (int localSubChunkZ = 0; localSubChunkZ < subChunksPerChunk; localSubChunkZ++)
            {
                GenerateGrassForSubChunk(
                    record,
                    grassSettings,
                    treeSettings,
                    worldSeed,
                    chunkSize,
                    worldScale,
                    meshHeightMultiplier,
                    localSubChunkX,
                    localSubChunkZ);
            }
        }
    }

    public static void GenerateGrassForSubChunk(
        ChunkRecord record,
        GrassSettings grassSettings,
        TreeSettings treeSettings,
        int worldSeed,
        int chunkSize,
        float worldScale,
        float meshHeightMultiplier,
        int localSubChunkX,
        int localSubChunkZ)
    {
        int subChunksPerChunk = Mathf.Max(1, grassSettings.subChunksPerChunk);
        EnsureNearGrassStorage(record, subChunksPerChunk);

        ChunkFoliageData foliageData = record.FoliageData;

        localSubChunkX = Mathf.Clamp(localSubChunkX, 0, subChunksPerChunk - 1);
        localSubChunkZ = Mathf.Clamp(localSubChunkZ, 0, subChunksPerChunk - 1);

        foliageData.ClearNearGrassSubChunk(localSubChunkX, localSubChunkZ);

        if (record.SurfaceTypeMap == null || record.HeightMap == null || record.BiomeMap == null)
        {
            foliageData.MarkNearGrassSubChunkGenerated(localSubChunkX, localSubChunkZ);
            return;
        }

        int cellsPerAxis = Mathf.Max(1, grassSettings.cellsPerAxis);
        float cellSize = (float)chunkSize / cellsPerAxis;
        float subChunkSize = (float)chunkSize / subChunksPerChunk;
        float subChunkMinX = localSubChunkX * subChunkSize;
        float subChunkMinZ = localSubChunkZ * subChunkSize;
        float subChunkMaxX = localSubChunkX == subChunksPerChunk - 1
            ? chunkSize
            : subChunkMinX + subChunkSize;
        float subChunkMaxZ = localSubChunkZ == subChunksPerChunk - 1
            ? chunkSize
            : subChunkMinZ + subChunkSize;
        int startCellX = Mathf.Clamp(Mathf.FloorToInt(subChunkMinX / cellSize) - 1, 0, cellsPerAxis - 1);
        int endCellX = Mathf.Clamp(Mathf.CeilToInt(subChunkMaxX / cellSize), 0, cellsPerAxis - 1);
        int startCellZ = Mathf.Clamp(Mathf.FloorToInt(subChunkMinZ / cellSize) - 1, 0, cellsPerAxis - 1);
        int endCellZ = Mathf.Clamp(Mathf.CeilToInt(subChunkMaxZ / cellSize), 0, cellsPerAxis - 1);

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;

        float treeExclusionRadiusSqr = treeSettings != null
            ? treeSettings.grassExclusionRadius * treeSettings.grassExclusionRadius
            : 0f;
        float bushExclusionRadiusSqr = treeSettings != null
            ? treeSettings.bushGrassExclusionRadius * treeSettings.bushGrassExclusionRadius
            : 0f;
        float rockExclusionRadiusSqr = treeSettings != null
            ? treeSettings.rockGrassExclusionRadius * treeSettings.rockGrassExclusionRadius
            : 0f;

        int cellCountX = endCellX - startCellX + 1;
        int cellCountZ = endCellZ - startCellZ + 1;
        int candidateCount = cellCountX * cellCountZ;

        NativeArray<float> heightMap = FlattenFloatMap(record.HeightMap, Allocator.TempJob, out int heightMapWidth, out int heightMapHeight);
        NativeArray<SurfaceType> surfaceMap = FlattenSurfaceMap(record.SurfaceTypeMap, Allocator.TempJob, out int surfaceMapWidth, out int surfaceMapHeight);
        NativeArray<BiomeType> biomeMap = FlattenBiomeMap(record.BiomeMap, Allocator.TempJob, out int biomeMapWidth, out int biomeMapHeight);
        NativeArray<GroundCoverType> groundCoverMap = FlattenGroundCoverMap(record.GroundCoverMap, Allocator.TempJob, out int groundCoverMapWidth, out int groundCoverMapHeight);
        NativeArray<float2> treeExclusionPositions = CreateTreeExclusionPositions(foliageData.treeCubeInstances, Allocator.TempJob);
        NativeArray<float2> bushExclusionPositions = CreateTreeExclusionPositions(foliageData.bushInstances, Allocator.TempJob);
        NativeArray<float2> rockExclusionPositions = CreateRockExclusionPositions(foliageData.rockInstances, Allocator.TempJob);
        NativeArray<GrassSubChunkDiscoveryResult> results =
            new NativeArray<GrassSubChunkDiscoveryResult>(candidateCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        try
        {
            GrassSubChunkDiscoveryJob job = new GrassSubChunkDiscoveryJob
            {
                heightMap = heightMap,
                heightMapWidth = heightMapWidth,
                heightMapHeight = heightMapHeight,
                surfaceMap = surfaceMap,
                surfaceMapWidth = surfaceMapWidth,
                surfaceMapHeight = surfaceMapHeight,
                biomeMap = biomeMap,
                biomeMapWidth = biomeMapWidth,
                biomeMapHeight = biomeMapHeight,
                groundCoverMap = groundCoverMap,
                groundCoverMapWidth = groundCoverMapWidth,
                groundCoverMapHeight = groundCoverMapHeight,
                hasGroundCoverMap = record.GroundCoverMap != null,
                treeExclusionPositions = treeExclusionPositions,
                bushExclusionPositions = bushExclusionPositions,
                rockExclusionPositions = rockExclusionPositions,
                treeExclusionRadiusSqr = treeExclusionRadiusSqr,
                bushExclusionRadiusSqr = bushExclusionRadiusSqr,
                rockExclusionRadiusSqr = rockExclusionRadiusSqr,
                results = results,
                worldSeed = worldSeed,
                seedOffset = grassSettings.seedOffset,
                chunkCoordX = record.ChunkCoord.x,
                chunkCoordZ = record.ChunkCoord.z,
                chunkSize = chunkSize,
                startCellX = startCellX,
                startCellZ = startCellZ,
                cellCountX = cellCountX,
                cellSize = cellSize,
                cellJitter = Mathf.Clamp01(grassSettings.cellJitter),
                subChunkMinX = subChunkMinX,
                subChunkMinZ = subChunkMinZ,
                subChunkMaxX = subChunkMaxX,
                subChunkMaxZ = subChunkMaxZ,
                includeMaxX = localSubChunkX == subChunksPerChunk - 1,
                includeMaxZ = localSubChunkZ == subChunksPerChunk - 1,
                topLeftX = topLeftX,
                bottomLeftZ = bottomLeftZ,
                worldScale = worldScale,
                meshHeightMultiplier = meshHeightMultiplier,
                randomizeYaw = grassSettings.randomizeYaw,
                minScale = grassSettings.uniformScaleRange.x,
                maxScale = grassSettings.uniformScaleRange.y
            };

            JobHandle handle = job.Schedule(candidateCount, 32);
            handle.Complete();

            List<FoliageInstanceData> subChunkInstances =
                foliageData.nearGrassInstancesBySubChunk[localSubChunkX, localSubChunkZ];

            for (int i = 0; i < results.Length; i++)
            {
                GrassSubChunkDiscoveryResult result = results[i];
                if (result.valid == 0)
                    continue;

                subChunkInstances.Add(new FoliageInstanceData(
                    new Vector3(result.localPosition.x, result.localPosition.y, result.localPosition.z),
                    Quaternion.Euler(0f, result.yaw, 0f),
                    Vector3.one * result.uniformScale,
                    result.selectionRank,
                    result.forestBlend));
            }
        }
        finally
        {
            if (heightMap.IsCreated)
                heightMap.Dispose();
            if (surfaceMap.IsCreated)
                surfaceMap.Dispose();
            if (biomeMap.IsCreated)
                biomeMap.Dispose();
            if (groundCoverMap.IsCreated)
                groundCoverMap.Dispose();
            if (treeExclusionPositions.IsCreated)
                treeExclusionPositions.Dispose();
            if (bushExclusionPositions.IsCreated)
                bushExclusionPositions.Dispose();
            if (rockExclusionPositions.IsCreated)
                rockExclusionPositions.Dispose();
            if (results.IsCreated)
                results.Dispose();
        }

        SortSubChunkBucketBySelectionRank(foliageData, localSubChunkX, localSubChunkZ);
        foliageData.MarkNearGrassSubChunkGenerated(localSubChunkX, localSubChunkZ);
    }

    public static void GenerateBillboardGrassForChunk(
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
        foliageData.ClearBillboards();

        if (record.SurfaceTypeMap == null || record.HeightMap == null || record.BiomeMap == null)
            return;

        int cellsPerAxis = Mathf.Max(1, grassSettings.billboardCellsPerAxis);
        float cellSize = (float)chunkSize / cellsPerAxis;

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;
        float bushExclusionRadiusSqr = 0f;
        float rockExclusionRadiusSqr = 0f;

        if (treeSettings != null)
        {
            bushExclusionRadiusSqr =
                treeSettings.bushGrassExclusionRadius * treeSettings.bushGrassExclusionRadius;
            rockExclusionRadiusSqr =
                treeSettings.rockGrassExclusionRadius * treeSettings.rockGrassExclusionRadius;
        }

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

                if (!AllowsInstancedGrass(record, paddedX, paddedZ))
                    continue;

                float forestBlend = GetGrassForestBlend(record, paddedX, paddedZ);

                float height = SampleHeightBilinear(
                    record.HeightMap,
                    sampleX,
                    sampleZ,
                    chunkSize);

                float localX = (topLeftX + sampleX) * worldScale;
                float localZ = (bottomLeftZ + sampleZ) * worldScale;
                float localY = height * meshHeightMultiplier * worldScale;

                if (treeSettings != null &&
                    ((bushExclusionRadiusSqr > 0f && IsInsideTreeExclusion(
                        localX,
                        localZ,
                        foliageData.bushInstances,
                        bushExclusionRadiusSqr)) ||
                     (rockExclusionRadiusSqr > 0f && IsInsideRockExclusion(
                        localX,
                        localZ,
                        foliageData.rockInstances,
                        rockExclusionRadiusSqr))))
                {
                    continue;
                }

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
                        Vector3.one * uniformScale,
                        forestBlend));
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

        if (record.SurfaceTypeMap == null || record.HeightMap == null || record.WorldFeaturePlan == null)
        {
            foliageData.treeCubesGenerated = true;
            return;
        }

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;

        for (int i = 0; i < record.WorldFeaturePlan.Placements.Count; i++)
        {
            WorldFeaturePlacement placement = record.WorldFeaturePlan.Placements[i];

            if (placement.featureType != WorldFeatureType.Tree)
                continue;

            if (placement.sampleX < 0f || placement.sampleX > chunkSize ||
                placement.sampleZ < 0f || placement.sampleZ > chunkSize)
                continue;

            int mapX = Mathf.Clamp(Mathf.RoundToInt(placement.sampleX), 0, chunkSize);
            int mapZ = Mathf.Clamp(Mathf.RoundToInt(placement.sampleZ), 0, chunkSize);

            int paddedX = mapX + 1;
            int paddedZ = mapZ + 1;

            if (record.SurfaceTypeMap[paddedX, paddedZ] != SurfaceType.Grass)
                continue;

            float height = SampleHeightBilinear(
                record.HeightMap,
                placement.sampleX,
                placement.sampleZ,
                chunkSize);

            Vector3 finalLocalPosition = new Vector3(
                (topLeftX + placement.sampleX) * worldScale,
                height * meshHeightMultiplier * worldScale,
                (bottomLeftZ + placement.sampleZ) * worldScale);

            GetDeterministicTreeColors(
                placement.variant,
                worldSeed,
                treeSettings != null ? treeSettings.seedOffset : 0,
                record.ChunkCoord,
                placement.sampleX,
                placement.sampleZ,
                out Color32 leafTint,
                out Color32 barkTint);

            foliageData.treeCubeInstances.Add(new TreeInstanceData(
                finalLocalPosition,
                placement.rotation,
                placement.scale,
                placement.variant,
                leafTint,
                barkTint));
        }

        foliageData.treeCubesGenerated = true;
    }

    public static void GenerateBushesForChunk(
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
        foliageData.ClearBushes();

        if (record.SurfaceTypeMap == null || record.HeightMap == null || record.WorldFeaturePlan == null)
        {
            foliageData.bushesGenerated = true;
            return;
        }

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;

        for (int i = 0; i < record.WorldFeaturePlan.Placements.Count; i++)
        {
            WorldFeaturePlacement placement = record.WorldFeaturePlan.Placements[i];

            if (placement.featureType != WorldFeatureType.Bush)
                continue;

            if (placement.sampleX < 0f || placement.sampleX > chunkSize ||
                placement.sampleZ < 0f || placement.sampleZ > chunkSize)
                continue;

            int mapX = Mathf.Clamp(Mathf.RoundToInt(placement.sampleX), 0, chunkSize);
            int mapZ = Mathf.Clamp(Mathf.RoundToInt(placement.sampleZ), 0, chunkSize);

            int paddedX = mapX + 1;
            int paddedZ = mapZ + 1;

            if (record.SurfaceTypeMap[paddedX, paddedZ] != SurfaceType.Grass)
                continue;

            float height = SampleHeightBilinear(
                record.HeightMap,
                placement.sampleX,
                placement.sampleZ,
                chunkSize);

            Vector3 finalLocalPosition = new Vector3(
                (topLeftX + placement.sampleX) * worldScale,
                height * meshHeightMultiplier * worldScale,
                (bottomLeftZ + placement.sampleZ) * worldScale);

            foliageData.bushInstances.Add(new TreeInstanceData(
                finalLocalPosition,
                placement.rotation,
                placement.scale,
                placement.variant));
        }

        foliageData.bushesGenerated = true;
    }

    public static void GenerateRocksForChunk(
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
        foliageData.ClearRocks();

        if (record.SurfaceTypeMap == null || record.HeightMap == null || record.WorldFeaturePlan == null)
        {
            foliageData.rocksGenerated = true;
            return;
        }

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;

        for (int i = 0; i < record.WorldFeaturePlan.Placements.Count; i++)
        {
            WorldFeaturePlacement placement = record.WorldFeaturePlan.Placements[i];

            if (placement.featureType != WorldFeatureType.Boulder)
                continue;

            if (placement.sampleX < 0f || placement.sampleX > chunkSize ||
                placement.sampleZ < 0f || placement.sampleZ > chunkSize)
                continue;

            int mapX = Mathf.Clamp(Mathf.RoundToInt(placement.sampleX), 0, chunkSize);
            int mapZ = Mathf.Clamp(Mathf.RoundToInt(placement.sampleZ), 0, chunkSize);

            int paddedX = mapX + 1;
            int paddedZ = mapZ + 1;

            if (record.SurfaceTypeMap[paddedX, paddedZ] != SurfaceType.Grass)
                continue;

            float height = SampleHeightBilinear(
                record.HeightMap,
                placement.sampleX,
                placement.sampleZ,
                chunkSize);

            Vector3 finalLocalPosition = new Vector3(
                (topLeftX + placement.sampleX) * worldScale,
                height * meshHeightMultiplier * worldScale,
                (bottomLeftZ + placement.sampleZ) * worldScale);

            foliageData.rockInstances.Add(new RockInstanceData(
                finalLocalPosition,
                placement.rotation,
                placement.scale,
                placement.prefabIndex));
        }

        foliageData.rocksGenerated = true;
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

    private static bool IsInsideRockExclusion(
        float localX,
        float localZ,
        List<RockInstanceData> rockInstances,
        float exclusionRadiusSqr)
    {
        for (int i = 0; i < rockInstances.Count; i++)
        {
            RockInstanceData rock = rockInstances[i];

            float dx = localX - rock.localPosition.x;
            float dz = localZ - rock.localPosition.z;

            float distSqr = dx * dx + dz * dz;

            if (distSqr < exclusionRadiusSqr)
                return true;
        }

        return false;
    }

    private static bool AllowsInstancedGrass(ChunkRecord record, int paddedX, int paddedZ)
    {
        if (record.SurfaceTypeMap[paddedX, paddedZ] != SurfaceType.Grass)
            return false;

        if (record.GroundCoverMap == null)
            return true;

        GroundCoverType groundCover = record.GroundCoverMap[paddedX, paddedZ];
        return groundCover == GroundCoverType.Default ||
               groundCover == GroundCoverType.DarkGrass;
    }

    private static float GetGrassForestBlend(ChunkRecord record, int paddedX, int paddedZ)
    {
        if (record.GroundCoverMap != null &&
            record.GroundCoverMap[paddedX, paddedZ] == GroundCoverType.DarkGrass)
        {
            return 1f;
        }

        if (record.BiomeMap != null &&
            record.BiomeMap[paddedX, paddedZ] == BiomeType.Forest)
        {
            return 1f;
        }

        return 0f;
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

    private static void SortSubChunkBucketBySelectionRank(
        ChunkFoliageData foliageData,
        int localSubChunkX,
        int localSubChunkZ)
    {
        foliageData.nearGrassInstancesBySubChunk[localSubChunkX, localSubChunkZ].Sort((a, b) =>
            a.selectionRank.CompareTo(b.selectionRank));
    }

    private static void EnsureNearGrassStorage(ChunkRecord record, int subChunksPerChunk)
    {
        if (record.FoliageData == null)
        {
            record.FoliageData = new ChunkFoliageData();
        }

        if (record.FoliageData.nearGrassInstancesBySubChunk == null ||
            record.FoliageData.nearGrassSubChunkGenerated == null ||
            record.FoliageData.subChunksPerChunk != subChunksPerChunk)
        {
            record.FoliageData.InitializeNearGrass(subChunksPerChunk);
        }
    }

    private static bool IsSampleInsideSubChunk(
        float sampleX,
        float sampleZ,
        float minX,
        float minZ,
        float maxX,
        float maxZ,
        bool includeMaxX,
        bool includeMaxZ)
    {
        bool insideX = includeMaxX
            ? sampleX >= minX && sampleX <= maxX
            : sampleX >= minX && sampleX < maxX;

        bool insideZ = includeMaxZ
            ? sampleZ >= minZ && sampleZ <= maxZ
            : sampleZ >= minZ && sampleZ < maxZ;

        return insideX && insideZ;
    }

    private static NativeArray<float> FlattenFloatMap(float[,] source, Allocator allocator, out int width, out int height)
    {
        if (source == null)
        {
            width = 0;
            height = 0;
            return new NativeArray<float>(0, allocator);
        }

        width = source.GetLength(0);
        height = source.GetLength(1);
        NativeArray<float> result = new NativeArray<float>(width * height, allocator, NativeArrayOptions.UninitializedMemory);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                result[FlattenIndex(x, z, height)] = source[x, z];
            }
        }

        return result;
    }

    private static NativeArray<SurfaceType> FlattenSurfaceMap(SurfaceType[,] source, Allocator allocator, out int width, out int height)
    {
        if (source == null)
        {
            width = 0;
            height = 0;
            return new NativeArray<SurfaceType>(0, allocator);
        }

        width = source.GetLength(0);
        height = source.GetLength(1);
        NativeArray<SurfaceType> result = new NativeArray<SurfaceType>(width * height, allocator, NativeArrayOptions.UninitializedMemory);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                result[FlattenIndex(x, z, height)] = source[x, z];
            }
        }

        return result;
    }

    private static NativeArray<BiomeType> FlattenBiomeMap(BiomeType[,] source, Allocator allocator, out int width, out int height)
    {
        if (source == null)
        {
            width = 0;
            height = 0;
            return new NativeArray<BiomeType>(0, allocator);
        }

        width = source.GetLength(0);
        height = source.GetLength(1);
        NativeArray<BiomeType> result = new NativeArray<BiomeType>(width * height, allocator, NativeArrayOptions.UninitializedMemory);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                result[FlattenIndex(x, z, height)] = source[x, z];
            }
        }

        return result;
    }

    private static NativeArray<GroundCoverType> FlattenGroundCoverMap(GroundCoverType[,] source, Allocator allocator, out int width, out int height)
    {
        if (source == null)
        {
            width = 0;
            height = 0;
            return new NativeArray<GroundCoverType>(0, allocator);
        }

        width = source.GetLength(0);
        height = source.GetLength(1);
        NativeArray<GroundCoverType> result = new NativeArray<GroundCoverType>(width * height, allocator, NativeArrayOptions.UninitializedMemory);

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                result[FlattenIndex(x, z, height)] = source[x, z];
            }
        }

        return result;
    }

    private static NativeArray<float2> CreateTreeExclusionPositions(List<TreeInstanceData> instances, Allocator allocator)
    {
        int count = instances != null ? instances.Count : 0;
        NativeArray<float2> result = new NativeArray<float2>(count, allocator, NativeArrayOptions.UninitializedMemory);

        for (int i = 0; i < count; i++)
        {
            Vector3 position = instances[i].localPosition;
            result[i] = new float2(position.x, position.z);
        }

        return result;
    }

    private static NativeArray<float2> CreateRockExclusionPositions(List<RockInstanceData> instances, Allocator allocator)
    {
        int count = instances != null ? instances.Count : 0;
        NativeArray<float2> result = new NativeArray<float2>(count, allocator, NativeArrayOptions.UninitializedMemory);

        for (int i = 0; i < count; i++)
        {
            Vector3 position = instances[i].localPosition;
            result[i] = new float2(position.x, position.z);
        }

        return result;
    }

    private static int FlattenIndex(int x, int z, int height)
    {
        return x * height + z;
    }

    private struct GrassSubChunkDiscoveryResult
    {
        public byte valid;
        public float3 localPosition;
        public float yaw;
        public float uniformScale;
        public uint selectionRank;
        public float forestBlend;
    }

    [BurstCompile]
    private struct GrassSubChunkDiscoveryJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> heightMap;
        public int heightMapWidth;
        public int heightMapHeight;
        [ReadOnly] public NativeArray<SurfaceType> surfaceMap;
        public int surfaceMapWidth;
        public int surfaceMapHeight;
        [ReadOnly] public NativeArray<BiomeType> biomeMap;
        public int biomeMapWidth;
        public int biomeMapHeight;
        [ReadOnly] public NativeArray<GroundCoverType> groundCoverMap;
        public int groundCoverMapWidth;
        public int groundCoverMapHeight;
        public bool hasGroundCoverMap;
        [ReadOnly] public NativeArray<float2> treeExclusionPositions;
        [ReadOnly] public NativeArray<float2> bushExclusionPositions;
        [ReadOnly] public NativeArray<float2> rockExclusionPositions;
        public float treeExclusionRadiusSqr;
        public float bushExclusionRadiusSqr;
        public float rockExclusionRadiusSqr;
        [WriteOnly] public NativeArray<GrassSubChunkDiscoveryResult> results;
        public int worldSeed;
        public int seedOffset;
        public int chunkCoordX;
        public int chunkCoordZ;
        public int chunkSize;
        public int startCellX;
        public int startCellZ;
        public int cellCountX;
        public float cellSize;
        public float cellJitter;
        public float subChunkMinX;
        public float subChunkMinZ;
        public float subChunkMaxX;
        public float subChunkMaxZ;
        public bool includeMaxX;
        public bool includeMaxZ;
        public float topLeftX;
        public float bottomLeftZ;
        public float worldScale;
        public float meshHeightMultiplier;
        public bool randomizeYaw;
        public float minScale;
        public float maxScale;

        public void Execute(int index)
        {
            int cellX = startCellX + index % cellCountX;
            int cellZ = startCellZ + index / cellCountX;

            int cellHash = Hash7(worldSeed, seedOffset, chunkCoordX, chunkCoordZ, cellX, cellZ, 713);
            float jitterX = math.lerp(0.5f, Hash01(cellHash + 31), cellJitter);
            float jitterZ = math.lerp(0.5f, Hash01(cellHash + 67), cellJitter);
            float sampleX = (cellX + jitterX) * cellSize;
            float sampleZ = (cellZ + jitterZ) * cellSize;

            if (!IsSampleInsideSubChunk(sampleX, sampleZ))
            {
                results[index] = default;
                return;
            }

            int mapX = math.clamp((int)math.round(sampleX), 0, chunkSize);
            int mapZ = math.clamp((int)math.round(sampleZ), 0, chunkSize);
            int paddedX = mapX + 1;
            int paddedZ = mapZ + 1;

            if (!AllowsInstancedGrass(paddedX, paddedZ))
            {
                results[index] = default;
                return;
            }

            float localX = (topLeftX + sampleX) * worldScale;
            float localZ = (bottomLeftZ + sampleZ) * worldScale;
            float2 localXZ = new float2(localX, localZ);

            if (IsInsideExclusion(localXZ, treeExclusionPositions, treeExclusionRadiusSqr) ||
                IsInsideExclusion(localXZ, bushExclusionPositions, bushExclusionRadiusSqr) ||
                IsInsideExclusion(localXZ, rockExclusionPositions, rockExclusionRadiusSqr))
            {
                results[index] = default;
                return;
            }

            float height = SampleHeightBilinear(sampleX, sampleZ);
            float yaw = randomizeYaw
                ? Hash01(Hash7(worldSeed, seedOffset, chunkCoordX, chunkCoordZ, cellX, cellZ, 17)) * 360f
                : 0f;
            float uniformScale = math.lerp(
                minScale,
                maxScale,
                Hash01(Hash7(worldSeed, seedOffset, chunkCoordX, chunkCoordZ, cellX, cellZ, 29)));
            uint selectionRank = (uint)Hash7(worldSeed, seedOffset, chunkCoordX, chunkCoordZ, cellX, cellZ, 101);

            results[index] = new GrassSubChunkDiscoveryResult
            {
                valid = 1,
                localPosition = new float3(localX, height * meshHeightMultiplier * worldScale, localZ),
                yaw = yaw,
                uniformScale = uniformScale,
                selectionRank = selectionRank,
                forestBlend = GetGrassForestBlend(paddedX, paddedZ)
            };
        }

        private bool IsSampleInsideSubChunk(float sampleX, float sampleZ)
        {
            bool insideX = includeMaxX
                ? sampleX >= subChunkMinX && sampleX <= subChunkMaxX
                : sampleX >= subChunkMinX && sampleX < subChunkMaxX;

            bool insideZ = includeMaxZ
                ? sampleZ >= subChunkMinZ && sampleZ <= subChunkMaxZ
                : sampleZ >= subChunkMinZ && sampleZ < subChunkMaxZ;

            return insideX && insideZ;
        }

        private bool AllowsInstancedGrass(int paddedX, int paddedZ)
        {
            if (ReadSurface(paddedX, paddedZ) != SurfaceType.Grass)
                return false;

            if (!hasGroundCoverMap)
                return true;

            GroundCoverType groundCover = ReadGroundCover(paddedX, paddedZ);
            return groundCover == GroundCoverType.Default ||
                   groundCover == GroundCoverType.DarkGrass;
        }

        private float GetGrassForestBlend(int paddedX, int paddedZ)
        {
            if (hasGroundCoverMap && ReadGroundCover(paddedX, paddedZ) == GroundCoverType.DarkGrass)
                return 1f;

            return ReadBiome(paddedX, paddedZ) == BiomeType.Forest ? 1f : 0f;
        }

        private bool IsInsideExclusion(float2 localXZ, NativeArray<float2> positions, float exclusionRadiusSqr)
        {
            if (exclusionRadiusSqr <= 0f)
                return false;

            for (int i = 0; i < positions.Length; i++)
            {
                float2 delta = localXZ - positions[i];
                if (math.lengthsq(delta) < exclusionRadiusSqr)
                    return true;
            }

            return false;
        }

        private float SampleHeightBilinear(float sampleX, float sampleZ)
        {
            float x = math.clamp(sampleX, 0f, chunkSize);
            float z = math.clamp(sampleZ, 0f, chunkSize);

            int x0 = (int)math.floor(x);
            int z0 = (int)math.floor(z);
            int x1 = math.min(x0 + 1, chunkSize);
            int z1 = math.min(z0 + 1, chunkSize);

            float tx = x - x0;
            float tz = z - z0;

            int px0 = x0 + 1;
            int pz0 = z0 + 1;
            int px1 = x1 + 1;
            int pz1 = z1 + 1;

            float h00 = ReadHeight(px0, pz0);
            float h10 = ReadHeight(px1, pz0);
            float h01 = ReadHeight(px0, pz1);
            float h11 = ReadHeight(px1, pz1);

            float hx0 = math.lerp(h00, h10, tx);
            float hx1 = math.lerp(h01, h11, tx);

            return math.lerp(hx0, hx1, tz);
        }

        private float ReadHeight(int x, int z)
        {
            x = math.clamp(x, 0, heightMapWidth - 1);
            z = math.clamp(z, 0, heightMapHeight - 1);
            return heightMap[x * heightMapHeight + z];
        }

        private SurfaceType ReadSurface(int x, int z)
        {
            x = math.clamp(x, 0, surfaceMapWidth - 1);
            z = math.clamp(z, 0, surfaceMapHeight - 1);
            return surfaceMap[x * surfaceMapHeight + z];
        }

        private BiomeType ReadBiome(int x, int z)
        {
            x = math.clamp(x, 0, biomeMapWidth - 1);
            z = math.clamp(z, 0, biomeMapHeight - 1);
            return biomeMap[x * biomeMapHeight + z];
        }

        private GroundCoverType ReadGroundCover(int x, int z)
        {
            x = math.clamp(x, 0, groundCoverMapWidth - 1);
            z = math.clamp(z, 0, groundCoverMapHeight - 1);
            return groundCoverMap[x * groundCoverMapHeight + z];
        }

        private static int Hash7(int v0, int v1, int v2, int v3, int v4, int v5, int v6)
        {
            uint hash = 2166136261u;
            hash = MixHash(hash, v0);
            hash = MixHash(hash, v1);
            hash = MixHash(hash, v2);
            hash = MixHash(hash, v3);
            hash = MixHash(hash, v4);
            hash = MixHash(hash, v5);
            hash = MixHash(hash, v6);
            return (int)hash;
        }

        private static uint MixHash(uint hash, int value)
        {
            hash ^= (uint)value;
            hash *= 16777619u;
            hash ^= hash >> 13;
            hash *= 1274126177u;
            hash ^= hash >> 16;
            return hash;
        }

        private static float Hash01(int hash)
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

    private static void GetDeterministicTreeColors(
        WorldFeatureVariant variant,
        int worldSeed,
        int seedOffset,
        ChunkCoord chunkCoord,
        float sampleX,
        float sampleZ,
        out Color32 leafTint,
        out Color32 barkTint)
    {
        int cellX = Mathf.RoundToInt(sampleX * 10f);
        int cellZ = Mathf.RoundToInt(sampleZ * 10f);
        int variantSeed = (int)variant * 193;
        int baseHash = Hash(worldSeed, seedOffset, chunkCoord.x, chunkCoord.z, cellX, cellZ, variantSeed, 911);

        if (variant == WorldFeatureVariant.SugarMapleTree)
        {
            leafTint = PickWeightedColor(
                Hash01(baseHash + 17),
                new Color(1.0f, 0.74f, 0.22f, 1f),
                new Color(1.0f, 0.52f, 0.18f, 1f),
                new Color(0.82f, 0.22f, 0.14f, 1f),
                Hash01(baseHash + 31),
                0.68f,
                0.92f);

            barkTint = Color.Lerp(
                new Color(0.88f, 0.86f, 0.80f, 1f),
                new Color(1.04f, 1.01f, 0.93f, 1f),
                Hash01(baseHash + 43));
            return;
        }

        if (variant == WorldFeatureVariant.MapleTree)
        {
            leafTint = Color.white;
            barkTint = Color.white;
            return;
        }

        leafTint = Color.Lerp(
            new Color(0.92f, 0.98f, 0.88f, 1f),
            new Color(1.06f, 1.02f, 0.94f, 1f),
            Hash01(baseHash + 17));
        barkTint = Color.Lerp(
            new Color(0.90f, 0.88f, 0.82f, 1f),
            new Color(1.05f, 1.00f, 0.92f, 1f),
            Hash01(baseHash + 43));
    }

    private static Color32 PickWeightedColor(
        float selector,
        Color first,
        Color second,
        Color third,
        float blend,
        float firstThreshold,
        float secondThreshold)
    {
        Color a;
        Color b;

        if (selector < firstThreshold)
        {
            a = first;
            b = second;
        }
        else if (selector < secondThreshold)
        {
            a = second;
            b = third;
        }
        else
        {
            a = third;
            b = first;
        }

        return (Color32)Color.Lerp(a, b, blend * 0.65f);
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
