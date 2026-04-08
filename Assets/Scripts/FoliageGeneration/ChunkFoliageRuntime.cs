using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class ChunkFoliageRuntime
{
    public Transform root;

    public Mesh grassMesh;
    public Material grassMaterial;

    public Mesh billboardMesh;
    public Material billboardMaterial;

    public bool isVisible;

    private readonly List<Matrix4x4[]> grassMatrixBatches = new List<Matrix4x4[]>();
    private readonly List<Matrix4x4[]> billboardMatrixBatches = new List<Matrix4x4[]>();

    public bool IsCreated => root != null;

    public void ClearCachedBatches()
    {
        grassMatrixBatches.Clear();
        billboardMatrixBatches.Clear();
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

    public void CacheGrassMatrices(List<Matrix4x4> worldMatrices)
    {
        CacheMatrices(worldMatrices, grassMatrixBatches);
    }

    public void CacheBillboardMatrices(List<Matrix4x4> worldMatrices)
    {
        CacheMatrices(worldMatrices, billboardMatrixBatches);
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
}