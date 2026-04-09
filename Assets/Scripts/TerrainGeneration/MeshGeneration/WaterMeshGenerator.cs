using System.Collections.Generic;
using UnityEngine;

public static class LakeMeshGenerator
{
    private const float LakeWaterLevel = TerrainWaterSettings.WaterLevel;
    private const float WaterSurfaceOffset = 0.02f;

    public static WaterMeshData GenerateLakeMesh(
        float[,] heightMap,
        WaterState[,] waterStateMap,
        float[,] riverMaskMap,
        float heightMultiplier,
        int stepIncrement,
        float worldScale)
    {
        int paddedWidth = heightMap.GetLength(0);
        int chunkSize = paddedWidth - 3;

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;
        float waterY = LakeWaterLevel * heightMultiplier * worldScale + WaterSurfaceOffset;

        bool[,] lakeCellMask = BuildLakeCellMask(waterStateMap, chunkSize);
        int renderableCellCount = CountRenderableBlocks(lakeCellMask, chunkSize, stepIncrement);

        if (renderableCellCount == 0)
            return new WaterMeshData(0);

        WaterMeshData meshData = new WaterMeshData(renderableCellCount);

        for (int localY = 0; localY <= chunkSize - stepIncrement; localY += stepIncrement)
        {
            for (int localX = 0; localX <= chunkSize - stepIncrement; localX += stepIncrement)
            {
                if (!BlockContainsRenderableRect(lakeCellMask, localX, localY, stepIncrement, stepIncrement, chunkSize))
                    continue;

                Vector3 a = new Vector3(
                    (topLeftX + localX) * worldScale,
                    waterY,
                    (bottomLeftZ + localY) * worldScale);

                Vector3 b = new Vector3(
                    (topLeftX + localX + stepIncrement) * worldScale,
                    waterY,
                    (bottomLeftZ + localY) * worldScale);

                Vector3 c = new Vector3(
                    (topLeftX + localX) * worldScale,
                    waterY,
                    (bottomLeftZ + localY + stepIncrement) * worldScale);

                Vector3 d = new Vector3(
                    (topLeftX + localX + stepIncrement) * worldScale,
                    waterY,
                    (bottomLeftZ + localY + stepIncrement) * worldScale);

                Vector2 uvA = new Vector2(localX / (float)chunkSize, localY / (float)chunkSize);
                Vector2 uvB = new Vector2((localX + stepIncrement) / (float)chunkSize, localY / (float)chunkSize);
                Vector2 uvC = new Vector2(localX / (float)chunkSize, (localY + stepIncrement) / (float)chunkSize);
                Vector2 uvD = new Vector2((localX + stepIncrement) / (float)chunkSize, (localY + stepIncrement) / (float)chunkSize);

                meshData.AddCell(a, b, c, d, uvA, uvB, uvC, uvD);
            }
        }

        return meshData;
    }

    private static bool[,] BuildLakeCellMask(WaterState[,] waterStateMap, int chunkSize)
    {
        bool[,] lakeCellMask = new bool[chunkSize, chunkSize];

        for (int localY = 0; localY < chunkSize; localY++)
        {
            for (int localX = 0; localX < chunkSize; localX++)
            {
                int x = localX + 1;
                int y = localY + 1;
                int x1 = x + 1;
                int y1 = y + 1;

                int waterCornerCount = 0;

                if (IsRenderableWater(waterStateMap[x, y])) waterCornerCount++;
                if (IsRenderableWater(waterStateMap[x1, y])) waterCornerCount++;
                if (IsRenderableWater(waterStateMap[x, y1])) waterCornerCount++;
                if (IsRenderableWater(waterStateMap[x1, y1])) waterCornerCount++;

                lakeCellMask[localX, localY] = waterCornerCount >= 1;
            }
        }

        return lakeCellMask;
    }

    private static bool IsRenderableWater(WaterState waterState)
    {
        return waterState == WaterState.Shallow || waterState == WaterState.Deep;
    }

