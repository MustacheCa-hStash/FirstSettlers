using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public struct TreeRenderPart
{
    public Mesh mesh;
    public Material material;
    public Matrix4x4 childLocalMatrix;
}

public struct TreeBillboardRenderData
{
    public Mesh mesh;
    public Material material;

    public TreeBillboardRenderData(Mesh mesh, Material material)
    {
        this.mesh = mesh;
        this.material = material;
    }
}

public struct GrassRenderBatch
{
    public Matrix4x4[] matrices;
    public Vector4[] instanceData;

    public GrassRenderBatch(Matrix4x4[] matrices, Vector4[] instanceData)
    {
        this.matrices = matrices;
        this.instanceData = instanceData;
    }
}

public class ChunkFoliageRuntime
{
    public Transform root;

    public Mesh grassMesh;
    public Material grassMaterial;
    public bool receiveGrassShadows;
    public int grassInstanceDataPropertyId;
    public Color forestDarkGrassColor;
    public Color forestMidGrassColor;
    public Color forestLightGrassColor;

    public Mesh billboardMesh;
    public Material billboardMaterial;

    public Mesh flowerMesh;
    public Material flowerMaterial;
    public int flowerPetalColorPropertyId;

    public GameObject mapleTreePrefab;
    public GameObject spruceTreePrefab;
    public GameObject fallbackTreePrefab;
    public TreeBillboardRenderData mapleTreeBillboard;
    public TreeBillboardRenderData spruceTreeBillboard;
    public TreeBillboardRenderData fallbackTreeBillboard;

    public bool isVisible;

    private readonly List<GrassRenderBatch> grassRenderBatches = new List<GrassRenderBatch>();
    private readonly List<GrassRenderBatch> billboardRenderBatches = new List<GrassRenderBatch>();
    private readonly MaterialPropertyBlock grassPropertyBlock = new MaterialPropertyBlock();
    private readonly List<FlowerRenderBatch> flowerRenderBatches = new List<FlowerRenderBatch>();
    private readonly MaterialPropertyBlock flowerPropertyBlock = new MaterialPropertyBlock();

    private readonly List<Matrix4x4[]> mapleTreeBillboardMatrixBatches = new List<Matrix4x4[]>();
    private readonly List<Matrix4x4[]> spruceTreeBillboardMatrixBatches = new List<Matrix4x4[]>();

    private GameObject treeGameObjectRoot;
    private readonly List<GameObject> treeGameObjects = new List<GameObject>();

    private FoliageRepresentationMode currentTreeRepresentationMode;
    private bool hasCurrentTreeRepresentation;

    public bool IsCreated => root != null;
    public int GpuGrassInstanceCount =>
        CountGrassInstances(grassRenderBatches) +
        CountGrassInstances(billboardRenderBatches);
    public int GpuFlowerInstanceCount => CountFlowerInstances();
    public int GpuTreeInstanceCount =>
        CountMatrices(mapleTreeBillboardMatrixBatches) +
        CountMatrices(spruceTreeBillboardMatrixBatches);

    public bool HasCurrentTreeRepresentation(FoliageRepresentationMode mode)
    {
        return hasCurrentTreeRepresentation &&
               currentTreeRepresentationMode == mode;
    }

    public void SetCurrentTreeRepresentation(FoliageRepresentationMode mode)
    {
        currentTreeRepresentationMode = mode;
        hasCurrentTreeRepresentation = true;
    }

    public void ClearCurrentTreeRepresentation()
    {
        hasCurrentTreeRepresentation = false;
    }

    public void ClearCachedBatches()
    {
        grassRenderBatches.Clear();
        billboardRenderBatches.Clear();
        flowerRenderBatches.Clear();
        mapleTreeBillboardMatrixBatches.Clear();
        spruceTreeBillboardMatrixBatches.Clear();
        ClearTreeGameObjects();
        ClearCurrentTreeRepresentation();
    }

    public void SetVisible(bool visible)
    {
        isVisible = visible;

        if (root != null)
        {
            root.gameObject.SetActive(visible);
        }
    }

    public bool HasValidGrassRenderData()
    {
        return grassMesh != null &&
               grassMaterial != null &&
               grassRenderBatches.Count > 0;
    }

    public bool HasValidBillboardRenderData()
    {
        return billboardMesh != null &&
               billboardMaterial != null &&
               billboardRenderBatches.Count > 0;
    }

    public bool HasValidFlowerRenderData()
    {
        return flowerMesh != null && flowerMaterial != null && flowerRenderBatches.Count > 0;
    }

    public bool HasValidTreeBillboardRenderData()
    {
        return HasValidBillboardBatch(mapleTreeBillboard, mapleTreeBillboardMatrixBatches) ||
               HasValidBillboardBatch(spruceTreeBillboard, spruceTreeBillboardMatrixBatches);
    }

    public bool HasTreeGameObjects()
    {
        return treeGameObjects.Count > 0;
    }

