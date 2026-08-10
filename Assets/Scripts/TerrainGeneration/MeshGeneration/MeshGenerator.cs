using System.Collections.Generic;
using UnityEngine;

public static class MeshGenerator
{
    public static MeshData GenerateTerrainMesh(
        ChunkCoord chunkCoord,
        float[,] heightMap,
        BiomeType[,] biomeMap,
        SurfaceType[,] surfaceTypeMap,
        WaterState[,] waterStateMap,
        float heightMultiplier,
        int stepIncrement,
        float worldScale,
        float[,] riverMaskMap)
    {
        int paddedWidth = heightMap.GetLength(0);
        int chunkSize = paddedWidth - 3;

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;

        MeshData meshData = new MeshData(EstimateTerrainVertexCapacity(chunkSize, stepIncrement));
        Dictionary<int, int> vertexIndicesByGridCoordinate = new Dictionary<int, int>(meshData.VertexCapacity);
        int gridVerticesPerLine = chunkSize + 1;

        int GetVertexIndex(int x, int z)
        {
            int key = z * gridVerticesPerLine + x;
            if (vertexIndicesByGridCoordinate.TryGetValue(key, out int existingIndex))
                return existingIndex;

            int paddedX = x + 1;
            int paddedZ = z + 1;

            float localWorldX = (topLeftX + x) * worldScale;
            float localWorldZ = (bottomLeftZ + z) * worldScale;
            float h = heightMap[paddedX, paddedZ] * heightMultiplier * worldScale;

            int vertexIndex = meshData.AddVertex(
                new Vector3(localWorldX, h, localWorldZ),
                CalculateHeightMapNormal(heightMap, paddedX, paddedZ, heightMultiplier),
                new Vector2((float)x / chunkSize, (float)z / chunkSize),
                SurfaceTypeClassifier.GenerateColor(
                    surfaceTypeMap[paddedX, paddedZ],
                    waterStateMap[paddedX, paddedZ]));

            vertexIndicesByGridCoordinate.Add(key, vertexIndex);
            return vertexIndex;
        }

        int strip = Mathf.Max(1, stepIncrement);
        int interiorMin = strip;
        int interiorMax = chunkSize - strip;

        for (int z = interiorMin; z < interiorMax; z += stepIncrement)
        {
            for (int x = interiorMin; x < interiorMax; x += stepIncrement)
            {
                int a = GetVertexIndex(x, z);
                int b = GetVertexIndex(x, z + stepIncrement);
                int c = GetVertexIndex(x + stepIncrement, z + stepIncrement);
                int d = GetVertexIndex(x + stepIncrement, z);

                meshData.AddTriangle(a, b, c);
                meshData.AddTriangle(a, c, d);
            }
        }

        for (int x0 = 0; x0 < chunkSize; x0 += stepIncrement)
        {
            int x1 = Mathf.Min(x0 + stepIncrement, chunkSize);

            int anchor = GetVertexIndex(x0, 0);

            int prev = GetVertexIndex(x0 + 1, 0);
            for (int x = x0 + 2; x <= x1; x++)
            {
                int next = GetVertexIndex(x, 0);
                meshData.AddTriangle(anchor, next, prev);
                prev = next;
            }

            int innerRight = GetVertexIndex(x1, strip);
            int innerLeft = GetVertexIndex(x0, strip);

            meshData.AddTriangle(anchor, innerRight, prev);
            meshData.AddTriangle(anchor, innerLeft, innerRight);
        }

        for (int x0 = 0; x0 < chunkSize; x0 += stepIncrement)
        {
            int x1 = Mathf.Min(x0 + stepIncrement, chunkSize);

            int anchor = GetVertexIndex(x0, chunkSize - strip);

            int prev = GetVertexIndex(x0, chunkSize);
            for (int x = x0 + 1; x <= x1; x++)
            {
                int next = GetVertexIndex(x, chunkSize);
                meshData.AddTriangle(anchor, prev, next);
                prev = next;
            }

            int innerRight = GetVertexIndex(x1, chunkSize - strip);
            meshData.AddTriangle(anchor, prev, innerRight);
        }

        for (int z0 = strip; z0 < chunkSize - strip; z0 += stepIncrement)
        {
            int z1 = Mathf.Min(z0 + stepIncrement, chunkSize - strip);

            int anchor = GetVertexIndex(0, z0);

            int prev = GetVertexIndex(0, z0 + 1);
            for (int z = z0 + 2; z <= z1; z++)
            {
                int next = GetVertexIndex(0, z);
                meshData.AddTriangle(anchor, prev, next);
                prev = next;
            }

            int innerBottom = GetVertexIndex(strip, z1);
            int innerTop = GetVertexIndex(strip, z0);

            meshData.AddTriangle(anchor, prev, innerBottom);
            meshData.AddTriangle(anchor, innerBottom, innerTop);
        }

        for (int z0 = strip; z0 < chunkSize - strip; z0 += stepIncrement)
        {
            int z1 = Mathf.Min(z0 + stepIncrement, chunkSize - strip);

            int anchor = GetVertexIndex(chunkSize - strip, z0);

            int prev = GetVertexIndex(chunkSize - strip, z1);

            int first = GetVertexIndex(chunkSize, z1);
            meshData.AddTriangle(anchor, prev, first);
            prev = first;

            for (int z = z1 - 1; z >= z0; z--)
            {
                int next = GetVertexIndex(chunkSize, z);
                meshData.AddTriangle(anchor, prev, next);
                prev = next;
            }
        }

        return meshData;
    }