    private static int CountRenderableBlocks(bool[,] cellMask, int chunkSize, int stepIncrement)
    {
        int count = 0;

        for (int localY = 0; localY <= chunkSize - stepIncrement; localY += stepIncrement)
        {
            for (int localX = 0; localX <= chunkSize - stepIncrement; localX += stepIncrement)
            {
                if (BlockContainsRenderableRect(cellMask, localX, localY, stepIncrement, stepIncrement, chunkSize))
                    count++;
            }
        }

        return count;
    }

    private static bool BlockContainsRenderableRect(bool[,] cellMask, int startX, int startY, int width, int height, int chunkSize)
    {
        int maxX = Mathf.Min(startX + width, chunkSize);
        int maxY = Mathf.Min(startY + height, chunkSize);

        for (int y = startY; y < maxY; y++)
        {
            for (int x = startX; x < maxX; x++)
            {
                if (cellMask[x, y])
                    return true;
            }
        }

        return false;
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
        int paddedWidth = heightMap.GetLength(0);
        int chunkSize = paddedWidth - 3;

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;

        bool[,] riverCellMask = BuildRiverCellMask(riverMaskMap, chunkSize);

        if (stepIncrement == 1)
        {
            return GenerateRiverMeshLOD0(
                heightMap,
                riverCellMask,
                heightMultiplier,
                worldScale);
        }

        int strip = Mathf.Max(1, stepIncrement);
        int interiorMin = strip;
        int interiorMax = chunkSize - strip;

        WaterMeshData meshData = new WaterMeshData(0);

        Vector3[,] positions = new Vector3[chunkSize + 1, chunkSize + 1];
        Vector2[,] uvs = new Vector2[chunkSize + 1, chunkSize + 1];

        for (int z = 0; z <= chunkSize; z++)
        {
            for (int x = 0; x <= chunkSize; x++)
            {
                float h = heightMap[x + 1, z + 1] * heightMultiplier * worldScale + WaterSurfaceOffset;

                positions[x, z] = new Vector3(
                    (topLeftX + x) * worldScale,
                    h,
                    (bottomLeftZ + z) * worldScale);

                uvs[x, z] = new Vector2(
                    x / (float)chunkSize,
                    z / (float)chunkSize);
            }
        }

        for (int z = interiorMin; z < interiorMax; z += stepIncrement)
        {
            for (int x = interiorMin; x < interiorMax; x += stepIncrement)
            {
                int spanX = Mathf.Min(stepIncrement, interiorMax - x);
                int spanZ = Mathf.Min(stepIncrement, interiorMax - z);

                if (!BlockContainsRenderableRect(riverCellMask, x, z, spanX, spanZ, chunkSize))
                    continue;

                AddTri(meshData, positions, uvs, x, z, x, z + spanZ, x + spanX, z + spanZ);
                AddTri(meshData, positions, uvs, x, z, x + spanX, z + spanZ, x + spanX, z);
            }
        }

        for (int x0 = 0; x0 < chunkSize; x0 += stepIncrement)
        {
            int x1 = Mathf.Min(x0 + stepIncrement, chunkSize);

            if (!BlockContainsRenderableRect(riverCellMask, x0, 0, x1 - x0, strip, chunkSize))
                continue;

            int anchorX = x0;
            int anchorZ = 0;

            int prevX = x0 + 1;
            int prevZ = 0;

            for (int x = x0 + 2; x <= x1; x++)
            {
                AddTri(meshData, positions, uvs, anchorX, anchorZ, x, 0, prevX, prevZ);
                prevX = x;
            }

            AddTri(meshData, positions, uvs, anchorX, anchorZ, x1, strip, prevX, prevZ);
            AddTri(meshData, positions, uvs, anchorX, anchorZ, x0, strip, x1, strip);
        }

        for (int x0 = 0; x0 < chunkSize; x0 += stepIncrement)
        {
            int x1 = Mathf.Min(x0 + stepIncrement, chunkSize);

            if (!BlockContainsRenderableRect(riverCellMask, x0, chunkSize - strip, x1 - x0, strip, chunkSize))
                continue;

            int anchorX = x0;
            int anchorZ = chunkSize - strip;

            int prevX = x0;
            int prevZ = chunkSize;

            for (int x = x0 + 1; x <= x1; x++)
            {
                AddTri(meshData, positions, uvs, anchorX, anchorZ, prevX, prevZ, x, chunkSize);
                prevX = x;
            }

            AddTri(meshData, positions, uvs, anchorX, anchorZ, prevX, prevZ, x1, chunkSize - strip);
        }

        for (int z0 = strip; z0 < chunkSize - strip; z0 += stepIncrement)
        {
            int z1 = Mathf.Min(z0 + stepIncrement, chunkSize - strip);

            if (!BlockContainsRenderableRect(riverCellMask, 0, z0, strip, z1 - z0, chunkSize))
                continue;

            int anchorX = 0;
            int anchorZ = z0;

            int prevX = 0;
            int prevZ = z0 + 1;

            for (int z = z0 + 2; z <= z1; z++)
            {
                AddTri(meshData, positions, uvs, anchorX, anchorZ, prevX, prevZ, 0, z);
                prevZ = z;
            }

            AddTri(meshData, positions, uvs, anchorX, anchorZ, 0, z1, strip, z1);
            AddTri(meshData, positions, uvs, anchorX, anchorZ, strip, z1, strip, z0);
        }

        for (int z0 = strip; z0 < chunkSize - strip; z0 += stepIncrement)
        {
            int z1 = Mathf.Min(z0 + stepIncrement, chunkSize - strip);

            if (!BlockContainsRenderableRect(riverCellMask, chunkSize - strip, z0, strip, z1 - z0, chunkSize))
                continue;

            int anchorX = chunkSize - strip;
            int anchorZ = z0;

            int prevX = chunkSize - strip;
            int prevZ = z1;

            AddTri(meshData, positions, uvs, anchorX, anchorZ, prevX, prevZ, chunkSize, z1);
            prevX = chunkSize;
            prevZ = z1;

            for (int z = z1 - 1; z >= z0; z--)
            {
                AddTri(meshData, positions, uvs, anchorX, anchorZ, prevX, prevZ, chunkSize, z);
                prevZ = z;
            }
        }

        if (meshData.VertexCount == 0)
            return new WaterMeshData(0);

        return meshData;
    }