    public void CacheGrassMatrices(List<Matrix4x4> worldMatrices, List<Vector4> instanceData)
    {
        CacheGrassRenderBatches(worldMatrices, instanceData, grassRenderBatches);
    }

    public void CacheBillboardMatrices(List<Matrix4x4> worldMatrices, List<Vector4> instanceData)
    {
        CacheGrassRenderBatches(worldMatrices, instanceData, billboardRenderBatches);
    }

    private void CacheGrassRenderBatches(
        List<Matrix4x4> worldMatrices,
        List<Vector4> instanceData,
        List<GrassRenderBatch> targetBatches)
    {
        targetBatches.Clear();

        if (worldMatrices == null || instanceData == null)
            return;

        if (worldMatrices.Count != instanceData.Count)
        {
            Debug.LogError("Grass matrix and instance data counts must match.");
            return;
        }

        const int maxBatchSize = 1023;
        int totalCount = worldMatrices.Count;
        int startIndex = 0;

        while (startIndex < totalCount)
        {
            int batchCount = Mathf.Min(maxBatchSize, totalCount - startIndex);
            Matrix4x4[] matrixBatch = new Matrix4x4[batchCount];
            Vector4[] instanceDataBatch = new Vector4[batchCount];

            for (int i = 0; i < batchCount; i++)
            {
                matrixBatch[i] = worldMatrices[startIndex + i];
                instanceDataBatch[i] = instanceData[startIndex + i];
            }

            targetBatches.Add(new GrassRenderBatch(matrixBatch, instanceDataBatch));
            startIndex += batchCount;
        }
    }

    public void CacheFlowerBatches(List<Matrix4x4> worldMatrices, List<Vector4> petalColors)
    {
        flowerRenderBatches.Clear();

        if (worldMatrices == null || petalColors == null)
            return;

        if (worldMatrices.Count != petalColors.Count)
        {
            Debug.LogError("Flower matrix and petal color counts must match.");
            return;
        }

        const int maxBatchSize = 1023;
        int totalCount = worldMatrices.Count;
        int startIndex = 0;

        while (startIndex < totalCount)
        {
            int batchCount = Mathf.Min(maxBatchSize, totalCount - startIndex);
            Matrix4x4[] matrixBatch = new Matrix4x4[batchCount];
            Vector4[] petalColorBatch = new Vector4[batchCount];

            for (int i = 0; i < batchCount; i++)
            {
                matrixBatch[i] = worldMatrices[startIndex + i];
                petalColorBatch[i] = petalColors[startIndex + i];
            }

            flowerRenderBatches.Add(new FlowerRenderBatch(matrixBatch, petalColorBatch));
            startIndex += batchCount;
        }
    }

    public void CacheTreeBillboardMatrices(
        List<Matrix4x4> mapleWorldMatrices,
        List<Matrix4x4> spruceWorldMatrices)
    {
        CacheMatrices(mapleWorldMatrices, mapleTreeBillboardMatrixBatches);
        CacheMatrices(spruceWorldMatrices, spruceTreeBillboardMatrixBatches);
    }

    private void CacheMatrices(List<Matrix4x4> worldMatrices, List<Matrix4x4[]> targetBatches)
    {
        targetBatches.Clear();

        const int maxBatchSize = 1023;
        int totalCount = worldMatrices.Count;
        int startIndex = 0;

        while (startIndex < totalCount)
        {
            int batchCount = Mathf.Min(maxBatchSize, totalCount - startIndex);
            Matrix4x4[] batch = new Matrix4x4[batchCount];

            for (int i = 0; i < batchCount; i++)
            {
                batch[i] = worldMatrices[startIndex + i];
            }

            targetBatches.Add(batch);
            startIndex += batchCount;
        }
    }

    private int CountMatrices(List<Matrix4x4[]> batches)
    {
        int count = 0;

        for (int i = 0; i < batches.Count; i++)
        {
            if (batches[i] != null)
                count += batches[i].Length;
        }

        return count;
    }

    private int CountGrassInstances(List<GrassRenderBatch> batches)
    {
        int count = 0;

        for (int i = 0; i < batches.Count; i++)
        {
            if (batches[i].matrices != null)
                count += batches[i].matrices.Length;
        }

        return count;
    }

    private int CountFlowerInstances()
    {
        int count = 0;

        for (int i = 0; i < flowerRenderBatches.Count; i++)
        {
            if (flowerRenderBatches[i].matrices != null)
                count += flowerRenderBatches[i].matrices.Length;
        }

        return count;
    }

    public void RebuildTreeGameObjects(
        List<TreeInstanceData> instances,
        Transform chunkRoot)
    {
        ClearTreeGameObjects();

        if (instances == null || chunkRoot == null || root == null)
            return;

        treeGameObjectRoot = new GameObject("Tree_GameObjects");
        treeGameObjectRoot.transform.SetParent(root, false);

        for (int i = 0; i < instances.Count; i++)
        {
            TreeInstanceData instance = instances[i];
            GameObject prefab = GetTreePrefab(instance.variant);

            if (prefab == null)
                continue;

            GameObject treeObject = Object.Instantiate(prefab, treeGameObjectRoot.transform);
            treeObject.transform.localPosition = instance.localPosition;
            treeObject.transform.localRotation = instance.localRotation;
            treeObject.transform.localScale = instance.localScale;

            treeGameObjects.Add(treeObject);
        }
    }

