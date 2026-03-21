using UnityEngine;

public static class LakeMeshGenerator
{
    private const float LakeWaterLevel = TerrainWaterSettings.WaterLevel;
    private const float WaterSurfaceOffset = 0.02f;
    private const float RiverExclusionThreshold = 0.40f;

    public static WaterMeshData GenerateLakeMesh(
        float[,] heightMap,
        WaterState[,] waterStateMap,
        float[,] riverMaskMap,
        float heightMultiplier,
        int stepIncrement,
        float worldScale)
    {
        int width = heightMap.GetLength(0);
        int height = heightMap.GetLength(1);

        float topLeftX = (width - 1) / -2f;
        float bottomLeftZ = (height - 1) / -2f;

        int waterCellCount = CountRenderableLakeCells(waterStateMap, riverMaskMap, stepIncrement);

        if (waterCellCount == 0)
            return new WaterMeshData(0);

        WaterMeshData meshData = new WaterMeshData(waterCellCount);

        float waterY = LakeWaterLevel * heightMultiplier * worldScale + WaterSurfaceOffset;

        for (int y = 0; y < height - 1; y += stepIncrement)
        {
            for (int x = 0; x < width - 1; x += stepIncrement)
            {
                if (!IsRenderableLakeCell(waterStateMap, riverMaskMap, x, y, stepIncrement))
                    continue;

                Vector3 a = new Vector3(
                    (topLeftX + x) * worldScale,
                    waterY,
                    (bottomLeftZ + y) * worldScale);

                Vector3 b = new Vector3(
                    (topLeftX + x + stepIncrement) * worldScale,
                    waterY,
                    (bottomLeftZ + y) * worldScale);

                Vector3 c = new Vector3(
                    (topLeftX + x) * worldScale,
                    waterY,
                    (bottomLeftZ + y + stepIncrement) * worldScale);

                Vector3 d = new Vector3(
                    (topLeftX + x + stepIncrement) * worldScale,
                    waterY,
                    (bottomLeftZ + y + stepIncrement) * worldScale);

                Vector2 uvA = new Vector2(x / (float)(width - 1), y / (float)(height - 1));
                Vector2 uvB = new Vector2((x + stepIncrement) / (float)(width - 1), y / (float)(height - 1));
                Vector2 uvC = new Vector2(x / (float)(width - 1), (y + stepIncrement) / (float)(height - 1));
                Vector2 uvD = new Vector2((x + stepIncrement) / (float)(width - 1), (y + stepIncrement) / (float)(height - 1));

                meshData.AddQuad(a, b, c, d, uvA, uvB, uvC, uvD);
            }
        }

        return meshData;
    }

    private static int CountRenderableLakeCells(
        WaterState[,] waterStateMap,
        float[,] riverMaskMap,
        int stepIncrement)
    {
        int width = waterStateMap.GetLength(0);
        int height = waterStateMap.GetLength(1);

        int count = 0;

        for (int y = 0; y < height - 1; y += stepIncrement)
        {
            for (int x = 0; x < width - 1; x += stepIncrement)
            {
                if (IsRenderableLakeCell(waterStateMap, riverMaskMap, x, y, stepIncrement))
                    count++;
            }
        }

        return count;
    }

    private static bool IsRenderableLakeCell(
    WaterState[,] waterStateMap,
    float[,] riverMaskMap,
    int x,
    int y,
    int stepIncrement)
    {
        int x1 = x + stepIncrement;
        int y1 = y + stepIncrement;

        if (x1 >= waterStateMap.GetLength(0) || y1 >= waterStateMap.GetLength(1))
            return false;

        int waterCornerCount = 0;

        if (IsRenderableWater(waterStateMap[x, y])) waterCornerCount++;
        if (IsRenderableWater(waterStateMap[x1, y])) waterCornerCount++;
        if (IsRenderableWater(waterStateMap[x, y1])) waterCornerCount++;
        if (IsRenderableWater(waterStateMap[x1, y1])) waterCornerCount++;

        return waterCornerCount >= 1;
    }

    private static bool IsRenderableWater(WaterState waterState)
    {
        return waterState == WaterState.Shallow || waterState == WaterState.Deep;
    }
}

public static class RiverMeshGenerator
{
    private const float WaterSurfaceOffset = 0.02f;
    private const float RiverInclusionThreshold = 0.40f;