    private static WaterMeshData GenerateRiverMeshLOD0(
        float[,] heightMap,
        bool[,] riverCellMask,
        float heightMultiplier,
        float worldScale)
    {
        int paddedWidth = heightMap.GetLength(0);
        int chunkSize = paddedWidth - 3;

        float topLeftX = chunkSize / -2f;
        float bottomLeftZ = chunkSize / -2f;

        int renderableCellCount = 0;
        for (int z = 0; z < chunkSize; z++)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                if (riverCellMask[x, z])
                    renderableCellCount++;
            }
        }

        if (renderableCellCount == 0)
            return new WaterMeshData(0);

        WaterMeshData meshData = new WaterMeshData(renderableCellCount);

        for (int z = 0; z < chunkSize; z++)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                if (!riverCellMask[x, z])
                    continue;

                Vector3 a = BuildRiverVertex(heightMap, topLeftX, bottomLeftZ, x, z, heightMultiplier, worldScale);
                Vector3 b = BuildRiverVertex(heightMap, topLeftX, bottomLeftZ, x + 1, z, heightMultiplier, worldScale);
                Vector3 c = BuildRiverVertex(heightMap, topLeftX, bottomLeftZ, x, z + 1, heightMultiplier, worldScale);
                Vector3 d = BuildRiverVertex(heightMap, topLeftX, bottomLeftZ, x + 1, z + 1, heightMultiplier, worldScale);

                Vector2 uvA = new Vector2(x / (float)chunkSize, z / (float)chunkSize);
                Vector2 uvB = new Vector2((x + 1) / (float)chunkSize, z / (float)chunkSize);
                Vector2 uvC = new Vector2(x / (float)chunkSize, (z + 1) / (float)chunkSize);
                Vector2 uvD = new Vector2((x + 1) / (float)chunkSize, (z + 1) / (float)chunkSize);

                meshData.AddCell(a, b, c, d, uvA, uvB, uvC, uvD);
            }
        }

        return meshData;
    }

    private static Vector3 BuildRiverVertex(
        float[,] heightMap,
        float topLeftX,
        float bottomLeftZ,
        int x,
        int z,
        float heightMultiplier,
        float worldScale)
    {
        float h = heightMap[x + 1, z + 1] * heightMultiplier * worldScale + WaterSurfaceOffset;

        return new Vector3(
            (topLeftX + x) * worldScale,
            h,
            (bottomLeftZ + z) * worldScale);
    }

    private static void AddTri(
        WaterMeshData meshData,
        Vector3[,] positions,
        Vector2[,] uvs,
        int ax, int az,
        int bx, int bz,
        int cx, int cz)
    {
        meshData.AddTriangle(
            positions[ax, az], positions[bx, bz], positions[cx, cz],
            uvs[ax, az], uvs[bx, bz], uvs[cx, cz]);
    }

    private static bool[,] BuildRiverCellMask(float[,] riverMaskMap, int chunkSize)
    {
        bool[,] riverCellMask = new bool[chunkSize, chunkSize];

        for (int localY = 0; localY < chunkSize; localY++)
        {
            for (int localX = 0; localX < chunkSize; localX++)
            {
                int x = localX + 1;
                int y = localY + 1;

                int count = 0;
                if (riverMaskMap[x, y] >= RiverInclusionThreshold) count++;
                if (riverMaskMap[x + 1, y] >= RiverInclusionThreshold) count++;
                if (riverMaskMap[x, y + 1] >= RiverInclusionThreshold) count++;
                if (riverMaskMap[x + 1, y + 1] >= RiverInclusionThreshold) count++;

                riverCellMask[localX, localY] = count >= 1;
            }
        }

        return riverCellMask;
    }

    private static bool BlockContainsRenderableRect(bool[,] cellMask, int startX, int startY, int width, int height, int chunkSize)
    {
        int maxX = Mathf.Min(startX + width, chunkSize);
        int maxY = Mathf.Min(startY + height, chunkSize);

        for (int y = startY; y < maxY; y++)
        {
            for (int x = startX; x < maxX; x++)
            {
                if (cellMask[x, y])
                    return true;
            }
        }

        return false;
    }
}