    public void ClearTreeGameObjects()
    {
        for (int i = 0; i < treeGameObjects.Count; i++)
        {
            if (treeGameObjects[i] != null)
            {
                Object.Destroy(treeGameObjects[i]);
            }
        }

        treeGameObjects.Clear();

        if (treeGameObjectRoot != null)
        {
            Object.Destroy(treeGameObjectRoot);
            treeGameObjectRoot = null;
        }
    }

    public void ClearFlowerBatches()
    {
        flowerRenderBatches.Clear();
    }

    public void ClearTreeBillboardMatrices()
    {
        mapleTreeBillboardMatrixBatches.Clear();
        spruceTreeBillboardMatrixBatches.Clear();
    }

    public void DrawGrass()
    {
        if (!isVisible || !HasValidGrassRenderData())
            return;

        for (int i = 0; i < grassRenderBatches.Count; i++)
        {
            DrawInstancedBatch(grassMesh, grassMaterial, grassRenderBatches[i]);
        }
    }

    public void DrawBillboards()
    {
        if (!isVisible || !HasValidBillboardRenderData())
            return;

        for (int i = 0; i < billboardRenderBatches.Count; i++)
        {
            DrawInstancedBatch(billboardMesh, billboardMaterial, billboardRenderBatches[i]);
        }
    }

    private void DrawInstancedBatch(
        Mesh mesh,
        Material material,
        GrassRenderBatch batch)
    {
        grassPropertyBlock.Clear();
        grassPropertyBlock.SetVectorArray(grassInstanceDataPropertyId, batch.instanceData);
        grassPropertyBlock.SetColor("_ForestDarkGrassColor", forestDarkGrassColor);
        grassPropertyBlock.SetColor("_ForestMidGrassColor", forestMidGrassColor);
        grassPropertyBlock.SetColor("_ForestLightGrassColor", forestLightGrassColor);

        Graphics.DrawMeshInstanced(
            mesh,
            0,
            material,
            batch.matrices,
            batch.matrices.Length,
            grassPropertyBlock,
            ShadowCastingMode.Off,
            receiveGrassShadows
        );
    }

    public void DrawFlowers()
    {
        if (!isVisible || !HasValidFlowerRenderData())
            return;

        for (int i = 0; i < flowerRenderBatches.Count; i++)
        {
            FlowerRenderBatch batch = flowerRenderBatches[i];

            if (batch.matrices == null || batch.petalColors == null)
                continue;

            flowerPropertyBlock.Clear();
            flowerPropertyBlock.SetVectorArray(flowerPetalColorPropertyId, batch.petalColors);

            Graphics.DrawMeshInstanced(
                flowerMesh,
                0,
                flowerMaterial,
                batch.matrices,
                batch.matrices.Length,
                flowerPropertyBlock,
                ShadowCastingMode.Off,
                true
            );
        }
    }

    public void DrawTreeBillboards(bool castShadows, bool receiveShadows)
    {
        if (!isVisible || !HasValidTreeBillboardRenderData())
            return;

        ShadowCastingMode shadowMode = castShadows
            ? ShadowCastingMode.On
            : ShadowCastingMode.Off;

        DrawTreeBillboardBatches(mapleTreeBillboard, mapleTreeBillboardMatrixBatches, shadowMode, receiveShadows);
        DrawTreeBillboardBatches(spruceTreeBillboard, spruceTreeBillboardMatrixBatches, shadowMode, receiveShadows);
    }

    private void DrawTreeBillboardBatches(
        TreeBillboardRenderData renderData,
        List<Matrix4x4[]> batches,
        ShadowCastingMode shadowMode,
        bool receiveShadows)
    {
        if (renderData.mesh == null || renderData.material == null)
            return;

        for (int i = 0; i < batches.Count; i++)
        {
            Graphics.DrawMeshInstanced(
                renderData.mesh,
                0,
                renderData.material,
                batches[i],
                batches[i].Length,
                null,
                shadowMode,
                receiveShadows
            );
        }
    }

    private bool HasValidBillboardBatch(TreeBillboardRenderData renderData, List<Matrix4x4[]> batches)
    {
        return renderData.mesh != null &&
               renderData.material != null &&
               batches.Count > 0;
    }

    private GameObject GetTreePrefab(WorldFeatureVariant variant)
    {
        if (variant == WorldFeatureVariant.MapleTree && mapleTreePrefab != null)
            return mapleTreePrefab;

        if (variant == WorldFeatureVariant.SpruceTree && spruceTreePrefab != null)
            return spruceTreePrefab;

        return fallbackTreePrefab;
    }
}
