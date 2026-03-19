using UnityEngine;

public static class MeshGenerator
{
    public static MeshData GenerateTerrainMesh(float[,] heightMap, BiomeType[,] biomeMap, float heightMultiplier, int stepIncrement)
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
                meshData.vertices[vertexIndex] = new Vector3(topLeftX + x, heightMap[x, y] * heightMultiplier, bottomLeftZ + y);
                meshData.uvs[vertexIndex] = new Vector2(x / (float)(width - 1), y / (float)(height - 1));
                meshData.colors[vertexIndex] = GenerateColorFromBiomeType(biomeMap[x, y]);

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

    private static Color GenerateColorFromBiomeType(BiomeType biomeType)
    {
        Color color;

        switch (biomeType)
        {
            case BiomeType.Water:
                color = new Color(0.05f, 0.25f, 0.6f);     // deep blue
                break;

            case BiomeType.Beach:
                color = new Color(0.8f, 0.75f, 0.55f);     // sand
                break;

            case BiomeType.Grassland:
                color = new Color(0.25f, 0.6f, 0.25f);     // bright green
                break;

            case BiomeType.Forest:
                color = new Color(0.05f, 0.4f, 0.05f);     // dark green
                break;

            case BiomeType.Desert:
                color = new Color(0.9f, 0.8f, 0.4f);       // yellow/tan
                break;

            case BiomeType.Rock:
                color = new Color(0.45f, 0.45f, 0.45f);    // grey
                break;

            case BiomeType.Snow:
                color = new Color(1f, 1f, 1f);             // white
                break;

            default:
                color = Color.magenta;                     // error/debug fallback
                break;
        }

        return color;
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
