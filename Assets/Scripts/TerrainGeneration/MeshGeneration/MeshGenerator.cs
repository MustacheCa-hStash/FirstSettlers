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
        float textureTileSize = 2f;

        int paddedWidth = heightMap.GetLength(0);
        int chunkSize = paddedWidth - 3;

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;

        float chunkWorldCenterX = (chunkCoord.x * chunkSize + chunkSize * 0.5f) * worldScale;
        float chunkWorldCenterZ = (chunkCoord.z * chunkSize + chunkSize * 0.5f) * worldScale;

        int verticesPerLine = chunkSize + 1;
        MeshData meshData = new MeshData(verticesPerLine, verticesPerLine);

        for (int z = 0; z <= chunkSize; z++)
        {
            for (int x = 0; x <= chunkSize; x++)
            {
                int paddedX = x + 1;
                int paddedZ = z + 1;

                float localWorldX = (topLeftX + x) * worldScale;
                float localWorldZ = (bottomLeftZ + z) * worldScale;

                float worldX = chunkWorldCenterX + localWorldX;
                float worldZ = chunkWorldCenterZ + localWorldZ;

                float h = heightMap[paddedX, paddedZ] * heightMultiplier * worldScale;

                int i = z * verticesPerLine + x;

                meshData.vertices[i] = new Vector3(localWorldX, h, localWorldZ);
                meshData.uvs[i] = new Vector2(worldX / textureTileSize, worldZ / textureTileSize);

                meshData.colors[i] = SurfaceTypeClassifier.GenerateColor(
                    surfaceTypeMap[paddedX, paddedZ],
                    waterStateMap[paddedX, paddedZ]);

                meshData.normals[i] = CalculateHeightMapNormal(heightMap, paddedX, paddedZ, heightMultiplier);
            }
        }

        int strip = Mathf.Max(1, stepIncrement);
        int interiorMin = strip;
        int interiorMax = chunkSize - strip;

        for (int z = interiorMin; z < interiorMax; z += stepIncrement)
        {
            for (int x = interiorMin; x < interiorMax; x += stepIncrement)
            {
                int a = Index(x, z, verticesPerLine);
                int b = Index(x, z + stepIncrement, verticesPerLine);
                int c = Index(x + stepIncrement, z + stepIncrement, verticesPerLine);
                int d = Index(x + stepIncrement, z, verticesPerLine);

                meshData.AddTriangle(a, b, c);
                meshData.AddTriangle(a, c, d);
            }
        }

        for (int x0 = 0; x0 < chunkSize; x0 += stepIncrement)
        {
            int x1 = Mathf.Min(x0 + stepIncrement, chunkSize);

            int anchor = Index(x0, 0, verticesPerLine);

            int prev = Index(x0 + 1, 0, verticesPerLine);
            for (int x = x0 + 2; x <= x1; x++)
            {
                int next = Index(x, 0, verticesPerLine);
                meshData.AddTriangle(anchor, next, prev);
                prev = next;
            }

            int innerRight = Index(x1, strip, verticesPerLine);
            int innerLeft = Index(x0, strip, verticesPerLine);

            meshData.AddTriangle(anchor, innerRight, prev);
            meshData.AddTriangle(anchor, innerLeft, innerRight);
        }

        for (int x0 = 0; x0 < chunkSize; x0 += stepIncrement)
        {
            int x1 = Mathf.Min(x0 + stepIncrement, chunkSize);

            int anchor = Index(x0, chunkSize - strip, verticesPerLine);

            int prev = Index(x0, chunkSize, verticesPerLine);
            for (int x = x0 + 1; x <= x1; x++)
            {
                int next = Index(x, chunkSize, verticesPerLine);
                meshData.AddTriangle(anchor, prev, next);
                prev = next;
            }

            int innerRight = Index(x1, chunkSize - strip, verticesPerLine);
            meshData.AddTriangle(anchor, prev, innerRight);
        }

        for (int z0 = strip; z0 < chunkSize - strip; z0 += stepIncrement)
        {
            int z1 = Mathf.Min(z0 + stepIncrement, chunkSize - strip);

            int anchor = Index(0, z0, verticesPerLine);

            int prev = Index(0, z0 + 1, verticesPerLine);
            for (int z = z0 + 2; z <= z1; z++)
            {
                int next = Index(0, z, verticesPerLine);
                meshData.AddTriangle(anchor, prev, next);
                prev = next;
            }

            int innerBottom = Index(strip, z1, verticesPerLine);
            int innerTop = Index(strip, z0, verticesPerLine);

            meshData.AddTriangle(anchor, prev, innerBottom);
            meshData.AddTriangle(anchor, innerBottom, innerTop);
        }

        for (int z0 = strip; z0 < chunkSize - strip; z0 += stepIncrement)
        {
            int z1 = Mathf.Min(z0 + stepIncrement, chunkSize - strip);

            int anchor = Index(chunkSize - strip, z0, verticesPerLine);

            int prev = Index(chunkSize - strip, z1, verticesPerLine);

            int first = Index(chunkSize, z1, verticesPerLine);
            meshData.AddTriangle(anchor, prev, first);
            prev = first;

            for (int z = z1 - 1; z >= z0; z--)
            {
                int next = Index(chunkSize, z, verticesPerLine);
                meshData.AddTriangle(anchor, prev, next);
                prev = next;
            }
        }

        return meshData;
    }

    private static int Index(int x, int z, int size)
    {
        return z * size + x;
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
    public int[] triangles;
    public Color[] colors;

    private int triangleIndex;

    public MeshData(int meshWidth, int meshHeight)
    {
        vertices = new Vector3[meshWidth * meshHeight];
        normals = new Vector3[meshWidth * meshHeight];
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
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.colors = colors;
        mesh.RecalculateBounds();
        return mesh;
    }
}