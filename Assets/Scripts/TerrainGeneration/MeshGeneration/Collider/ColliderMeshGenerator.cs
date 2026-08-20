using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public static class ColliderMeshGenerator
{
    public static MeshData GenerateColliderMesh(
        float[,] heightMap,
        float heightMultiplier,
        int stepIncrement,
        float worldScale)
    {
        int paddedWidth = heightMap.GetLength(0);
        int chunkSize = paddedWidth - 3;
        int safeStepIncrement = Mathf.Max(1, stepIncrement);

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;

        int verticesPerLine = chunkSize / safeStepIncrement + 1;
        int vertexCount = verticesPerLine * verticesPerLine;
        int cellCount = Mathf.Max(0, verticesPerLine - 1) * Mathf.Max(0, verticesPerLine - 1);

        NativeArray<float> heights = default;
        NativeArray<float3> nativeVertices = default;
        NativeArray<int> nativeTriangles = default;

        try
        {
            heights = CopyFloatMapToNative(heightMap, Allocator.TempJob, out _, out int mapHeight);
            nativeVertices = new NativeArray<float3>(vertexCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            nativeTriangles = new NativeArray<int>(cellCount * 6, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            ColliderVertexBuildJob vertexJob = new ColliderVertexBuildJob
            {
                heightMap = heights,
                mapHeight = mapHeight,
                chunkSize = chunkSize,
                stepIncrement = safeStepIncrement,
                verticesPerLine = verticesPerLine,
                topLeftX = topLeftX,
                bottomLeftZ = bottomLeftZ,
                heightMultiplier = heightMultiplier,
                worldScale = worldScale,
                vertices = nativeVertices
            };
            JobHandle vertexHandle = vertexJob.Schedule(vertexCount, 64);

            ColliderTriangleBuildJob triangleJob = new ColliderTriangleBuildJob
            {
                verticesPerLine = verticesPerLine,
                triangles = nativeTriangles
            };
            JobHandle triangleHandle = triangleJob.Schedule(cellCount, 64);

            JobHandle.CombineDependencies(vertexHandle, triangleHandle).Complete();

            return CreateMeshData(nativeVertices, nativeTriangles);
        }
        finally
        {
            if (heights.IsCreated)
                heights.Dispose();
            if (nativeVertices.IsCreated)
                nativeVertices.Dispose();
            if (nativeTriangles.IsCreated)
                nativeTriangles.Dispose();
        }
    }

    private static NativeArray<float> CopyFloatMapToNative(float[,] source, Allocator allocator, out int width, out int height)
    {
        width = source.GetLength(0);
        height = source.GetLength(1);
        NativeArray<float> result =
            new NativeArray<float>(width * height, allocator, NativeArrayOptions.UninitializedMemory);

        for (int x = 0; x < width; x++)
        {
            int rowOffset = x * height;
            for (int z = 0; z < height; z++)
                result[rowOffset + z] = source[x, z];
        }

        return result;
    }

    private static MeshData CreateMeshData(NativeArray<float3> nativeVertices, NativeArray<int> nativeTriangles)
    {
        Vector3[] vertices = new Vector3[nativeVertices.Length];
        Vector3[] normals = new Vector3[nativeVertices.Length];
        Vector2[] uvs = new Vector2[nativeVertices.Length];
        Color[] colors = new Color[nativeVertices.Length];
        int[] triangles = new int[nativeTriangles.Length];

        for (int i = 0; i < nativeVertices.Length; i++)
        {
            float3 vertex = nativeVertices[i];
            vertices[i] = new Vector3(vertex.x, vertex.y, vertex.z);
        }

        nativeTriangles.CopyTo(triangles);
        return new MeshData(vertices, normals, uvs, colors, triangles);
    }

    [BurstCompile]
    private struct ColliderVertexBuildJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> heightMap;
        public int mapHeight;
        public int chunkSize;
        public int stepIncrement;
        public int verticesPerLine;
        public float topLeftX;
        public float bottomLeftZ;
        public float heightMultiplier;
        public float worldScale;

        [WriteOnly] public NativeArray<float3> vertices;

        public void Execute(int vertexIndex)
        {
            int xIndex = vertexIndex % verticesPerLine;
            int zIndex = vertexIndex / verticesPerLine;
            int localX = xIndex * stepIncrement;
            int localZ = zIndex * stepIncrement;
            int paddedX = localX + 1;
            int paddedZ = localZ + 1;

            vertices[vertexIndex] = new float3(
                (topLeftX + localX) * worldScale,
                heightMap[paddedX * mapHeight + paddedZ] * heightMultiplier * worldScale,
                (bottomLeftZ + localZ) * worldScale);
        }
    }

    [BurstCompile]
    private struct ColliderTriangleBuildJob : IJobParallelFor
    {
        public int verticesPerLine;

        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<int> triangles;

        public void Execute(int cellIndex)
        {
            int cellsPerLine = verticesPerLine - 1;
            int xIndex = cellIndex % cellsPerLine;
            int zIndex = cellIndex / cellsPerLine;
            int vertexIndex = zIndex * verticesPerLine + xIndex;
            int triangleIndex = cellIndex * 6;

            triangles[triangleIndex] = vertexIndex;
            triangles[triangleIndex + 1] = vertexIndex + verticesPerLine;
            triangles[triangleIndex + 2] = vertexIndex + verticesPerLine + 1;
            triangles[triangleIndex + 3] = vertexIndex;
            triangles[triangleIndex + 4] = vertexIndex + verticesPerLine + 1;
            triangles[triangleIndex + 5] = vertexIndex + 1;
        }
    }
}
