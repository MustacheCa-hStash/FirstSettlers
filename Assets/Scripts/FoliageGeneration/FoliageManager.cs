using System.Collections.Generic;
using UnityEngine;

public class FoliageManager
{
    private readonly Transform foliageParent;
    private readonly GrassSettings grassSettings;
    private readonly TreeSettings treeSettings;
    private readonly int worldSeed;
    private readonly int chunkSize;
    private readonly float worldScale;
    private readonly float meshHeightMultiplier;

    private Mesh grassMesh;
    private Material grassMaterial;

    private Mesh billboardGrassMesh;
    private Material billboardGrassMaterial;

    private readonly List<TreeGpuLODRenderData> treeGpuLODs = new List<TreeGpuLODRenderData>();

    private Mesh treeBillboardMesh;
    private Material treeBillboardMaterial;

    public FoliageManager(Transform foliageParent, GrassSettings grassSettings, TreeSettings treeSettings, int worldSeed,
        int chunkSize, float worldScale, float meshHeightMultiplier)
    {
        this.foliageParent = foliageParent;
        this.grassSettings = grassSettings;
        this.treeSettings = treeSettings;
        this.worldSeed = worldSeed;
        this.chunkSize = chunkSize;
        this.worldScale = worldScale;
        this.meshHeightMultiplier = meshHeightMultiplier;

        ResolveGrassRenderAssets();
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
            bool useTrees = IsWithinTreeRenderRange(viewerCoord, coord);
            bool useFoliage = useNearGrass || useBillboardGrass || useTrees;

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
                runtime.FoliageRuntime.ClearTreeGpuMatrices();
                runtime.FoliageRuntime.ClearTreeBillboardMatrices();
                runtime.FoliageRuntime.ClearCurrentTreeRepresentation();
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
            bool useTrees = IsWithinTreeRenderRange(viewerCoord, coord);
            bool useFoliage = useNearGrass || useBillboardGrass || useTrees;

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

    private void RebuildTreeRepresentationIfNeeded(
        ChunkRuntime runtime,
        ChunkRecord record,
        ChunkCoord viewerCoord)
    {
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;

        FoliageRepresentationMode mode = GetTreeRepresentationMode(viewerCoord, record.ChunkCoord);
        int gpuLODIndex = GetTreeGpuLODIndex(viewerCoord, record.ChunkCoord, mode);

        if (foliageRuntime.HasCurrentTreeRepresentation(mode, gpuLODIndex))
            return;

        foliageRuntime.ClearTreeGameObjects();
        foliageRuntime.ClearTreeGpuMatrices();
        foliageRuntime.ClearTreeBillboardMatrices();

        if (mode == FoliageRepresentationMode.GameObjectWithCollision)
        {
            foliageRuntime.RebuildTreeGameObjects(
                treeSettings.treeLOD0GameObjectPrefab,
                record.FoliageData.treeCubeInstances,
                runtime.RootTransform);
        }
        else if (mode == FoliageRepresentationMode.GPUInstancedWithCollision ||
                 mode == FoliageRepresentationMode.GPUInstancedNoCollision)
        {
            RebuildTreeGpuMatrices(runtime, record);
        }
        else if (mode == FoliageRepresentationMode.GPUInstancedBillboard)
        {
            RebuildTreeBillboardMatrices(runtime, record);
        }

        foliageRuntime.SetCurrentTreeRepresentation(mode, gpuLODIndex);
    }

    private void DrawTreesForChunk(
        ChunkRuntime runtime,
        ChunkCoord viewerCoord,
        ChunkCoord chunkCoord)
    {
        if (runtime.FoliageRuntime == null)
            return;

        FoliageRepresentationMode mode = GetTreeRepresentationMode(viewerCoord, chunkCoord);
        int gpuLODIndex = GetTreeGpuLODIndex(viewerCoord, chunkCoord, mode);

        if (mode == FoliageRepresentationMode.GPUInstancedWithCollision ||
            mode == FoliageRepresentationMode.GPUInstancedNoCollision)
        {
            runtime.FoliageRuntime.DrawTreeGpuLOD(
                gpuLODIndex,
                treeSettings.castTreeShadows,
                treeSettings.receiveTreeShadows);
        }
        else if (mode == FoliageRepresentationMode.GPUInstancedBillboard)
        {
            runtime.FoliageRuntime.DrawTreeBillboards(
                treeSettings.castTreeShadows,
                treeSettings.receiveTreeShadows);
        }
    }

    private void RebuildTreeGpuMatrices(ChunkRuntime runtime, ChunkRecord record)
    {
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;
        ChunkFoliageData data = record.FoliageData;

        List<Matrix4x4> worldMatrices = new List<Matrix4x4>();
        Matrix4x4 chunkLocalToWorld = runtime.RootTransform.localToWorldMatrix;

        for (int i = 0; i < data.treeCubeInstances.Count; i++)
        {
            TreeInstanceData instance = data.treeCubeInstances[i];

            Matrix4x4 localMatrix = Matrix4x4.TRS(
                instance.localPosition,
                instance.localRotation,
                instance.localScale);

            Matrix4x4 worldMatrix = chunkLocalToWorld * localMatrix;
            worldMatrices.Add(worldMatrix);
        }

        foliageRuntime.CacheTreeGpuMatrices(worldMatrices);
    }

    private void RebuildTreeBillboardMatrices(ChunkRuntime runtime, ChunkRecord record)
    {
        ChunkFoliageRuntime foliageRuntime = runtime.FoliageRuntime;
        ChunkFoliageData data = record.FoliageData;

        List<Matrix4x4> worldMatrices = new List<Matrix4x4>();
        Matrix4x4 chunkLocalToWorld = runtime.RootTransform.localToWorldMatrix;

        for (int i = 0; i < data.treeCubeInstances.Count; i++)
        {
            TreeInstanceData instance = data.treeCubeInstances[i];

            Matrix4x4 localMatrix = Matrix4x4.TRS(
                instance.localPosition,
                instance.localRotation,
                instance.localScale);

            Matrix4x4 worldMatrix = chunkLocalToWorld * localMatrix;
            worldMatrices.Add(worldMatrix);
        }

        foliageRuntime.CacheTreeBillboardMatrices(worldMatrices);
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
                }
            }
        }

        foliageRuntime.CacheGrassMatrices(worldMatrices);
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
                }
            }
        }

        foliageRuntime.CacheBillboardMatrices(worldMatrices);
    }

    private FoliageRepresentationMode GetTreeRepresentationMode(
        ChunkCoord viewerCoord,
        ChunkCoord targetCoord)
    {
        int ring = GetChunkRingDistance(viewerCoord, targetCoord);

        if (ring <= treeSettings.gameObjectTreeChunkRingRadius)
            return FoliageRepresentationMode.GameObjectWithCollision;

        if (ring <= treeSettings.gpuInstancedTreeChunkRingRadius)
            return FoliageRepresentationMode.GPUInstancedNoCollision;

        return FoliageRepresentationMode.GPUInstancedBillboard;
    }

    private int GetTreeGpuLODIndex(
        ChunkCoord viewerCoord,
        ChunkCoord targetCoord,
        FoliageRepresentationMode mode)
    {
        if (mode != FoliageRepresentationMode.GPUInstancedWithCollision &&
            mode != FoliageRepresentationMode.GPUInstancedNoCollision)
        {
            return -1;
        }

        int ring = GetChunkRingDistance(viewerCoord, targetCoord);
        int firstGpuRing = treeSettings.gameObjectTreeChunkRingRadius + 1;
        int lodIndex = ring - firstGpuRing;

        if (treeGpuLODs.Count == 0)
            return -1;

        return Mathf.Clamp(lodIndex, 0, treeGpuLODs.Count - 1);
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
        return record.HeightMap != null && record.SurfaceTypeMap != null;
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

        chunkRuntime.FoliageRuntime.billboardMesh = billboardGrassMesh;
        chunkRuntime.FoliageRuntime.billboardMaterial = billboardGrassMaterial;

        chunkRuntime.FoliageRuntime.treeGpuLODs = treeGpuLODs;
        chunkRuntime.FoliageRuntime.treeBillboardMesh = treeBillboardMesh;
        chunkRuntime.FoliageRuntime.treeBillboardMaterial = treeBillboardMaterial;

        chunkRuntime.FoliageRuntime.SetVisible(false);
    }

    private void ResolveGrassRenderAssets()
    {
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

    private void ResolveTreeRenderAssets()
    {
        treeGpuLODs.Clear();

        if (treeSettings.treeLOD0GameObjectPrefab == null)
        {
            Debug.LogError("Tree LOD0 GameObject prefab is missing.");
        }

        if (treeSettings.treeGPUInstancedLODPrefabs == null ||
            treeSettings.treeGPUInstancedLODPrefabs.Length == 0)
        {
            Debug.LogError("No GPU-instanced tree LOD prefabs assigned.");
        }
        else
        {
            for (int i = 0; i < treeSettings.treeGPUInstancedLODPrefabs.Length; i++)
            {
                GameObject prefab = treeSettings.treeGPUInstancedLODPrefabs[i];

                if (prefab == null)
                {
                    Debug.LogError($"Tree GPU LOD prefab at index {i} is missing.");
                    continue;
                }

                MeshFilter meshFilter = prefab.GetComponent<MeshFilter>();
                MeshRenderer meshRenderer = prefab.GetComponent<MeshRenderer>();

                if (meshFilter == null || meshFilter.sharedMesh == null)
                {
                    Debug.LogError($"Tree GPU LOD prefab at index {i} must have a MeshFilter with a mesh on the root.");
                    continue;
                }

                if (meshRenderer == null || meshRenderer.sharedMaterial == null)
                {
                    Debug.LogError($"Tree GPU LOD prefab at index {i} must have a MeshRenderer with one shared material on the root.");
                    continue;
                }

                treeGpuLODs.Add(new TreeGpuLODRenderData(
                    meshFilter.sharedMesh,
                    meshRenderer.sharedMaterial));
            }
        }

        if (treeSettings.treeBillboardPrefab != null)
        {
            MeshFilter meshFilter = treeSettings.treeBillboardPrefab.GetComponent<MeshFilter>();
            MeshRenderer meshRenderer = treeSettings.treeBillboardPrefab.GetComponent<MeshRenderer>();

            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                Debug.LogError("Tree billboard prefab must have a MeshFilter with a mesh on the root.");
            }
            else
            {
                treeBillboardMesh = meshFilter.sharedMesh;
            }

            if (meshRenderer == null || meshRenderer.sharedMaterial == null)
            {
                Debug.LogError("Tree billboard prefab must have a MeshRenderer with one shared material on the root.");
            }
            else
            {
                treeBillboardMaterial = meshRenderer.sharedMaterial;
            }
        }
    }
}