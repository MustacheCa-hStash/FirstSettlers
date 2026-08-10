using System.Collections.Generic;
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
        List<ChunkCoord> orderedActiveCoords)
    {
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
            bool useFoliage = useNearGrass || useBillboardGrass || useFlowers || useTrees;

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
                runtime.FoliageRuntime.ClearTreeGameObjects();
                runtime.FoliageRuntime.ClearTreeBillboardMatrices();
                runtime.FoliageRuntime.ClearCurrentTreeRepresentation();
            }

            if (useFlowers && HasFlowerRenderAssets())
            {
                if (record.FoliageData == null || !record.FoliageData.flowersGenerated)
                {
                    EnsureFlowersGenerated(record);
                }

                RebuildFlowerBatches(runtime, record);
            }
            else
            {
                runtime.FoliageRuntime.ClearFlowerBatches();
            }

            if (useNearGrass)
            {
                if (record.FoliageData == null || !record.FoliageData.nearGrassGenerated)
                {
                    FoliageGenerator.GenerateGrassForChunk(
                        record,
                        grassSettings,
                        treeSettings,
                        worldSeed,
                        chunkSize,
                        worldScale,
                        meshHeightMultiplier);
                }

                RebuildGrassMatricesForViewerSubChunk(runtime, record, viewerGlobalSubChunk);
            }
            else if (useBillboardGrass)
            {
                if (record.FoliageData == null || !record.FoliageData.billboardGenerated)
                {
                    FoliageGenerator.GenerateBillboardGrassForChunk(
                        record,
                        grassSettings,
                        worldSeed,
                        chunkSize,
                        worldScale,
                        meshHeightMultiplier);
                }

                RebuildBillboardMatrices(runtime, record, viewerCoord);
            }

            runtime.FoliageRuntime.SetVisible(true);
        }
    }

    public void DrawVisibleFoliageEveryFrame(
        ChunkManager chunkManager,
        ChunkCoord viewerCoord,
        SubChunkCoord viewerGlobalSubChunk,
        List<ChunkCoord> orderedActiveCoords)
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
            bool useFoliage = useNearGrass || useBillboardGrass || useFlowers || useTrees;

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

            if (useTrees)
            {
                EnsureTreesGenerated(record);
                RebuildTreeRepresentationIfNeeded(runtime, record, viewerCoord);
            }

            if (useNearGrass)
            {
                if (record.FoliageData == null || !record.FoliageData.nearGrassGenerated)
                {
                    FoliageGenerator.GenerateGrassForChunk(
                        record,
                        grassSettings,
                        treeSettings,
                        worldSeed,
                        chunkSize,
                        worldScale,
                        meshHeightMultiplier);
                }

                if (!runtime.FoliageRuntime.HasValidGrassRenderData())
                {
                    RebuildGrassMatricesForViewerSubChunk(runtime, record, viewerGlobalSubChunk);
                }

                runtime.FoliageRuntime.SetVisible(true);
                runtime.FoliageRuntime.DrawGrass();
            }
            else if (useBillboardGrass)
            {
                if (record.FoliageData == null || !record.FoliageData.billboardGenerated)
                {
                    FoliageGenerator.GenerateBillboardGrassForChunk(
                        record,
                        grassSettings,
                        worldSeed,
                        chunkSize,
                        worldScale,
                        meshHeightMultiplier);
                }

                if (!runtime.FoliageRuntime.HasValidBillboardRenderData())
                {
                    RebuildBillboardMatrices(runtime, record, viewerCoord);
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
                    RebuildFlowerBatches(runtime, record);
                }

                runtime.FoliageRuntime.SetVisible(true);
                runtime.FoliageRuntime.DrawFlowers();
            }

            DrawTreesForChunk(runtime, viewerCoord, coord);
        }
    }

    private void EnsureTreesGenerated(ChunkRecord record)
    {
        if (record.FoliageData == null || !record.FoliageData.treeCubesGenerated)
        {
            FoliageGenerator.GenerateTreeCubesForChunk(
                record,
                treeSettings,
                worldSeed,
                chunkSize,
                worldScale,
                meshHeightMultiplier);
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
            FoliageGenerator.GenerateFlowersForChunk(
                record,
                flowerSettings,
                worldSeed,
                chunkSize,
                worldScale,
                meshHeightMultiplier);
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
            foliageRuntime.RebuildTreeGameObjects(
                record.FoliageData.treeCubeInstances,
                runtime.RootTransform);
        }
        else if (mode == FoliageRepresentationMode.GPUInstancedBillboard)
        {
            RebuildTreeBillboardMatrices(runtime, record);
        }

        foliageRuntime.SetCurrentTreeRepresentation(mode);
    }

    private void DrawTreesForChunk(
        ChunkRuntime runtime,
        ChunkCoord viewerCoord,
        ChunkCoord chunkCoord)
    {
        if (runtime.FoliageRuntime == null)
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
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;
        ChunkFoliageData data = record.FoliageData;

        List<Matrix4x4> mapleWorldMatrices = new List<Matrix4x4>();
        List<Matrix4x4> sugarMapleWorldMatrices = new List<Matrix4x4>();
        List<Matrix4x4> birchAspenWorldMatrices = new List<Matrix4x4>();
        List<Matrix4x4> beechWorldMatrices = new List<Matrix4x4>();
        List<Matrix4x4> spruceWorldMatrices = new List<Matrix4x4>();
        List<Matrix4x4> whitePineWorldMatrices = new List<Matrix4x4>();
        List<Matrix4x4> oakWorldMatrices = new List<Matrix4x4>();
        Matrix4x4 chunkLocalToWorld = runtime.RootTransform.localToWorldMatrix;

        for (int i = 0; i < data.treeCubeInstances.Count; i++)
        {
            TreeInstanceData instance = data.treeCubeInstances[i];

            Matrix4x4 localMatrix = Matrix4x4.TRS(
                instance.localPosition,
                instance.localRotation,
                instance.localScale);

            Matrix4x4 worldMatrix = chunkLocalToWorld * localMatrix;

            AddTreeBillboardMatrix(
                instance.variant,
                worldMatrix,
                mapleWorldMatrices,
                sugarMapleWorldMatrices,
                birchAspenWorldMatrices,
                beechWorldMatrices,
                spruceWorldMatrices,
                whitePineWorldMatrices,
                oakWorldMatrices);
        }

        foliageRuntime.CacheTreeBillboardMatrices(
            mapleWorldMatrices,
            sugarMapleWorldMatrices,
            birchAspenWorldMatrices,
            beechWorldMatrices,
            spruceWorldMatrices,
            whitePineWorldMatrices,
            oakWorldMatrices);
    }

    private void AddTreeBillboardMatrix(
        WorldFeatureVariant variant,
        Matrix4x4 worldMatrix,
        List<Matrix4x4> mapleWorldMatrices,
        List<Matrix4x4> sugarMapleWorldMatrices,
        List<Matrix4x4> birchAspenWorldMatrices,
        List<Matrix4x4> beechWorldMatrices,
        List<Matrix4x4> spruceWorldMatrices,
        List<Matrix4x4> whitePineWorldMatrices,
        List<Matrix4x4> oakWorldMatrices)
    {
        switch (variant)
        {
            case WorldFeatureVariant.SugarMapleTree:
                sugarMapleWorldMatrices.Add(worldMatrix);
                break;
            case WorldFeatureVariant.BirchAspenTree:
                birchAspenWorldMatrices.Add(worldMatrix);
                break;
            case WorldFeatureVariant.BeechTree:
                beechWorldMatrices.Add(worldMatrix);
                break;
            case WorldFeatureVariant.SpruceTree:
                spruceWorldMatrices.Add(worldMatrix);
                break;
            case WorldFeatureVariant.WhitePineTree:
                whitePineWorldMatrices.Add(worldMatrix);
                break;
            case WorldFeatureVariant.OakTree:
                oakWorldMatrices.Add(worldMatrix);
                break;
            default:
                mapleWorldMatrices.Add(worldMatrix);
                break;
        }
    }

    private void RebuildFlowerBatches(ChunkRuntime runtime, ChunkRecord record)
    {
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;
        ChunkFoliageData data = record.FoliageData;

        if (foliageRuntime == null || data == null || data.flowerInstances == null)
            return;

        List<Matrix4x4> worldMatrices = new List<Matrix4x4>();
        List<Vector4> petalColors = new List<Vector4>();
        Matrix4x4 chunkLocalToWorld = runtime.RootTransform.localToWorldMatrix;

        for (int i = 0; i < data.flowerInstances.Count; i++)
        {
            FlowerInstanceData instance = data.flowerInstances[i];

            Matrix4x4 localMatrix = Matrix4x4.TRS(
                instance.localPosition,
                instance.localRotation,
                instance.localScale);

            Matrix4x4 worldMatrix = chunkLocalToWorld * localMatrix;
            worldMatrices.Add(worldMatrix);
            petalColors.Add(Color32ToVector4(instance.petalColor));
        }

        foliageRuntime.CacheFlowerBatches(worldMatrices, petalColors);
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

        List<Matrix4x4> worldMatrices = new List<Matrix4x4>();
        List<Vector4> instanceData = new List<Vector4>();
        Matrix4x4 chunkLocalToWorld = runtime.RootTransform.localToWorldMatrix;

        int subChunksPerChunk = data.subChunksPerChunk;

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

                    Matrix4x4 localMatrix = Matrix4x4.TRS(
                        instance.localPosition,
                        instance.localRotation,
                        instance.localScale);

                    Matrix4x4 worldMatrix = chunkLocalToWorld * localMatrix;
                    worldMatrices.Add(worldMatrix);
                    instanceData.Add(new Vector4(instance.forestBlend, 0f, 0f, 0f));
                }
            }
        }

        foliageRuntime.CacheGrassMatrices(worldMatrices, instanceData);
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

        List<Matrix4x4> worldMatrices = new List<Matrix4x4>();
        List<Vector4> instanceData = new List<Vector4>();
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

                    Vector3 scaledScale = instance.localScale * scaleMultiplier;

                    Matrix4x4 localMatrix = Matrix4x4.TRS(
                        instance.localPosition,
                        instance.localRotation,
                        scaledScale);

                    Matrix4x4 worldMatrix = chunkLocalToWorld * localMatrix;
                    worldMatrices.Add(worldMatrix);
                    instanceData.Add(new Vector4(instance.forestBlend, 0f, 0f, 0f));
                }
            }
        }

        foliageRuntime.CacheBillboardMatrices(worldMatrices, instanceData);
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
        return ring <= treeSettings.billboardTreeChunkRingRadius;
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
        chunkRuntime.FoliageRuntime.mapleTreeBillboard = mapleTreeBillboard;
        chunkRuntime.FoliageRuntime.sugarMapleTreeBillboard = sugarMapleTreeBillboard;
        chunkRuntime.FoliageRuntime.birchAspenTreeBillboard = birchAspenTreeBillboard;
        chunkRuntime.FoliageRuntime.beechTreeBillboard = beechTreeBillboard;
        chunkRuntime.FoliageRuntime.spruceTreeBillboard = spruceTreeBillboard;
        chunkRuntime.FoliageRuntime.whitePineTreeBillboard = whitePineTreeBillboard;
        chunkRuntime.FoliageRuntime.oakTreeBillboard = oakTreeBillboard;
        chunkRuntime.FoliageRuntime.fallbackTreeBillboard = fallbackTreeBillboard;

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

        fallbackTreeBillboard = ResolveTreeBillboardRenderData(
            treeSettings.treeBillboardPrefab,
            "fallback tree billboard");

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
    }

    private void WarnMissingTreePrefab(GameObject prefab, string label)
    {
        if (prefab == null && treeSettings.treeLOD0GameObjectPrefab == null)
        {
            Debug.LogWarning($"{label} tree prefab is missing and no fallback tree prefab is assigned.");
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
