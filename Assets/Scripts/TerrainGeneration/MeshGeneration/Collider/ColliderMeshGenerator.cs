using UnityEngine;

public static class ColliderMeshGenerator
{
    public static MeshData GenerateColliderMesh(
        float[,] heightMap,
        float heightMultiplier,
        int stepIncrement,
        float worldScale)
    {
        int width = heightMap.GetLength(0);
        int height = heightMap.GetLength(1);

        float topLeftX = (width - 1) / -2f;
        float bottomLeftZ = (height - 1) / -2f;

        int verticesPerLine = (width - 1) / stepIncrement + 1;
        MeshData meshData = new MeshData(verticesPerLine, verticesPerLine);

        int vertexIndex = 0;

        for (int y = 0; y < height; y += stepIncrement)
        {
            for (int x = 0; x < width; x += stepIncrement)
            {
                meshData.vertices[vertexIndex] = new Vector3(
                    (topLeftX + x) * worldScale,
                    heightMap[x, y] * heightMultiplier * worldScale,
                    (bottomLeftZ + y) * worldScale
                );

                if (x < width - 1 && y < height - 1)
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