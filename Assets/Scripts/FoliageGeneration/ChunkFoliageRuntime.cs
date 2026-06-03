using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public struct TreeRenderPart
{
    public Mesh mesh;
    public Material material;
    public Matrix4x4 childLocalMatrix;
}

public struct TreeGpuLODRenderData
{
    public Mesh mesh;
    public Material material;

    public TreeGpuLODRenderData(Mesh mesh, Material material)
    {
        this.mesh = mesh;
        this.material = material;
    }
}

public class ChunkFoliageRuntime
{
    public Transform root;

    public Mesh grassMesh;
    public Material grassMaterial;

    public Mesh billboardMesh;
    public Material billboardMaterial;

    public List<TreeGpuLODRenderData> treeGpuLODs = new List<TreeGpuLODRenderData>();

    public Mesh treeBillboardMesh;
    public Material treeBillboardMaterial;

    public bool isVisible;

    private readonly List<Matrix4x4[]> grassMatrixBatches = new List<Matrix4x4[]>();
    private readonly List<Matrix4x4[]> billboardMatrixBatches = new List<Matrix4x4[]>();

    private readonly List<Matrix4x4[]> treeGpuMatrixBatches = new List<Matrix4x4[]>();
    private readonly List<Matrix4x4[]> treeBillboardMatrixBatches = new List<Matrix4x4[]>();

    private GameObject treeGameObjectRoot;
    private readonly List<GameObject> treeGameObjects = new List<GameObject>();

    private FoliageRepresentationMode currentTreeRepresentationMode;
    private int currentTreeGpuLODIndex = -1;
    private bool hasCurrentTreeRepresentation;

    public bool IsCreated => root != null;

    public bool HasCurrentTreeRepresentation(
        FoliageRepresentationMode mode,
        int gpuLODIndex)
    {
        return hasCurrentTreeRepresentation &&
               currentTreeRepresentationMode == mode &&
               currentTreeGpuLODIndex == gpuLODIndex;
    }

    public void SetCurrentTreeRepresentation(
        FoliageRepresentationMode mode,
        int gpuLODIndex)
    {
        currentTreeRepresentationMode = mode;
        currentTreeGpuLODIndex = gpuLODIndex;
        hasCurrentTreeRepresentation = true;
    }

    public void ClearCurrentTreeRepresentation()
    {
        hasCurrentTreeRepresentation = false;
        currentTreeGpuLODIndex = -1;
    }

    public void ClearCachedBatches()
    {
        grassMatrixBatches.Clear();
        billboardMatrixBatches.Clear();
        treeGpuMatrixBatches.Clear();
        treeBillboardMatrixBatches.Clear();
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
        return grassMesh != null && grassMaterial != null && grassMatrixBatches.Count > 0;
    }

    public bool HasValidBillboardRenderData()
    {
        return billboardMesh != null && billboardMaterial != null && billboardMatrixBatches.Count > 0;
    }

    public bool HasValidTreeGpuRenderData(int gpuLODIndex)
    {
        return gpuLODIndex >= 0 &&
               treeGpuLODs != null &&
               gpuLODIndex < treeGpuLODs.Count &&
               treeGpuLODs[gpuLODIndex].mesh != null &&
               treeGpuLODs[gpuLODIndex].material != null &&
               treeGpuMatrixBatches.Count > 0;
    }

    public bool HasValidTreeBillboardRenderData()
    {
        return treeBillboardMesh != null &&
               treeBillboardMaterial != null &&
               treeBillboardMatrixBatches.Count > 0;
    }

    public bool HasTreeGameObjects()
    {
        return treeGameObjects.Count > 0;
    }

    public void CacheGrassMatrices(List<Matrix4x4> worldMatrices)
    {
        CacheMatrices(worldMatrices, grassMatrixBatches);
    }

    public void CacheBillboardMatrices(List<Matrix4x4> worldMatrices)
    {
        CacheMatrices(worldMatrices, billboardMatrixBatches);
    }

    public void CacheTreeGpuMatrices(List<Matrix4x4> worldMatrices)
    {
        CacheMatrices(worldMatrices, treeGpuMatrixBatches);
    }

    public void CacheTreeBillboardMatrices(List<Matrix4x4> worldMatrices)
    {
        CacheMatrices(worldMatrices, treeBillboardMatrixBatches);
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

    public void RebuildTreeGameObjects(
        GameObject prefab,
        List<TreeInstanceData> instances,
        Transform chunkRoot)
    {
        ClearTreeGameObjects();

        if (prefab == null || instances == null || chunkRoot == null || root == null)
            return;

        treeGameObjectRoot = new GameObject("Tree_GameObjects");
        treeGameObjectRoot.transform.SetParent(root, false);

        for (int i = 0; i < instances.Count; i++)
        {
            TreeInstanceData instance = instances[i];

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

    public void ClearTreeGpuMatrices()
    {
        treeGpuMatrixBatches.Clear();
    }

    public void ClearTreeBillboardMatrices()
    {
        treeBillboardMatrixBatches.Clear();
    }

    public void DrawGrass()
    {
        if (!isVisible || !HasValidGrassRenderData())
            return;

        for (int i = 0; i < grassMatrixBatches.Count; i++)
        {
            Graphics.DrawMeshInstanced(
                grassMesh,
                0,
                grassMaterial,
                grassMatrixBatches[i],
                grassMatrixBatches[i].Length,
                null,
                ShadowCastingMode.Off,
                true
            );
        }
    }

    public void DrawBillboards()
    {
        if (!isVisible || !HasValidBillboardRenderData())
            return;

        for (int i = 0; i < billboardMatrixBatches.Count; i++)
        {
            Graphics.DrawMeshInstanced(
                billboardMesh,
                0,
                billboardMaterial,
                billboardMatrixBatches[i],
                billboardMatrixBatches[i].Length,
                null,
                ShadowCastingMode.Off,
                true
            );
        }
    }

    public void DrawTreeGpuLOD(int gpuLODIndex, bool castShadows, bool receiveShadows)
    {
        if (!isVisible || !HasValidTreeGpuRenderData(gpuLODIndex))
            return;

        TreeGpuLODRenderData lodData = treeGpuLODs[gpuLODIndex];

        ShadowCastingMode shadowMode = castShadows
            ? ShadowCastingMode.On
            : ShadowCastingMode.Off;

        for (int i = 0; i < treeGpuMatrixBatches.Count; i++)
        {
            Graphics.DrawMeshInstanced(
                lodData.mesh,
                0,
                lodData.material,
                treeGpuMatrixBatches[i],
                treeGpuMatrixBatches[i].Length,
                null,
                shadowMode,
                receiveShadows
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

        for (int i = 0; i < treeBillboardMatrixBatches.Count; i++)
        {
            Graphics.DrawMeshInstanced(
                treeBillboardMesh,
                0,
                treeBillboardMaterial,
                treeBillboardMatrixBatches[i],
                treeBillboardMatrixBatches[i].Length,
                null,
                shadowMode,
                receiveShadows
            );
        }
    }
}