using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class FoliageManager
{
    private readonly Transform foliageParent;
    private readonly GrassSettings grassSettings;
    private readonly FlowerSettings flowerSettings;
    private readonly TreeSettings treeSettings;
    private readonly int worldSeed;
    private readonly int chunkSize;
    private readonly float worldScale;
    private readonly float meshHeightMultiplier;

    private Mesh grassMesh;
    private Material grassMaterial;
    private int grassInstanceDataPropertyId;

    private Mesh billboardGrassMesh;
    private Material billboardGrassMaterial;

    private Mesh flowerMesh;
    private Material flowerMaterial;
    private int flowerPetalColorPropertyId;

    private TreeBillboardRenderData mapleTreeBillboard;
    private TreeBillboardRenderData sugarMapleTreeBillboard;
    private TreeBillboardRenderData birchAspenTreeBillboard;
    private TreeBillboardRenderData beechTreeBillboard;
    private TreeBillboardRenderData spruceTreeBillboard;
    private TreeBillboardRenderData whitePineTreeBillboard;
    private TreeBillboardRenderData oakTreeBillboard;
    private TreeBillboardRenderData fallbackTreeBillboard;
    private TreeBillboardRenderData grasslandMapleTreeBillboard;
    private TreeBillboardRenderData grasslandBirchAspenTreeBillboard;
    private TreeBillboardRenderData grasslandWhitePineTreeBillboard;
    private TreeBillboardRenderData grasslandOakTreeBillboard;
    private TreeBillboardRenderData grasslandWillowTreeBillboard;
    private TreeBillboardRenderData grasslandFallbackTreeBillboard;
    private readonly Queue<GrassSubChunkWorkItem> pendingGrassSubChunkWork = new();
    private readonly HashSet<GrassSubChunkWorkKey> queuedGrassSubChunks = new();
    private readonly HashSet<ChunkCoord> dirtyGrassChunks = new();
    private readonly List<FoliageBatchWorkItem> pendingFoliageBatchWork = new();
    private readonly HashSet<FoliageBatchWorkKey> queuedFoliageBatchWork = new();

    public FoliageManager(Transform foliageParent, GrassSettings grassSettings, FlowerSettings flowerSettings, TreeSettings treeSettings, int worldSeed,
        int chunkSize, float worldScale, float meshHeightMultiplier)
    {
        this.foliageParent = foliageParent;
        this.grassSettings = grassSettings;
        this.flowerSettings = flowerSettings;
        this.treeSettings = treeSettings;
        this.worldSeed = worldSeed;
        this.chunkSize = chunkSize;
        this.worldScale = worldScale;
        this.meshHeightMultiplier = meshHeightMultiplier;

        ResolveGrassRenderAssets();
        ResolveFlowerRenderAssets();
        ResolveTreeRenderAssets();
    }

    public void HandleViewerSubChunkChanged(
        ChunkManager chunkManager,
        ChunkCoord viewerCoord,
        SubChunkCoord viewerGlobalSubChunk,
        List<ChunkCoord> orderedActiveCoords,
        bool viewerChunkChanged)
    {
        long stageStart = TerrainGenerationProfiler.GetTimestamp();

        for (int i = 0; i < orderedActiveCoords.Count; i++)
        {
            ChunkCoord coord = orderedActiveCoords[i];
            ChunkRecord record = chunkManager.GetChunkRecord(coord);
            ChunkRuntime runtime = chunkManager.GetChunkRuntime(record);

            if (record == null || runtime == null)
                continue;

            EnsureFoliageRuntimeExists(runtime, record);

            bool useNearGrass = IsWithinNearGrass(viewerCoord, coord);
            bool useBillboardGrass = IsWithinBillboardGrass(viewerCoord, coord);
            bool useFlowers = IsWithinFlowerRenderRange(viewerCoord, coord);
            bool useTrees = IsWithinTreeRenderRange(viewerCoord, coord);
            bool useBushes = IsWithinBushRenderRange(viewerCoord, coord);
            bool useRocks = IsWithinRockRenderRange(viewerCoord, coord);
            bool useFoliage = useNearGrass || useBillboardGrass || useFlowers || useTrees || useBushes || useRocks;

            if (!HasRequiredTerrainData(record))
            {
                runtime.FoliageRuntime.ClearCachedBatches();
                runtime.FoliageRuntime.SetVisible(false);
                continue;
            }

            if (!useFoliage)
            {
                runtime.FoliageRuntime.ClearCachedBatches();
                runtime.FoliageRuntime.SetVisible(false);
                continue;
            }

            if (useTrees)
            {
                EnsureTreesGenerated(record);
                RebuildTreeRepresentationIfNeeded(runtime, record, viewerCoord);
            }
            else
            {
                runtime.FoliageRuntime.ClearTreeRepresentation();
            }

            if (useBushes)
            {
                EnsureBushesGenerated(record);
                RebuildBushGameObjectsIfNeeded(runtime, record);
            }
            else
            {
                runtime.FoliageRuntime.ClearBushGameObjects();
            }

            if (useRocks)
            {
                EnsureRocksGenerated(record);
                RebuildRockGameObjectsIfNeeded(runtime, record);
            }
            else
            {
                runtime.FoliageRuntime.ClearRockGameObjects();
            }

            if (useFlowers && HasFlowerRenderAssets())
            {
                if (record.FoliageData == null || !record.FoliageData.flowersGenerated)
                {
                    EnsureFlowersGenerated(record);
                }

                if (!runtime.FoliageRuntime.HasValidFlowerRenderData())
                    EnqueueFoliageBatchRebuild(record, FoliageBatchWorkType.Flower);
            }
            else
            {
                runtime.FoliageRuntime.ClearFlowerBatches();
            }

            if (useNearGrass)
            {
                EnsureRocksGenerated(record);
                EnqueueMissingGrassSubChunks(record, viewerGlobalSubChunk);
                EnqueueFoliageBatchRebuild(record, FoliageBatchWorkType.NearGrass);
            }
            else if (useBillboardGrass)
            {
                if (record.FoliageData == null || !record.FoliageData.billboardGenerated)
                {
                    EnsureRocksGenerated(record);

                    long billboardGenerationStart = TerrainGenerationProfiler.GetTimestamp();
                    FoliageGenerator.GenerateBillboardGrassForChunk(
                        record,
                        grassSettings,
                        treeSettings,
                        worldSeed,
                        chunkSize,
                        worldScale,
                        meshHeightMultiplier);
                    TerrainGenerationProfiler.Record(
                        TerrainGenerationProfileStage.FoliageBillboardGrassGeneration,
                        billboardGenerationStart);
                }

                if (viewerChunkChanged || !runtime.FoliageRuntime.HasValidBillboardRenderData())
                    EnqueueFoliageBatchRebuild(record, FoliageBatchWorkType.BillboardGrass);
            }

            runtime.FoliageRuntime.SetVisible(true);
        }

        TerrainGenerationProfiler.Record(
            TerrainGenerationProfileStage.FoliageHandleSubChunkChanged,
            stageStart);
    }

    public void DrawVisibleFoliageEveryFrame(
        ChunkManager chunkManager,
        ChunkCoord viewerCoord,
        SubChunkCoord viewerGlobalSubChunk,
        List<ChunkCoord> orderedActiveCoords)
    {
        long stageStart = TerrainGenerationProfiler.GetTimestamp();

        ProcessPendingGrassSubChunkWork(chunkManager, viewerCoord, viewerGlobalSubChunk);
        ProcessPendingFoliageBatchWork(chunkManager, viewerCoord, viewerGlobalSubChunk);

        for (int i = 0; i < orderedActiveCoords.Count; i++)
        {
            ChunkCoord coord = orderedActiveCoords[i];
            ChunkRecord record = chunkManager.GetChunkRecord(coord);
            ChunkRuntime runtime = chunkManager.GetChunkRuntime(record);

            if (record == null || runtime == null || runtime.FoliageRuntime == null)
                continue;

            bool useNearGrass = IsWithinNearGrass(viewerCoord, coord);
            bool useBillboardGrass = IsWithinBillboardGrass(viewerCoord, coord);
            bool useFlowers = IsWithinFlowerRenderRange(viewerCoord, coord);
            bool useTrees = IsWithinTreeRenderRange(viewerCoord, coord);
            bool useBushes = IsWithinBushRenderRange(viewerCoord, coord);
            bool useRocks = IsWithinRockRenderRange(viewerCoord, coord);
            bool useFoliage = useNearGrass || useBillboardGrass || useFlowers || useTrees || useBushes || useRocks;

            if (!HasRequiredTerrainData(record))
            {
                runtime.FoliageRuntime.SetVisible(false);
                continue;
            }

            if (!useFoliage)
            {
                runtime.FoliageRuntime.SetVisible(false);
                continue;
            }

            runtime.FoliageRuntime.SetVisible(true);

            if (useTrees)
            {
                EnsureTreesGenerated(record);
                RebuildTreeRepresentationIfNeeded(runtime, record, viewerCoord);
            }
            else
            {
                runtime.FoliageRuntime.ClearTreeRepresentation();
            }

            if (useBushes)
            {
                EnsureBushesGenerated(record);
                RebuildBushGameObjectsIfNeeded(runtime, record);
            }
            else
            {
                runtime.FoliageRuntime.ClearBushGameObjects();
            }

            if (useRocks)
            {
                EnsureRocksGenerated(record);
                RebuildRockGameObjectsIfNeeded(runtime, record);
            }
            else
            {
                runtime.FoliageRuntime.ClearRockGameObjects();
            }

            if (useNearGrass)
            {
                EnsureRocksGenerated(record);
                EnqueueMissingGrassSubChunks(record, viewerGlobalSubChunk);

                if (!runtime.FoliageRuntime.HasValidGrassRenderData())
                {
                    EnqueueFoliageBatchRebuild(record, FoliageBatchWorkType.NearGrass);
                }

                runtime.FoliageRuntime.SetVisible(true);
                runtime.FoliageRuntime.DrawGrass();
            }
            else if (useBillboardGrass)
            {
                if (record.FoliageData == null || !record.FoliageData.billboardGenerated)
                {
                    EnsureRocksGenerated(record);

                    long billboardGenerationStart = TerrainGenerationProfiler.GetTimestamp();
                    FoliageGenerator.GenerateBillboardGrassForChunk(
                        record,
                        grassSettings,
                        treeSettings,
                        worldSeed,
                        chunkSize,
                        worldScale,
                        meshHeightMultiplier);
                    TerrainGenerationProfiler.Record(
                        TerrainGenerationProfileStage.FoliageBillboardGrassGeneration,
                        billboardGenerationStart);
                }

                if (!runtime.FoliageRuntime.HasValidBillboardRenderData())
                {
                    EnqueueFoliageBatchRebuild(record, FoliageBatchWorkType.BillboardGrass);
                }

                runtime.FoliageRuntime.SetVisible(true);
                runtime.FoliageRuntime.DrawBillboards();
            }

            if (useFlowers && HasFlowerRenderAssets())
            {
                if (record.FoliageData == null || !record.FoliageData.flowersGenerated)
                {
                    EnsureFlowersGenerated(record);
                }

                if (!runtime.FoliageRuntime.HasValidFlowerRenderData())
                {
                    EnqueueFoliageBatchRebuild(record, FoliageBatchWorkType.Flower);
                }

                runtime.FoliageRuntime.SetVisible(true);
                runtime.FoliageRuntime.DrawFlowers();
            }

            DrawTreesForChunk(runtime, viewerCoord, coord);
        }

        TerrainGenerationProfiler.Record(
            TerrainGenerationProfileStage.FoliageDrawVisibleEveryFrame,
            stageStart);
    }

    public void AccumulateVisibleFoliageRenderStats(
        ChunkManager chunkManager,
        ChunkCoord viewerCoord,
        List<ChunkCoord> orderedActiveCoords,
        ref WorldRenderStatsDebugInfo stats)
    {
        for (int i = 0; i < orderedActiveCoords.Count; i++)
        {
            ChunkCoord coord = orderedActiveCoords[i];
            ChunkRecord record = chunkManager.GetChunkRecord(coord);
            ChunkRuntime runtime = chunkManager.GetChunkRuntime(record);

            if (record == null || runtime == null || runtime.FoliageRuntime == null)
                continue;

            bool useNearGrass = IsWithinNearGrass(viewerCoord, coord);
            bool useBillboardGrass = IsWithinBillboardGrass(viewerCoord, coord);
            bool useFlowers = IsWithinFlowerRenderRange(viewerCoord, coord);
            bool useTrees = IsWithinTreeRenderRange(viewerCoord, coord);
            bool useBushes = IsWithinBushRenderRange(viewerCoord, coord);
            bool useRocks = IsWithinRockRenderRange(viewerCoord, coord);
            bool useFoliage = useNearGrass || useBillboardGrass || useFlowers || useTrees || useBushes || useRocks;

            if (!HasRequiredTerrainData(record) || !useFoliage)
                continue;

            if (useNearGrass)
            {
                runtime.FoliageRuntime.AccumulateGrassRenderStats(ref stats);
            }
            else if (useBillboardGrass)
            {
                runtime.FoliageRuntime.AccumulateBillboardGrassRenderStats(ref stats);
            }

            if (useFlowers && HasFlowerRenderAssets())
                runtime.FoliageRuntime.AccumulateFlowerRenderStats(ref stats);

            if (useTrees)
            {
                FoliageRepresentationMode mode = GetTreeRepresentationMode(viewerCoord, coord);

                if (mode == FoliageRepresentationMode.GPUInstancedBillboard)
                    runtime.FoliageRuntime.AccumulateTreeBillboardRenderStats(ref stats);
                else if (mode == FoliageRepresentationMode.GameObjectWithCollision)
                    runtime.FoliageRuntime.AccumulateTreeGameObjectRenderStats(ref stats);
            }

            if (useBushes)
                runtime.FoliageRuntime.AccumulateBushGameObjectRenderStats(ref stats);

            if (useRocks)
                runtime.FoliageRuntime.AccumulateRockGameObjectRenderStats(ref stats);
        }
    }

    private void EnsureTreesGenerated(ChunkRecord record)
    {
        if (record.FoliageData == null || !record.FoliageData.treeCubesGenerated)
        {
            long stageStart = TerrainGenerationProfiler.GetTimestamp();
            FoliageGenerator.GenerateTreeCubesForChunk(
                record,
                treeSettings,
                worldSeed,
                chunkSize,
                worldScale,
                meshHeightMultiplier);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageTreeGeneration,
                stageStart);
        }
    }

    private void EnsureFlowersGenerated(ChunkRecord record)
    {
        if (!IsFlowerSystemEnabled())
            return;

        if (record.FoliageData == null || !record.FoliageData.treeCubesGenerated)
        {
            EnsureTreesGenerated(record);
        }

        if (record.FoliageData == null || !record.FoliageData.flowersGenerated)
        {
            long stageStart = TerrainGenerationProfiler.GetTimestamp();
            FoliageGenerator.GenerateFlowersForChunk(
                record,
                flowerSettings,
                worldSeed,
                chunkSize,
                worldScale,
                meshHeightMultiplier);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageFlowerGeneration,
                stageStart);
        }
    }

    private void EnsureBushesGenerated(ChunkRecord record)
    {
        if (record.FoliageData == null || !record.FoliageData.bushesGenerated)
        {
            long stageStart = TerrainGenerationProfiler.GetTimestamp();
            FoliageGenerator.GenerateBushesForChunk(
                record,
                treeSettings,
                worldSeed,
                chunkSize,
                worldScale,
                meshHeightMultiplier);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageBushGeneration,
                stageStart);
        }
    }

    private void EnsureRocksGenerated(ChunkRecord record)
    {
        if (record.FoliageData == null || !record.FoliageData.rocksGenerated)
        {
            long stageStart = TerrainGenerationProfiler.GetTimestamp();
            FoliageGenerator.GenerateRocksForChunk(
                record,
                treeSettings,
                worldSeed,
                chunkSize,
                worldScale,
                meshHeightMultiplier);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageRockGeneration,
                stageStart);
        }
    }

    private void RebuildTreeRepresentationIfNeeded(
        ChunkRuntime runtime,
        ChunkRecord record,
        ChunkCoord viewerCoord)
    {
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;

        FoliageRepresentationMode mode = GetTreeRepresentationMode(viewerCoord, record.ChunkCoord);
        if (foliageRuntime.HasCurrentTreeRepresentation(mode))
            return;

        foliageRuntime.ClearTreeGameObjects();
        foliageRuntime.ClearTreeBillboardMatrices();

        if (mode == FoliageRepresentationMode.GameObjectWithCollision)
        {
            long stageStart = TerrainGenerationProfiler.GetTimestamp();
            foliageRuntime.RebuildTreeGameObjects(
                record.FoliageData.treeCubeInstances,
                runtime.RootTransform);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageTreeGameObjectRebuild,
                stageStart);
        }
        else if (mode == FoliageRepresentationMode.GPUInstancedBillboard)
        {
            RebuildTreeBillboardMatrices(runtime, record);
        }

        foliageRuntime.SetCurrentTreeRepresentation(mode);
    }

    private void RebuildBushGameObjectsIfNeeded(ChunkRuntime runtime, ChunkRecord record)
    {
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;

        if (foliageRuntime == null || foliageRuntime.HasCurrentBushRepresentation())
            return;

        long stageStart = TerrainGenerationProfiler.GetTimestamp();
        foliageRuntime.RebuildBushGameObjects(
            record.FoliageData.bushInstances,
            runtime.RootTransform);
        TerrainGenerationProfiler.Record(
            TerrainGenerationProfileStage.FoliageBushGameObjectRebuild,
            stageStart);
    }

    private void RebuildRockGameObjectsIfNeeded(ChunkRuntime runtime, ChunkRecord record)
    {
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;

        if (foliageRuntime == null || foliageRuntime.HasCurrentRockRepresentation())
            return;

        long stageStart = TerrainGenerationProfiler.GetTimestamp();
        foliageRuntime.RebuildRockGameObjects(
            record.FoliageData.rockInstances,
            runtime.RootTransform);
        TerrainGenerationProfiler.Record(
            TerrainGenerationProfileStage.FoliageRockGameObjectRebuild,
            stageStart);
    }

    private void DrawTreesForChunk(
        ChunkRuntime runtime,
        ChunkCoord viewerCoord,
        ChunkCoord chunkCoord)
    {
        if (runtime.FoliageRuntime == null)
            return;

        if (!IsWithinTreeRenderRange(viewerCoord, chunkCoord))
            return;

        FoliageRepresentationMode mode = GetTreeRepresentationMode(viewerCoord, chunkCoord);

        if (mode == FoliageRepresentationMode.GPUInstancedBillboard)
        {
            runtime.FoliageRuntime.DrawTreeBillboards(
                treeSettings.castTreeShadows,
                treeSettings.receiveTreeShadows);
        }
    }

    private void RebuildTreeBillboardMatrices(ChunkRuntime runtime, ChunkRecord record)
    {
        long stageStart = TerrainGenerationProfiler.GetTimestamp();
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;
        ChunkFoliageData data = record.FoliageData;

        List<Matrix4x4> mapleWorldMatrices = new List<Matrix4x4>();
        List<Vector4> mapleLeafTints = new List<Vector4>();
        List<Matrix4x4> sugarMapleWorldMatrices = new List<Matrix4x4>();
        List<Vector4> sugarMapleLeafTints = new List<Vector4>();
        List<Matrix4x4> birchAspenWorldMatrices = new List<Matrix4x4>();
        List<Vector4> birchAspenLeafTints = new List<Vector4>();
        List<Matrix4x4> beechWorldMatrices = new List<Matrix4x4>();
        List<Vector4> beechLeafTints = new List<Vector4>();
        List<Matrix4x4> spruceWorldMatrices = new List<Matrix4x4>();
        List<Vector4> spruceLeafTints = new List<Vector4>();
        List<Matrix4x4> whitePineWorldMatrices = new List<Matrix4x4>();
        List<Vector4> whitePineLeafTints = new List<Vector4>();
        List<Matrix4x4> oakWorldMatrices = new List<Matrix4x4>();
        List<Vector4> oakLeafTints = new List<Vector4>();
        List<Matrix4x4> grasslandMapleWorldMatrices = new List<Matrix4x4>();
        List<Vector4> grasslandMapleLeafTints = new List<Vector4>();
        List<Matrix4x4> grasslandBirchAspenWorldMatrices = new List<Matrix4x4>();
        List<Vector4> grasslandBirchAspenLeafTints = new List<Vector4>();
        List<Matrix4x4> grasslandWhitePineWorldMatrices = new List<Matrix4x4>();
        List<Vector4> grasslandWhitePineLeafTints = new List<Vector4>();
        List<Matrix4x4> grasslandOakWorldMatrices = new List<Matrix4x4>();
        List<Vector4> grasslandOakLeafTints = new List<Vector4>();
        List<Matrix4x4> grasslandWillowWorldMatrices = new List<Matrix4x4>();
        List<Vector4> grasslandWillowLeafTints = new List<Vector4>();
        Matrix4x4 chunkLocalToWorld = runtime.RootTransform.localToWorldMatrix;

        int instanceCount = data.treeCubeInstances.Count;
        if (instanceCount > 0)
        {
            NativeArray<TreeBillboardRenderSourceData> sources =
                new NativeArray<TreeBillboardRenderSourceData>(instanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            NativeArray<float4x4> nativeMatrices =
                new NativeArray<float4x4>(instanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            NativeArray<float4> nativeLeafTints =
                new NativeArray<float4>(instanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            try
            {
                for (int i = 0; i < instanceCount; i++)
                {
                    TreeInstanceData instance = data.treeCubeInstances[i];
                    Vector4 leafTint = Color32ToLinearVector4(instance.leafTint);

                    sources[i] = new TreeBillboardRenderSourceData
                    {
                        localPosition = new float3(
                            instance.localPosition.x,
                            instance.localPosition.y,
                            instance.localPosition.z),
                        localRotation = new quaternion(
                            instance.localRotation.x,
                            instance.localRotation.y,
                            instance.localRotation.z,
                            instance.localRotation.w),
                        localScale = new float3(
                            instance.localScale.x,
                            instance.localScale.y,
                            instance.localScale.z),
                        leafTint = new float4(leafTint.x, leafTint.y, leafTint.z, leafTint.w)
                    };
                }

                TreeBillboardRenderBatchBuildJob job = new TreeBillboardRenderBatchBuildJob
                {
                    sources = sources,
                    chunkLocalToWorld = ToFloat4x4(chunkLocalToWorld),
                    matrices = nativeMatrices,
                    leafTints = nativeLeafTints
                };

                JobHandle handle = job.Schedule(instanceCount, 64);
                handle.Complete();

                for (int i = 0; i < instanceCount; i++)
                {
                    AddTreeBillboardMatrix(
                        data.treeCubeInstances[i].variant,
                        ToMatrix4x4(nativeMatrices[i]),
                        ToVector4(nativeLeafTints[i]),
                        mapleWorldMatrices,
                        mapleLeafTints,
                        sugarMapleWorldMatrices,
                        sugarMapleLeafTints,
                        birchAspenWorldMatrices,
                        birchAspenLeafTints,
                        beechWorldMatrices,
                        beechLeafTints,
                        spruceWorldMatrices,
                        spruceLeafTints,
                        whitePineWorldMatrices,
                        whitePineLeafTints,
                        oakWorldMatrices,
                        oakLeafTints,
                        grasslandMapleWorldMatrices,
                        grasslandMapleLeafTints,
                        grasslandBirchAspenWorldMatrices,
                        grasslandBirchAspenLeafTints,
                        grasslandWhitePineWorldMatrices,
                        grasslandWhitePineLeafTints,
                        grasslandOakWorldMatrices,
                        grasslandOakLeafTints,
                        grasslandWillowWorldMatrices,
                        grasslandWillowLeafTints);
                }
            }
            finally
            {
                if (sources.IsCreated)
                    sources.Dispose();
                if (nativeMatrices.IsCreated)
                    nativeMatrices.Dispose();
                if (nativeLeafTints.IsCreated)
                    nativeLeafTints.Dispose();
            }
        }

        foliageRuntime.CacheTreeBillboardMatrices(
            mapleWorldMatrices,
            mapleLeafTints,
            sugarMapleWorldMatrices,
            sugarMapleLeafTints,
            birchAspenWorldMatrices,
            birchAspenLeafTints,
            beechWorldMatrices,
            beechLeafTints,
            spruceWorldMatrices,
            spruceLeafTints,
            whitePineWorldMatrices,
            whitePineLeafTints,
            oakWorldMatrices,
            oakLeafTints,
            grasslandMapleWorldMatrices,
            grasslandMapleLeafTints,
            grasslandBirchAspenWorldMatrices,
            grasslandBirchAspenLeafTints,
            grasslandWhitePineWorldMatrices,
            grasslandWhitePineLeafTints,
            grasslandOakWorldMatrices,
            grasslandOakLeafTints,
            grasslandWillowWorldMatrices,
            grasslandWillowLeafTints);
        TerrainGenerationProfiler.Record(
            TerrainGenerationProfileStage.FoliageTreeBillboardBatchBuild,
            stageStart);
    }

    private void AddTreeBillboardMatrix(
        WorldFeatureVariant variant,
        Matrix4x4 worldMatrix,
        Vector4 leafTint,
        List<Matrix4x4> mapleWorldMatrices,
        List<Vector4> mapleLeafTints,
        List<Matrix4x4> sugarMapleWorldMatrices,
        List<Vector4> sugarMapleLeafTints,
        List<Matrix4x4> birchAspenWorldMatrices,
        List<Vector4> birchAspenLeafTints,
        List<Matrix4x4> beechWorldMatrices,
        List<Vector4> beechLeafTints,
        List<Matrix4x4> spruceWorldMatrices,
        List<Vector4> spruceLeafTints,
        List<Matrix4x4> whitePineWorldMatrices,
        List<Vector4> whitePineLeafTints,
        List<Matrix4x4> oakWorldMatrices,
        List<Vector4> oakLeafTints,
        List<Matrix4x4> grasslandMapleWorldMatrices,
        List<Vector4> grasslandMapleLeafTints,
        List<Matrix4x4> grasslandBirchAspenWorldMatrices,
        List<Vector4> grasslandBirchAspenLeafTints,
        List<Matrix4x4> grasslandWhitePineWorldMatrices,
        List<Vector4> grasslandWhitePineLeafTints,
        List<Matrix4x4> grasslandOakWorldMatrices,
        List<Vector4> grasslandOakLeafTints,
        List<Matrix4x4> grasslandWillowWorldMatrices,
        List<Vector4> grasslandWillowLeafTints)
    {
        switch (variant)
        {
            case WorldFeatureVariant.SugarMapleTree:
                sugarMapleWorldMatrices.Add(worldMatrix);
                sugarMapleLeafTints.Add(leafTint);
                break;
            case WorldFeatureVariant.BirchAspenTree:
                birchAspenWorldMatrices.Add(worldMatrix);
                birchAspenLeafTints.Add(leafTint);
                break;
            case WorldFeatureVariant.BeechTree:
                beechWorldMatrices.Add(worldMatrix);
                beechLeafTints.Add(leafTint);
                break;
            case WorldFeatureVariant.SpruceTree:
                spruceWorldMatrices.Add(worldMatrix);
                spruceLeafTints.Add(leafTint);
                break;
            case WorldFeatureVariant.WhitePineTree:
                whitePineWorldMatrices.Add(worldMatrix);
                whitePineLeafTints.Add(leafTint);
                break;
            case WorldFeatureVariant.OakTree:
                oakWorldMatrices.Add(worldMatrix);
                oakLeafTints.Add(leafTint);
                break;
            case WorldFeatureVariant.GrasslandMapleTree:
                grasslandMapleWorldMatrices.Add(worldMatrix);
                grasslandMapleLeafTints.Add(leafTint);
                break;
            case WorldFeatureVariant.GrasslandBirchAspenTree:
                grasslandBirchAspenWorldMatrices.Add(worldMatrix);
                grasslandBirchAspenLeafTints.Add(leafTint);
                break;
            case WorldFeatureVariant.GrasslandWhitePineTree:
                grasslandWhitePineWorldMatrices.Add(worldMatrix);
                grasslandWhitePineLeafTints.Add(leafTint);
                break;
            case WorldFeatureVariant.GrasslandOakTree:
                grasslandOakWorldMatrices.Add(worldMatrix);
                grasslandOakLeafTints.Add(leafTint);
                break;
            case WorldFeatureVariant.GrasslandWillowTree:
                grasslandWillowWorldMatrices.Add(worldMatrix);
                grasslandWillowLeafTints.Add(leafTint);
                break;
            default:
                mapleWorldMatrices.Add(worldMatrix);
                mapleLeafTints.Add(leafTint);
                break;
        }
    }

    private void EnqueueMissingGrassSubChunks(
        ChunkRecord record,
        SubChunkCoord viewerGlobalSubChunk)
    {
        long stageStart = TerrainGenerationProfiler.GetTimestamp();

        if (!HasRequiredTerrainData(record))
            return;

        ChunkFoliageData data = EnsureGrassSubChunkStorage(record);
        int subChunksPerChunk = data.subChunksPerChunk;
        int activeSubChunkRadius = GetActiveGrassSubChunkRadius(subChunksPerChunk);
        int chunkGlobalSubX = record.ChunkCoord.x * subChunksPerChunk;
        int chunkGlobalSubZ = record.ChunkCoord.z * subChunksPerChunk;
        int viewerLocalSubX = viewerGlobalSubChunk.x - chunkGlobalSubX;
        int viewerLocalSubZ = viewerGlobalSubChunk.z - chunkGlobalSubZ;

        for (int radius = 0; radius <= activeSubChunkRadius; radius++)
        {
            int minSubX = Mathf.Max(0, viewerLocalSubX - radius);
            int maxSubX = Mathf.Min(subChunksPerChunk - 1, viewerLocalSubX + radius);
            int minSubZ = Mathf.Max(0, viewerLocalSubZ - radius);
            int maxSubZ = Mathf.Min(subChunksPerChunk - 1, viewerLocalSubZ + radius);

            for (int localSubX = minSubX; localSubX <= maxSubX; localSubX++)
            {
                for (int localSubZ = minSubZ; localSubZ <= maxSubZ; localSubZ++)
                {
                    int localDx = Mathf.Abs(localSubX - viewerLocalSubX);
                    int localDz = Mathf.Abs(localSubZ - viewerLocalSubZ);
                    if (Mathf.Max(localDx, localDz) != radius)
                        continue;

                    if (!IsGrassSubChunkDesired(
                            record.ChunkCoord,
                            localSubX,
                            localSubZ,
                            viewerGlobalSubChunk,
                            subChunksPerChunk,
                            activeSubChunkRadius))
                        continue;

                    if (data.IsNearGrassSubChunkGenerated(localSubX, localSubZ))
                        continue;

                    GrassSubChunkWorkKey key = new GrassSubChunkWorkKey(record.ChunkCoord, localSubX, localSubZ);
                    if (!queuedGrassSubChunks.Add(key))
                        continue;

                    pendingGrassSubChunkWork.Enqueue(new GrassSubChunkWorkItem(key));
                }
            }
        }

        TerrainGenerationProfiler.Record(
            TerrainGenerationProfileStage.FoliageGrassSubChunkEnqueue,
            stageStart);
        TerrainGenerationProfiler.RecordFoliageQueueSnapshot(
            pendingGrassSubChunkWork.Count,
            queuedGrassSubChunks.Count,
            dirtyGrassChunks.Count,
            pendingFoliageBatchWork.Count);
    }

    private void ProcessPendingGrassSubChunkWork(
        ChunkManager chunkManager,
        ChunkCoord viewerCoord,
        SubChunkCoord viewerGlobalSubChunk)
    {
        int maxGenerations = Mathf.Max(1, grassSettings.maxSubChunkGenerationsPerFrame);
        float budgetMs = Mathf.Max(0f, grassSettings.subChunkGenerationBudgetMsPerFrame);
        long frameStart = TerrainGenerationProfiler.GetTimestamp();
        int generatedCount = 0;

        while (pendingGrassSubChunkWork.Count > 0 && generatedCount < maxGenerations)
        {
            if (budgetMs > 0f && TerrainGenerationProfiler.GetElapsedMilliseconds(frameStart) >= budgetMs)
                break;

            GrassSubChunkWorkItem workItem = pendingGrassSubChunkWork.Dequeue();
            queuedGrassSubChunks.Remove(workItem.Key);

            if (!IsWithinNearGrass(viewerCoord, workItem.Key.ChunkCoord))
                continue;

            ChunkRecord record = chunkManager.GetChunkRecord(workItem.Key.ChunkCoord);
            if (record == null || !HasRequiredTerrainData(record))
                continue;

            ChunkFoliageData data = EnsureGrassSubChunkStorage(record);
            int activeSubChunkRadius = GetActiveGrassSubChunkRadius(data.subChunksPerChunk);
            if (!IsGrassSubChunkDesired(
                    record.ChunkCoord,
                    workItem.Key.LocalSubChunkX,
                    workItem.Key.LocalSubChunkZ,
                    viewerGlobalSubChunk,
                    data.subChunksPerChunk,
                    activeSubChunkRadius))
            {
                continue;
            }

            if (data.IsNearGrassSubChunkGenerated(workItem.Key.LocalSubChunkX, workItem.Key.LocalSubChunkZ))
                continue;

            EnsureRocksGenerated(record);
            long discoveryStart = TerrainGenerationProfiler.GetTimestamp();
            FoliageGenerator.GenerateGrassForSubChunk(
                record,
                grassSettings,
                treeSettings,
                worldSeed,
                chunkSize,
                worldScale,
                meshHeightMultiplier,
                workItem.Key.LocalSubChunkX,
                workItem.Key.LocalSubChunkZ);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageGrassSubChunkDiscovery,
                discoveryStart);

            dirtyGrassChunks.Add(record.ChunkCoord);
            generatedCount++;
        }

        foreach (ChunkCoord chunkCoord in dirtyGrassChunks)
        {
            ChunkRecord record = chunkManager.GetChunkRecord(chunkCoord);
            ChunkRuntime runtime = chunkManager.GetChunkRuntime(record);

            if (record == null || runtime == null || runtime.FoliageRuntime == null)
                continue;

            if (IsWithinNearGrass(viewerCoord, chunkCoord))
            {
                EnqueueFoliageBatchRebuild(record, FoliageBatchWorkType.NearGrass);
            }
        }

        dirtyGrassChunks.Clear();
        TerrainGenerationProfiler.RecordFoliageQueueSnapshot(
            pendingGrassSubChunkWork.Count,
            queuedGrassSubChunks.Count,
            dirtyGrassChunks.Count,
            pendingFoliageBatchWork.Count);
    }

    private void EnqueueFoliageBatchRebuild(ChunkRecord record, FoliageBatchWorkType workType)
    {
        if (record == null)
            return;

        FoliageBatchWorkKey key = new FoliageBatchWorkKey(record.ChunkCoord, workType);
        if (!queuedFoliageBatchWork.Add(key))
            return;

        pendingFoliageBatchWork.Add(new FoliageBatchWorkItem(key));
        TerrainGenerationProfiler.RecordFoliageQueueSnapshot(
            pendingGrassSubChunkWork.Count,
            queuedGrassSubChunks.Count,
            dirtyGrassChunks.Count,
            pendingFoliageBatchWork.Count);
    }

    private void ProcessPendingFoliageBatchWork(
        ChunkManager chunkManager,
        ChunkCoord viewerCoord,
        SubChunkCoord viewerGlobalSubChunk)
    {
        int maxRebuilds = Mathf.Max(1, grassSettings.maxRenderBatchRebuildsPerFrame);
        float budgetMs = Mathf.Max(0f, grassSettings.renderBatchRebuildBudgetMsPerFrame);
        long frameStart = TerrainGenerationProfiler.GetTimestamp();
        int rebuildCount = 0;

        while (pendingFoliageBatchWork.Count > 0 && rebuildCount < maxRebuilds)
        {
            if (budgetMs > 0f && TerrainGenerationProfiler.GetElapsedMilliseconds(frameStart) >= budgetMs)
                break;

            FoliageBatchWorkItem workItem = PopNearestFoliageBatchWork(viewerCoord);
            queuedFoliageBatchWork.Remove(workItem.Key);

            ChunkRecord record = chunkManager.GetChunkRecord(workItem.Key.ChunkCoord);
            ChunkRuntime runtime = chunkManager.GetChunkRuntime(record);

            if (record == null || runtime == null || runtime.FoliageRuntime == null || !HasRequiredTerrainData(record))
                continue;

            if (!IsFoliageBatchWorkStillWanted(record, viewerCoord, workItem.Key.WorkType))
                continue;

            switch (workItem.Key.WorkType)
            {
                case FoliageBatchWorkType.NearGrass:
                    RebuildGrassMatricesForViewerSubChunk(runtime, record, viewerGlobalSubChunk);
                    rebuildCount++;
                    break;
                case FoliageBatchWorkType.BillboardGrass:
                    RebuildBillboardMatrices(runtime, record, viewerCoord);
                    rebuildCount++;
                    break;
                case FoliageBatchWorkType.Flower:
                    RebuildFlowerBatches(runtime, record);
                    rebuildCount++;
                    break;
            }
        }

        TerrainGenerationProfiler.RecordFoliageQueueSnapshot(
            pendingGrassSubChunkWork.Count,
            queuedGrassSubChunks.Count,
            dirtyGrassChunks.Count,
            pendingFoliageBatchWork.Count);
    }

    private FoliageBatchWorkItem PopNearestFoliageBatchWork(ChunkCoord viewerCoord)
    {
        int bestIndex = 0;
        int bestDistance = int.MaxValue;

        for (int i = 0; i < pendingFoliageBatchWork.Count; i++)
        {
            FoliageBatchWorkItem candidate = pendingFoliageBatchWork[i];
            int distance = GetChunkRingDistance(viewerCoord, candidate.Key.ChunkCoord);

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = i;
        }

        FoliageBatchWorkItem result = pendingFoliageBatchWork[bestIndex];
        pendingFoliageBatchWork.RemoveAt(bestIndex);
        return result;
    }

    private bool IsFoliageBatchWorkStillWanted(
        ChunkRecord record,
        ChunkCoord viewerCoord,
        FoliageBatchWorkType workType)
    {
        switch (workType)
        {
            case FoliageBatchWorkType.NearGrass:
                return IsWithinNearGrass(viewerCoord, record.ChunkCoord);
            case FoliageBatchWorkType.BillboardGrass:
                return IsWithinBillboardGrass(viewerCoord, record.ChunkCoord);
            case FoliageBatchWorkType.Flower:
                return IsWithinFlowerRenderRange(viewerCoord, record.ChunkCoord) &&
                       HasFlowerRenderAssets();
            default:
                return false;
        }
    }

    private ChunkFoliageData EnsureGrassSubChunkStorage(ChunkRecord record)
    {
        if (record.FoliageData == null)
        {
            record.FoliageData = new ChunkFoliageData();
        }

        int subChunksPerChunk = Mathf.Max(1, grassSettings.subChunksPerChunk);
        if (record.FoliageData.nearGrassInstancesBySubChunk == null ||
            record.FoliageData.nearGrassSubChunkGenerated == null ||
            record.FoliageData.subChunksPerChunk != subChunksPerChunk)
        {
            record.FoliageData.InitializeNearGrass(subChunksPerChunk);
        }

        return record.FoliageData;
    }

    private int GetActiveGrassSubChunkRadius(int subChunksPerChunk)
    {
        if (grassSettings.activeSubChunkRadius > 0)
            return grassSettings.activeSubChunkRadius;

        return Mathf.Max(1, (grassSettings.activeRingRadius + 1) * subChunksPerChunk);
    }

    private static bool IsGrassSubChunkDesired(
        ChunkCoord chunkCoord,
        int localSubX,
        int localSubZ,
        SubChunkCoord viewerGlobalSubChunk,
        int subChunksPerChunk,
        int activeSubChunkRadius)
    {
        subChunksPerChunk = Mathf.Max(1, subChunksPerChunk);
        int globalSubX = chunkCoord.x * subChunksPerChunk + localSubX;
        int globalSubZ = chunkCoord.z * subChunksPerChunk + localSubZ;
        int dx = Mathf.Abs(globalSubX - viewerGlobalSubChunk.x);
        int dz = Mathf.Abs(globalSubZ - viewerGlobalSubChunk.z);
        return dx <= activeSubChunkRadius && dz <= activeSubChunkRadius;
    }

    private void RebuildFlowerBatches(ChunkRuntime runtime, ChunkRecord record)
    {
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;
        ChunkFoliageData data = record.FoliageData;

        if (foliageRuntime == null || data == null || data.flowerInstances == null)
            return;

        long stageStart = TerrainGenerationProfiler.GetTimestamp();
        int instanceCount = data.flowerInstances.Count;

        if (instanceCount == 0)
        {
            foliageRuntime.CacheFlowerBatches(Array.Empty<Matrix4x4>(), Array.Empty<Vector4>());
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageFlowerBatchBuild,
                stageStart);
            return;
        }

        Matrix4x4 chunkLocalToWorld = runtime.RootTransform.localToWorldMatrix;
        NativeArray<FlowerRenderSourceData> sources =
            new NativeArray<FlowerRenderSourceData>(instanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<float4x4> nativeMatrices =
            new NativeArray<float4x4>(instanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<float4> nativePetalColors =
            new NativeArray<float4>(instanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        try
        {
            for (int i = 0; i < instanceCount; i++)
            {
                FlowerInstanceData instance = data.flowerInstances[i];
                Vector4 petalColor = Color32ToVector4(instance.petalColor);

                sources[i] = new FlowerRenderSourceData
                {
                    localPosition = new float3(
                        instance.localPosition.x,
                        instance.localPosition.y,
                        instance.localPosition.z),
                    localRotation = new quaternion(
                        instance.localRotation.x,
                        instance.localRotation.y,
                        instance.localRotation.z,
                        instance.localRotation.w),
                    localScale = new float3(
                        instance.localScale.x,
                        instance.localScale.y,
                        instance.localScale.z),
                    petalColor = new float4(petalColor.x, petalColor.y, petalColor.z, petalColor.w)
                };
            }

            FlowerRenderBatchBuildJob job = new FlowerRenderBatchBuildJob
            {
                sources = sources,
                chunkLocalToWorld = ToFloat4x4(chunkLocalToWorld),
                matrices = nativeMatrices,
                petalColors = nativePetalColors
            };

            JobHandle handle = job.Schedule(instanceCount, 64);
            handle.Complete();

            Matrix4x4[] worldMatrices = new Matrix4x4[instanceCount];
            Vector4[] petalColors = new Vector4[instanceCount];

            for (int i = 0; i < instanceCount; i++)
            {
                worldMatrices[i] = ToMatrix4x4(nativeMatrices[i]);
                petalColors[i] = ToVector4(nativePetalColors[i]);
            }

            foliageRuntime.CacheFlowerBatches(worldMatrices, petalColors);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageFlowerBatchBuild,
                stageStart);
        }
        finally
        {
            if (sources.IsCreated)
                sources.Dispose();
            if (nativeMatrices.IsCreated)
                nativeMatrices.Dispose();
            if (nativePetalColors.IsCreated)
                nativePetalColors.Dispose();
        }
    }

    private void RebuildGrassMatricesForViewerSubChunk(
        ChunkRuntime runtime,
        ChunkRecord record,
        SubChunkCoord viewerGlobalSubChunk)
    {
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;
        ChunkFoliageData data = record.FoliageData;

        if (foliageRuntime == null || data == null || data.nearGrassInstancesBySubChunk == null)
            return;

        long stageStart = TerrainGenerationProfiler.GetTimestamp();
        Matrix4x4 chunkLocalToWorld = runtime.RootTransform.localToWorldMatrix;

        int subChunksPerChunk = data.subChunksPerChunk;
        int selectedInstanceCount = 0;

        for (int localSubX = 0; localSubX < subChunksPerChunk; localSubX++)
        {
            for (int localSubZ = 0; localSubZ < subChunksPerChunk; localSubZ++)
            {
                int targetGlobalSubX = record.ChunkCoord.x * subChunksPerChunk + localSubX;
                int targetGlobalSubZ = record.ChunkCoord.z * subChunksPerChunk + localSubZ;

                int dx = targetGlobalSubX - viewerGlobalSubChunk.x;
                int dz = targetGlobalSubZ - viewerGlobalSubChunk.z;
                int distSqr = dx * dx + dz * dz;

                float density = GetDensityForDistanceSqr(distSqr);

                List<FoliageInstanceData> subChunkInstances =
                    data.nearGrassInstancesBySubChunk[localSubX, localSubZ];

                int totalCount = subChunkInstances.Count;
                int renderCount = Mathf.FloorToInt(totalCount * density);

                if (density > 0f && totalCount > 0)
                {
                    renderCount = Mathf.Clamp(renderCount, 1, totalCount);
                }

                selectedInstanceCount += renderCount;
            }
        }

        if (selectedInstanceCount == 0)
        {
            foliageRuntime.CacheGrassMatrices(Array.Empty<Matrix4x4>(), Array.Empty<Vector4>());
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageGrassRenderBatchBuild,
                stageStart);
            return;
        }

        NativeArray<GrassRenderSourceData> sources =
            new NativeArray<GrassRenderSourceData>(selectedInstanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<float4x4> nativeMatrices =
            new NativeArray<float4x4>(selectedInstanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<float4> nativeInstanceData =
            new NativeArray<float4>(selectedInstanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        try
        {
            int sourceIndex = 0;

            for (int localSubX = 0; localSubX < subChunksPerChunk; localSubX++)
            {
                for (int localSubZ = 0; localSubZ < subChunksPerChunk; localSubZ++)
                {
                    int targetGlobalSubX = record.ChunkCoord.x * subChunksPerChunk + localSubX;
                    int targetGlobalSubZ = record.ChunkCoord.z * subChunksPerChunk + localSubZ;

                    int dx = targetGlobalSubX - viewerGlobalSubChunk.x;
                    int dz = targetGlobalSubZ - viewerGlobalSubChunk.z;
                    int distSqr = dx * dx + dz * dz;

                    float density = GetDensityForDistanceSqr(distSqr);

                    List<FoliageInstanceData> subChunkInstances =
                        data.nearGrassInstancesBySubChunk[localSubX, localSubZ];

                    int totalCount = subChunkInstances.Count;
                    int renderCount = Mathf.FloorToInt(totalCount * density);

                    if (density > 0f && totalCount > 0)
                    {
                        renderCount = Mathf.Clamp(renderCount, 1, totalCount);
                    }

                    for (int i = 0; i < renderCount; i++)
                    {
                        FoliageInstanceData instance = subChunkInstances[i];
                        sources[sourceIndex] = new GrassRenderSourceData
                        {
                            localPosition = new float3(
                                instance.localPosition.x,
                                instance.localPosition.y,
                                instance.localPosition.z),
                            localRotation = new quaternion(
                                instance.localRotation.x,
                                instance.localRotation.y,
                                instance.localRotation.z,
                                instance.localRotation.w),
                            localScale = new float3(
                                instance.localScale.x,
                                instance.localScale.y,
                                instance.localScale.z),
                            selectionRank = instance.selectionRank,
                            forestBlend = instance.forestBlend
                        };
                        sourceIndex++;
                    }
                }
            }

            GrassRenderBatchBuildJob job = new GrassRenderBatchBuildJob
            {
                sources = sources,
                chunkLocalToWorld = ToFloat4x4(chunkLocalToWorld),
                matrices = nativeMatrices,
                instanceData = nativeInstanceData
            };

            JobHandle handle = job.Schedule(selectedInstanceCount, 64);
            handle.Complete();

            Matrix4x4[] worldMatrices = new Matrix4x4[selectedInstanceCount];
            Vector4[] instanceData = new Vector4[selectedInstanceCount];

            for (int i = 0; i < selectedInstanceCount; i++)
            {
                worldMatrices[i] = ToMatrix4x4(nativeMatrices[i]);
                float4 nativeData = nativeInstanceData[i];
                instanceData[i] = new Vector4(nativeData.x, nativeData.y, nativeData.z, nativeData.w);
            }

            foliageRuntime.CacheGrassMatrices(worldMatrices, instanceData);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageGrassRenderBatchBuild,
                stageStart);
        }
        finally
        {
            if (sources.IsCreated)
                sources.Dispose();
            if (nativeMatrices.IsCreated)
                nativeMatrices.Dispose();
            if (nativeInstanceData.IsCreated)
                nativeInstanceData.Dispose();
        }
    }

    private static float4x4 ToFloat4x4(Matrix4x4 matrix)
    {
        return new float4x4(
            new float4(matrix.m00, matrix.m10, matrix.m20, matrix.m30),
            new float4(matrix.m01, matrix.m11, matrix.m21, matrix.m31),
            new float4(matrix.m02, matrix.m12, matrix.m22, matrix.m32),
            new float4(matrix.m03, matrix.m13, matrix.m23, matrix.m33));
    }

    private static Matrix4x4 ToMatrix4x4(float4x4 matrix)
    {
        Matrix4x4 result = new Matrix4x4();
        result.m00 = matrix.c0.x;
        result.m10 = matrix.c0.y;
        result.m20 = matrix.c0.z;
        result.m30 = matrix.c0.w;
        result.m01 = matrix.c1.x;
        result.m11 = matrix.c1.y;
        result.m21 = matrix.c1.z;
        result.m31 = matrix.c1.w;
        result.m02 = matrix.c2.x;
        result.m12 = matrix.c2.y;
        result.m22 = matrix.c2.z;
        result.m32 = matrix.c2.w;
        result.m03 = matrix.c3.x;
        result.m13 = matrix.c3.y;
        result.m23 = matrix.c3.z;
        result.m33 = matrix.c3.w;
        return result;
    }

    private static Vector4 ToVector4(float4 value)
    {
        return new Vector4(value.x, value.y, value.z, value.w);
    }

    private struct GrassRenderSourceData
    {
        public float3 localPosition;
        public quaternion localRotation;
        public float3 localScale;
        public uint selectionRank;
        public float forestBlend;
    }

    [BurstCompile]
    private struct GrassRenderBatchBuildJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<GrassRenderSourceData> sources;
        public float4x4 chunkLocalToWorld;
        [WriteOnly] public NativeArray<float4x4> matrices;
        [WriteOnly] public NativeArray<float4> instanceData;

        public void Execute(int index)
        {
            GrassRenderSourceData source = sources[index];
            matrices[index] = math.mul(
                chunkLocalToWorld,
                float4x4.TRS(source.localPosition, source.localRotation, source.localScale));
            instanceData[index] = new float4(
                source.forestBlend,
                SelectionRankToUnitPhase(source.selectionRank),
                0f,
                0f);
        }

        private static float SelectionRankToUnitPhase(uint selectionRank)
        {
            const float inv24Bit = 1f / 16777216f;
            return (selectionRank & 0x00FFFFFFu) * inv24Bit;
        }
    }

    private struct BillboardGrassRenderSourceData
    {
        public float3 localPosition;
        public quaternion localRotation;
        public float3 localScale;
        public float forestBlend;
    }

    [BurstCompile]
    private struct BillboardGrassRenderBatchBuildJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<BillboardGrassRenderSourceData> sources;
        public float4x4 chunkLocalToWorld;
        public float scaleMultiplier;
        [WriteOnly] public NativeArray<float4x4> matrices;
        [WriteOnly] public NativeArray<float4> instanceData;

        public void Execute(int index)
        {
            BillboardGrassRenderSourceData source = sources[index];
            matrices[index] = math.mul(
                chunkLocalToWorld,
                float4x4.TRS(source.localPosition, source.localRotation, source.localScale * scaleMultiplier));
            instanceData[index] = new float4(source.forestBlend, 0f, 0f, 0f);
        }
    }

    private struct FlowerRenderSourceData
    {
        public float3 localPosition;
        public quaternion localRotation;
        public float3 localScale;
        public float4 petalColor;
    }

    [BurstCompile]
    private struct FlowerRenderBatchBuildJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<FlowerRenderSourceData> sources;
        public float4x4 chunkLocalToWorld;
        [WriteOnly] public NativeArray<float4x4> matrices;
        [WriteOnly] public NativeArray<float4> petalColors;

        public void Execute(int index)
        {
            FlowerRenderSourceData source = sources[index];
            matrices[index] = math.mul(
                chunkLocalToWorld,
                float4x4.TRS(source.localPosition, source.localRotation, source.localScale));
            petalColors[index] = source.petalColor;
        }
    }

    private struct TreeBillboardRenderSourceData
    {
        public float3 localPosition;
        public quaternion localRotation;
        public float3 localScale;
        public float4 leafTint;
    }

    [BurstCompile]
    private struct TreeBillboardRenderBatchBuildJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<TreeBillboardRenderSourceData> sources;
        public float4x4 chunkLocalToWorld;
        [WriteOnly] public NativeArray<float4x4> matrices;
        [WriteOnly] public NativeArray<float4> leafTints;

        public void Execute(int index)
        {
            TreeBillboardRenderSourceData source = sources[index];
            matrices[index] = math.mul(
                chunkLocalToWorld,
                float4x4.TRS(source.localPosition, source.localRotation, source.localScale));
            leafTints[index] = source.leafTint;
        }
    }

    private void RebuildBillboardMatrices(
        ChunkRuntime runtime,
        ChunkRecord record,
        ChunkCoord viewerCoord)
    {
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;
        ChunkFoliageData data = record.FoliageData;

        if (foliageRuntime == null || data == null || data.billboardGrassInstances == null)
            return;

        long stageStart = TerrainGenerationProfiler.GetTimestamp();
        Matrix4x4 chunkLocalToWorld = runtime.RootTransform.localToWorldMatrix;

        int chunkRing = GetChunkRingDistance(viewerCoord, record.ChunkCoord);
        float densityMultiplier = GetBillboardDensityMultiplierForChunkRing(chunkRing);
        float scaleMultiplier = GetBillboardScaleMultiplierForChunkRing(chunkRing);

        int cellsPerAxis = Mathf.Max(1, grassSettings.billboardCellsPerAxis);
        float cellSize = (float)chunkSize / cellsPerAxis;

        List<BillboardFoliageInstanceData>[,] cellBuckets =
            new List<BillboardFoliageInstanceData>[cellsPerAxis, cellsPerAxis];

        for (int x = 0; x < cellsPerAxis; x++)
        {
            for (int z = 0; z < cellsPerAxis; z++)
            {
                cellBuckets[x, z] = new List<BillboardFoliageInstanceData>();
            }
        }

        for (int i = 0; i < data.billboardGrassInstances.Count; i++)
        {
            BillboardFoliageInstanceData instance = data.billboardGrassInstances[i];

            float localX = (instance.localPosition.x / worldScale) + chunkSize / 2f;
            float localZ = (instance.localPosition.z / worldScale) + chunkSize / 2f;

            int cellX = Mathf.Clamp(Mathf.FloorToInt(localX / cellSize), 0, cellsPerAxis - 1);
            int cellZ = Mathf.Clamp(Mathf.FloorToInt(localZ / cellSize), 0, cellsPerAxis - 1);

            cellBuckets[cellX, cellZ].Add(instance);
        }

        List<BillboardFoliageInstanceData> selectedInstances = new List<BillboardFoliageInstanceData>();

        for (int cellX = 0; cellX < cellsPerAxis; cellX++)
        {
            for (int cellZ = 0; cellZ < cellsPerAxis; cellZ++)
            {
                List<BillboardFoliageInstanceData> bucket = cellBuckets[cellX, cellZ];
                int totalCount = bucket.Count;

                if (totalCount == 0)
                    continue;

                int renderCount = Mathf.FloorToInt(totalCount * densityMultiplier);

                if (densityMultiplier > 0f)
                {
                    renderCount = Mathf.Clamp(renderCount, 1, totalCount);
                }

                for (int i = 0; i < renderCount; i++)
                {
                    BillboardFoliageInstanceData instance = bucket[i];
                    selectedInstances.Add(instance);
                }
            }
        }

        int selectedCount = selectedInstances.Count;
        if (selectedCount == 0)
        {
            foliageRuntime.CacheBillboardMatrices(Array.Empty<Matrix4x4>(), Array.Empty<Vector4>());
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageBillboardGrassBatchBuild,
                stageStart);
            return;
        }

        NativeArray<BillboardGrassRenderSourceData> sources =
            new NativeArray<BillboardGrassRenderSourceData>(selectedCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<float4x4> nativeMatrices =
            new NativeArray<float4x4>(selectedCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        NativeArray<float4> nativeInstanceData =
            new NativeArray<float4>(selectedCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

        try
        {
            for (int i = 0; i < selectedCount; i++)
            {
                BillboardFoliageInstanceData instance = selectedInstances[i];
                sources[i] = new BillboardGrassRenderSourceData
                {
                    localPosition = new float3(
                        instance.localPosition.x,
                        instance.localPosition.y,
                        instance.localPosition.z),
                    localRotation = new quaternion(
                        instance.localRotation.x,
                        instance.localRotation.y,
                        instance.localRotation.z,
                        instance.localRotation.w),
                    localScale = new float3(
                        instance.localScale.x,
                        instance.localScale.y,
                        instance.localScale.z),
                    forestBlend = instance.forestBlend
                };
            }

            BillboardGrassRenderBatchBuildJob job = new BillboardGrassRenderBatchBuildJob
            {
                sources = sources,
                chunkLocalToWorld = ToFloat4x4(chunkLocalToWorld),
                scaleMultiplier = scaleMultiplier,
                matrices = nativeMatrices,
                instanceData = nativeInstanceData
            };

            JobHandle handle = job.Schedule(selectedCount, 64);
            handle.Complete();

            Matrix4x4[] worldMatrices = new Matrix4x4[selectedCount];
            Vector4[] instanceData = new Vector4[selectedCount];

            for (int i = 0; i < selectedCount; i++)
            {
                worldMatrices[i] = ToMatrix4x4(nativeMatrices[i]);
                instanceData[i] = ToVector4(nativeInstanceData[i]);
            }

            foliageRuntime.CacheBillboardMatrices(worldMatrices, instanceData);
            TerrainGenerationProfiler.Record(
                TerrainGenerationProfileStage.FoliageBillboardGrassBatchBuild,
                stageStart);
        }
        finally
        {
            if (sources.IsCreated)
                sources.Dispose();
            if (nativeMatrices.IsCreated)
                nativeMatrices.Dispose();
            if (nativeInstanceData.IsCreated)
                nativeInstanceData.Dispose();
        }
    }

    private FoliageRepresentationMode GetTreeRepresentationMode(
        ChunkCoord viewerCoord,
        ChunkCoord targetCoord)
    {
        int ring = GetChunkRingDistance(viewerCoord, targetCoord);

        if (ring <= treeSettings.gameObjectTreeChunkRingRadius)
            return FoliageRepresentationMode.GameObjectWithCollision;

        return FoliageRepresentationMode.GPUInstancedBillboard;
    }

    private bool IsWithinTreeRenderRange(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        int ring = GetChunkRingDistance(viewerCoord, targetCoord);
        return ring <= treeSettings.gameObjectTreeChunkRingRadius ||
               IsWithinBillboardTreeRenderRange(ring);
    }

    private bool IsWithinBillboardTreeRenderRange(int ring)
    {
        int billboardStartRing = Mathf.Max(
            treeSettings.gameObjectTreeChunkRingRadius + 1,
            treeSettings.billboardTreeChunkStartRingRadius);

        return ring >= billboardStartRing &&
               ring <= treeSettings.billboardTreeChunkRingRadius;
    }

    private bool IsWithinBushRenderRange(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        int ring = GetChunkRingDistance(viewerCoord, targetCoord);
        return ring <= treeSettings.gameObjectBushChunkRingRadius;
    }

    private bool IsWithinRockRenderRange(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        int ring = GetChunkRingDistance(viewerCoord, targetCoord);
        return ring <= treeSettings.gameObjectRockChunkRingRadius;
    }

    private int GetChunkRingDistance(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        int dx = Mathf.Abs(targetCoord.x - viewerCoord.x);
        int dz = Mathf.Abs(targetCoord.z - viewerCoord.z);
        return Mathf.Max(dx, dz);
    }

    private float GetBillboardDensityMultiplierForChunkRing(int chunkRing)
    {
        if (chunkRing <= 2)
            return 1f;

        return 1f / (chunkRing - 1);
    }

    private float GetBillboardScaleMultiplierForChunkRing(int chunkRing)
    {
        if (chunkRing <= 2)
            return 1f;

        return 1f + 0.25f * (chunkRing - 2);
    }

    private float GetDensityForDistanceSqr(int distSqr)
    {
        if (distSqr <= 3 * 3)
            return grassSettings.densityRadius3;

        if (distSqr <= 6 * 6)
            return grassSettings.densityRadius6;

        if (distSqr <= 10 * 10)
            return grassSettings.densityRadius10;

        return grassSettings.densityBeyond10;
    }

    private bool IsWithinNearGrass(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        int dx = Mathf.Abs(targetCoord.x - viewerCoord.x);
        int dz = Mathf.Abs(targetCoord.z - viewerCoord.z);
        return dx <= grassSettings.activeRingRadius && dz <= grassSettings.activeRingRadius;
    }

    private bool IsWithinFlowerRenderRange(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        if (!IsFlowerSystemEnabled())
            return false;

        int dx = Mathf.Abs(targetCoord.x - viewerCoord.x);
        int dz = Mathf.Abs(targetCoord.z - viewerCoord.z);
        return dx <= flowerSettings.activeRingRadius && dz <= flowerSettings.activeRingRadius;
    }

    private bool IsWithinBillboardGrass(ChunkCoord viewerCoord, ChunkCoord targetCoord)
    {
        int absDx = Mathf.Abs(targetCoord.x - viewerCoord.x);
        int absDz = Mathf.Abs(targetCoord.z - viewerCoord.z);

        bool insideNearSquare =
            absDx <= grassSettings.activeRingRadius &&
            absDz <= grassSettings.activeRingRadius;

        if (insideNearSquare)
            return false;

        int dx = targetCoord.x - viewerCoord.x;
        int dz = targetCoord.z - viewerCoord.z;
        int distSqr = dx * dx + dz * dz;

        int billboardRangeSqr = grassSettings.billboardRingRadius * grassSettings.billboardRingRadius;
        return distSqr <= billboardRangeSqr;
    }

    private bool HasRequiredTerrainData(ChunkRecord record)
    {
        return record.HeightMap != null &&
               record.SurfaceTypeMap != null &&
               record.BiomeMap != null;
    }

    private bool IsFlowerSystemEnabled()
    {
        return flowerSettings != null && flowerSettings.enableFlowers;
    }

    private bool HasFlowerRenderAssets()
    {
        return flowerMesh != null && flowerMaterial != null;
    }

    private static Vector4 Color32ToVector4(Color32 color)
    {
        const float inv255 = 1f / 255f;
        return new Vector4(
            color.r * inv255,
            color.g * inv255,
            color.b * inv255,
            color.a * inv255);
    }

    private static Vector4 Color32ToLinearVector4(Color32 color)
    {
        Color linearColor = ((Color)color).linear;
        return new Vector4(linearColor.r, linearColor.g, linearColor.b, linearColor.a);
    }

    private readonly struct GrassSubChunkWorkItem
    {
        public readonly GrassSubChunkWorkKey Key;

        public GrassSubChunkWorkItem(GrassSubChunkWorkKey key)
        {
            Key = key;
        }
    }

    private readonly struct GrassSubChunkWorkKey : IEquatable<GrassSubChunkWorkKey>
    {
        public readonly ChunkCoord ChunkCoord;
        public readonly int LocalSubChunkX;
        public readonly int LocalSubChunkZ;

        public GrassSubChunkWorkKey(ChunkCoord chunkCoord, int localSubChunkX, int localSubChunkZ)
        {
            ChunkCoord = chunkCoord;
            LocalSubChunkX = localSubChunkX;
            LocalSubChunkZ = localSubChunkZ;
        }

        public bool Equals(GrassSubChunkWorkKey other)
        {
            return ChunkCoord == other.ChunkCoord &&
                   LocalSubChunkX == other.LocalSubChunkX &&
                   LocalSubChunkZ == other.LocalSubChunkZ;
        }

        public override bool Equals(object obj)
        {
            return obj is GrassSubChunkWorkKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ChunkCoord, LocalSubChunkX, LocalSubChunkZ);
        }
    }

    private enum FoliageBatchWorkType
    {
        NearGrass,
        BillboardGrass,
        Flower
    }

    private readonly struct FoliageBatchWorkItem
    {
        public readonly FoliageBatchWorkKey Key;

        public FoliageBatchWorkItem(FoliageBatchWorkKey key)
        {
            Key = key;
        }
    }

    private readonly struct FoliageBatchWorkKey : IEquatable<FoliageBatchWorkKey>
    {
        public readonly ChunkCoord ChunkCoord;
        public readonly FoliageBatchWorkType WorkType;

        public FoliageBatchWorkKey(ChunkCoord chunkCoord, FoliageBatchWorkType workType)
        {
            ChunkCoord = chunkCoord;
            WorkType = workType;
        }

        public bool Equals(FoliageBatchWorkKey other)
        {
            return ChunkCoord == other.ChunkCoord &&
                   WorkType == other.WorkType;
        }

        public override bool Equals(object obj)
        {
            return obj is FoliageBatchWorkKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ChunkCoord, WorkType);
        }
    }

    private void EnsureFoliageRuntimeExists(ChunkRuntime chunkRuntime, ChunkRecord record)
    {
        if (chunkRuntime.FoliageRuntime != null && chunkRuntime.FoliageRuntime.IsCreated)
            return;

        chunkRuntime.FoliageRuntime = new ChunkFoliageRuntime();

        GameObject root = new GameObject($"Foliage_{record.ChunkCoord.x}_{record.ChunkCoord.z}");
        root.transform.SetParent(chunkRuntime.RootTransform, false);

        chunkRuntime.FoliageRuntime.root = root.transform;

        chunkRuntime.FoliageRuntime.grassMesh = grassMesh;
        chunkRuntime.FoliageRuntime.grassMaterial = grassMaterial;
        chunkRuntime.FoliageRuntime.receiveGrassShadows = grassSettings.receiveGrassShadows;
        chunkRuntime.FoliageRuntime.grassInstanceDataPropertyId = grassInstanceDataPropertyId;
        chunkRuntime.FoliageRuntime.forestDarkGrassColor = grassSettings.forestDarkGrassColor;
        chunkRuntime.FoliageRuntime.forestMidGrassColor = grassSettings.forestMidGrassColor;
        chunkRuntime.FoliageRuntime.forestLightGrassColor = grassSettings.forestLightGrassColor;

        chunkRuntime.FoliageRuntime.billboardMesh = billboardGrassMesh;
        chunkRuntime.FoliageRuntime.billboardMaterial = billboardGrassMaterial;

        chunkRuntime.FoliageRuntime.flowerMesh = flowerMesh;
        chunkRuntime.FoliageRuntime.flowerMaterial = flowerMaterial;
        chunkRuntime.FoliageRuntime.flowerPetalColorPropertyId = flowerPetalColorPropertyId;

        chunkRuntime.FoliageRuntime.mapleTreePrefab = treeSettings.mapleTreePrefab;
        chunkRuntime.FoliageRuntime.sugarMapleTreePrefab = treeSettings.sugarMapleTreePrefab;
        chunkRuntime.FoliageRuntime.birchAspenTreePrefab = treeSettings.birchAspenTreePrefab;
        chunkRuntime.FoliageRuntime.beechTreePrefab = treeSettings.beechTreePrefab;
        chunkRuntime.FoliageRuntime.spruceTreePrefab = treeSettings.spruceTreePrefab;
        chunkRuntime.FoliageRuntime.whitePineTreePrefab = treeSettings.whitePineTreePrefab;
        chunkRuntime.FoliageRuntime.oakTreePrefab = treeSettings.oakTreePrefab;
        chunkRuntime.FoliageRuntime.fallbackTreePrefab = treeSettings.treeLOD0GameObjectPrefab;
        chunkRuntime.FoliageRuntime.grasslandMapleTreePrefab = treeSettings.grasslandMapleTreePrefab;
        chunkRuntime.FoliageRuntime.grasslandBirchAspenTreePrefab = treeSettings.grasslandBirchAspenTreePrefab;
        chunkRuntime.FoliageRuntime.grasslandWhitePineTreePrefab = treeSettings.grasslandWhitePineTreePrefab;
        chunkRuntime.FoliageRuntime.grasslandOakTreePrefab = treeSettings.grasslandOakTreePrefab;
        chunkRuntime.FoliageRuntime.grasslandWillowTreePrefab = treeSettings.grasslandWillowTreePrefab;
        chunkRuntime.FoliageRuntime.grasslandFallbackTreePrefab = treeSettings.grasslandTreeFallbackPrefab;
        chunkRuntime.FoliageRuntime.blueberryBushPrefab = treeSettings.blueberryBushPrefab;
        chunkRuntime.FoliageRuntime.raspberryBushPrefab = treeSettings.raspberryBushPrefab;
        chunkRuntime.FoliageRuntime.strawberryBushPrefab = treeSettings.strawberryBushPrefab;
        chunkRuntime.FoliageRuntime.blackberryBushPrefab = treeSettings.blackberryBushPrefab;
        chunkRuntime.FoliageRuntime.fallbackBushPrefab = treeSettings.fallbackBushPrefab;
        chunkRuntime.FoliageRuntime.forestRockPrefabs = treeSettings.forestRockPrefabs;
        chunkRuntime.FoliageRuntime.forestRockFallbackPrefab = treeSettings.forestRockFallbackPrefab;
        chunkRuntime.FoliageRuntime.grasslandRockPrefabs = treeSettings.grasslandRockPrefabs;
        chunkRuntime.FoliageRuntime.grasslandRockFallbackPrefab = treeSettings.grasslandRockFallbackPrefab;
        chunkRuntime.FoliageRuntime.grasslandLargeRockPrefabs = treeSettings.grasslandLargeRockPrefabs;
        chunkRuntime.FoliageRuntime.grasslandLargeRockFallbackPrefab = treeSettings.grasslandLargeRockFallbackPrefab;
        chunkRuntime.FoliageRuntime.mapleTreeBillboard = mapleTreeBillboard;
        chunkRuntime.FoliageRuntime.sugarMapleTreeBillboard = sugarMapleTreeBillboard;
        chunkRuntime.FoliageRuntime.birchAspenTreeBillboard = birchAspenTreeBillboard;
        chunkRuntime.FoliageRuntime.beechTreeBillboard = beechTreeBillboard;
        chunkRuntime.FoliageRuntime.spruceTreeBillboard = spruceTreeBillboard;
        chunkRuntime.FoliageRuntime.whitePineTreeBillboard = whitePineTreeBillboard;
        chunkRuntime.FoliageRuntime.oakTreeBillboard = oakTreeBillboard;
        chunkRuntime.FoliageRuntime.fallbackTreeBillboard = fallbackTreeBillboard;
        chunkRuntime.FoliageRuntime.grasslandMapleTreeBillboard = grasslandMapleTreeBillboard;
        chunkRuntime.FoliageRuntime.grasslandBirchAspenTreeBillboard = grasslandBirchAspenTreeBillboard;
        chunkRuntime.FoliageRuntime.grasslandWhitePineTreeBillboard = grasslandWhitePineTreeBillboard;
        chunkRuntime.FoliageRuntime.grasslandOakTreeBillboard = grasslandOakTreeBillboard;
        chunkRuntime.FoliageRuntime.grasslandWillowTreeBillboard = grasslandWillowTreeBillboard;
        chunkRuntime.FoliageRuntime.grasslandFallbackTreeBillboard = grasslandFallbackTreeBillboard;

        chunkRuntime.FoliageRuntime.SetVisible(false);
    }

    private void ResolveGrassRenderAssets()
    {
        string instanceDataPropertyName = string.IsNullOrEmpty(grassSettings.grassInstanceDataPropertyName)
            ? "_GrassInstanceData"
            : grassSettings.grassInstanceDataPropertyName;

        grassInstanceDataPropertyId = Shader.PropertyToID(instanceDataPropertyName);

        if (grassSettings.grassPrefab == null)
        {
            Debug.LogError("Grass prefab is missing.");
        }
        else
        {
            MeshFilter meshFilter = grassSettings.grassPrefab.GetComponentInChildren<MeshFilter>();
            MeshRenderer meshRenderer = grassSettings.grassPrefab.GetComponentInChildren<MeshRenderer>();

            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                Debug.LogError("Grass prefab missing MeshFilter or mesh.");
            }
            else
            {
                grassMesh = meshFilter.sharedMesh;
            }

            if (meshRenderer == null || meshRenderer.sharedMaterial == null)
            {
                Debug.LogError("Grass prefab missing MeshRenderer or material.");
            }
            else
            {
                grassMaterial = meshRenderer.sharedMaterial;
            }
        }

        if (grassSettings.billboardGrassPrefab == null)
        {
            Debug.LogError("Billboard grass prefab is missing.");
        }
        else
        {
            MeshFilter meshFilter = grassSettings.billboardGrassPrefab.GetComponentInChildren<MeshFilter>();
            MeshRenderer meshRenderer = grassSettings.billboardGrassPrefab.GetComponentInChildren<MeshRenderer>();

            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                Debug.LogError("Billboard grass prefab missing MeshFilter or mesh.");
            }
            else
            {
                billboardGrassMesh = meshFilter.sharedMesh;
            }

            if (meshRenderer == null || meshRenderer.sharedMaterial == null)
            {
                Debug.LogError("Billboard grass prefab missing MeshRenderer or material.");
            }
            else
            {
                billboardGrassMaterial = meshRenderer.sharedMaterial;
            }
        }
    }

    private void ResolveFlowerRenderAssets()
    {
        if (!IsFlowerSystemEnabled())
            return;

        string petalColorPropertyName = string.IsNullOrEmpty(flowerSettings.flowerPetalColorPropertyName)
            ? "_FlowerPetalColor"
            : flowerSettings.flowerPetalColorPropertyName;

        flowerPetalColorPropertyId = Shader.PropertyToID(petalColorPropertyName);

        if (flowerSettings.flowerPrefab == null)
        {
            Debug.LogWarning("Flower prefab is missing. Flowers will not render until one is assigned.");
            return;
        }

        MeshFilter meshFilter = flowerSettings.flowerPrefab.GetComponentInChildren<MeshFilter>();
        MeshRenderer meshRenderer = flowerSettings.flowerPrefab.GetComponentInChildren<MeshRenderer>();

        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogError("Flower prefab missing MeshFilter or mesh.");
        }
        else
        {
            flowerMesh = meshFilter.sharedMesh;
        }

        if (meshRenderer == null || meshRenderer.sharedMaterial == null)
        {
            Debug.LogError("Flower prefab missing MeshRenderer or material.");
        }
        else
        {
            flowerMaterial = meshRenderer.sharedMaterial;
            flowerMaterial.enableInstancing = true;
        }
    }

    private void ResolveTreeRenderAssets()
    {
        WarnMissingTreePrefab(treeSettings.mapleTreePrefab, "Maple");
        WarnMissingTreePrefab(treeSettings.sugarMapleTreePrefab, "Sugar maple");
        WarnMissingTreePrefab(treeSettings.birchAspenTreePrefab, "Birch/aspen");
        WarnMissingTreePrefab(treeSettings.beechTreePrefab, "Beech");
        WarnMissingTreePrefab(treeSettings.spruceTreePrefab, "Spruce");
        WarnMissingTreePrefab(treeSettings.whitePineTreePrefab, "White pine");
        WarnMissingTreePrefab(treeSettings.oakTreePrefab, "Oak");
        WarnMissingGrasslandTreePrefab(treeSettings.grasslandMapleTreePrefab, "Grassland maple");
        WarnMissingGrasslandTreePrefab(treeSettings.grasslandBirchAspenTreePrefab, "Grassland birch/aspen");
        WarnMissingGrasslandTreePrefab(treeSettings.grasslandWhitePineTreePrefab, "Grassland white pine");
        WarnMissingGrasslandTreePrefab(treeSettings.grasslandOakTreePrefab, "Grassland oak");
        WarnMissingGrasslandTreePrefab(treeSettings.grasslandWillowTreePrefab, "Grassland willow");
        WarnMissingBushPrefab(treeSettings.blueberryBushPrefab, "Blueberry");
        WarnMissingBushPrefab(treeSettings.raspberryBushPrefab, "Raspberry");
        WarnMissingBushPrefab(treeSettings.strawberryBushPrefab, "Strawberry");
        WarnMissingBushPrefab(treeSettings.blackberryBushPrefab, "Blackberry");

        fallbackTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.treeBillboardPrefab,
            "fallback tree billboard");

        grasslandFallbackTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.grasslandTreeBillboardFallbackPrefab,
            "grassland fallback tree billboard");

        mapleTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.mapleTreeBillboardPrefab,
            "maple tree billboard");

        sugarMapleTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.sugarMapleTreeBillboardPrefab,
            "sugar maple tree billboard");

        birchAspenTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.birchAspenTreeBillboardPrefab,
            "birch/aspen tree billboard");

        beechTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.beechTreeBillboardPrefab,
            "beech tree billboard");

        spruceTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.spruceTreeBillboardPrefab,
            "spruce tree billboard");

        whitePineTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.whitePineTreeBillboardPrefab,
            "white pine tree billboard");

        oakTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.oakTreeBillboardPrefab,
            "oak tree billboard");

        grasslandMapleTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.grasslandMapleTreeBillboardPrefab,
            "grassland maple tree billboard");

        grasslandBirchAspenTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.grasslandBirchAspenTreeBillboardPrefab,
            "grassland birch/aspen tree billboard");

        grasslandWhitePineTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.grasslandWhitePineTreeBillboardPrefab,
            "grassland white pine tree billboard");

        grasslandOakTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.grasslandOakTreeBillboardPrefab,
            "grassland oak tree billboard");

        grasslandWillowTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.grasslandWillowTreeBillboardPrefab,
            "grassland willow tree billboard");

        if (grasslandFallbackTreeBillboard.mesh == null)
            grasslandFallbackTreeBillboard = fallbackTreeBillboard;

        if (mapleTreeBillboard.mesh == null)
            mapleTreeBillboard = fallbackTreeBillboard;

        if (sugarMapleTreeBillboard.mesh == null)
            sugarMapleTreeBillboard = fallbackTreeBillboard;

        if (birchAspenTreeBillboard.mesh == null)
            birchAspenTreeBillboard = fallbackTreeBillboard;

        if (beechTreeBillboard.mesh == null)
            beechTreeBillboard = fallbackTreeBillboard;

        if (spruceTreeBillboard.mesh == null)
            spruceTreeBillboard = fallbackTreeBillboard;

        if (whitePineTreeBillboard.mesh == null)
            whitePineTreeBillboard = fallbackTreeBillboard;

        if (oakTreeBillboard.mesh == null)
            oakTreeBillboard = fallbackTreeBillboard;

        if (grasslandMapleTreeBillboard.mesh == null)
            grasslandMapleTreeBillboard = grasslandFallbackTreeBillboard;

        if (grasslandBirchAspenTreeBillboard.mesh == null)
            grasslandBirchAspenTreeBillboard = grasslandFallbackTreeBillboard;

        if (grasslandWhitePineTreeBillboard.mesh == null)
            grasslandWhitePineTreeBillboard = grasslandFallbackTreeBillboard;

        if (grasslandOakTreeBillboard.mesh == null)
            grasslandOakTreeBillboard = grasslandFallbackTreeBillboard;

        if (grasslandWillowTreeBillboard.mesh == null)
            grasslandWillowTreeBillboard = grasslandFallbackTreeBillboard;
    }

    private void WarnMissingTreePrefab(GameObject prefab, string label)
    {
        if (prefab == null && treeSettings.treeLOD0GameObjectPrefab == null)
        {
            Debug.LogWarning($"{label} tree prefab is missing and no fallback tree prefab is assigned.");
        }
    }

    private void WarnMissingGrasslandTreePrefab(GameObject prefab, string label)
    {
        if (prefab == null &&
            treeSettings.grasslandTreeFallbackPrefab == null &&
            treeSettings.treeLOD0GameObjectPrefab == null)
        {
            Debug.LogWarning($"{label} tree prefab is missing and no grassland or general fallback tree prefab is assigned.");
        }
    }

    private void WarnMissingBushPrefab(GameObject prefab, string label)
    {
        if (prefab == null && treeSettings.fallbackBushPrefab == null)
        {
            Debug.LogWarning($"{label} bush prefab is missing and no fallback bush prefab is assigned.");
        }
    }

    private TreeBillboardRenderData ResolveTreeBillboardRenderData(GameObject prefab, string label)
    {
        if (prefab == null)
            return new TreeBillboardRenderData(null, null);

        MeshFilter meshFilter = prefab.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = prefab.GetComponent<MeshRenderer>();

        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            Debug.LogError($"{label} prefab must have a MeshFilter with a mesh on the root.");
            return new TreeBillboardRenderData(null, null);
        }

        if (meshRenderer == null || meshRenderer.sharedMaterial == null)
        {
            Debug.LogError($"{label} prefab must have a MeshRenderer with one shared material on the root.");
            return new TreeBillboardRenderData(null, null);
        }

        meshRenderer.sharedMaterial.enableInstancing = true;
        return new TreeBillboardRenderData(meshFilter.sharedMesh, meshRenderer.sharedMaterial);
    }
}