    public static WaterMeshData GenerateRiverMesh(
        float[,] heightMap,
        WaterState[,] waterStateMap,
        float[,] riverMaskMap,
        float heightMultiplier,
        int stepIncrement,
        float worldScale)
    {
        int width = heightMap.GetLength(0);
        int height = heightMap.GetLength(1);

        float topLeftX = (width - 1) / -2f;
        float bottomLeftZ = (height - 1) / -2f;

        int riverCellCount = CountRenderableRiverCells(waterStateMap, riverMaskMap, stepIncrement);

        if (riverCellCount == 0)
            return new WaterMeshData(0);

        WaterMeshData meshData = new WaterMeshData(riverCellCount);

        for (int y = 0; y < height - 1; y += stepIncrement)
        {
            for (int x = 0; x < width - 1; x += stepIncrement)
            {
                if (!IsRenderableRiverCell(waterStateMap, riverMaskMap, x, y, stepIncrement))
                    continue;

                int x1 = x + stepIncrement;
                int y1 = y + stepIncrement;

                float heightA = heightMap[x, y] * heightMultiplier * worldScale + WaterSurfaceOffset;
                float heightB = heightMap[x1, y] * heightMultiplier * worldScale + WaterSurfaceOffset;
                float heightC = heightMap[x, y1] * heightMultiplier * worldScale + WaterSurfaceOffset;
                float heightD = heightMap[x1, y1] * heightMultiplier * worldScale + WaterSurfaceOffset;

                Vector3 a = new Vector3(
                    (topLeftX + x) * worldScale,
                    heightA,
                    (bottomLeftZ + y) * worldScale);

                Vector3 b = new Vector3(
                    (topLeftX + x1) * worldScale,
                    heightB,
                    (bottomLeftZ + y) * worldScale);

                Vector3 c = new Vector3(
                    (topLeftX + x) * worldScale,
                    heightC,
                    (bottomLeftZ + y1) * worldScale);

                Vector3 d = new Vector3(
                    (topLeftX + x1) * worldScale,
                    heightD,
                    (bottomLeftZ + y1) * worldScale);

                Vector2 uvA = new Vector2(x / (float)(width - 1), y / (float)(height - 1));
                Vector2 uvB = new Vector2(x1 / (float)(width - 1), y / (float)(height - 1));
                Vector2 uvC = new Vector2(x / (float)(width - 1), y1 / (float)(height - 1));
                Vector2 uvD = new Vector2(x1 / (float)(width - 1), y1 / (float)(height - 1));

                meshData.AddQuad(a, b, c, d, uvA, uvB, uvC, uvD);
            }
        }

        return meshData;
    }

    private static int CountRenderableRiverCells(
        WaterState[,] waterStateMap,
        float[,] riverMaskMap,
        int stepIncrement)
    {
        int width = waterStateMap.GetLength(0);
        int height = waterStateMap.GetLength(1);

        int count = 0;

        for (int y = 0; y < height - 1; y += stepIncrement)
        {
            for (int x = 0; x < width - 1; x += stepIncrement)
            {
                if (IsRenderableRiverCell(waterStateMap, riverMaskMap, x, y, stepIncrement))
                    count++;
            }
        }

        return count;
    }

    private static bool IsRenderableRiverCell(
        WaterState[,] waterStateMap,
        float[,] riverMaskMap,
        int x,
        int y,
        int stepIncrement)
    {
        int x1 = x + stepIncrement;
        int y1 = y + stepIncrement;

        if (x1 >= waterStateMap.GetLength(0) || y1 >= waterStateMap.GetLength(1))
            return false;

        int riverCornerCount = 0;

        if (riverMaskMap[x, y] >= RiverInclusionThreshold) riverCornerCount++;
        if (riverMaskMap[x1, y] >= RiverInclusionThreshold) riverCornerCount++;
        if (riverMaskMap[x, y1] >= RiverInclusionThreshold) riverCornerCount++;
        if (riverMaskMap[x1, y1] >= RiverInclusionThreshold) riverCornerCount++;

        return riverCornerCount >= 1;
    }

}



public class WaterMeshData
{
    public Vector3[] vertices;
    public Vector2[] uvs;
    public int[] triangles;
    public Color[] colors;

    private int vertexIndex;
    private int triangleIndex;

    public WaterMeshData(int quadCount)
    {
        vertices = new Vector3[quadCount * 4];
        uvs = new Vector2[quadCount * 4];
        triangles = new int[quadCount * 6];
        colors = new Color[quadCount * 4];
    }

    public void AddQuad(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector2 uvA,
        Vector2 uvB,
        Vector2 uvC,
        Vector2 uvD)
    {
        vertices[vertexIndex] = a;
        vertices[vertexIndex + 1] = b;
        vertices[vertexIndex + 2] = c;
        vertices[vertexIndex + 3] = d;

        uvs[vertexIndex] = uvA;
        uvs[vertexIndex + 1] = uvB;
        uvs[vertexIndex + 2] = uvC;
        uvs[vertexIndex + 3] = uvD;

        Color waterColor = new Color(0.05f, 0.25f, 0.60f, 1f);
        colors[vertexIndex] = waterColor;
        colors[vertexIndex + 1] = waterColor;
        colors[vertexIndex + 2] = waterColor;
        colors[vertexIndex + 3] = waterColor;

        triangles[triangleIndex] = vertexIndex;
        triangles[triangleIndex + 1] = vertexIndex + 2;
        triangles[triangleIndex + 2] = vertexIndex + 3;

        triangles[triangleIndex + 3] = vertexIndex;
        triangles[triangleIndex + 4] = vertexIndex + 3;
        triangles[triangleIndex + 5] = vertexIndex + 1;

        vertexIndex += 4;
        triangleIndex += 6;
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