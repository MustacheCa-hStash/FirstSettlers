using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class ChunkFoliageRuntime
{
    public Transform root;

    public Mesh grassMesh;
    public Material grassMaterial;

    public bool isVisible;
    public int cachedInstanceCount = -1;

    private readonly List<Matrix4x4[]> grassMatrixBatches = new List<Matrix4x4[]>();

    public bool IsCreated => root != null;

    public void ClearCachedBatches()
    {
        grassMatrixBatches.Clear();
        cachedInstanceCount = -1;
    }

    public void SetVisible(bool visible)
    {
        isVisible = visible;

        if (root != null)
        {
            root.gameObject.SetActive(visible);
        }
    }

    public bool HasValidRenderData()
    {
        return grassMesh != null && grassMaterial != null && grassMatrixBatches.Count > 0;
    }

    public void CacheMatrices(List<Matrix4x4> worldMatrices)
    {
        grassMatrixBatches.Clear();

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

            grassMatrixBatches.Add(batch);
            startIndex += batchCount;
        }

        cachedInstanceCount = totalCount;
    }

    public void Draw()
    {
        if (!isVisible || !HasValidRenderData())
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
                ShadowCastingMode.On,
                true
            );
        }
    }
}