    private static int EstimateTerrainVertexCapacity(int chunkSize, int stepIncrement)
    {
        if (stepIncrement <= 1)
            return (chunkSize + 1) * (chunkSize + 1);

        int coarseVerticesPerLine = chunkSize / stepIncrement + 1;
        int coarseInteriorEstimate = coarseVerticesPerLine * coarseVerticesPerLine;
        int stitchedBorderEstimate = (chunkSize + 1) * 4;
        return Mathf.Min((chunkSize + 1) * (chunkSize + 1), coarseInteriorEstimate + stitchedBorderEstimate);
    }

    private static Vector3 CalculateHeightMapNormal(float[,] heightMap, int x, int z, float heightMultiplier)
    {
        float left = heightMap[x - 1, z];
        float right = heightMap[x + 1, z];
        float down = heightMap[x, z - 1];
        float up = heightMap[x, z + 1];

        float dx = (right - left) * heightMultiplier;
        float dz = (up - down) * heightMultiplier;

        return new Vector3(-dx, 2f, -dz).normalized;
    }
}

public class MeshData
{
    public Vector3[] vertices;
    public Vector3[] normals;
    public Vector2[] uvs;
    public Color[] colors;

    private readonly List<Vector3> vertexList;
    private readonly List<Vector3> normalList;
    private readonly List<Vector2> uvList;
    private readonly List<Color> colorList;
    private readonly List<int> triangles;

    public int VertexCapacity => vertexList != null ? vertexList.Capacity : vertices.Length;

    public MeshData(int meshWidth, int meshHeight)
    {
        vertices = new Vector3[meshWidth * meshHeight];
        normals = new Vector3[meshWidth * meshHeight];
        uvs = new Vector2[meshWidth * meshHeight];
        colors = new Color[meshWidth * meshHeight];
        triangles = new List<int>((meshWidth - 1) * (meshHeight - 1) * 6);
    }

    public MeshData(int initialVertexCapacity)
    {
        int safeVertexCapacity = Mathf.Max(4, initialVertexCapacity);
        vertexList = new List<Vector3>(safeVertexCapacity);
        normalList = new List<Vector3>(safeVertexCapacity);
        uvList = new List<Vector2>(safeVertexCapacity);
        colorList = new List<Color>(safeVertexCapacity);
        triangles = new List<int>(safeVertexCapacity * 6);
    }

    public int AddVertex(Vector3 vertex, Vector3 normal, Vector2 uv, Color color)
    {
        if (vertexList == null)
            throw new System.InvalidOperationException("AddVertex requires dynamic MeshData.");

        int index = vertexList.Count;
        vertexList.Add(vertex);
        normalList.Add(normal);
        uvList.Add(uv);
        colorList.Add(color);
        return index;
    }

    public Vector3 GetVertex(int index)
    {
        if (vertexList != null)
            return vertexList[index];

        return vertices[index];
    }

    public void AddTriangle(int a, int b, int c)
    {
        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
    }

    public Mesh CreateMesh()
    {
        Mesh mesh = new Mesh();

        if (vertexList != null)
        {
            mesh.SetVertices(vertexList);
            mesh.SetNormals(normalList);
            mesh.SetUVs(0, uvList);
            mesh.SetColors(colorList);
        }
        else
        {
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.colors = colors;
        }

        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