public class WaterMeshData
{
    private readonly List<Vector3> vertices;
    private readonly List<Vector2> uvs;
    private readonly List<int> triangles;
    private readonly List<Color> colors;

    private static readonly Color WaterColor = new Color(0.05f, 0.25f, 0.60f, 1f);

    public int VertexCount => vertices.Count;

    public WaterMeshData(int initialCellCount)
    {
        int initialVertexCapacity = Mathf.Max(4, initialCellCount * 4);
        int initialTriangleCapacity = Mathf.Max(6, initialCellCount * 6);

        vertices = new List<Vector3>(initialVertexCapacity);
        uvs = new List<Vector2>(initialVertexCapacity);
        triangles = new List<int>(initialTriangleCapacity);
        colors = new List<Color>(initialVertexCapacity);
    }

    public void AddCell(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector2 uvA,
        Vector2 uvB,
        Vector2 uvC,
        Vector2 uvD)
    {
        int baseIndex = vertices.Count;

        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);

        uvs.Add(uvA);
        uvs.Add(uvB);
        uvs.Add(uvC);
        uvs.Add(uvD);

        colors.Add(WaterColor);
        colors.Add(WaterColor);
        colors.Add(WaterColor);
        colors.Add(WaterColor);

        triangles.Add(baseIndex);
        triangles.Add(baseIndex + 2);
        triangles.Add(baseIndex + 1);

        triangles.Add(baseIndex + 1);
        triangles.Add(baseIndex + 2);
        triangles.Add(baseIndex + 3);
    }

    public void AddTriangle(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector2 uvA,
        Vector2 uvB,
        Vector2 uvC)
    {
        int baseIndex = vertices.Count;

        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);

        uvs.Add(uvA);
        uvs.Add(uvB);
        uvs.Add(uvC);

        colors.Add(WaterColor);
        colors.Add(WaterColor);
        colors.Add(WaterColor);

        triangles.Add(baseIndex);
        triangles.Add(baseIndex + 1);
        triangles.Add(baseIndex + 2);
    }

    public Mesh CreateMesh()
    {
        Mesh mesh = new Mesh();
        mesh.vertices = vertices.ToArray();
        mesh.uv = uvs.ToArray();
        mesh.triangles = triangles.ToArray();
        mesh.colors = colors.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}