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
        int paddedHeight = heightMap.GetLength(1);

        int chunkSize = paddedWidth - 3;

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;

        int verticesPerLine = chunkSize / stepIncrement + 1;
        MeshData meshData = new MeshData(verticesPerLine, verticesPerLine);

        int vertexIndex = 0;

        for (int localZ = 0; localZ <= chunkSize; localZ += stepIncrement)
        {
            for (int localX = 0; localX <= chunkSize; localX += stepIncrement)
            {
                int paddedX = localX + 1;
                int paddedZ = localZ + 1;

                meshData.vertices[vertexIndex] = new Vector3(
                    (topLeftX + localX) * worldScale,
                    heightMap[paddedX, paddedZ] * heightMultiplier * worldScale,
                    (bottomLeftZ + localZ) * worldScale
                );

                int xIndex = localX / stepIncrement;
                int zIndex = localZ / stepIncrement;

                if (xIndex < verticesPerLine - 1 && zIndex < verticesPerLine - 1)
                {
                    meshData.AddTriangle(
                        vertexIndex,
                        vertexIndex + verticesPerLine,
                        vertexIndex + verticesPerLine + 1
                    );

                    meshData.AddTriangle(
                        vertexIndex,
                        vertexIndex + verticesPerLine + 1,
                        vertexIndex + 1
                    );
                }

                vertexIndex++;
            }
        }

        return meshData;
    }
}