using UnityEngine;

public static class MeshGenerator
{
    public static MeshData GenerateTerrainMesh(float[,] heightMap, BiomeType[,] biomeMap, SurfaceType[,] surfaceTypeMap,
        WaterState[,] waterStateMap, float heightMultiplier, int stepIncrement, float worldScale, float[,] riverMaskMap)
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
                (bottomLeftZ + y) * worldScale);
                meshData.uvs[vertexIndex] = new Vector2(x / (float)(width - 1), y / (float)(height - 1));
                //meshData.colors[vertexIndex] = BiomeClassifier.GenerateColorFromBiomeType(biomeMap[x, y]);
                //meshData.colors[vertexIndex] = BiomeClassifier.GenerateDebugColorFromRiverMask(riverMaskMap[x, y]);
                meshData.colors[vertexIndex] = SurfaceTypeClassifier.GenerateColor(surfaceTypeMap[x, y], 
                    waterStateMap[x, y]);

                if (x < width - 1 && y < height - 1)
                {
                    meshData.AddTriangle(vertexIndex, vertexIndex + verticesPerLine, vertexIndex + verticesPerLine + 1);
                    meshData.AddTriangle(vertexIndex, vertexIndex + verticesPerLine + 1, vertexIndex + 1);
                }

                vertexIndex++;
            }
        }

        return meshData;

    }

}

public class MeshData
{
    public Vector3[] vertices;
    public Vector2[] uvs;
    public int[] triangles;
    public Color[] colors;

    private int triangleIndex;

    public MeshData(int meshWidth, int meshHeight)
    {
        vertices = new Vector3[meshWidth * meshHeight];
        uvs = new Vector2[meshWidth * meshHeight];
        triangles = new int[(meshWidth - 1) * (meshHeight - 1) * 6];
        colors = new Color[meshWidth * meshHeight];
    }

    public void AddTriangle(int a, int b, int c)
    {
        triangles[triangleIndex] = a;
        triangles[triangleIndex + 1] = b;
        triangles[triangleIndex + 2] = c;
        triangleIndex += 3;
    }

    public Mesh CreateMesh()
    {
        Mesh mesh = new Mesh();
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.colors = colors;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
