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
        CloverSettings cloverSettings,
        TreeSettings treeSettings,
        int worldSeed,
        int chunkSize,
        float worldScale,
        float meshHeightMultiplier,
        bool applyCloverInfluence = true)
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
                    cloverSettings,
                    treeSettings,
                    worldSeed,
                    chunkSize,
                    worldScale,
                    meshHeightMultiplier,
                    localSubChunkX,
                    localSubChunkZ,
                    applyCloverInfluence);
            }
        }
    }

    public static void GenerateGrassForSubChunk(
        ChunkRecord record,
        GrassSettings grassSettings,
        CloverSettings cloverSettings,
        TreeSettings treeSettings,
        int worldSeed,
        int chunkSize,
        float worldScale,
        float meshHeightMultiplier,
        int localSubChunkX,
        int localSubChunkZ,
        bool applyCloverInfluence = true)
    {
        if (TryScheduleGrassForSubChunk(
                record,
                grassSettings,
                cloverSettings,
                treeSettings,
                worldSeed,
                chunkSize,
                worldScale,
                meshHeightMultiplier,
                localSubChunkX,
                localSubChunkZ,
                applyCloverInfluence,
                out GrassSubChunkGenerationJob job))
        {
            job.CompleteAndApply();
        }
    }

    public static bool TryScheduleGrassForSubChunk(
        ChunkRecord record,
        GrassSettings grassSettings,
        CloverSettings cloverSettings,
        TreeSettings treeSettings,
        int worldSeed,
        int chunkSize,
        float worldScale,
        float meshHeightMultiplier,
        int localSubChunkX,
        int localSubChunkZ,
        bool applyCloverInfluence,
        out GrassSubChunkGenerationJob scheduledJob)
    {
        scheduledJob = null;

        int subChunksPerChunk = Mathf.Max(1, grassSettings.subChunksPerChunk);
        EnsureNearGrassStorage(record, subChunksPerChunk);

        ChunkFoliageData foliageData = record.FoliageData;

        localSubChunkX = Mathf.Clamp(localSubChunkX, 0, subChunksPerChunk - 1);
        localSubChunkZ = Mathf.Clamp(localSubChunkZ, 0, subChunksPerChunk - 1);

        foliageData.ClearNearGrassSubChunk(localSubChunkX, localSubChunkZ);

        if (record.SurfaceTypeMap == null || record.HeightMap == null || record.BiomeMap == null)
        {
            foliageData.MarkNearGrassSubChunkGenerated(localSubChunkX, localSubChunkZ, applyCloverInfluence);
            return false;
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

        const Allocator asyncAllocator = Allocator.Persistent;
        NativeArray<float> heightMap = FlattenFloatMap(record.HeightMap, asyncAllocator, out int heightMapWidth, out int heightMapHeight);
        NativeArray<SurfaceType> surfaceMap = FlattenSurfaceMap(record.SurfaceTypeMap, asyncAllocator, out int surfaceMapWidth, out int surfaceMapHeight);
        NativeArray<BiomeType> biomeMap = FlattenBiomeMap(record.BiomeMap, asyncAllocator, out int biomeMapWidth, out int biomeMapHeight);
        NativeArray<GroundCoverType> groundCoverMap = FlattenGroundCoverMap(record.GroundCoverMap, asyncAllocator, out int groundCoverMapWidth, out int groundCoverMapHeight);
        NativeArray<float2> treeExclusionPositions = CreateTreeExclusionPositions(foliageData.treeCubeInstances, asyncAllocator);
        NativeArray<float2> bushExclusionPositions = CreateBushExclusionPositions(foliageData.bushInstances, asyncAllocator);
        NativeArray<float2> rockExclusionPositions = CreateRockExclusionPositions(foliageData.rockInstances, asyncAllocator);
        NativeArray<float4> cloverInfluences = CreateCloverInfluences(
            applyCloverInfluence ? foliageData.cloverInstances : null,
            cloverSettings,
            asyncAllocator);
        NativeArray<GrassSubChunkDiscoveryResult> results =
            new NativeArray<GrassSubChunkDiscoveryResult>(candidateCount, asyncAllocator, NativeArrayOptions.UninitializedMemory);

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
                cloverInfluences = cloverInfluences,
                treeExclusionRadiusSqr = treeExclusionRadiusSqr,
                bushExclusionRadiusSqr = bushExclusionRadiusSqr,
                rockExclusionRadiusSqr = rockExclusionRadiusSqr,
                cloverGrassDensityInsidePatch = applyCloverInfluence && cloverSettings != null
                    ? Mathf.Clamp01(cloverSettings.grassDensityInsidePatch)
                    : 1f,
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
            scheduledJob = new GrassSubChunkGenerationJob(
                record,
                localSubChunkX,
                localSubChunkZ,
                applyCloverInfluence,
                handle,
                heightMap,
                surfaceMap,
                biomeMap,
                groundCoverMap,
                treeExclusionPositions,
                bushExclusionPositions,
                rockExclusionPositions,
                cloverInfluences,
                results);
            return true;
        }
        catch
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
            if (cloverInfluences.IsCreated)
                cloverInfluences.Dispose();
            if (results.IsCreated)
                results.Dispose();

            throw;
        }
    }

    public sealed class GrassSubChunkGenerationJob : System.IDisposable
    {
        private readonly ChunkRecord record;
        private readonly int localSubChunkX;
        private readonly int localSubChunkZ;
        private readonly bool applyCloverInfluence;
        private JobHandle handle;
        private NativeArray<float> heightMap;
        private NativeArray<SurfaceType> surfaceMap;
        private NativeArray<BiomeType> biomeMap;
        private NativeArray<GroundCoverType> groundCoverMap;
        private NativeArray<float2> treeExclusionPositions;
        private NativeArray<float2> bushExclusionPositions;
        private NativeArray<float2> rockExclusionPositions;
        private NativeArray<float4> cloverInfluences;
        private NativeArray<GrassSubChunkDiscoveryResult> results;
        private bool disposed;

        internal GrassSubChunkGenerationJob(
            ChunkRecord record,
            int localSubChunkX,
            int localSubChunkZ,
            bool applyCloverInfluence,
            JobHandle handle,
            NativeArray<float> heightMap,
            NativeArray<SurfaceType> surfaceMap,
            NativeArray<BiomeType> biomeMap,
            NativeArray<GroundCoverType> groundCoverMap,
            NativeArray<float2> treeExclusionPositions,
            NativeArray<float2> bushExclusionPositions,
            NativeArray<float2> rockExclusionPositions,
            NativeArray<float4> cloverInfluences,
            NativeArray<GrassSubChunkDiscoveryResult> results)
        {
            this.record = record;
            this.localSubChunkX = localSubChunkX;
            this.localSubChunkZ = localSubChunkZ;
            this.applyCloverInfluence = applyCloverInfluence;
            this.handle = handle;
            this.heightMap = heightMap;
            this.surfaceMap = surfaceMap;
            this.biomeMap = biomeMap;
            this.groundCoverMap = groundCoverMap;
            this.treeExclusionPositions = treeExclusionPositions;
            this.bushExclusionPositions = bushExclusionPositions;
            this.rockExclusionPositions = rockExclusionPositions;
            this.cloverInfluences = cloverInfluences;
            this.results = results;
        }

        public bool IsCompleted => disposed || handle.IsCompleted;

        public void CompleteAndApply()
        {
            if (disposed)
                return;

            handle.Complete();

            try
            {
                ChunkFoliageData foliageData = record.FoliageData;
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

                SortSubChunkBucketBySelectionRank(foliageData, localSubChunkX, localSubChunkZ);
                foliageData.MarkNearGrassSubChunkGenerated(localSubChunkX, localSubChunkZ, applyCloverInfluence);
            }
            finally
            {
                DisposeArrays();
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;

            handle.Complete();
            DisposeArrays();
        }

        private void DisposeArrays()
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
            if (cloverInfluences.IsCreated)
                cloverInfluences.Dispose();
            if (results.IsCreated)
                results.Dispose();

            disposed = true;
        }
    }

    public static void GenerateBillboardGrassForChunk(
        ChunkRecord record,
        GrassSettings grassSettings,
        CloverSettings cloverSettings,
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

        int cellsPerAxis = Mathf.Max(1, grassSettings.cellsPerAxis);
        float cellSize = (float)chunkSize / cellsPerAxis;

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;
        float treeExclusionRadiusSqr = 0f;
        float bushExclusionRadiusSqr = 0f;
        float rockExclusionRadiusSqr = 0f;

        if (treeSettings != null)
        {
            treeExclusionRadiusSqr =
                treeSettings.grassExclusionRadius * treeSettings.grassExclusionRadius;
            bushExclusionRadiusSqr =
                treeSettings.bushGrassExclusionRadius * treeSettings.bushGrassExclusionRadius;
            rockExclusionRadiusSqr =
                treeSettings.rockGrassExclusionRadius * treeSettings.rockGrassExclusionRadius;
        }

        int candidateCount = cellsPerAxis * cellsPerAxis;
        NativeArray<float> heightMap = FlattenFloatMap(record.HeightMap, Allocator.TempJob, out int heightMapWidth, out int heightMapHeight);
        NativeArray<SurfaceType> surfaceMap = FlattenSurfaceMap(record.SurfaceTypeMap, Allocator.TempJob, out int surfaceMapWidth, out int surfaceMapHeight);
        NativeArray<BiomeType> biomeMap = FlattenBiomeMap(record.BiomeMap, Allocator.TempJob, out int biomeMapWidth, out int biomeMapHeight);
        NativeArray<GroundCoverType> groundCoverMap = FlattenGroundCoverMap(record.GroundCoverMap, Allocator.TempJob, out int groundCoverMapWidth, out int groundCoverMapHeight);
        NativeArray<float2> treeExclusionPositions = CreateTreeExclusionPositions(foliageData.treeCubeInstances, Allocator.TempJob);
        NativeArray<float2> bushExclusionPositions = CreateBushExclusionPositions(foliageData.bushInstances, Allocator.TempJob);
        NativeArray<float2> rockExclusionPositions = CreateRockExclusionPositions(foliageData.rockInstances, Allocator.TempJob);
        NativeArray<float4> cloverInfluences = CreateCloverInfluences(
            foliageData.cloverInstances,
            cloverSettings,
            Allocator.TempJob);
        NativeArray<GrassSubChunkDiscoveryResult> results =
            new NativeArray<GrassSubChunkDiscoveryResult>(candidateCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        try
        {
            BillboardGrassDiscoveryJob job = new BillboardGrassDiscoveryJob
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
                cloverInfluences = cloverInfluences,
                treeExclusionRadiusSqr = treeExclusionRadiusSqr,
                bushExclusionRadiusSqr = bushExclusionRadiusSqr,
                rockExclusionRadiusSqr = rockExclusionRadiusSqr,
                cloverGrassDensityInsidePatch = cloverSettings != null
                    ? Mathf.Clamp01(cloverSettings.grassDensityInsidePatch)
                    : 1f,
                results = results,
                worldSeed = worldSeed,
                seedOffset = grassSettings.seedOffset,
                chunkCoordX = record.ChunkCoord.x,
                chunkCoordZ = record.ChunkCoord.z,
                chunkSize = chunkSize,
                cellsPerAxis = cellsPerAxis,
                cellSize = cellSize,
                cellJitter = Mathf.Clamp01(grassSettings.cellJitter),
                topLeftX = topLeftX,
                bottomLeftZ = bottomLeftZ,
                worldScale = worldScale,
                meshHeightMultiplier = meshHeightMultiplier,
                randomizeYaw = grassSettings.randomizeYaw,
                minScale = grassSettings.uniformScaleRange.x,
                maxScale = grassSettings.uniformScaleRange.y
            };

            JobHandle handle = job.Schedule(candidateCount, 64);
            handle.Complete();

            for (int i = 0; i < results.Length; i++)
            {
                GrassSubChunkDiscoveryResult result = results[i];
                if (result.valid == 0)
                    continue;

                foliageData.billboardGrassInstances.Add(
                    new BillboardFoliageInstanceData(
                        new Vector3(result.localPosition.x, result.localPosition.y, result.localPosition.z),
                        Quaternion.Euler(0f, result.yaw, 0f),
                        Vector3.one * result.uniformScale,
                        result.selectionRank,
                        result.forestBlend));
            }

            foliageData.billboardGrassInstances.Sort((a, b) =>
                a.selectionRank.CompareTo(b.selectionRank));
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
            if (cloverInfluences.IsCreated)
                cloverInfluences.Dispose();
            if (results.IsCreated)
                results.Dispose();
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

        int globalCellCountX = globalCellMaxX - globalCellMinX + 1;
        int globalCellCountZ = globalCellMaxZ - globalCellMinZ + 1;
        int patchCandidateCount = globalCellCountX * globalCellCountZ * maxPatchCentersPerCell;
        int flowerCandidateCount = patchCandidateCount * maxFlowersPerPatch;

        NativeArray<float> heightMap = FlattenFloatMap(record.HeightMap, Allocator.TempJob, out int heightMapWidth, out int heightMapHeight);
        NativeArray<SurfaceType> surfaceMap = FlattenSurfaceMap(record.SurfaceTypeMap, Allocator.TempJob, out int surfaceMapWidth, out int surfaceMapHeight);
        NativeArray<BiomeType> biomeMap = FlattenBiomeMap(record.BiomeMap, Allocator.TempJob, out int biomeMapWidth, out int biomeMapHeight);
        NativeArray<float> slopeMap = FlattenFloatMap(record.SlopeMap, Allocator.TempJob, out int slopeMapWidth, out int slopeMapHeight);
        NativeArray<byte> allowedBiomeMask = CreateAllowedBiomeMask(flowerSettings, Allocator.TempJob);
        NativeArray<float2> treeExclusionPositions = CreateTreeExclusionPositions(foliageData.treeCubeInstances, Allocator.TempJob);
        NativeArray<FlowerDiscoveryResult> results =
            new NativeArray<FlowerDiscoveryResult>(flowerCandidateCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        try
        {
            FlowerDiscoveryJob job = new FlowerDiscoveryJob
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
                slopeMap = slopeMap,
                slopeMapWidth = slopeMapWidth,
                slopeMapHeight = slopeMapHeight,
                hasSlopeMap = record.SlopeMap != null,
                allowedBiomeMask = allowedBiomeMask,
                treeExclusionPositions = treeExclusionPositions,
                treeExclusionRadiusSqr = treeExclusionRadiusSqr,
                results = results,
                worldSeed = worldSeed,
                seedOffset = flowerSettings.seedOffset,
                chunkSize = chunkSize,
                globalCellMinX = globalCellMinX,
                globalCellMinZ = globalCellMinZ,
                globalCellCountX = globalCellCountX,
                maxPatchCentersPerCell = maxPatchCentersPerCell,
                minFlowersPerPatch = minFlowersPerPatch,
                maxFlowersPerPatch = maxFlowersPerPatch,
                patchCellSize = patchCellSize,
                patchNoiseScale = patchNoiseScale,
                patchNoiseThreshold = patchNoiseThreshold,
                patchSpawnChance = Mathf.Clamp01(flowerSettings.patchSpawnChance),
                minPatchRadius = flowerSettings.patchRadiusRange.x,
                maxPatchRadius = flowerSettings.patchRadiusRange.y,
                chunkSampleMinX = chunkSampleMinX,
                chunkSampleMinZ = chunkSampleMinZ,
                chunkSampleMaxX = chunkSampleMaxX,
                chunkSampleMaxZ = chunkSampleMaxZ,
                topLeftX = topLeftX,
                bottomLeftZ = bottomLeftZ,
                worldScale = worldScale,
                meshHeightMultiplier = meshHeightMultiplier,
                maxSlope = flowerSettings.maxSlope,
                randomizeYaw = flowerSettings.randomizeYaw,
                minScale = flowerSettings.uniformScaleRange.x,
                maxScale = flowerSettings.uniformScaleRange.y
            };

            JobHandle handle = job.Schedule(flowerCandidateCount, 64);
            handle.Complete();

            for (int i = 0; i < results.Length; i++)
            {
                FlowerDiscoveryResult result = results[i];
                if (result.valid == 0)
                    continue;

                Color32 petalColor = GetDeterministicPetalColor(
                    flowerSettings,
                    result.biome,
                    result.flowerHash);

                foliageData.flowerInstances.Add(new FlowerInstanceData(
                    new Vector3(result.localPosition.x, result.localPosition.y, result.localPosition.z),
                    Quaternion.Euler(0f, result.yaw, 0f),
                    Vector3.one * result.uniformScale,
                    petalColor));
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
            if (slopeMap.IsCreated)
                slopeMap.Dispose();
            if (allowedBiomeMask.IsCreated)
                allowedBiomeMask.Dispose();
            if (treeExclusionPositions.IsCreated)
                treeExclusionPositions.Dispose();
            if (results.IsCreated)
                results.Dispose();
        }

        foliageData.flowersGenerated = true;
    }

    public static void GenerateCloverForChunk(
        ChunkRecord record,
        CloverSettings cloverSettings,
        int cloverPrefabCount,
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
        foliageData.ClearClover();

        if (cloverSettings == null || !cloverSettings.enableClover || cloverPrefabCount <= 0)
        {
            foliageData.cloverGenerated = true;
            return;
        }

        if (record.SurfaceTypeMap == null || record.HeightMap == null || record.BiomeMap == null)
        {
            foliageData.cloverGenerated = true;
            return;
        }

        float chunkSampleMinX = record.ChunkCoord.x * chunkSize;
        float chunkSampleMinZ = record.ChunkCoord.z * chunkSize;
        float chunkSampleMaxX = chunkSampleMinX + chunkSize;
        float chunkSampleMaxZ = chunkSampleMinZ + chunkSize;

        float patchCellSize = Mathf.Max(0.1f, cloverSettings.patchCellSize);
        float minPatchRadius = Mathf.Max(0f, Mathf.Min(cloverSettings.patchRadiusRange.x, cloverSettings.patchRadiusRange.y));
        float maxPatchRadius = Mathf.Max(
            Mathf.Max(cloverSettings.patchRadiusRange.x, cloverSettings.patchRadiusRange.y),
            0f);
        float minUniformScale = Mathf.Min(cloverSettings.uniformScaleRange.x, cloverSettings.uniformScaleRange.y);
        float maxUniformScale = Mathf.Max(cloverSettings.uniformScaleRange.x, cloverSettings.uniformScaleRange.y);
        float padding = maxPatchRadius + patchCellSize;

        float expandedMinX = chunkSampleMinX - padding;
        float expandedMaxX = chunkSampleMaxX + padding;
        float expandedMinZ = chunkSampleMinZ - padding;
        float expandedMaxZ = chunkSampleMaxZ + padding;

        int globalCellMinX = Mathf.FloorToInt(expandedMinX / patchCellSize);
        int globalCellMaxX = Mathf.FloorToInt(expandedMaxX / patchCellSize);
        int globalCellMinZ = Mathf.FloorToInt(expandedMinZ / patchCellSize);
        int globalCellMaxZ = Mathf.FloorToInt(expandedMaxZ / patchCellSize);

        int maxPatchCentersPerCell = Mathf.Max(1, cloverSettings.maxPatchCentersPerCell);
        int minClumpsPerPatch = Mathf.Max(1, cloverSettings.minClumpsPerPatch);
        int maxClumpsPerPatch = Mathf.Max(minClumpsPerPatch, cloverSettings.maxClumpsPerPatch);
        float patchNoiseScale = Mathf.Max(0.0001f, cloverSettings.patchNoiseScale);

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;

        int globalCellCountX = globalCellMaxX - globalCellMinX + 1;
        int globalCellCountZ = globalCellMaxZ - globalCellMinZ + 1;
        int patchCandidateCount = globalCellCountX * globalCellCountZ * maxPatchCentersPerCell;
        int clumpCandidateCount = patchCandidateCount * maxClumpsPerPatch;

        NativeArray<float> heightMap = FlattenFloatMap(record.HeightMap, Allocator.TempJob, out int heightMapWidth, out int heightMapHeight);
        NativeArray<SurfaceType> surfaceMap = FlattenSurfaceMap(record.SurfaceTypeMap, Allocator.TempJob, out int surfaceMapWidth, out int surfaceMapHeight);
        NativeArray<BiomeType> biomeMap = FlattenBiomeMap(record.BiomeMap, Allocator.TempJob, out int biomeMapWidth, out int biomeMapHeight);
        NativeArray<GroundCoverType> groundCoverMap = FlattenGroundCoverMap(record.GroundCoverMap, Allocator.TempJob, out int groundCoverMapWidth, out int groundCoverMapHeight);
        NativeArray<float> slopeMap = FlattenFloatMap(record.SlopeMap, Allocator.TempJob, out int slopeMapWidth, out int slopeMapHeight);
        NativeArray<float2> treeExclusionPositions = CreateTreeExclusionPositions(foliageData.treeCubeInstances, Allocator.TempJob);
        NativeArray<float2> bushExclusionPositions = CreateBushExclusionPositions(foliageData.bushInstances, Allocator.TempJob);
        NativeArray<float2> rockExclusionPositions = CreateRockExclusionPositions(foliageData.rockInstances, Allocator.TempJob);
        NativeArray<CloverDiscoveryResult> results =
            new NativeArray<CloverDiscoveryResult>(clumpCandidateCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        try
        {
            CloverDiscoveryJob job = new CloverDiscoveryJob
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
                slopeMap = slopeMap,
                slopeMapWidth = slopeMapWidth,
                slopeMapHeight = slopeMapHeight,
                hasSlopeMap = record.SlopeMap != null,
                treeExclusionPositions = treeExclusionPositions,
                bushExclusionPositions = bushExclusionPositions,
                rockExclusionPositions = rockExclusionPositions,
                treeExclusionRadiusSqr = cloverSettings.treeExclusionRadius * cloverSettings.treeExclusionRadius,
                bushExclusionRadiusSqr = cloverSettings.bushExclusionRadius * cloverSettings.bushExclusionRadius,
                rockExclusionRadiusSqr = cloverSettings.rockExclusionRadius * cloverSettings.rockExclusionRadius,
                results = results,
                worldSeed = worldSeed,
                seedOffset = cloverSettings.seedOffset,
                chunkSize = chunkSize,
                globalCellMinX = globalCellMinX,
                globalCellMinZ = globalCellMinZ,
                globalCellCountX = globalCellCountX,
                maxPatchCentersPerCell = maxPatchCentersPerCell,
                minClumpsPerPatch = minClumpsPerPatch,
                maxClumpsPerPatch = maxClumpsPerPatch,
                patchCellSize = patchCellSize,
                patchNoiseScale = patchNoiseScale,
                patchNoiseThreshold = Mathf.Clamp01(cloverSettings.patchNoiseThreshold),
                patchSpawnChance = Mathf.Clamp01(cloverSettings.patchSpawnChance),
                minPatchRadius = minPatchRadius,
                maxPatchRadius = maxPatchRadius,
                chunkSampleMinX = chunkSampleMinX,
                chunkSampleMinZ = chunkSampleMinZ,
                chunkSampleMaxX = chunkSampleMaxX,
                chunkSampleMaxZ = chunkSampleMaxZ,
                topLeftX = topLeftX,
                bottomLeftZ = bottomLeftZ,
                worldScale = worldScale,
                meshHeightMultiplier = meshHeightMultiplier,
                maxSlope = cloverSettings.maxSlope,
                randomizeYaw = cloverSettings.randomizeYaw,
                minScale = minUniformScale,
                maxScale = maxUniformScale,
                prefabCount = cloverPrefabCount,
                grassInfluenceRadius = Mathf.Max(0.01f, cloverSettings.grassInfluenceRadius)
            };

            JobHandle handle = job.Schedule(clumpCandidateCount, 64);
            handle.Complete();

            for (int i = 0; i < results.Length; i++)
            {
                CloverDiscoveryResult result = results[i];
                if (result.valid == 0)
                    continue;

                foliageData.cloverInstances.Add(new CloverInstanceData(
                    new Vector3(result.localPosition.x, result.localPosition.y, result.localPosition.z),
                    new Quaternion(
                        result.localRotation.value.x,
                        result.localRotation.value.y,
                        result.localRotation.value.z,
                        result.localRotation.value.w),
                    Vector3.one * result.uniformScale,
                    result.selectionRank,
                    result.grassInfluenceRadius,
                    result.prefabIndex));
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
            if (slopeMap.IsCreated)
                slopeMap.Dispose();
            if (treeExclusionPositions.IsCreated)
                treeExclusionPositions.Dispose();
            if (bushExclusionPositions.IsCreated)
                bushExclusionPositions.Dispose();
            if (rockExclusionPositions.IsCreated)
                rockExclusionPositions.Dispose();
            if (results.IsCreated)
                results.Dispose();
        }

        foliageData.cloverGenerated = true;
    }

    public static void GenerateDandelionsForChunk(
        ChunkRecord record,
        DandelionSettings dandelionSettings,
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
        foliageData.ClearDandelions();

        if (dandelionSettings == null || !dandelionSettings.enableDandelions)
        {
            foliageData.dandelionsGenerated = true;
            return;
        }

        if (record.SurfaceTypeMap == null || record.HeightMap == null || record.BiomeMap == null)
        {
            foliageData.dandelionsGenerated = true;
            return;
        }

        float chunkSampleMinX = record.ChunkCoord.x * chunkSize;
        float chunkSampleMinZ = record.ChunkCoord.z * chunkSize;
        float chunkSampleMaxX = chunkSampleMinX + chunkSize;
        float chunkSampleMaxZ = chunkSampleMinZ + chunkSize;

        float patchCellSize = Mathf.Max(0.1f, dandelionSettings.patchCellSize);
        float minPatchRadius = Mathf.Max(0f, Mathf.Min(dandelionSettings.patchRadiusRange.x, dandelionSettings.patchRadiusRange.y));
        float maxPatchRadius = Mathf.Max(
            Mathf.Max(dandelionSettings.patchRadiusRange.x, dandelionSettings.patchRadiusRange.y),
            0f);
        float minUniformScale = Mathf.Min(dandelionSettings.uniformScaleRange.x, dandelionSettings.uniformScaleRange.y);
        float maxUniformScale = Mathf.Max(dandelionSettings.uniformScaleRange.x, dandelionSettings.uniformScaleRange.y);
        float padding = maxPatchRadius + patchCellSize;

        float expandedMinX = chunkSampleMinX - padding;
        float expandedMaxX = chunkSampleMaxX + padding;
        float expandedMinZ = chunkSampleMinZ - padding;
        float expandedMaxZ = chunkSampleMaxZ + padding;

        int globalCellMinX = Mathf.FloorToInt(expandedMinX / patchCellSize);
        int globalCellMaxX = Mathf.FloorToInt(expandedMaxX / patchCellSize);
        int globalCellMinZ = Mathf.FloorToInt(expandedMinZ / patchCellSize);
        int globalCellMaxZ = Mathf.FloorToInt(expandedMaxZ / patchCellSize);

        int maxPatchCentersPerCell = Mathf.Max(1, dandelionSettings.maxPatchCentersPerCell);
        int minDandelionsPerPatch = Mathf.Max(1, dandelionSettings.minDandelionsPerPatch);
        int maxDandelionsPerPatch = Mathf.Max(minDandelionsPerPatch, dandelionSettings.maxDandelionsPerPatch);
        float patchNoiseScale = Mathf.Max(0.0001f, dandelionSettings.patchNoiseScale);

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;

        int globalCellCountX = globalCellMaxX - globalCellMinX + 1;
        int globalCellCountZ = globalCellMaxZ - globalCellMinZ + 1;
        int patchCandidateCount = globalCellCountX * globalCellCountZ * maxPatchCentersPerCell;
        int dandelionCandidateCount = patchCandidateCount * maxDandelionsPerPatch;

        NativeArray<float> heightMap = FlattenFloatMap(record.HeightMap, Allocator.TempJob, out int heightMapWidth, out int heightMapHeight);
        NativeArray<SurfaceType> surfaceMap = FlattenSurfaceMap(record.SurfaceTypeMap, Allocator.TempJob, out int surfaceMapWidth, out int surfaceMapHeight);
        NativeArray<BiomeType> biomeMap = FlattenBiomeMap(record.BiomeMap, Allocator.TempJob, out int biomeMapWidth, out int biomeMapHeight);
        NativeArray<GroundCoverType> groundCoverMap = FlattenGroundCoverMap(record.GroundCoverMap, Allocator.TempJob, out int groundCoverMapWidth, out int groundCoverMapHeight);
        NativeArray<float> slopeMap = FlattenFloatMap(record.SlopeMap, Allocator.TempJob, out int slopeMapWidth, out int slopeMapHeight);
        NativeArray<float2> treeExclusionPositions = CreateTreeExclusionPositions(foliageData.treeCubeInstances, Allocator.TempJob);
        NativeArray<float2> bushExclusionPositions = CreateBushExclusionPositions(foliageData.bushInstances, Allocator.TempJob);
        NativeArray<float2> rockExclusionPositions = CreateRockExclusionPositions(foliageData.rockInstances, Allocator.TempJob);
        NativeArray<CloverDiscoveryResult> results =
            new NativeArray<CloverDiscoveryResult>(dandelionCandidateCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        try
        {
            CloverDiscoveryJob job = new CloverDiscoveryJob
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
                slopeMap = slopeMap,
                slopeMapWidth = slopeMapWidth,
                slopeMapHeight = slopeMapHeight,
                hasSlopeMap = record.SlopeMap != null,
                treeExclusionPositions = treeExclusionPositions,
                bushExclusionPositions = bushExclusionPositions,
                rockExclusionPositions = rockExclusionPositions,
                treeExclusionRadiusSqr = dandelionSettings.treeExclusionRadius * dandelionSettings.treeExclusionRadius,
                bushExclusionRadiusSqr = dandelionSettings.bushExclusionRadius * dandelionSettings.bushExclusionRadius,
                rockExclusionRadiusSqr = dandelionSettings.rockExclusionRadius * dandelionSettings.rockExclusionRadius,
                results = results,
                worldSeed = worldSeed,
                seedOffset = dandelionSettings.seedOffset,
                chunkSize = chunkSize,
                globalCellMinX = globalCellMinX,
                globalCellMinZ = globalCellMinZ,
                globalCellCountX = globalCellCountX,
                maxPatchCentersPerCell = maxPatchCentersPerCell,
                minClumpsPerPatch = minDandelionsPerPatch,
                maxClumpsPerPatch = maxDandelionsPerPatch,
                patchCellSize = patchCellSize,
                patchNoiseScale = patchNoiseScale,
                patchNoiseThreshold = Mathf.Clamp01(dandelionSettings.patchNoiseThreshold),
                patchSpawnChance = Mathf.Clamp01(dandelionSettings.patchSpawnChance),
                minPatchRadius = minPatchRadius,
                maxPatchRadius = maxPatchRadius,
                chunkSampleMinX = chunkSampleMinX,
                chunkSampleMinZ = chunkSampleMinZ,
                chunkSampleMaxX = chunkSampleMaxX,
                chunkSampleMaxZ = chunkSampleMaxZ,
                topLeftX = topLeftX,
                bottomLeftZ = bottomLeftZ,
                worldScale = worldScale,
                meshHeightMultiplier = meshHeightMultiplier,
                maxSlope = dandelionSettings.maxSlope,
                randomizeYaw = dandelionSettings.randomizeYaw,
                minScale = minUniformScale,
                maxScale = maxUniformScale,
                prefabCount = 1,
                grassInfluenceRadius = 0.01f
            };

            JobHandle handle = job.Schedule(dandelionCandidateCount, 64);
            handle.Complete();

            for (int i = 0; i < results.Length; i++)
            {
                CloverDiscoveryResult result = results[i];
                if (result.valid == 0)
                    continue;

                foliageData.dandelionInstances.Add(new DandelionInstanceData(
                    new Vector3(result.localPosition.x, result.localPosition.y, result.localPosition.z),
                    new Quaternion(
                        result.localRotation.value.x,
                        result.localRotation.value.y,
                        result.localRotation.value.z,
                        result.localRotation.value.w),
                    Vector3.one * result.uniformScale,
                    result.selectionRank));
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
            if (slopeMap.IsCreated)
                slopeMap.Dispose();
            if (treeExclusionPositions.IsCreated)
                treeExclusionPositions.Dispose();
            if (bushExclusionPositions.IsCreated)
                bushExclusionPositions.Dispose();
            if (rockExclusionPositions.IsCreated)
                rockExclusionPositions.Dispose();
            if (results.IsCreated)
                results.Dispose();
        }

        foliageData.dandelionsGenerated = true;
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

            ulong bushId = CreateStableBushId(
                record.ChunkCoord,
                i,
                placement.variant,
                placement.sampleX,
                placement.sampleZ);

            foliageData.bushInstances.Add(new BerryBushInstanceData(
                bushId,
                record.ChunkCoord,
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
                placement.variant,
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

    private static bool IsInsideBushExclusion(
        float localX,
        float localZ,
        List<BerryBushInstanceData> bushInstances,
        float exclusionRadiusSqr)
    {
        for (int i = 0; i < bushInstances.Count; i++)
        {
            BerryBushInstanceData bush = bushInstances[i];

            float dx = localX - bush.localPosition.x;
            float dz = localZ - bush.localPosition.z;

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

    private static NativeArray<float2> CreateBushExclusionPositions(List<BerryBushInstanceData> instances, Allocator allocator)
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

    private static NativeArray<float4> CreateCloverInfluences(
        List<CloverInstanceData> instances,
        CloverSettings cloverSettings,
        Allocator allocator)
    {
        int count = instances != null ? instances.Count : 0;
        NativeArray<float4> result = new NativeArray<float4>(count, allocator, NativeArrayOptions.UninitializedMemory);
        float fadePadding = cloverSettings != null ? Mathf.Max(0f, cloverSettings.grassFadePadding) : 0f;

        for (int i = 0; i < count; i++)
        {
            CloverInstanceData instance = instances[i];
            float radius = Mathf.Max(0.01f, instance.grassInfluenceRadius + fadePadding);
            float coreRadius = Mathf.Max(0.01f, instance.grassInfluenceRadius);
            result[i] = new float4(instance.localPosition.x, instance.localPosition.z, radius, coreRadius);
        }

        return result;
    }

    private static NativeArray<byte> CreateAllowedBiomeMask(FlowerSettings flowerSettings, Allocator allocator)
    {
        const int biomeCount = 9;
        NativeArray<byte> result = new NativeArray<byte>(biomeCount, allocator, NativeArrayOptions.ClearMemory);

        if (flowerSettings.allowedBiomes == null || flowerSettings.allowedBiomes.Length == 0)
        {
            for (int i = 0; i < result.Length; i++)
                result[i] = 1;

            return result;
        }

        for (int i = 0; i < flowerSettings.allowedBiomes.Length; i++)
        {
            int biomeIndex = (int)flowerSettings.allowedBiomes[i];
            if (biomeIndex >= 0 && biomeIndex < result.Length)
                result[biomeIndex] = 1;
        }

        return result;
    }

    private static int FlattenIndex(int x, int z, int height)
    {
        return x * height + z;
    }

    private static int Hash6(int v0, int v1, int v2, int v3, int v4, int v5)
    {
        unchecked
        {
            uint hash = 2166136261u;
            hash = MixHash(hash, v0);
            hash = MixHash(hash, v1);
            hash = MixHash(hash, v2);
            hash = MixHash(hash, v3);
            hash = MixHash(hash, v4);
            hash = MixHash(hash, v5);
            return (int)hash;
        }
    }

    private static int Hash7(int v0, int v1, int v2, int v3, int v4, int v5, int v6)
    {
        unchecked
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
    }

    private static uint MixHash(uint hash, int value)
    {
        unchecked
        {
            hash ^= (uint)value;
            hash *= 16777619u;
            hash ^= hash >> 13;
            hash *= 1274126177u;
            hash ^= hash >> 16;
            return hash;
        }
    }

    internal struct GrassSubChunkDiscoveryResult
    {
        public byte valid;
        public float3 localPosition;
        public float yaw;
        public float uniformScale;
        public uint selectionRank;
        public float forestBlend;
    }

    private struct FlowerDiscoveryResult
    {
        public byte valid;
        public float3 localPosition;
        public float yaw;
        public float uniformScale;
        public BiomeType biome;
        public int flowerHash;
    }

    private struct CloverDiscoveryResult
    {
        public byte valid;
        public float3 localPosition;
        public quaternion localRotation;
        public float uniformScale;
        public uint selectionRank;
        public float grassInfluenceRadius;
        public int prefabIndex;
    }

    [BurstCompile]
    private struct BillboardGrassDiscoveryJob : IJobParallelFor
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
        [ReadOnly] public NativeArray<float4> cloverInfluences;
        public float treeExclusionRadiusSqr;
        public float bushExclusionRadiusSqr;
        public float rockExclusionRadiusSqr;
        public float cloverGrassDensityInsidePatch;
        [WriteOnly] public NativeArray<GrassSubChunkDiscoveryResult> results;
        public int worldSeed;
        public int seedOffset;
        public int chunkCoordX;
        public int chunkCoordZ;
        public int chunkSize;
        public int cellsPerAxis;
        public float cellSize;
        public float cellJitter;
        public float topLeftX;
        public float bottomLeftZ;
        public float worldScale;
        public float meshHeightMultiplier;
        public bool randomizeYaw;
        public float minScale;
        public float maxScale;

        public void Execute(int index)
        {
            int cellX = index % cellsPerAxis;
            int cellZ = index / cellsPerAxis;

            int cellHash = Hash7(worldSeed, seedOffset, chunkCoordX, chunkCoordZ, cellX, cellZ, 713);
            float jitterX = math.lerp(0.5f, Hash01(cellHash + 31), cellJitter);
            float jitterZ = math.lerp(0.5f, Hash01(cellHash + 67), cellJitter);
            float sampleX = (cellX + jitterX) * cellSize;
            float sampleZ = (cellZ + jitterZ) * cellSize;
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

            float cloverInfluence = GetCloverInfluence(localXZ);
            if (cloverInfluence > 0f)
            {
                float grassKeepChance = math.lerp(1f, cloverGrassDensityInsidePatch, cloverInfluence);
                if (Hash01(cellHash + 509) > grassKeepChance)
                {
                    results[index] = default;
                    return;
                }
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

            return 0f;
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

        private float GetCloverInfluence(float2 localXZ)
        {
            float influence = 0f;

            for (int i = 0; i < cloverInfluences.Length; i++)
            {
                float4 clover = cloverInfluences[i];
                float2 delta = localXZ - clover.xy;
                float dist = math.length(delta);
                float radius = math.max(clover.z, 0.01f);
                float coreRadius = math.min(math.max(clover.w, 0.01f), radius);
                float fade = math.saturate((radius - dist) / math.max(radius - coreRadius, 0.01f));
                float core = dist <= coreRadius ? 1f : 0f;
                influence = math.max(influence, math.max(core, fade));
            }

            return influence;
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
            return math.lerp(math.lerp(h00, h10, tx), math.lerp(h01, h11, tx), tz);
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
    }

    [BurstCompile]
    private struct FlowerDiscoveryJob : IJobParallelFor
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
        [ReadOnly] public NativeArray<float> slopeMap;
        public int slopeMapWidth;
        public int slopeMapHeight;
        public bool hasSlopeMap;
        [ReadOnly] public NativeArray<byte> allowedBiomeMask;
        [ReadOnly] public NativeArray<float2> treeExclusionPositions;
        public float treeExclusionRadiusSqr;
        [WriteOnly] public NativeArray<FlowerDiscoveryResult> results;
        public int worldSeed;
        public int seedOffset;
        public int chunkSize;
        public int globalCellMinX;
        public int globalCellMinZ;
        public int globalCellCountX;
        public int maxPatchCentersPerCell;
        public int minFlowersPerPatch;
        public int maxFlowersPerPatch;
        public float patchCellSize;
        public float patchNoiseScale;
        public float patchNoiseThreshold;
        public float patchSpawnChance;
        public float minPatchRadius;
        public float maxPatchRadius;
        public float chunkSampleMinX;
        public float chunkSampleMinZ;
        public float chunkSampleMaxX;
        public float chunkSampleMaxZ;
        public float topLeftX;
        public float bottomLeftZ;
        public float worldScale;
        public float meshHeightMultiplier;
        public float maxSlope;
        public bool randomizeYaw;
        public float minScale;
        public float maxScale;

        public void Execute(int index)
        {
            int flowersPerPatch = maxFlowersPerPatch;
            int patchLinear = index / flowersPerPatch;
            int flowerIndex = index - patchLinear * flowersPerPatch;
            int patchIndex = patchLinear % maxPatchCentersPerCell;
            int cellLinear = patchLinear / maxPatchCentersPerCell;
            int globalCellX = globalCellMinX + cellLinear % globalCellCountX;
            int globalCellZ = globalCellMinZ + cellLinear / globalCellCountX;

            int patchHash = Hash6(worldSeed, seedOffset, globalCellX, globalCellZ, patchIndex, 421);
            float centerGlobalSampleX = (globalCellX + Hash01(patchHash + 31)) * patchCellSize;
            float centerGlobalSampleZ = (globalCellZ + Hash01(patchHash + 67)) * patchCellSize;

            float patchNoise = SampleValueNoise(
                centerGlobalSampleX * patchNoiseScale,
                centerGlobalSampleZ * patchNoiseScale,
                worldSeed + seedOffset);
            float patchNoise01 = math.saturate((patchNoise + 1f) * 0.5f);

            if (patchNoise01 < patchNoiseThreshold)
            {
                results[index] = default;
                return;
            }

            float patchStrength = patchNoiseThreshold >= 0.999f
                ? 1f
                : InverseLerp(patchNoiseThreshold, 1f, patchNoise01);
            float spawnChance = math.saturate(patchSpawnChance * patchStrength);

            if (Hash01(patchHash + 103) > spawnChance)
            {
                results[index] = default;
                return;
            }

            float patchRadius = math.max(
                0f,
                math.lerp(minPatchRadius, maxPatchRadius, Hash01(patchHash + 139)));

            if (centerGlobalSampleX + patchRadius < chunkSampleMinX ||
                centerGlobalSampleX - patchRadius > chunkSampleMaxX ||
                centerGlobalSampleZ + patchRadius < chunkSampleMinZ ||
                centerGlobalSampleZ - patchRadius > chunkSampleMaxZ)
            {
                results[index] = default;
                return;
            }

            int flowersInPatch = GetDeterministicCount(minFlowersPerPatch, maxFlowersPerPatch, patchHash + 173);
            if (flowerIndex >= flowersInPatch)
            {
                results[index] = default;
                return;
            }

            int flowerHash = Hash7(worldSeed, seedOffset, globalCellX, globalCellZ, patchIndex, flowerIndex, 557);
            float angle = Hash01(flowerHash + 19) * math.PI * 2f;
            float radius = math.sqrt(Hash01(flowerHash + 41)) * patchRadius;
            float globalSampleX = centerGlobalSampleX + math.cos(angle) * radius;
            float globalSampleZ = centerGlobalSampleZ + math.sin(angle) * radius;

            if (globalSampleX < chunkSampleMinX || globalSampleX > chunkSampleMaxX ||
                globalSampleZ < chunkSampleMinZ || globalSampleZ > chunkSampleMaxZ)
            {
                results[index] = default;
                return;
            }

            float localSampleX = globalSampleX - chunkSampleMinX;
            float localSampleZ = globalSampleZ - chunkSampleMinZ;
            int mapX = math.clamp((int)math.round(localSampleX), 0, chunkSize);
            int mapZ = math.clamp((int)math.round(localSampleZ), 0, chunkSize);
            int paddedX = mapX + 1;
            int paddedZ = mapZ + 1;

            if (!IsValidFlowerSample(paddedX, paddedZ, out BiomeType biome))
            {
                results[index] = default;
                return;
            }

            float localX = (topLeftX + localSampleX) * worldScale;
            float localZ = (bottomLeftZ + localSampleZ) * worldScale;
            float2 localXZ = new float2(localX, localZ);

            if (IsInsideExclusion(localXZ, treeExclusionPositions, treeExclusionRadiusSqr))
            {
                results[index] = default;
                return;
            }

            float height = SampleHeightBilinear(localSampleX, localSampleZ);
            float yaw = randomizeYaw ? Hash01(flowerHash + 83) * 360f : 0f;
            float uniformScale = math.lerp(minScale, maxScale, Hash01(flowerHash + 127));

            results[index] = new FlowerDiscoveryResult
            {
                valid = 1,
                localPosition = new float3(
                    localX,
                    height * meshHeightMultiplier * worldScale,
                    localZ),
                yaw = yaw,
                uniformScale = uniformScale,
                biome = biome,
                flowerHash = flowerHash
            };
        }

        private bool IsValidFlowerSample(int paddedX, int paddedZ, out BiomeType biome)
        {
            biome = ReadBiome(paddedX, paddedZ);

            if (ReadSurface(paddedX, paddedZ) != SurfaceType.Grass)
                return false;

            int biomeIndex = (int)biome;
            if (biomeIndex < 0 || biomeIndex >= allowedBiomeMask.Length || allowedBiomeMask[biomeIndex] == 0)
                return false;

            if (hasSlopeMap && ReadSlope(paddedX, paddedZ) > maxSlope)
                return false;

            return true;
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
            return math.lerp(math.lerp(h00, h10, tx), math.lerp(h01, h11, tx), tz);
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

        private float ReadSlope(int x, int z)
        {
            x = math.clamp(x, 0, slopeMapWidth - 1);
            z = math.clamp(z, 0, slopeMapHeight - 1);
            return slopeMap[x * slopeMapHeight + z];
        }

        private static int GetDeterministicCount(int minCount, int maxCount, int hash)
        {
            if (maxCount <= minCount)
                return minCount;

            int range = maxCount - minCount + 1;
            int offset = (int)math.floor(Hash01(hash) * range);
            return math.clamp(minCount + offset, minCount, maxCount);
        }

        private static float SampleValueNoise(float x, float z, int seed)
        {
            int ix = (int)math.floor(x);
            int iz = (int)math.floor(z);
            float fx = x - ix;
            float fz = z - iz;
            float u = Quintic(fx);
            float v = Quintic(fz);
            float a = HashToSignedValue(ix, iz, seed);
            float b = HashToSignedValue(ix + 1, iz, seed);
            float c = HashToSignedValue(ix, iz + 1, seed);
            float d = HashToSignedValue(ix + 1, iz + 1, seed);
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

        private static float HashToSignedValue(int x, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)seed;
                h ^= 374761393u * (uint)x;
                h ^= 668265263u * (uint)z;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;

                float value01 = (h & 0x00FFFFFFu) / 16777215f;
                return value01 * 2f - 1f;
            }
        }

        private static float InverseLerp(float a, float b, float value)
        {
            return math.saturate((value - a) / (b - a));
        }
    }

    [BurstCompile]
    private struct CloverDiscoveryJob : IJobParallelFor
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
        [ReadOnly] public NativeArray<float> slopeMap;
        public int slopeMapWidth;
        public int slopeMapHeight;
        public bool hasSlopeMap;
        [ReadOnly] public NativeArray<float2> treeExclusionPositions;
        [ReadOnly] public NativeArray<float2> bushExclusionPositions;
        [ReadOnly] public NativeArray<float2> rockExclusionPositions;
        public float treeExclusionRadiusSqr;
        public float bushExclusionRadiusSqr;
        public float rockExclusionRadiusSqr;
        [WriteOnly] public NativeArray<CloverDiscoveryResult> results;
        public int worldSeed;
        public int seedOffset;
        public int chunkSize;
        public int globalCellMinX;
        public int globalCellMinZ;
        public int globalCellCountX;
        public int maxPatchCentersPerCell;
        public int minClumpsPerPatch;
        public int maxClumpsPerPatch;
        public float patchCellSize;
        public float patchNoiseScale;
        public float patchNoiseThreshold;
        public float patchSpawnChance;
        public float minPatchRadius;
        public float maxPatchRadius;
        public float chunkSampleMinX;
        public float chunkSampleMinZ;
        public float chunkSampleMaxX;
        public float chunkSampleMaxZ;
        public float topLeftX;
        public float bottomLeftZ;
        public float worldScale;
        public float meshHeightMultiplier;
        public float maxSlope;
        public bool randomizeYaw;
        public float minScale;
        public float maxScale;
        public int prefabCount;
        public float grassInfluenceRadius;

        public void Execute(int index)
        {
            int clumpsPerPatch = maxClumpsPerPatch;
            int patchLinear = index / clumpsPerPatch;
            int clumpIndex = index - patchLinear * clumpsPerPatch;
            int patchIndex = patchLinear % maxPatchCentersPerCell;
            int cellLinear = patchLinear / maxPatchCentersPerCell;
            int globalCellX = globalCellMinX + cellLinear % globalCellCountX;
            int globalCellZ = globalCellMinZ + cellLinear / globalCellCountX;

            int patchHash = Hash6(worldSeed, seedOffset, globalCellX, globalCellZ, patchIndex, 811);
            float centerGlobalSampleX = (globalCellX + Hash01(patchHash + 31)) * patchCellSize;
            float centerGlobalSampleZ = (globalCellZ + Hash01(patchHash + 67)) * patchCellSize;

            float broadNoise = SampleValueNoise(
                centerGlobalSampleX * patchNoiseScale,
                centerGlobalSampleZ * patchNoiseScale,
                worldSeed + seedOffset);
            float fineNoise = SampleValueNoise(
                centerGlobalSampleX * patchNoiseScale * 3.7f + 19.1f,
                centerGlobalSampleZ * patchNoiseScale * 3.7f - 7.4f,
                worldSeed + seedOffset + 37);
            float colonyNoise01 = math.saturate((broadNoise + 1f) * 0.5f);
            float breakupNoise01 = math.saturate((fineNoise + 1f) * 0.5f);
            float patchSuitability = colonyNoise01 * 0.78f + breakupNoise01 * 0.22f;

            if (patchSuitability < patchNoiseThreshold)
            {
                results[index] = default;
                return;
            }

            float patchStrength = patchNoiseThreshold >= 0.999f
                ? 1f
                : InverseLerp(patchNoiseThreshold, 1f, patchSuitability);
            float spawnChance = math.saturate(patchSpawnChance * math.lerp(0.55f, 1f, patchStrength));

            if (Hash01(patchHash + 103) > spawnChance)
            {
                results[index] = default;
                return;
            }

            float patchRadius = math.max(
                0f,
                math.lerp(minPatchRadius, maxPatchRadius, Hash01(patchHash + 139)));

            if (centerGlobalSampleX + patchRadius < chunkSampleMinX ||
                centerGlobalSampleX - patchRadius > chunkSampleMaxX ||
                centerGlobalSampleZ + patchRadius < chunkSampleMinZ ||
                centerGlobalSampleZ - patchRadius > chunkSampleMaxZ)
            {
                results[index] = default;
                return;
            }

            int clumpsInPatch = GetDeterministicCount(minClumpsPerPatch, maxClumpsPerPatch, patchHash + 173);
            if (clumpIndex >= clumpsInPatch)
            {
                results[index] = default;
                return;
            }

            int clumpHash = Hash7(worldSeed, seedOffset, globalCellX, globalCellZ, patchIndex, clumpIndex, 977);
            float angle = Hash01(clumpHash + 19) * math.PI * 2f;
            float radius = math.sqrt(Hash01(clumpHash + 41)) * patchRadius;
            float stretch = math.lerp(0.72f, 1.28f, Hash01(patchHash + 197));
            float crossStretch = math.lerp(0.78f, 1.12f, Hash01(patchHash + 211));
            float patchAngle = Hash01(patchHash + 223) * math.PI * 2f;
            float x = math.cos(angle) * radius * stretch;
            float z = math.sin(angle) * radius * crossStretch;
            float sinPatch = math.sin(patchAngle);
            float cosPatch = math.cos(patchAngle);
            float globalSampleX = centerGlobalSampleX + x * cosPatch - z * sinPatch;
            float globalSampleZ = centerGlobalSampleZ + x * sinPatch + z * cosPatch;

            if (globalSampleX < chunkSampleMinX || globalSampleX > chunkSampleMaxX ||
                globalSampleZ < chunkSampleMinZ || globalSampleZ > chunkSampleMaxZ)
            {
                results[index] = default;
                return;
            }

            float localSampleX = globalSampleX - chunkSampleMinX;
            float localSampleZ = globalSampleZ - chunkSampleMinZ;
            int mapX = math.clamp((int)math.round(localSampleX), 0, chunkSize);
            int mapZ = math.clamp((int)math.round(localSampleZ), 0, chunkSize);
            int paddedX = mapX + 1;
            int paddedZ = mapZ + 1;

            if (!IsValidCloverSample(paddedX, paddedZ))
            {
                results[index] = default;
                return;
            }

            float localX = (topLeftX + localSampleX) * worldScale;
            float localZ = (bottomLeftZ + localSampleZ) * worldScale;
            float2 localXZ = new float2(localX, localZ);

            if (IsInsideExclusion(localXZ, treeExclusionPositions, treeExclusionRadiusSqr) ||
                IsInsideExclusion(localXZ, bushExclusionPositions, bushExclusionRadiusSqr) ||
                IsInsideExclusion(localXZ, rockExclusionPositions, rockExclusionRadiusSqr))
            {
                results[index] = default;
                return;
            }

            float height = SampleHeightBilinear(localSampleX, localSampleZ);
            float yaw = randomizeYaw ? Hash01(clumpHash + 83) * 360f : 0f;
            quaternion localRotation = CreateSurfaceAlignedRotation(
                yaw,
                SampleTerrainNormal(paddedX, paddedZ));
            float uniformScale = math.lerp(minScale, maxScale, Hash01(clumpHash + 127));
            int prefabIndex = prefabCount > 1
                ? math.min(prefabCount - 1, (int)math.floor(Hash01(clumpHash + 239) * prefabCount))
                : 0;

            results[index] = new CloverDiscoveryResult
            {
                valid = 1,
                localPosition = new float3(
                    localX,
                    height * meshHeightMultiplier * worldScale,
                    localZ),
                localRotation = localRotation,
                uniformScale = uniformScale,
                selectionRank = (uint)clumpHash,
                grassInfluenceRadius = math.max(0.01f, grassInfluenceRadius * uniformScale),
                prefabIndex = prefabIndex
            };
        }

        private bool IsValidCloverSample(int paddedX, int paddedZ)
        {
            if (ReadSurface(paddedX, paddedZ) != SurfaceType.Grass)
                return false;

            if (ReadBiome(paddedX, paddedZ) != BiomeType.Grassland)
                return false;

            if (hasGroundCoverMap)
            {
                GroundCoverType groundCover = ReadGroundCover(paddedX, paddedZ);
                if (groundCover != GroundCoverType.Default &&
                    groundCover != GroundCoverType.DarkGrass)
                {
                    return false;
                }
            }

            if (hasSlopeMap && ReadSlope(paddedX, paddedZ) > maxSlope)
                return false;

            return true;
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

        private quaternion CreateSurfaceAlignedRotation(float yawDegrees, float3 surfaceNormal)
        {
            float3 up = math.normalizesafe(surfaceNormal, new float3(0f, 1f, 0f));
            float yawRadians = math.radians(yawDegrees);
            float3 yawForward = new float3(math.sin(yawRadians), 0f, math.cos(yawRadians));
            float3 forward = yawForward - up * math.dot(yawForward, up);

            if (math.lengthsq(forward) < 0.0001f)
            {
                forward = math.cross(new float3(1f, 0f, 0f), up);
                if (math.lengthsq(forward) < 0.0001f)
                    forward = math.cross(new float3(0f, 0f, 1f), up);
            }

            return quaternion.LookRotationSafe(math.normalize(forward), up);
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
            return math.lerp(math.lerp(h00, h10, tx), math.lerp(h01, h11, tx), tz);
        }

        private float3 SampleTerrainNormal(int paddedX, int paddedZ)
        {
            float left = ReadHeight(paddedX - 1, paddedZ);
            float right = ReadHeight(paddedX + 1, paddedZ);
            float down = ReadHeight(paddedX, paddedZ - 1);
            float up = ReadHeight(paddedX, paddedZ + 1);
            float dx = (right - left) * meshHeightMultiplier;
            float dz = (up - down) * meshHeightMultiplier;

            return math.normalize(new float3(-dx, 2f, -dz));
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

        private float ReadSlope(int x, int z)
        {
            x = math.clamp(x, 0, slopeMapWidth - 1);
            z = math.clamp(z, 0, slopeMapHeight - 1);
            return slopeMap[x * slopeMapHeight + z];
        }

        private static int GetDeterministicCount(int minCount, int maxCount, int hash)
        {
            if (maxCount <= minCount)
                return minCount;

            int range = maxCount - minCount + 1;
            int offset = (int)math.floor(Hash01(hash) * range);
            return math.clamp(minCount + offset, minCount, maxCount);
        }

        private static float SampleValueNoise(float x, float z, int seed)
        {
            int ix = (int)math.floor(x);
            int iz = (int)math.floor(z);
            float fx = x - ix;
            float fz = z - iz;
            float u = Quintic(fx);
            float v = Quintic(fz);
            float a = HashToSignedValue(ix, iz, seed);
            float b = HashToSignedValue(ix + 1, iz, seed);
            float c = HashToSignedValue(ix, iz + 1, seed);
            float d = HashToSignedValue(ix + 1, iz + 1, seed);
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

        private static float HashToSignedValue(int x, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)seed;
                h ^= 374761393u * (uint)x;
                h ^= 668265263u * (uint)z;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;

                float value01 = (h & 0x00FFFFFFu) / 16777215f;
                return value01 * 2f - 1f;
            }
        }

        private static float InverseLerp(float a, float b, float value)
        {
            return math.saturate((value - a) / (b - a));
        }
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
        [ReadOnly] public NativeArray<float4> cloverInfluences;
        public float treeExclusionRadiusSqr;
        public float bushExclusionRadiusSqr;
        public float rockExclusionRadiusSqr;
        public float cloverGrassDensityInsidePatch;
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

            float cloverInfluence = GetCloverInfluence(localXZ);
            if (cloverInfluence > 0f)
            {
                float grassKeepChance = math.lerp(1f, cloverGrassDensityInsidePatch, cloverInfluence);
                if (Hash01(cellHash + 509) > grassKeepChance)
                {
                    results[index] = default;
                    return;
                }
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

            return 0f;
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

        private float GetCloverInfluence(float2 localXZ)
        {
            float influence = 0f;

            for (int i = 0; i < cloverInfluences.Length; i++)
            {
                float4 clover = cloverInfluences[i];
                float2 delta = localXZ - clover.xy;
                float dist = math.length(delta);
                float radius = math.max(clover.z, 0.01f);
                float coreRadius = math.min(math.max(clover.w, 0.01f), radius);
                float fade = math.saturate((radius - dist) / math.max(radius - coreRadius, 0.01f));
                float core = dist <= coreRadius ? 1f : 0f;
                influence = math.max(influence, math.max(core, fade));
            }

            return influence;
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

        if (variant == WorldFeatureVariant.GrasslandMapleTree)
        {
            leafTint = PickWeightedColor(
                Hash01(baseHash + 17),
                new Color(0.42f, 0.68f, 0.32f, 1f),
                new Color(0.36f, 0.58f, 0.28f, 1f),
                new Color(0.50f, 0.64f, 0.30f, 1f),
                Hash01(baseHash + 31),
                0.55f,
                0.86f);
            leafTint.a = 0;

            barkTint = Color.Lerp(
                new Color(0.88f, 0.86f, 0.80f, 1f),
                new Color(1.02f, 0.98f, 0.90f, 1f),
                Hash01(baseHash + 43));
            return;
        }

        if (variant == WorldFeatureVariant.GrasslandWillowTree)
        {
            leafTint = PickWeightedColor(
                Hash01(baseHash + 17),
                new Color(0.50f, 0.66f, 0.36f, 1f),
                new Color(0.42f, 0.58f, 0.30f, 1f),
                new Color(0.62f, 0.70f, 0.42f, 1f),
                Hash01(baseHash + 31),
                0.54f,
                0.84f);
            leafTint.a = 0;

            barkTint = Color.Lerp(
                new Color(0.70f, 0.64f, 0.52f, 1f),
                new Color(0.92f, 0.86f, 0.70f, 1f),
                Hash01(baseHash + 43));
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

    private static ulong CreateStableBushId(
        ChunkCoord chunkCoord,
        int placementIndex,
        WorldFeatureVariant variant,
        float sampleX,
        float sampleZ)
    {
        int quantizedSampleX = Mathf.RoundToInt(sampleX * 100f);
        int quantizedSampleZ = Mathf.RoundToInt(sampleZ * 100f);

        unchecked
        {
            ulong hash = 14695981039346656037UL;
            hash = MixStableBushId(hash, chunkCoord.x);
            hash = MixStableBushId(hash, chunkCoord.z);
            hash = MixStableBushId(hash, placementIndex);
            hash = MixStableBushId(hash, (int)variant);
            hash = MixStableBushId(hash, quantizedSampleX);
            hash = MixStableBushId(hash, quantizedSampleZ);
            return hash;
        }
    }

    private static ulong MixStableBushId(ulong hash, int value)
    {
        unchecked
        {
            hash ^= (uint)value;
            hash *= 1099511628211UL;
            hash ^= (uint)(value >> 16);
            hash *= 1099511628211UL;
            return hash;
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
