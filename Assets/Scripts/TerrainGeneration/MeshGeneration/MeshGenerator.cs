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
        int paddedHeight = heightMap.GetLength(1);

        int chunkSize = paddedWidth - 3;

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;

        float chunkWorldCenterX = (chunkCoord.x * chunkSize + chunkSize * 0.5f) * worldScale;
        float chunkWorldCenterZ = (chunkCoord.z * chunkSize + chunkSize * 0.5f) * worldScale;

        int verticesPerLine = chunkSize / stepIncrement + 1;

        MeshData meshData = new MeshData(verticesPerLine, verticesPerLine);
        int vertexIndex = 0;

        for (int localZ = 0; localZ <= chunkSize; localZ += stepIncrement)
        {
            for (int localX = 0; localX <= chunkSize; localX += stepIncrement)
            {
                int paddedX = localX + 1;
                int paddedZ = localZ + 1;

                float localWorldX = (topLeftX + localX) * worldScale;
                float localWorldZ = (bottomLeftZ + localZ) * worldScale;

                float worldX = chunkWorldCenterX + localWorldX;
                float worldZ = chunkWorldCenterZ + localWorldZ;

                float vertexHeight = heightMap[paddedX, paddedZ] * heightMultiplier * worldScale;

                meshData.vertices[vertexIndex] = new Vector3(localWorldX, vertexHeight, localWorldZ);
                meshData.uvs[vertexIndex] = new Vector2(worldX / textureTileSize, worldZ / textureTileSize);

                // meshData.colors[vertexIndex] = BiomeClassifier.GenerateColorFromBiomeType(biomeMap[paddedX, paddedZ]);
                // meshData.colors[vertexIndex] = BiomeClassifier.GenerateDebugColorFromRiverMask(riverMaskMap[paddedX, paddedZ]);
                meshData.colors[vertexIndex] = SurfaceTypeClassifier.GenerateColor(
                    surfaceTypeMap[paddedX, paddedZ],
                    waterStateMap[paddedX, paddedZ]);

                meshData.normals[vertexIndex] = CalculateHeightMapNormal(heightMap, paddedX, paddedZ, heightMultiplier);

                int xIndex = localX / stepIncrement;
                int zIndex = localZ / stepIncrement;

                if (xIndex < verticesPerLine - 1 && zIndex < verticesPerLine - 1)
                {
                    meshData.AddTriangle(vertexIndex, vertexIndex + verticesPerLine, vertexIndex + verticesPerLine + 1);
                    meshData.AddTriangle(vertexIndex, vertexIndex + verticesPerLine + 1, vertexIndex + 1);
                }

                vertexIndex++;
            }
        }

        return meshData;
    }

    private static Vector3 CalculateHeightMapNormal(float[,] heightMap, int x, int z, float heightMultiplier)
    {
        float left = heightMap[x - 1, z];
        float right = heightMap[x + 1, z];
        float down = heightMap[x, z - 1];
        float up = heightMap[x, z + 1];

        float dx = (right - left) * heightMultiplier;
        float dz = (up - down) * heightMultiplier;

        Vector3 normal = new Vector3(-dx, 2f, -dz).normalized;
        return normal;
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